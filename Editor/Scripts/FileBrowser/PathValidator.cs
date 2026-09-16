using System;
using System.Collections.Generic;
using System.IO;
using System.Text;



// ════════════════════════════════════════════════════════════════════
//  一、路径校验：错误码定义
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 路径校验失败的原因。
/// 用枚举而不是 bool，是为了让调用方能针对不同错误给出不同提示，
/// 也方便在日志里区分"用户输入错误"和"程序逻辑错误"。
/// </summary>
public enum PathError
{
    /// <summary>没有错误，路径合法。</summary>
    None = 0,

    /// <summary>路径为 null，或只包含空白字符。</summary>
    Empty,

    /// <summary>路径总长度超限。</summary>
    TooLong,

    /// <summary>路径中某一段（单个目录名或文件名）长度超限。</summary>
    SegmentTooLong,

    /// <summary>包含操作系统不允许的字符。</summary>
    IllegalChar,

    /// <summary>使用了操作系统保留的设备名（如 CON、NUL、COM1）。</summary>
    ReservedName,

    /// <summary>段名以空格或英文句点结尾（Windows 会静默去掉，易造成歧义）。</summary>
    TrailingSpaceOrDot,

    /// <summary>使用了过多的 ".."，试图越过根目录，例如 "/../a"。</summary>
    EscapeRoot,

    /// <summary>拼接时某个片段非法（用于区分是"某段"而非"整条"出错）。</summary>
    InvalidSegment,
}

// ════════════════════════════════════════════════════════════════════
//  二、路径校验器：只负责"单个段名"的合法性
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 路径段的合法性校验规则。
///
/// 设计说明：
///   校验被拆成两层——
///     ① 本类负责【单个段名】的规则（长度、字符、保留名……）；
///     ② VfsPath 负责【整条路径】的结构（拆段、处理 "." / ".."、越界判断）。
///   这样分层的好处是：规则集中、容易单测、容易按平台替换。
/// </summary>
public static class PathValidator
{
    /// <summary>整条路径允许的最大字符数（对齐 Windows 的常见限制）。</summary>
    public const int MaxPathLength = 4096;

    /// <summary>单个目录名 / 文件名允许的最大字符数。</summary>
    public const int MaxSegmentLength = 255;

    /// <summary>
    /// 不允许出现在段名里的字符。
    /// 注意：'/' 和 '\' 不在此列，因为它们在拆段阶段就已经被当作分隔符切掉了。
    /// </summary>
    private static readonly char[] s_illegalChars =
        { '\0', '<', '>', ':', '"', '|', '?', '*' };

    /// <summary>
    /// 系统保留设备名。
    /// 在 Windows 上，"CON"、"NUL" 等名字即使带扩展名（CON.txt）也无法创建，
    /// 所以在虚拟层里直接一并禁止，避免跨平台行为不一致。
    /// </summary>
    private static readonly HashSet<string> s_reservedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5",
            "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
            "LPT6", "LPT7", "LPT8", "LPT9",
        };

    /// <summary>
    /// 校验【单个路径段】（即一个目录名或文件名，不含分隔符）是否合法。
    ///
    /// 调用约定：
    ///   · 传入的 segment 必须是已经用 '/' 或 '\' 拆好的单段文本；
    ///   · 返回 false 时，通过 out 参数 error 告知具体是哪种违规；
    ///   · 返回 true 时，error 一定是 PathError.None。
    ///
    /// 为什么要把"单段校验"单独拆出来？
    ///   ① 拼接路径时可以逐段校验，出错能精确定位到"是哪一段坏了"；
    ///   ② 规则集中在一处，方便按平台替换（比如 Linux 可以放宽一些规则）；
    ///   ③ 便于单元测试，不用构造整棵树就能验证规则。
    /// </summary>
    /// <param name="segment">单个目录名或文件名，例如 "readme.txt"、"images"。</param>
    /// <param name="error">失败时输出具体原因；成功时为 PathError.None。</param>
    /// <returns>合法返回 true，非法返回 false。</returns>
    public static bool IsLegalSegment(string segment, out PathError error)
    {
        // ─────────────────────────────────────────────────────────
        // 初始化：先假定"没有错误"。
        // 后面任何一条规则命中时，会在返回前把它改成对应的错误码，
        // 这样调用方只要看返回值就知道该不该读 error。
        // ─────────────────────────────────────────────────────────
        error = PathError.None;

        // ─────────────────────────────────────────────────────────
        // 判断 1：空段直接放行（不算错误）。
        //
        // 为什么放行？
        //   拆分路径时，"//" 或开头/结尾的 "/" 都会产生空段，
        //   这些空段是"写法冗余"，不是"非法名字"，应由上层过滤掉。
        //   例如 "/a//b" 拆出来是 ["", "a", "", "b"]，
        //   中间那两个空段不应该让整条路径失败。
        //
        // 如果这里报错，那么 "/a//b" 这种常见写法就会被无理由拒绝。
        // ─────────────────────────────────────────────────────────
        if (segment.Length == 0) return true;

        // ─────────────────────────────────────────────────────────
        // 判断 2："." 和 ".." 直接放行（不算错误）。
        //
        // 为什么放行？
        //   它们是"相对路径记号"，不是真实的目录名：
        //     "."  表示"当前目录"；
        //     ".." 表示"上一级目录"。
        //   它们的语义（向上、停留）需要在【整条路径的层级栈】上处理，
        //   放在 VfsPath.TryParse 里做弹栈/忽略，而不是在这里判定非法。
        //
        // 如果这里直接判非法，那么 "/a/../b" 这种正常路径就会被拒绝。
        // ─────────────────────────────────────────────────────────
        if (segment == "." || segment == "..") return true;

        // ─────────────────────────────────────────────────────────
        // 判断 3：段名长度不能超过 MaxSegmentLength（默认 255）。
        //
        // 为什么限制？
        //   · 大多数文件系统对单个目录项名字有长度上限（NTFS/ext4 都是 255）；
        //   · 超长名字在底层存储时会被截断或报错，与其等到写入时崩，
        //     不如在入口处就拒绝，错误更早、更明确；
        //   · 也给下游的序列化、日志留出安全的缓冲区。
        // ─────────────────────────────────────────────────────────
        if (segment.Length > MaxSegmentLength)
        { error = PathError.SegmentTooLong; return false; }

        // ─────────────────────────────────────────────────────────
        // 判断 4：段名中不能包含非法字符。
        //
        // s_illegalChars 具体包含：
        //   '\0'  —— NUL 字符，会截断底层 C 字符串，必须禁止；
        //   '<'   —— Windows 保留；
        //   '>'   —— Windows 保留；
        //   ':'   —— Windows 用来分隔盘符（C:），且是 NTFS 数据流分隔符；
        //   '"'   —— Windows 保留；
        //   '|'   —— Windows 保留；
        //   '?'   —— Windows 通配符；
        //   '*'   —— Windows 通配符。
        //
        // 注意：'/' 和 '\' 不在这里判，
        //       因为它们在拆段阶段就已经被当作分隔符切掉了，
        //       不会以"段内字符"的形式出现在 segment 里。
        // ─────────────────────────────────────────────────────────
        if (segment.IndexOfAny(s_illegalChars) >= 0)
        { error = PathError.IllegalChar; return false; }

        // ─────────────────────────────────────────────────────────
        // 判断 5：段名不能以【空格】或【英文句点】结尾。
        //
        // 为什么要禁止"以空格结尾"？
        //   Windows 在创建文件时会静默把结尾的空格去掉，
        //   于是 "a " 和 "a" 实际上会落到同一个文件上，
        //   导致"写的时候叫 a，读的时候找不到"这种诡异 bug。
        //   与其承受这种歧义，不如在入口直接拒绝。
        //
        // 为什么要禁止"以点结尾"？
        //   同理，Windows 会静默去掉结尾的点：
        //   "a." 和 "a" 落到同一个文件；
        //   另外，".." 之外的多点结尾在跨平台时行为不一致，
        //   这里一刀切禁止最省心。
        //
        // 注意用 segment[^1]（C# 8 的索引运算符，等价于 segment[Length-1]）
        // 取最后一个字符，前面已经排除空串，所以这里不会越界。
        // ─────────────────────────────────────────────────────────
        if (segment[^1] == ' ' || segment[^1] == '.')
        { error = PathError.TrailingSpaceOrDot; return false; }

        // ─────────────────────────────────────────────────────────
        // 判断 6：段名的"主名"（第一个点之前的部分）不能是系统保留设备名。
        //
        // 什么是保留设备名？
        //   在 Windows 上，CON、PRN、AUX、NUL、COM1~COM9、LPT1~LPT9
        //   这些名字被系统当作"设备"而非普通文件。
        //   哪怕写成 "CON.txt"，Windows 依然会把它当成控制台设备，
        //   而不是一个普通文件，行为完全出乎意料。
        //
        // 为什么要先截到"主名"？
        //   因为 "CON.txt"、"NUL.log" 这类带扩展名的写法同样非法，
        //   所以取第一个点之前的部分（stem）再比较，
        //   就能把带扩展名的变体也一并拦住。
        //
        // 为什么要忽略大小写？
        //   Windows 的设备名判断是大小写不敏感的：
        //   "con"、"CON"、"Con" 都被视作同一个设备。
        //
        // 示例：
        //   "CON"        → dotIndex = -1，stem = "CON"   → 命中
        //   "CON.txt"    → dotIndex = 3， stem = "CON"   → 命中
        //   "CON.a.b.c"  → dotIndex = 3， stem = "CON"   → 命中
        //   ".gitignore" → dotIndex = 0， stem = ""      → 不命中（隐藏文件）
        //   "CONX"       → dotIndex = -1，stem = "CONX"  → 不命中
        // ─────────────────────────────────────────────────────────
        var dotIndex = segment.IndexOf('.');
        // 没有点 → 整段就是主名；有点 → 取点之前的部分作为主名
        var stem = dotIndex < 0 ? segment : segment[..dotIndex];
        if (s_reservedNames.Contains(stem))
        { error = PathError.ReservedName; return false; }

        // ─────────────────────────────────────────────────────────
        // 所有规则都通过：这是一个合法的路径段。
        // 此时 error 仍是初始化时的 PathError.None。
        // ─────────────────────────────────────────────────────────
        return true;
    }

    /// <summary>把错误码翻译成给用户看的中文说明。</summary>
    public static string Describe(PathError error) => error switch
    {
        PathError.None               => "路径合法",
        PathError.Empty              => "路径为空或只包含空白字符",
        PathError.TooLong            => $"路径总长度超过 {MaxPathLength} 个字符",
        PathError.SegmentTooLong     => $"目录名或文件名超过 {MaxSegmentLength} 个字符",
        PathError.IllegalChar        => "路径中包含非法字符（< > : \" | ? * 或 NUL 字符）",
        PathError.ReservedName       => "使用了系统保留设备名（如 CON、NUL、COM1）",
        PathError.TrailingSpaceOrDot => "目录名或文件名不能以空格或英文句点结尾",
        PathError.EscapeRoot         => "\"..\" 层级过多，已越过根目录",
        PathError.InvalidSegment     => "拼接的片段中包含非法名字",
        _                            => "未知错误",
    };
}

// ════════════════════════════════════════════════════════════════════
//  三、VfsPath：路径解析结果（校验通过 + 规范化）
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 一条已经通过合法性校验、并完成规范化的路径。
///
/// 它同时承载两个关键信息：
///   ① Normalized / Segments —— 用于在文件树中导航；
///   ② HasTrailingSeparator  —— 原始写法是否以 "/" 结尾，这是"用户想要目录"的语法线索。
/// </summary>
public sealed class VfsPath
{
    /// <summary>用户传入的原始字符串，保留用于日志和报错。</summary>
    public string Original { get; }

    /// <summary>规范化后的绝对路径，例如 "/a/b/c"；根目录固定为 "/"。</summary>
    public string Normalized { get; }

    /// <summary>拆好的段列表，例如 ["a","b","c"]；根目录为空数组。</summary>
    public IReadOnlyList<string> Segments { get; }

    /// <summary>
    /// 原始写法是否以分隔符结尾（"/" 或 "\"）。
    /// 例："/a/b/" → true；"/a/b" → false。
    /// 这是判断"用户意图是目录"的最可靠语法依据。
    /// </summary>
    public bool HasTrailingSeparator { get; }

    /// <summary>是否就是根目录 "/"。</summary>
    public bool IsRoot => Segments.Count == 0;

    /// <summary>最后一段的名字（文件名或目录名）；根目录返回空串。</summary>
    public string Name => IsRoot ? "" : Segments[^1];

    /// <summary>父目录的规范化路径；根目录的父目录仍是 "/"。</summary>
    public string ParentPath
    {
        get
        {
            if (Segments.Count <= 1) return "/";
            var sb = new StringBuilder();
            for (var i = 0; i < Segments.Count - 1; i++)
                sb.Append('/').Append(Segments[i]);
            return sb.ToString();
        }
    }

    private VfsPath(string original, string normalized,
                    string[] segments, bool hasTrailingSeparator)
    {
        Original = original;
        Normalized = normalized;
        Segments = segments;
        HasTrailingSeparator = hasTrailingSeparator;
    }

    /// <summary>
    /// 解析并校验一条路径。这是整个文件系统唯一的"入口校验点"。
    /// </summary>
    /// <param name="raw">用户输入的原始路径，允许用 "/" 或 "\" 作分隔符。</param>
    /// <param name="path">成功时输出解析结果。</param>
    /// <param name="error">失败时输出具体原因。</param>
    public static bool TryParse(string? raw, out VfsPath? path, out PathError error)
    {
        path = null;

        // ── 第 1 步：空值检查 ──
        //   把 null、""、"   " 都视为同一种错误，避免调用方到处写空判断。
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = PathError.Empty;
            return false;
        }

        // ── 第 2 步：总长度粗筛 ──
        //   用原始长度先做一次快速检查，避免对超长输入做昂贵的拆段操作。
        if (raw.Length > PathValidator.MaxPathLength)
        {
            error = PathError.TooLong;
            return false;
        }

        // ── 第 3 步：记录"是否以分隔符结尾" ──
        //   先 TrimEnd 再判断，这样 "dir/ " 这种带尾随空格的写法也能正确识别。
        //   这个信息会传给 VfsPath，用于后续推断用户意图（想建目录）。
        var trimmed = raw.TrimEnd();
        var trailingSep = trimmed.Length > 0 &&
                          (trimmed[^1] == '/' || trimmed[^1] == '\\');

        // ── 第 4 步：逐段拆分、校验、处理 "." 和 ".." ──
        //   这里用栈来模拟目录层级，".." 就是弹栈。
        var stack = new List<string>();

        // 统一把反斜杠当正斜杠，然后用 Split 拆段。
        // SplitOptions.None：保留空段，我们自己在循环里跳过，
        // 这样 "/a//b" 和 "/a/b" 结果一致。
        foreach (var segment in raw.Replace('\\', '/')
                                   .Split('/', StringSplitOptions.None))
        {
            // 空段：来自开头的 "/"、"//"、末尾的 "/"，直接忽略。
            if (segment.Length == 0) continue;

            // "." 表示当前目录，直接忽略。
            if (segment == ".") continue;

            // ".." 表示上一级目录，弹栈。
            if (segment == "..")
            {
                if (stack.Count == 0)
                {
                    // 栈已空还要往上走，说明越过了根目录，这是非法路径。
                    // 例："/../a"、"../../x"
                    error = PathError.EscapeRoot;
                    return false;
                }
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            // 普通段：交给 PathValidator 做字符 / 长度 / 保留名校验。
            if (!PathValidator.IsLegalSegment(segment, out error))
                return false;

            stack.Add(segment);
        }

        // ── 第 5 步：组装结果 ──
        //   "///"、"/./"、"/" 这类都会被规范化成 "/"。
        var normalized = stack.Count == 0
            ? "/"
            : "/" + string.Join('/', stack);

        path = new VfsPath(raw, normalized, stack.ToArray(), trailingSep);
        error = PathError.None;
        return true;
    }

    /// <summary>
    /// 解析路径，失败直接抛异常。适合"路径非法即为程序 bug"的场景。
    /// </summary>
    public static VfsPath Parse(string? raw)
    {
        if (!TryParse(raw, out var path, out var error))
            throw new ArgumentException(PathValidator.Describe(error), nameof(raw));
        return path!;
    }

    /// <summary>
    /// 从已经拆分、并确认合法的段列表直接构造 VfsPath。
    /// 拼接路径时走这条捷径，可以跳过重复的字符串拆分。
    /// </summary>
    internal static VfsPath FromSegments(IReadOnlyList<string> segments, string original)
    {
        var arr = new string[segments.Count];
        for (var i = 0; i < segments.Count; i++) arr[i] = segments[i];

        var normalized = arr.Length == 0 ? "/" : "/" + string.Join('/', arr);
        return new VfsPath(original, normalized, arr, hasTrailingSeparator: false);
    }

    public override string ToString() => Normalized;
}

// ════════════════════════════════════════════════════════════════════
//  四、PathCombiner：路径拼接工具
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 路径拼接与校验工具。
///
/// 设计原则：
///   ① 拼接【不】产生新的路径语法，只把片段摆好；
///   ② 拼接【前】逐段校验，出错能定位到具体片段；
///   ③ 拼接【后】重新解析一次，处理 ".."、"." 和长度；
///   ④ 所有拼接结果都返回 VfsPath，避免调用方再手工 parse。
///
/// 三种典型拼接场景：
///   场景 A：父目录 + 单个名字     → AppendName
///   场景 B：基准路径 + 相对路径   → Resolve
///   场景 C：多片段顺序拼接        → Combine
/// </summary>
public static class PathCombiner
{
    // ────────────────────────────────────────────────
    //  场景 A：父目录 + 单个子名
    // ────────────────────────────────────────────────

    /// <summary>
    /// 把一个子名附加到一个已解析的父路径后面。
    ///
    /// 特点：
    ///   · 父路径已经是 VfsPath（已规范化），无需再解析；
    ///   · 只需校验"子名"这一段的合法性；
    ///   · 结果一定不超过长度限制。
    /// </summary>
    /// <param name="parent">已解析的父路径，例如 VfsPath.Parse("/docs")。</param>
    /// <param name="childName">
    ///   子节点名字，不允许含分隔符（含分隔符请用 Combine）。
    /// </param>
    /// <param name="result">成功时输出拼接结果。</param>
    /// <param name="error">失败时输出原因。</param>
    public static bool AppendName(VfsPath parent, string childName,
                                  out VfsPath? result, out PathError error)
    {
        result = null;

        // 子名为空 → 直接报错，避免拼出 "/docs/" 这种有歧义的东西
        if (string.IsNullOrWhiteSpace(childName))
        { error = PathError.Empty; return false; }

        // 子名里出现分隔符 → 应该用 Combine，而不是 AppendName。
        // 这是常见的调用错误，明确报出来。
        if (childName.IndexOf('/') >= 0 || childName.IndexOf('\\') >= 0)
        { error = PathError.InvalidSegment; return false; }

        // 单段校验
        if (!PathValidator.IsLegalSegment(childName, out error))
            return false;

        // 长度预估：避免拼完再发现超长
        var estimatedLen = parent.Normalized.Length + 1 + childName.Length;
        if (estimatedLen > PathValidator.MaxPathLength)
        { error = PathError.TooLong; return false; }

        // 组装新段列表
        var segments = new List<string>(parent.Segments) { childName };
        result = VfsPath.FromSegments(segments,
                    parent.Normalized + "/" + childName);
        error = PathError.None;
        return true;
    }

    /// <summary>AppendName 的便捷重载：失败抛异常。</summary>
    public static VfsPath AppendName(VfsPath parent, string childName)
    {
        if (!AppendName(parent, childName, out var r, out var e))
            throw new ArgumentException(
                $"拼接失败：{PathValidator.Describe(e)}（片段：\"{childName}\"）");
        return r!;
    }

    // ────────────────────────────────────────────────
    //  场景 B：基准路径 + 相对路径
    // ────────────────────────────────────────────────

    /// <summary>
    /// 以 basePath 为基准，解析一条相对路径。
    ///
    /// 语义和 URL 拼接、POSIX realpath 一样：
    ///   Resolve("/docs/sub", "../a.txt")     → "/docs/a.txt"
    ///   Resolve("/docs",     "./b/c")        → "/docs/b/c"
    ///   Resolve("/docs",     "/etc/x")       → "/etc/x"   ← 绝对路径覆盖基准
    ///   Resolve("/",         "a")            → "/a"
    ///
    /// 实现方式：把 base 的段当作起点，把 relative 的段逐个应用到栈上。
    /// 这样 ".." 在栈空时能立刻发现"越过根"，行为与 VfsPath.TryParse 一致。
    /// </summary>
    public static bool Resolve(string basePath, string relativePath,
                               out VfsPath? result, out PathError error)
    {
        result = null;

        // ① 基准路径必须本身合法
        if (!VfsPath.TryParse(basePath, out var baseP, out error))
            return false;

        if (string.IsNullOrWhiteSpace(relativePath))
        { error = PathError.Empty; return false; }

        // ② 相对路径若以 "/" 或 "\" 开头，等价于绝对路径，忽略基准
        var relTrimmed = relativePath.TrimStart();
        var isAbsolute  = relTrimmed.Length > 0 &&
                         (relTrimmed[0] == '/' || relTrimmed[0] == '\\');

        // ③ 用栈承载当前层级
        var stack = new List<string>();
        if (!isAbsolute)
            stack.AddRange(baseP!.Segments);

        // ④ 逐段应用相对路径
        foreach (var segment in relativePath.Replace('\\', '/')
                                            .Split('/', StringSplitOptions.None))
        {
            if (segment.Length == 0) continue;
            if (segment == ".") continue;

            if (segment == "..")
            {
                if (stack.Count == 0)
                { error = PathError.EscapeRoot; return false; }
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            if (!PathValidator.IsLegalSegment(segment, out error))
                return false;

            stack.Add(segment);
        }

        // ⑤ 长度复检
        var normalized = stack.Count == 0 ? "/" : "/" + string.Join('/', stack);
        if (normalized.Length > PathValidator.MaxPathLength)
        { error = PathError.TooLong; return false; }

        result = VfsPath.FromSegments(stack,
                    isAbsolute ? relativePath : baseP!.Normalized + "/" + relativePath);
        error = PathError.None;
        return true;
    }

    public static VfsPath Resolve(string basePath, string relativePath)
    {
        if (!Resolve(basePath, relativePath, out var r, out var e))
            throw new ArgumentException(
                $"路径拼接失败：{PathValidator.Describe(e)}" +
                $"（base=\"{basePath}\"，rel=\"{relativePath}\"）");
        return r!;
    }

    // ────────────────────────────────────────────────
    //  场景 C：多片段顺序拼接
    // ────────────────────────────────────────────────

    /// <summary>
    /// 把多个片段按顺序拼成一条路径，行为类似 Path.Combine，但更严格。
    ///
    /// 规则：
    ///   ① 任意片段若为绝对路径（以 "/" 开头），会"重置"结果到该绝对路径；
    ///      —— 这一点与 .NET 的 Path.Combine 一致，方便调用方写出
    ///         "默认目录 + 用户可能给绝对路径" 的代码。
    ///   ② 中间的空片段被忽略，避免产生 "//"。
    ///   ③ 每个片段会先按段拆分并校验，出错能定位到具体片段。
    ///   ④ 最后统一走一遍规范化逻辑，处理 "." 和 ".."。
    /// </summary>
    public static bool Combine(out VfsPath? result, out PathError error,
                               params string[] parts)
    {
        result = null;

        if (parts is null || parts.Length == 0)
        { error = PathError.Empty; return false; }

        var stack = new List<string>();

        foreach (var raw in parts)
        {
            if (string.IsNullOrEmpty(raw)) continue;

            // 绝对片段：清空已拼的部分，从根重新开始
            if (raw[0] == '/' || raw[0] == '\\')
                stack.Clear();

            foreach (var segment in raw.Replace('\\', '/')
                                       .Split('/', StringSplitOptions.None))
            {
                if (segment.Length == 0) continue;
                if (segment == ".") continue;

                if (segment == "..")
                {
                    if (stack.Count == 0)
                    { error = PathError.EscapeRoot; return false; }
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                if (!PathValidator.IsLegalSegment(segment, out error))
                    return false;

                stack.Add(segment);
            }

            // 长度检查：早发现早返回，避免大片段继续累积
            var currentLen = stack.Count == 0 ? 1 : 1 + SumLength(stack);
            if (currentLen > PathValidator.MaxPathLength)
            { error = PathError.TooLong; return false; }
        }

        var normalized = stack.Count == 0 ? "/" : "/" + string.Join('/', stack);
        result = VfsPath.FromSegments(stack, normalized);
        error = PathError.None;
        return true;
    }

    public static VfsPath Combine(params string[] parts)
    {
        if (!Combine(out var r, out var e, parts))
            throw new ArgumentException($"路径拼接失败：{PathValidator.Describe(e)}");
        return r!;
    }

    private static int SumLength(List<string> segments)
    {
        var n = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            n += segments[i].Length;
            if (i > 0) n += 1;   // 分隔符
        }
        return n;
    }

    // ────────────────────────────────────────────────
    //  附加：求相对路径（反向操作，校验同样要做）
    // ────────────────────────────────────────────────

    /// <summary>
    /// 求 fromPath 到 toPath 的相对路径，结果只包含合法的段名。
    ///   GetRelative("/docs/a", "/docs/b/c.txt")  → "b/c.txt"
    ///   GetRelative("/docs",   "/docs")          → "."（表示同级）
    ///
    /// 实现：先从两端的段列表中消掉公共前缀，再为剩余的 from 段补 ".."，
    /// 最后接上剩余的 to 段。
    /// </summary>
    public static bool TryGetRelative(string fromPath, string toPath,
                                      out string relative, out PathError error)
    {
        relative = "";

        if (!VfsPath.TryParse(fromPath, out var from, out error)) return false;
        if (!VfsPath.TryParse(toPath,   out var to,   out error)) return false;

        // 消掉公共前缀
        var common = 0;
        var minLen = Math.Min(from!.Segments.Count, to!.Segments.Count);
        while (common < minLen &&
               string.Equals(from.Segments[common], to.Segments[common],
                             StringComparison.OrdinalIgnoreCase))
            common++;

        var parts = new List<string>();

        // 为 from 剩余层级补 ".."
        for (var i = common; i < from.Segments.Count; i++)
            parts.Add("..");

        // 接上 to 剩余层级
        for (var i = common; i < to.Segments.Count; i++)
            parts.Add(to.Segments[i]);

        relative = parts.Count == 0 ? "." : string.Join('/', parts);
        error = PathError.None;
        return true;
    }
}

// ════════════════════════════════════════════════════════════════════
//  五、节点模型
// ════════════════════════════════════════════════════════════════════

/// <summary>节点类型：文件还是目录。</summary>
public enum NodeType { File, Directory }

/// <summary>文件 / 目录的公共基类。</summary>
public abstract class VfsNode
{
    public string Name { get; internal set; } = "";
    public VfsDirectory Parent { get; internal set; }
    public DateTime CreatedAt { get; internal set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; internal set; } = DateTime.UtcNow;

    /// <summary>节点类型。这是判断"路径是文件还是目录"的唯一权威依据。</summary>
    public abstract NodeType Type { get; }

    /// <summary>绝对路径，如 /home/alice/a.txt。</summary>
    public string FullPath
    {
        get
        {
            if (Parent is null) return "/";
            var parts = new List<string>();
            for (VfsNode? n = this; n?.Parent is not null; n = n.Parent)
                parts.Add(n.Name);
            parts.Reverse();
            return "/" + string.Join('/', parts);
        }
    }

    public override string ToString() => $"{Type,-9} {FullPath}";
}

/// <summary>文件节点，内容保存在内存里。</summary>
public sealed class VfsFile : VfsNode
{
    private byte[] _data = Array.Empty<byte>();

    public override NodeType Type => NodeType.File;

    /// <summary>文件字节数。</summary>
    public long Length => _data.Length;

    public byte[] ReadAllBytes() => (byte[])_data.Clone();

    public void WriteAllBytes(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
        ModifiedAt = DateTime.UtcNow;
    }

    public string ReadAllText(Encoding? encoding = null)
        => (encoding ?? Encoding.UTF8).GetString(_data);

    public void WriteAllText(string text, Encoding? encoding = null)
        => WriteAllBytes((encoding ?? Encoding.UTF8).GetBytes(text));

    public Stream OpenRead() => new MemoryStream(_data, writable: false);
}

/// <summary>目录节点，内部用字典保存子节点。</summary>
public sealed class VfsDirectory : VfsNode
{
    // 想区分大小写就换成 StringComparer.Ordinal。
    private readonly Dictionary<string, VfsNode> _children =
        new(StringComparer.OrdinalIgnoreCase);

    public override NodeType Type => NodeType.Directory;

    public int Count => _children.Count;
    public IReadOnlyCollection<VfsNode> Children => _children.Values;

    internal VfsNode Find(string name)
        => _children.GetValueOrDefault(name);

    internal void Attach(VfsNode node) => _children[node.Name] = node;

    internal bool Detach(string name) => _children.Remove(name);
}

// ════════════════════════════════════════════════════════════════════
//  六、类型判断：查询结果 & 意图推断
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 查询一个路径"实际是什么"。
/// </summary>
public enum PathKind
{
    /// <summary>路径不存在。</summary>
    NotFound,

    /// <summary>路径指向一个已存在的文件。</summary>
    File,

    /// <summary>路径指向一个已存在的目录。</summary>
    Directory,
}

/// <summary>
/// 当路径还不存在时，从写法上推断用户意图。
///
/// ⚠ 重要认知：路径字符串本身【不携带】类型信息。
///   类型是存储在节点上的元数据，必须查到节点才能确定。
///   下面这些只是"书写习惯"带来的弱线索，不能当作事实。
/// </summary>
public enum PathIntent
{
    /// <summary>无法判断。</summary>
    Unknown,

    /// <summary>语法上明确表示目录（以 "/" 结尾）。</summary>
    WantDirectory,
}

// ════════════════════════════════════════════════════════════════════
//  七、文件系统主体
// ════════════════════════════════════════════════════════════════════

public sealed class MiniFileSystem
{
    private readonly VfsDirectory _root = new() { Name = "" };

    public VfsDirectory Root => _root;

    // ────────────────────────────────────────────────
    //  A. 查找
    // ────────────────────────────────────────────────

    /// <summary>按已解析的路径查找节点。</summary>
    public VfsNode Find(VfsPath path)
    {
        VfsNode current = _root;

        foreach (var segment in path.Segments)
        {
            // 中途遇到文件，说明路径不可能继续往下走。
            if (current is not VfsDirectory dir) return null;

            if (dir.Find(segment) is not { } child) return null;
            current = child;
        }
        return current;
    }

    /// <summary>按字符串路径查找节点（内部会做合法性校验，非法则抛异常）。</summary>
    public VfsNode Find(string rawPath) => Find(VfsPath.Parse(rawPath));

    public bool Exists(string rawPath)
    {
        // 这里用 TryParse 而不是 Parse：非法路径直接视作"不存在"，而不是崩溃。
        return VfsPath.TryParse(rawPath, out var p, out _) && Find(p!) is not null;
    }

    // ────────────────────────────────────────────────
    //  B. 【重点】判断路径是文件还是目录
    // ────────────────────────────────────────────────

    /// <summary>
    /// 判断一个路径指向的是文件、目录，还是不存在。
    ///
    /// ★ 核心原理：
    ///   路径只是"导航地址"，它本身不含类型信息。
    ///   必须先在树中定位到节点，再读节点的 Type 属性，才能得出类型。
    ///   这就是为什么 Linux 下 `ls -l` 必须真正访问 inode。
    /// </summary>
    public PathKind GetPathKind(string rawPath)
    {
        if (!VfsPath.TryParse(rawPath, out var path, out _))
            return PathKind.NotFound;

        var node = Find(path!);
        if (node is null) return PathKind.NotFound;

        return node.Type == NodeType.File ? PathKind.File : PathKind.Directory;
    }

    /// <summary>
    /// 与 <see cref="GetPathKind"/> 相同，但同时把节点引用带出来，
    /// 避免调用方为了拿到节点而重复查找一次。
    /// </summary>
    public bool TryResolve(string rawPath, out VfsNode? node, out PathKind kind)
    {
        node = null;
        kind = PathKind.NotFound;

        if (!VfsPath.TryParse(rawPath, out var path, out _))
            return false;

        node = Find(path!);
        if (node is null) return false;

        kind = node.Type == NodeType.File ? PathKind.File : PathKind.Directory;
        return true;
    }

    /// <summary>
    /// 【当路径尚不存在时】从写法上推断用户想要创建什么。
    ///
    /// 规则（只有一条真正可靠）：
    ///   "/a/b/"    → 尾随分隔符 → WantDirectory
    ///   "/a/b"     → 无尾随分隔符 → Unknown（"b" 可能是文件也可能是目录）
    ///   "/a/b.txt" → 有扩展名，但这【只是弱提示】，绝不能当判断依据，
    ///                因为 Linux 下完全可以存在名为 "b.txt" 的目录。
    ///
    /// 所以这里刻意不根据扩展名做判断，把扩展名规则留给应用层决定。
    /// </summary>
    public static PathIntent GuessIntent(VfsPath path)
    {
        if (path.HasTrailingSeparator)
            return PathIntent.WantDirectory;

        return PathIntent.Unknown;
    }

    /// <summary>
    /// 【把上面的能力串起来】按意图创建节点：
    ///   ① 路径非法 → 抛异常（由 VfsPath.Parse 完成校验）；
    ///   ② 路径已存在 → 直接返回已有节点；
    ///   ③ 路径不存在 → 按语法推断意图，推断不出来就用调用方给的默认值。
    /// </summary>
    /// <param name="rawPath">用户输入的路径。</param>
    /// <param name="fallback">语法推断不出意图时的兜底选择。</param>
    public VfsNode CreateByIntent(string rawPath,
                                  PathIntent fallback = PathIntent.Unknown,
                                  byte[] content = null)
    {
        // ① 校验并规范化（非法路径在这里就会抛 ArgumentException）
        var path = VfsPath.Parse(rawPath);

        // ② 已存在就直接返回
        if (Find(path) is { } existing) return existing;

        // ③ 推断意图
        var intent = GuessIntent(path);
        if (intent == PathIntent.Unknown) intent = fallback;

        // ④ 按意图创建
        if (intent == PathIntent.WantDirectory)
            return CreateDirectory(path);

        return CreateFile(path, content);
    }

    // ────────────────────────────────────────────────
    //  C. 创建
    // ────────────────────────────────────────────────

    /// <summary>逐级创建目录，行为类似 mkdir -p。</summary>
    public VfsDirectory CreateDirectory(VfsPath path)
    {
        var current = _root;

        foreach (var segment in path.Segments)
        {
            if (current.Find(segment) is { } child)
            {
                // 同名节点存在，但它必须是目录才能继续往下走。
                current = child as VfsDirectory
                    ?? throw new IOException(
                        $"路径中间存在同名文件，无法创建目录: {child.FullPath}");
            }
            else
            {
                var dir = new VfsDirectory { Name = segment, Parent = current };
                current.Attach(dir);
                current.ModifiedAt = DateTime.UtcNow;
                current = dir;
            }
        }
        return current;
    }

    /// <summary>逐级创建目录（字符串重载，内部会做合法性校验）。</summary>
    public VfsDirectory CreateDirectory(string rawPath)
        => CreateDirectory(VfsPath.Parse(rawPath));

    /// <summary>创建文件；父目录必须已存在。</summary>
    public VfsFile CreateFile(VfsPath path, byte[] content = null)
    {
        // 根目录不能变成文件。
        if (path.IsRoot)
            throw new IOException("根目录不能作为文件");

        // 父目录必须存在，且必须是目录。
        if (Find(path.ParentPath) is not VfsDirectory parent)
            throw new DirectoryNotFoundException($"父目录不存在: {path.ParentPath}");

        // 目标位置不能已有同名节点。
        if (parent.Find(path.Name) is not null)
            throw new IOException($"已存在同名节点: {path.Normalized}");

        var file = new VfsFile { Name = path.Name, Parent = parent };
        if (content is not null) file.WriteAllBytes(content);

        parent.Attach(file);
        parent.ModifiedAt = DateTime.UtcNow;
        return file;
    }

    /// <summary>创建文件（字符串重载）。</summary>
    public VfsFile CreateFile(string rawPath, byte[]? content = null)
        => CreateFile(VfsPath.Parse(rawPath), content);

    /// <summary>创建文本文件。</summary>
    public VfsFile CreateFile(string rawPath, string text, Encoding? encoding = null)
        => CreateFile(VfsPath.Parse(rawPath),
                      (encoding ?? Encoding.UTF8).GetBytes(text));

    // ────────────────────────────────────────────────
    //  D. 读取
    // ────────────────────────────────────────────────

    public string ReadAllText(string rawPath, Encoding encoding = null)
    {
        var path = VfsPath.Parse(rawPath);
        if (Find(path) is not VfsFile file)
            throw new FileNotFoundException($"不是文件或不存在: {path.Normalized}");
        return file.ReadAllText(encoding);
    }

    public IReadOnlyCollection<VfsNode> List(string rawPath = "/")
    {
        var path = VfsPath.Parse(rawPath);
        if (Find(path) is not VfsDirectory dir)
            throw new DirectoryNotFoundException($"不是目录或不存在: {path.Normalized}");
        return dir.Children;
    }

    /// <summary>深度优先遍历（默认递归）。</summary>
    public IEnumerable<VfsNode> Walk(string rawPath = "/", bool recursive = true)
    {
        var path = VfsPath.Parse(rawPath);
        if (Find(path) is not VfsDirectory dir)
            throw new DirectoryNotFoundException($"不是目录或不存在: {path.Normalized}");

        return WalkCore(dir, recursive);
    }

    private static IEnumerable<VfsNode> WalkCore(VfsDirectory dir, bool recursive)
    {
        foreach (var child in dir.Children)
        {
            yield return child;
            if (recursive && child is VfsDirectory sub)
                foreach (var n in WalkCore(sub, true))
                    yield return n;
        }
    }

    // ────────────────────────────────────────────────
    //  E. 删除 / 移动 / 复制
    // ────────────────────────────────────────────────

    public bool Delete(string rawPath, bool recursive = true)
    {
        var path = VfsPath.Parse(rawPath);

        if (path.IsRoot)
            throw new IOException("不能删除根目录");

        if (Find(path) is not { } node) return false;

        // 非递归删除时，目录必须为空。
        if (node is VfsDirectory { Count: > 0 } && !recursive)
            throw new IOException($"目录非空，无法删除: {path.Normalized}");

        var parent = node.Parent!;
        parent.Detach(node.Name);
        parent.ModifiedAt = DateTime.UtcNow;
        node.Parent = null;
        return true;
    }

    public VfsNode Move(string rawSource, string rawDestination)
    {
        var src = VfsPath.Parse(rawSource);
        var dst = VfsPath.Parse(rawDestination);

        if (src.IsRoot)
            throw new IOException("不能移动根目录");

        if (src.Normalized == dst.Normalized)
            return Find(src)!;

        var node = Find(src) ?? throw new FileNotFoundException($"源不存在: {src.Normalized}");

        if (Find(dst.ParentPath) is not VfsDirectory destDir)
            throw new DirectoryNotFoundException($"目标目录不存在: {dst.ParentPath}");

        if (destDir.Find(dst.Name) is { } existing)
        {
            if (ReferenceEquals(existing, node)) return node;
            throw new IOException($"目标已存在: {dst.Normalized}");
        }

        // 防止把目录移动到自己的子树中，否则会形成环。
        for (VfsDirectory? p = destDir; p is not null; p = p.Parent)
            if (ReferenceEquals(p, node))
                throw new IOException("不能把目录移动到它自身或其子目录中");

        var oldParent = node.Parent!;
        oldParent.Detach(node.Name);

        node.Name = dst.Name;
        node.Parent = destDir;
        node.ModifiedAt = DateTime.UtcNow;
        destDir.Attach(node);

        oldParent.ModifiedAt = DateTime.UtcNow;
        destDir.ModifiedAt = DateTime.UtcNow;
        return node;
    }

    public VfsNode Copy(string rawSource, string rawDestination)
    {
        var src = VfsPath.Parse(rawSource);
        var dst = VfsPath.Parse(rawDestination);

        var node = Find(src) ?? throw new FileNotFoundException($"源不存在: {src.Normalized}");

        if (Find(dst.ParentPath) is not VfsDirectory destDir)
            throw new DirectoryNotFoundException($"目标目录不存在: {dst.ParentPath}");

        if (destDir.Find(dst.Name) is not null)
            throw new IOException($"目标已存在: {dst.Normalized}");

        if (node is VfsDirectory d && IsSelfOrAncestor(d, destDir))
            throw new IOException("不能把目录复制到它自己的子目录中");

        var clone = Clone(node, dst.Name);
        clone.Parent = destDir;
        destDir.Attach(clone);
        destDir.ModifiedAt = DateTime.UtcNow;
        return clone;
    }

    private static bool IsSelfOrAncestor(VfsNode ancestor, VfsNode? node)
    {
        for (var n = node; n is not null; n = n.Parent)
            if (ReferenceEquals(n, ancestor)) return true;
        return false;
    }

    private static VfsNode Clone(VfsNode src, string name)
    {
        if (src is VfsFile f)
        {
            var nf = new VfsFile { Name = name, CreatedAt = f.CreatedAt };
            nf.WriteAllBytes(f.ReadAllBytes());
            return nf;
        }

        var d = (VfsDirectory)src;
        var nd = new VfsDirectory { Name = name, CreatedAt = d.CreatedAt };
        foreach (var child in d.Children)
        {
            var c = Clone(child, child.Name);
            c.Parent = nd;
            nd.Attach(c);
        }
        return nd;
    }

    // ────────────────────────────────────────────────
    //  F. 统计
    // ────────────────────────────────────────────────

    public (int Files, int Dirs, long Bytes) GetStats()
    {
        int files = 0, dirs = 0;
        long bytes = 0;

        foreach (var n in Walk("/"))
        {
            if (n is VfsFile f) { files++; bytes += f.Length; }
            else dirs++;
        }
        return (files, dirs, bytes);
    }

    // ────────────────────────────────────────────────
    //  G. 持久化（单文件镜像）
    // ────────────────────────────────────────────────

    private const uint Magic = 0x5346564D; // "MVFS"（小端）
    private const int Version = 1;

    public void Save(Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write(_root.Count);
        foreach (var child in _root.Children)
            WriteNode(w, child);
    }

    public static MiniFileSystem Load(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("不是有效的 MVFS 镜像");

        var version = r.ReadInt32();
        if (version != Version) throw new InvalidDataException($"不支持的镜像版本: {version}");

        var fs = new MiniFileSystem();
        var count = r.ReadInt32();
        for (var i = 0; i < count; i++)
            fs._root.Attach(ReadNode(r, fs._root));
        return fs;
    }

    private static void WriteNode(BinaryWriter w, VfsNode node)
    {
        w.Write((byte)node.Type);
        w.Write(node.Name);
        w.Write(node.CreatedAt.Ticks);
        w.Write(node.ModifiedAt.Ticks);

        if (node is VfsFile f)
        {
            var data = f.ReadAllBytes();
            w.Write(data.Length);
            w.Write(data);
        }
        else
        {
            var dir = (VfsDirectory)node;
            w.Write(dir.Count);
            foreach (var c in dir.Children) WriteNode(w, c);
        }
    }

    private static VfsNode ReadNode(BinaryReader r, VfsDirectory parent)
    {
        var type = (NodeType)r.ReadByte();
        var name = r.ReadString();
        var created = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
        var modified = new DateTime(r.ReadInt64(), DateTimeKind.Utc);

        if (type == NodeType.File)
        {
            var len = r.ReadInt32();
            var data = r.ReadBytes(len);
            var file = new VfsFile
            {
                Name = name, Parent = parent,
                CreatedAt = created, ModifiedAt = modified
            };
            file.WriteAllBytes(data);
            file.ModifiedAt = modified; // 恢复写入时间
            return file;
        }

        var dir = new VfsDirectory
        {
            Name = name, Parent = parent,
            CreatedAt = created, ModifiedAt = modified
        };
        var count = r.ReadInt32();
        for (var i = 0; i < count; i++)
            dir.Attach(ReadNode(r, dir));
        return dir;
    }
}

// ════════════════════════════════════════════════════════════════════
//  八、演示程序
// ════════════════════════════════════════════════════════════════════

internal static class Program
{
    private static void Main()
    {
        DemoPathValidation();
        DemoPathCombining();
        DemoKindOfExistingPath();
        DemoIntentOfNewPath();
        DemoCreateByIntent();
        DemoPersistence();
    }

    // ── 演示 1：路径合法性校验与规范化 ──
    private static void DemoPathValidation()
    {
        Console.WriteLine("═══ 演示 1：路径合法性校验与规范化 ═══");

        string[] samples =
        {
            "/home/alice/notes.txt",   // 正常
            "  ",                      // 空白
            "/a//b/./c",               // 多余斜杠 + 当前目录
            "/a/b/../c",               // 回退一级
            "/../escape",              // 越过根目录
            "/a/CON.txt",              // 保留名
            "/a/b.",                   // 以点结尾
            "/a/na|me",                // 非法字符
            "docs/",                   // 相对 + 尾随斜杠
            "/a/.gitignore",           // 隐藏文件（应通过）
            "/a/CONX",                 // 相似但不是保留名（应通过）
        };

        foreach (var s in samples)
        {
            if (VfsPath.TryParse(s, out var p, out var err))
            {
                Console.WriteLine($"  [OK ] \"{s}\"");
                Console.WriteLine(
                    $"         规范化 = {p!.Normalized}，" +
                    $" 段数 = {p.Segments.Count}，" +
                    $" 尾随分隔符 = {p.HasTrailingSeparator}");
            }
            else
            {
                Console.WriteLine($"  [ERR] \"{s}\"  →  {PathValidator.Describe(err)}");
            }
        }
        Console.WriteLine();
    }

    // ── 演示 2：路径拼接 ──
    private static void DemoPathCombining()
    {
        Console.WriteLine("═══ 演示 2：路径拼接 ═══");

        // 场景 A：父目录 + 单个名字
        var parent = VfsPath.Parse("/docs");
        var child = PathCombiner.AppendName(parent, "readme.md");
        Console.WriteLine($"  AppendName(\"/docs\", \"readme.md\")        = {child}");

        // 场景 B：基准路径 + 相对路径
        Console.WriteLine($"  Resolve(\"/docs/sub\", \"../a.txt\")        = " +
                          $"{PathCombiner.Resolve("/docs/sub", "../a.txt")}");
        Console.WriteLine($"  Resolve(\"/docs\", \"/etc/x\")             = " +
                          $"{PathCombiner.Resolve("/docs", "/etc/x")}");
        Console.WriteLine($"  Resolve(\"/\", \"a\")                      = " +
                          $"{PathCombiner.Resolve("/", "a")}");

        // 场景 C：多片段拼接
        Console.WriteLine($"  Combine(\"/root\", \"user\", \"f.txt\")      = " +
                          $"{PathCombiner.Combine("/root", "user", "f.txt")}");
        Console.WriteLine($"  Combine(\"/a\", \"/b\", \"c\")              = " +
                          $"{PathCombiner.Combine("/a", "/b", "c")}  ← 绝对片段重置");

        // 反向：求相对路径
        PathCombiner.TryGetRelative("/docs/a", "/docs/b/c.txt", out var rel, out _);
        Console.WriteLine($"  TryGetRelative(\"/docs/a\", \"/docs/b/c.txt\") = {rel}");

        // 拼接中的错误：子名带分隔符
        if (!PathCombiner.AppendName(parent, "a/b", out _, out var e2))
            Console.WriteLine($"  AppendName(\"/docs\", \"a/b\") 失败 → {PathValidator.Describe(e2)}");

        // 拼接中的错误：越根
        if (!PathCombiner.Combine(out _, out var e3, "/", "..", "x"))
            Console.WriteLine($"  Combine(\"/\", \"..\", \"x\") 失败 → {PathValidator.Describe(e3)}");

        Console.WriteLine();
    }

    // ── 演示 3：已存在路径 → 判断是文件还是目录 ──
    private static void DemoKindOfExistingPath()
    {
        Console.WriteLine("═══ 演示 3：已存在路径的类型判断（查节点读 Type）═══");

        var fs = new MiniFileSystem();
        fs.CreateDirectory("/docs");
        fs.CreateDirectory("/docs/images");
        fs.CreateFile("/docs/readme.md", "Hello, VFS!");

        string[] queries =
        {
            "/docs",              // 目录
            "/docs/readme.md",    // 文件
            "/docs/images",       // 目录
            "/docs/nothing",      // 不存在
            "/docs/readme.md/x",  // 拿文件当目录用 → 不存在
        };

        foreach (var q in queries)
        {
            var kind = fs.GetPathKind(q);
            Console.WriteLine($"  {q,-22} → {kind}");
        }

        // 顺带展示 TryResolve 能一次拿到节点引用
        if (fs.TryResolve("/docs/readme.md", out var node, out var k))
            Console.WriteLine($"\n  TryResolve 结果: kind = {k}，" +
                              $"内容 = \"{((VfsFile)node!).ReadAllText()}\"");

        Console.WriteLine();
    }

    // ── 演示 4：不存在的路径 → 从写法推断意图 ──
    private static void DemoIntentOfNewPath()
    {
        Console.WriteLine("═══ 演示 4：尚不存在的路径的意图推断 ═══");

        string[] candidates =
        {
            "/docs/sub/",      // 尾随斜杠 → 明确想建目录
            "/docs/a.txt",     // 有扩展名，但只是弱提示
            "/docs/plain",     // 无任何提示
        };

        foreach (var c in candidates)
        {
            var p = VfsPath.Parse(c);
            var intent = MiniFileSystem.GuessIntent(p);
            Console.WriteLine(
                $"  {c,-16} → 尾随斜杠 = {p.HasTrailingSeparator,-5}， 推断意图 = {intent}");
        }

        Console.WriteLine("  说明：只有尾随斜杠是可靠线索；扩展名不能作为判断依据。");
        Console.WriteLine();
    }

    // ── 演示 5：按意图自动创建 ──
    private static void DemoCreateByIntent()
    {
        Console.WriteLine("═══ 演示 5：CreateByIntent —— 校验 + 推断 + 创建 一条龙 ═══");

        var fs = new MiniFileSystem();

        fs.CreateByIntent("/logs/");                    // 尾随斜杠 → 建目录
        fs.CreateByIntent("/logs/app.txt",
                          PathIntent.Unknown,
                          Encoding.UTF8.GetBytes("line 1"));  // 无斜杠 → 走 fallback

        // 无斜杠、无扩展名 → fallback 为 Unknown 时按文件处理
        fs.CreateByIntent("/logs/archive");

        Console.WriteLine("  创建后的目录树：");
        foreach (var n in fs.Walk("/"))
            Console.WriteLine($"    {n}");

        var (files, dirs, bytes) = fs.GetStats();
        Console.WriteLine($"\n  统计：{files} 个文件，{dirs} 个目录，共 {bytes} 字节");
        Console.WriteLine();
    }

    // ── 演示 6：持久化 ──
    private static void DemoPersistence()
    {
        Console.WriteLine("═══ 演示 6：持久化（保存 / 加载镜像）═══");

        var fs = new MiniFileSystem();
        fs.CreateDirectory("/data/logs");
        fs.CreateFile("/data/logs/app.log", "2024-01-01 启动");
        fs.CreateFile("/data/readme.md", "示例文件");

        // 保存到内存流
        using var ms = new MemoryStream();
        fs.Save(ms);
        Console.WriteLine($"  已保存，镜像大小 = {ms.Length} 字节");

        // 重新加载
        ms.Position = 0;
        var fs2 = MiniFileSystem.Load(ms);

        Console.WriteLine("  重新加载后的目录树：");
        foreach (var n in fs2.Walk("/"))
            Console.WriteLine($"    {n}");

        var (files, dirs, bytes) = fs2.GetStats();
        Console.WriteLine($"\n  统计：{files} 个文件，{dirs} 个目录，共 {bytes} 字节");
    }
}