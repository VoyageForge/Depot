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
        /// 解析并校验一条路径。这是整个文件系统唯一的"入口校验点"。
        ///
        /// 处理流程：
        ///   ① 空值检查 → <see cref="PathError.Empty"/>；
        ///   ② 总长度粗筛，避免对超长输入做昂贵的拆段；
        ///   ③ 记录"是否以分隔符结尾"（先 Trim 再判断）；
        ///   ④ 逐段拆分、校验，用栈处理 "." / ".."；
        ///   ⑤ 组装规范化的绝对路径。
        /// </summary>
        /// <param name="raw">用户输入的原始路径，允许用 "/" 或 "\" 作分隔符。</param>
        /// <param name="path">成功时输出解析结果。</param>
        /// <param name="error">失败时输出具体原因。</param>
        /// <returns>解析成功返回 true。</returns>
        public static bool TryParse(string raw, out VfsPath path, out PathError error)
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
        public static VfsPath Parse(string raw)
        {
            if (!TryParse(raw, out var path, out var error))
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
