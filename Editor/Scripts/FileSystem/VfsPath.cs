using System;
using System.Collections.Generic;
using System.Text;

namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 一条已经通过合法性校验、并完成规范化的路径。
    ///
    /// 它同时承载两个关键信息：
    ///   ① <see cref="Normalized"/> / <see cref="Segments"/> —— 用于在文件树中导航；
    ///   ② <see cref="HasTrailingSeparator"/> —— 原始写法是否以 "/" 结尾，
    ///      这是"用户想要目录"的语法线索。
    ///
    /// 职责边界：本类只负责【整条路径】的结构（拆段、"." / ".."、越界判断），
    ///           单个段名的字符 / 长度 / 保留名规则交给 <see cref="PathValidator"/>。
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

        /// <summary>
        /// 本路径是否"爬到了根目录之外"，即规范化后以 ".." 开头。
        /// 只有在解析时允许越过根（<c>allowEscapeRoot: true</c>）才可能出现：
        /// 此时越界的 ".." 会被原样保留在段列表最前面，例如 "../x" → 段为 ["..","x"]。
        ///
        /// 这类路径在根目录之内的树里永远查不到节点，
        /// 它的意义是"能把根目录之外的路径表达出来"（配合
        /// <see cref="VirtualFileSystem.TryGetRealPath"/> 换算出真实磁盘路径）。
        /// </summary>
        public bool EscapesRoot => Segments.Count > 0 && Segments[0] == "..";

        /// <summary>最后一段的名字（文件名或目录名）；根目录返回空串。</summary>
        public string Name => IsRoot ? "" : Segments[Segments.Count - 1];

        /// <summary>
        /// 父目录的规范化路径；根目录的父目录仍是 "/"。
        /// 例："/a/b/c" → "/a/b"；"/a" → "/"；"/" → "/"。
        /// </summary>
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

        /// <summary>私有构造：外部只能通过 <see cref="TryParse"/> / <see cref="Parse"/> 创建，
        /// 以保证"每个 VfsPath 实例一定已经规范化且合法"。</summary>
        private VfsPath(string original, string normalized,
                        string[] segments, bool hasTrailingSeparator)
        {
            Original = original;
            Normalized = normalized;
            Segments = segments;
            HasTrailingSeparator = hasTrailingSeparator;
        }

        /// <summary>
        /// 解析并校验一条路径，并要求结果必须落在根目录之内。
        /// 这是最常用的重载，等价于 <c>TryParse(raw, allowEscapeRoot: false, ...)</c>。
        /// </summary>
        /// <param name="raw">用户输入的原始路径，允许用 "/" 或 "\" 作分隔符。</param>
        /// <param name="path">成功时输出解析结果。</param>
        /// <param name="error">失败时输出具体原因。</param>
        /// <returns>解析成功返回 true。</returns>
        public static bool TryParse(string raw, out VfsPath path, out PathError error)
            => TryParse(raw, RootEscapeMode.Error, out path, out error);

        /// <summary>
        /// 解析并校验一条路径。这是整个文件系统唯一的"入口校验点"。
        ///
        /// 处理流程：
        ///   ① 空值检查 → <see cref="PathError.Empty"/>；
        ///   ② 总长度粗筛，避免对超长输入做昂贵的拆段；
        ///   ③ 记录"是否以分隔符结尾"（先 Trim 再判断）；
        ///   ④ 逐段拆分、校验，用栈处理 "." / ".."；
        ///   ⑤ 组装规范化的绝对路径。
        ///
        /// <paramref name="escapeMode"/> 控制 ".." 爬到根之上时的行为，
        /// 三种取值见 <see cref="RootEscapeMode"/>：
        ///   · <see cref="RootEscapeMode.Clamp"/>：忽略越界的 ".."，停在根目录；
        ///   · <see cref="RootEscapeMode.Error"/>：报 <see cref="PathError.EscapeRoot"/>，
        ///     于是任何解析成功的路径都保证在根之内；
        ///   · <see cref="RootEscapeMode.Escape"/>：把越界的 ".." 原样保留为段
        ///     （"../x" → ["..","x"]），解析成功但 <see cref="EscapesRoot"/> 为 true。
        ///
        /// 注意本重载的"单参数版"<see cref="TryParse(string, out VfsPath, out PathError)"/>
        /// 用的是最严格的 <see cref="RootEscapeMode.Error"/>：它是最底层的解析原语，
        /// 保持"解析成功即一定在根内"这个强约束；要 OS 式夹取请显式传
        /// <see cref="RootEscapeMode.Clamp"/>（<see cref="VirtualFileSystem"/> 就是这么做的）。
        /// </summary>
        /// <param name="raw">用户输入的原始路径，允许用 "/" 或 "\" 作分隔符。</param>
        /// <param name="escapeMode">".." 越过根目录时的处理方式。</param>
        /// <param name="path">成功时输出解析结果。</param>
        /// <param name="error">失败时输出具体原因。</param>
        /// <returns>解析成功返回 true。</returns>
        public static bool TryParse(string raw, RootEscapeMode escapeMode,
                                    out VfsPath path, out PathError error)
        {
            path = null;

            // 第 1 步：空值检查。
            //   把 null、""、"   " 都视为同一种错误，避免调用方到处写空判断。
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = PathError.Empty;
                return false;
            }

            // 第 2 步：统一去掉首尾空白后再处理。
            //   统一 Trim 是刻意的：用户经常把带空格的路径粘进来，
            //   而段名"以空格结尾"本身在 PathValidator 里是非法的。
            //   这里先统一裁剪，可以让"整条路径首尾多空格"和"段名尾随空格"
            //   这两件事不再互相干扰，行为保持一致。
            var trimmed = raw.Trim();

            // 第 3 步：总长度粗筛（用裁剪后的长度，避免对超长输入做昂贵的拆段）。
            if (trimmed.Length > PathValidator.MaxPathLength)
            {
                error = PathError.TooLong;
                return false;
            }

            // 第 4 步：记录"是否以分隔符结尾"。
            //   这个信息会传给 VfsPath，用于后续推断用户意图（想建目录）。
            var trailingSep = trimmed.Length > 0 &&
                              (trimmed[trimmed.Length - 1] == '/' ||
                               trimmed[trimmed.Length - 1] == '\\');

            // 第 5 步：逐段拆分、校验、处理 "." 和 ".."。
            //   用栈模拟目录层级，".." 就是弹栈。
            var stack = new List<string>();

            // 栈里"已经越界"的 ".." 个数（它们永远排在栈的最前面）。
            // 为什么要单独记？因为这些 ".." 代表"根目录之外"，不能再被后面的
            // ".." 弹掉——否则 "../../x" 会被算成 "../x"，越界的层数就丢了。
            // 判据：栈里"根内的层级"数量 = stack.Count - escaped。
            var escaped = 0;

            // 统一把反斜杠当正斜杠，然后用 Split 拆段。
            // SplitOptions.None：保留空段，我们自己在循环里跳过，
            // 这样 "/a//b" 和 "/a/b" 结果一致。
            foreach (var segment in trimmed.Replace('\\', '/')
                                           .Split('/', StringSplitOptions.None))
            {
                // 空段：来自开头的 "/"、"//"、末尾的 "/"，直接忽略。
                if (segment.Length == 0) continue;

                // "." 表示当前目录，直接忽略。
                if (segment == ".") continue;

                // ".." 表示上一级目录。
                if (segment == "..")
                {
                    // 栈里已经没有"根内的层级"可弹了，说明再往上就要越过根目录。
                    // 例："/../a"、"../../x"
                    if (stack.Count == escaped)
                    {
                        switch (escapeMode)
                        {
                            case RootEscapeMode.Clamp:
                                // 夹取：与 Windows 的 "C:\.." 一样，停在根目录原地不动。
                                // 这里既不压栈也不弹栈，于是 ".." 被"吃掉"，
                                // "/.." → "/"、"../../x" → "/x"，
                                // 与 "C:\..\..\Windows" → "C:\Windows" 完全同构。
                                continue;

                            case RootEscapeMode.Escape:
                                // 允许越过根时，把越界的 ".." 原样压栈保留下来，
                                // 这样"越出去几层"不会丢失，交给上层去换算真实路径。
                                stack.Add("..");
                                escaped++;
                                continue;

                            default:
                                error = PathError.EscapeRoot;
                                return false;
                        }
                    }

                    // 还有根内的层级：正常弹栈，退到上一级。
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                // 普通段：交给 PathValidator 做字符 / 长度 / 保留名校验。
                if (!PathValidator.IsLegalSegment(segment, out error))
                    return false;

                stack.Add(segment);
            }

            // 第 6 步：组装结果。
            //   "///"、"/./"、"/" 这类都会被规范化成 "/"。
            var normalized = stack.Count == 0
                ? "/"
                : "/" + string.Join("/", stack);

            path = new VfsPath(raw, normalized, stack.ToArray(), trailingSep);
            error = PathError.None;
            return true;
        }

        /// <summary>
        /// 解析路径，失败直接抛异常。适合"路径非法即为程序 bug"的场景。
        /// </summary>
        /// <param name="raw">用户输入的原始路径。</param>
        /// <returns>解析结果。</returns>
        /// <exception cref="ArgumentException">路径非法时抛出，消息为中文错误说明。</exception>
        public static VfsPath Parse(string raw) => Parse(raw, RootEscapeMode.Error);

        /// <summary>
        /// 解析路径，失败直接抛异常；可指定 ".." 越过根目录时的处理方式。
        /// </summary>
        /// <param name="raw">用户输入的原始路径。</param>
        /// <param name="escapeMode">".." 越过根目录时的处理方式。</param>
        /// <returns>解析结果。</returns>
        /// <exception cref="ArgumentException">路径非法时抛出，消息为中文错误说明。</exception>
        public static VfsPath Parse(string raw, RootEscapeMode escapeMode)
        {
            if (!TryParse(raw, escapeMode, out var path, out var error))
                throw new ArgumentException(PathValidator.Describe(error), nameof(raw));
            return path;
        }

        /// <summary>
        /// 从已经拆分、并确认合法的段列表直接构造 VfsPath。
        /// 拼接路径时走这条捷径，可以跳过重复的字符串拆分与逐段校验。
        /// </summary>
        /// <param name="segments">已经过校验的段列表。</param>
        /// <param name="original">用于日志和报错的原始写法。</param>
        internal static VfsPath FromSegments(IReadOnlyList<string> segments, string original)
        {
            var arr = new string[segments.Count];
            for (var i = 0; i < segments.Count; i++) arr[i] = segments[i];

            var normalized = arr.Length == 0 ? "/" : "/" + string.Join("/", arr);
            return new VfsPath(original, normalized, arr, hasTrailingSeparator: false);
        }

        /// <summary>返回规范化路径，便于日志和调试输出。</summary>
        public override string ToString() => Normalized;
    }
}
