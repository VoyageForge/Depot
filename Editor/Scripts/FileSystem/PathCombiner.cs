using System;
using System.Collections.Generic;

namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 路径拼接与校验工具。
    ///
    /// 设计原则：
    ///   ① 拼接【不】产生新的路径语法，只把片段摆好；
    ///   ② 拼接【前】逐段校验，出错能定位到具体片段；
    ///   ③ 拼接【后】统一处理 ".."、"." 和长度；
    ///   ④ 所有拼接结果都返回 <see cref="VfsPath"/>，避免调用方再手工 Parse。
    ///
    /// 三种典型拼接场景：
    ///   场景 A：父目录 + 单个名字     → <see cref="AppendName(VfsPath, string)"/>
    ///   场景 B：基准路径 + 相对路径   → <see cref="Resolve(string, string)"/>
    ///   场景 C：多片段顺序拼接        → <see cref="Combine(string[])"/>
    /// </summary>
    public static class PathCombiner
    {
        // ════════════════════════════════════════════════════════════
        //  场景 A：父目录 + 单个子名
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 把一个子名附加到一个已解析的父路径后面。
        ///
        /// 特点：
        ///   · 父路径已经是 VfsPath（已规范化），无需再解析；
        ///   · 只需校验"子名"这一段的合法性；
        ///   · 结果一定不超过长度限制。
        /// </summary>
        /// <param name="parent">已解析的父路径，例如 VfsPath.Parse("/docs")。</param>
        /// <param name="childName">子节点名字，不允许含分隔符（含分隔符请用 Combine）。</param>
        /// <param name="result">成功时输出拼接结果。</param>
        /// <param name="error">失败时输出原因。</param>
        /// <returns>拼接成功返回 true。</returns>
        public static bool AppendName(VfsPath parent, string childName,
                                      out VfsPath result, out PathError error)
        {
            result = null;

            if (parent is null)
            { error = PathError.Empty; return false; }

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
            result = VfsPath.FromSegments(segments, parent.Normalized + "/" + childName);
            error = PathError.None;
            return true;
        }

        /// <summary>AppendName 的便捷重载：失败抛异常。</summary>
        /// <param name="parent">已解析的父路径。</param>
        /// <param name="childName">子节点名字。</param>
        /// <returns>拼接结果。</returns>
        /// <exception cref="ArgumentException">拼接失败时抛出。</exception>
        public static VfsPath AppendName(VfsPath parent, string childName)
        {
            if (!AppendName(parent, childName, out var result, out var error))
                throw new ArgumentException(
                    $"拼接失败：{PathValidator.Describe(error)}（片段：\"{childName}\"）");
            return result;
        }

        // ════════════════════════════════════════════════════════════
        //  场景 B：基准路径 + 相对路径
        // ════════════════════════════════════════════════════════════

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
        /// 这样 ".." 在栈空时能立刻发现"越过根"，行为与 <see cref="VfsPath.TryParse"/> 一致。
        /// </summary>
        /// <param name="basePath">基准路径，本身必须合法。</param>
        /// <param name="relativePath">相对路径；以 "/" 或 "\" 开头时视为绝对路径。</param>
        /// <param name="result">成功时输出解析结果。</param>
        /// <param name="error">失败时输出原因。</param>
        /// <returns>解析成功返回 true。</returns>
        public static bool Resolve(string basePath, string relativePath,
                                   out VfsPath result, out PathError error)
        {
            result = null;

            // ① 基准路径必须本身合法
            if (!VfsPath.TryParse(basePath, out var baseP, out error))
                return false;

            if (string.IsNullOrWhiteSpace(relativePath))
            { error = PathError.Empty; return false; }

            // ② 统一去空白，并以同一份文本做"是否绝对路径"的判断与拆段，
            //    避免"用裁剪后的文本判绝对、却用原文拆段"造成的不一致。
            var relTrimmed = relativePath.Trim();
            var isAbsolute = relTrimmed.Length > 0 &&
                             (relTrimmed[0] == '/' || relTrimmed[0] == '\\');

            // ③ 用栈承载当前层级：绝对路径从根开始，相对路径从基准路径开始。
            var stack = new List<string>();
            if (!isAbsolute)
                stack.AddRange(baseP.Segments);

            // ④ 逐段应用相对路径
            foreach (var segment in relTrimmed.Replace('\\', '/')
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
            var normalized = stack.Count == 0 ? "/" : "/" + string.Join("/", stack);
            if (normalized.Length > PathValidator.MaxPathLength)
            { error = PathError.TooLong; return false; }

            result = VfsPath.FromSegments(stack,
                isAbsolute ? relTrimmed : baseP.Normalized + "/" + relTrimmed);
            error = PathError.None;
            return true;
        }

        /// <summary>Resolve 的便捷重载：失败抛异常。</summary>
        /// <param name="basePath">基准路径。</param>
        /// <param name="relativePath">相对路径。</param>
        /// <returns>解析结果。</returns>
        /// <exception cref="ArgumentException">解析失败时抛出。</exception>
        public static VfsPath Resolve(string basePath, string relativePath)
        {
            if (!Resolve(basePath, relativePath, out var result, out var error))
                throw new ArgumentException(
                    $"路径拼接失败：{PathValidator.Describe(error)}" +
                    $"（base=\"{basePath}\"，rel=\"{relativePath}\"）");
            return result;
        }

        // ════════════════════════════════════════════════════════════
        //  场景 C：多片段顺序拼接
        // ════════════════════════════════════════════════════════════

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
        /// <param name="result">成功时输出拼接结果。</param>
        /// <param name="error">失败时输出原因。</param>
        /// <param name="parts">按顺序拼接的片段。</param>
        /// <returns>拼接成功返回 true。</returns>
        public static bool Combine(out VfsPath result, out PathError error,
                                   params string[] parts)
        {
            result = null;

            if (parts is null || parts.Length == 0)
            { error = PathError.Empty; return false; }

            var stack = new List<string>();

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;

                // 与 VfsPath.TryParse 一致：先去掉首尾空白再拆段。
                var raw = part.Trim();

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

            var normalized = stack.Count == 0 ? "/" : "/" + string.Join("/", stack);
            result = VfsPath.FromSegments(stack, normalized);
            error = PathError.None;
            return true;
        }

        /// <summary>Combine 的便捷重载：失败抛异常。</summary>
        /// <param name="parts">按顺序拼接的片段。</param>
        /// <returns>拼接结果。</returns>
        /// <exception cref="ArgumentException">拼接失败时抛出。</exception>
        public static VfsPath Combine(params string[] parts)
        {
            if (!Combine(out var result, out var error, parts))
                throw new ArgumentException($"路径拼接失败：{PathValidator.Describe(error)}");
            return result;
        }

        /// <summary>把若干段名的总字符数加起来（含段间分隔符，不含开头那个 '/'）。</summary>
        /// <param name="segments">段列表。</param>
        /// <returns>各段长度之和加上分隔符数量。</returns>
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

        // ════════════════════════════════════════════════════════════
        //  附加：求相对路径（反向操作，校验同样要做）
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 求 fromPath 到 toPath 的相对路径，结果只包含合法的段名。
        ///   TryGetRelative("/docs/sub", "/docs/sub/a.txt") → "a.txt"（在其下，无需回退）
        ///   TryGetRelative("/docs/a",   "/docs/b/c.txt")   → "../b/c.txt"（先退回 /docs 再往下）
        ///   TryGetRelative("/a/b/c",    "/a/x")            → "../../x"
        ///   TryGetRelative("/docs",     "/docs")           → "."（表示同级）
        ///
        /// 注意语义：fromPath 被当作【一个要从中出发的目录】，
        /// 所以 "/docs/a" 会被先回退掉，而不是当成同级兄弟节点。
        ///
        /// 实现：先从两端的段列表中消掉公共前缀，再为剩余的 from 段补 ".."，
        /// 最后接上剩余的 to 段。比较段名时忽略大小写，与虚拟文件系统的
        /// "目录查找忽略大小写"策略保持一致。
        /// </summary>
        /// <param name="fromPath">起点路径。</param>
        /// <param name="toPath">终点路径。</param>
        /// <param name="relative">成功时输出相对路径。</param>
        /// <param name="error">失败时输出原因。</param>
        /// <returns>计算成功返回 true。</returns>
        public static bool TryGetRelative(string fromPath, string toPath,
                                          out string relative, out PathError error)
        {
            relative = "";

            if (!VfsPath.TryParse(fromPath, out var from, out error)) return false;
            if (!VfsPath.TryParse(toPath, out var to, out error)) return false;

            // 消掉公共前缀。按 Ordinal 比较，与虚拟节点表的"区分大小写"策略一致：
            // "/Docs/a" 与 "/docs/a" 是两个不同的路径，公共前缀只有根。
            var common = 0;
            var minLen = Math.Min(from.Segments.Count, to.Segments.Count);
            while (common < minLen &&
                   string.Equals(from.Segments[common], to.Segments[common],
                                 StringComparison.Ordinal))
                common++;

            var parts = new List<string>();

            // 为 from 剩余层级补 ".."
            for (var i = common; i < from.Segments.Count; i++)
                parts.Add("..");

            // 接上 to 剩余层级
            for (var i = common; i < to.Segments.Count; i++)
                parts.Add(to.Segments[i]);

            relative = parts.Count == 0 ? "." : string.Join("/", parts);
            error = PathError.None;
            return true;
        }
    }
}
