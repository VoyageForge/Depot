using System;
using System.IO;

namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 针对【真实操作系统磁盘】的路径工具。
    ///
    /// 与 <see cref="VfsPath"/> / <see cref="PathCombiner"/> 的区别：
    ///   · 本类操作的是真实存在的文件与目录，判定类型要靠
    ///     <see cref="File.GetAttributes(string)"/> 去问操作系统；
    ///   · 那两者操作的是内存里的虚拟树，判定类型只读节点属性，不碰磁盘。
    ///
    /// 本类刻意不依赖 UnityEngine，因此可以脱离 Unity 环境单测。
    /// </summary>
    public static class DiskPath
    {
        /// <summary>
        /// 判断路径是否是"完全绝对路径"。
        /// 例：Windows 下 "C:\a"、"\\server\share" 为 true，".\a"、"a\b" 为 false。
        /// </summary>
        /// <param name="path">待判断的路径。</param>
        /// <returns>是绝对路径返回 true。</returns>
        public static bool IsFullyAbsolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return Path.IsPathRooted(path);
        }

        /// <summary>
        /// 把路径规范化为绝对路径（会解析 "." / ".." 并统一分隔符）。
        /// 依赖当前工作目录，因此相对路径的解析结果可能随进程状态变化。
        /// </summary>
        /// <param name="path">待规范化的路径。</param>
        /// <returns>绝对路径。</returns>
        public static string ToAbsolute(string path) => Path.GetFullPath(path);

        /// <summary>
        /// 判断 childPath 是否位于 parentPath 之下（含两者相等的情况），
        /// 并顺带算出相对路径，避免调用方再算一次。
        ///
        /// 实现基于 <see cref="Path.GetRelativePath(string, string)"/>：
        /// 相对结果为 "." 表示二者相同；以 ".." 开头（或本身又是绝对路径，
        /// 例如跨盘符）则表示不在其下。
        /// </summary>
        /// <param name="childPath">子路径。</param>
        /// <param name="parentPath">父路径，必须是绝对路径。</param>
        /// <param name="relative">输出相对于 parentPath 的相对路径。</param>
        /// <returns>childPath 在 parentPath 之下（或相等）返回 true。</returns>
        public static bool IsSubPathOf(string childPath, string parentPath, out string relative)
        {
            relative = "";

            if (string.IsNullOrWhiteSpace(childPath) ||
                string.IsNullOrWhiteSpace(parentPath))
                return false;

            // 先规范化为绝对路径，防止调用方传入未解析的相对路径。
            var fullChild = Path.GetFullPath(childPath);
            var fullParent = Path.GetFullPath(parentPath);

            var rel = Path.GetRelativePath(fullParent, fullChild);
            relative = rel;

            // 跨盘符时 GetRelativePath 会直接返回绝对路径，用 IsPathRooted 兜住。
            return !rel.StartsWith("..", StringComparison.Ordinal) &&
                   !Path.IsPathRooted(rel);
        }

        /// <summary>
        /// 把一条【输入路径】换算成相对于 rootPath 的写法，供虚拟路径层继续解析。
        ///
        /// 判定规则（顺序很重要）：
        ///   ① 带卷标 / UNC 的绝对路径（如 <c>D:\Proj\Assets\a\x</c>、<c>\\srv\share\x</c>）
        ///      —— 按真实磁盘路径处理：落在 rootPath 之内则换算成 <c>a/x</c>；
        ///         落在 rootPath 之外则报 <see cref="PathError.OutsideRoot"/>。
        ///   ② 其余一切写法（<c>/a/x</c>、<c>a/x</c>、<c>./a/x</c>）
        ///      —— 一律视为【相对于根目录】，即去掉前导分隔符后就是 <c>a/x</c>。
        ///         这样 "/a/x" 指的是 &lt;root&gt;/a/x，而不是当前盘符根目录下的 a/x。
        ///
        /// 为什么要把 "/a/x" 当相对路径？因为在虚拟路径空间里根就是 "/"，
        /// 浏览器地址栏里的 "/a/x" 表达的就是"从根往下走"，这与
        /// <see cref="VfsPath"/> 的规范化结果保持一致。
        /// </summary>
        /// <param name="rootPath">根目录的真实路径。</param>
        /// <param name="rawPath">用户输入的原始路径。</param>
        /// <param name="escapeMode">
        /// 根目录之外的【绝对路径】如何处理。注意本参数只影响这一类输入——
        /// 用 ".." 往上爬的越界由 <see cref="VfsPath"/> 按同一个模式处理：
        ///   · <see cref="RootEscapeMode.Escape"/>：换算成"先从根退出去"的相对写法
        ///     （如 <c>..\Other\a.txt</c>）；
        ///   · 其余模式（<see cref="RootEscapeMode.Clamp"/> 与
        ///     <see cref="RootEscapeMode.Error"/>）：报 <see cref="PathError.OutsideRoot"/>。
        ///     这里【不】做夹取：夹取的含义是"'..' 到顶了停在根目录"，
        ///     而"指名道姓给出根外的绝对路径"是另一回事，静默改写成根目录会丢信息。
        /// </param>
        /// <param name="rootRelative">
        /// 输出相对于根目录的路径（统一用系统分隔符）；
        /// 输入就是根目录自身时输出 "."。
        /// </param>
        /// <param name="error">失败时输出原因。</param>
        /// <returns>换算成功返回 true。</returns>
        public static bool TryMakeRelativeToRoot(string rootPath, string rawPath,
                                                RootEscapeMode escapeMode,
                                                out string rootRelative, out PathError error)
        {
            rootRelative = "";
            error = PathError.None;

            if (string.IsNullOrWhiteSpace(rootPath) ||
                string.IsNullOrWhiteSpace(rawPath))
            { error = PathError.Empty; return false; }

            var raw = rawPath.Trim();

            // ① 明确的绝对路径（有盘符或 UNC）——按真实磁盘路径处理，并做越界校验。
            if (HasVolumeRoot(raw))
            {
                string full;
                try
                {
                    full = Path.GetFullPath(raw);
                }
                catch (ArgumentException)
                { error = PathError.IllegalChar; return false; }
                catch (NotSupportedException)
                { error = PathError.IllegalChar; return false; }
                catch (PathTooLongException)
                { error = PathError.TooLong; return false; }

                string relative;
                if (!IsSubPathOf(full, rootPath, out relative))
                {
                    if (escapeMode != RootEscapeMode.Escape)
                    { error = PathError.OutsideRoot; return false; }

                    // 允许越界：算出"从根目录退出去"的相对写法，
                    // 交给 VfsPath 把前导的 ".." 保留成越界记号。
                    // 跨盘符时 GetRelativePath 会返回绝对路径，那种情况无法用
                    // 相对写法表达，会在 VfsPath 里因段内出现 ':' 而报 IllegalChar。
                    relative = Path.GetRelativePath(Path.GetFullPath(rootPath), full);
                }

                // GetRelativePath 对"根目录自身"返回 "."，这正是我们想要的表示法。
                rootRelative = relative;
                return true;
            }

            // ② 其余写法一律相对根目录：去掉前导分隔符。
            //    全部剥光说明输入就是根目录本身（例如 "/"），用 "." 表示，
            //    与 Path.GetRelativePath 的约定保持一致。
            var stripped = raw.TrimStart('/', '\\');
            if (stripped.Length == 0)
            {
                rootRelative = ".";
                return true;
            }

            // 统一成系统分隔符，让返回值只有一种写法，调用方不必两种都处理。
            rootRelative = stripped.Replace('/', Path.DirectorySeparatorChar)
                                   .Replace('\\', Path.DirectorySeparatorChar);
            return true;
        }

        /// <summary>
        /// <see cref="TryMakeRelativeToRoot(string, string, RootEscapeMode, out string, out PathError)"/>
        /// 的便捷重载：根外的绝对路径一律报 <see cref="PathError.OutsideRoot"/>。
        /// </summary>
        /// <param name="rootPath">根目录的真实路径。</param>
        /// <param name="rawPath">用户输入的原始路径。</param>
        /// <param name="rootRelative">输出相对于根目录的路径。</param>
        /// <param name="error">失败时输出原因。</param>
        /// <returns>换算成功返回 true。</returns>
        public static bool TryMakeRelativeToRoot(string rootPath, string rawPath,
                                                out string rootRelative, out PathError error)
            => TryMakeRelativeToRoot(rootPath, rawPath, RootEscapeMode.Error,
                                     out rootRelative, out error);

        /// <summary>
        /// 把【虚拟路径】换算成真实磁盘路径。
        /// 虚拟路径以 "/" 开头（可能含越界的 ".."，例如 "/../x"），
        /// 这里先去掉前导分隔符再与根目录拼接，最后用
        /// <see cref="Path.GetFullPath(string)"/> 把 ".." 解析掉。
        ///
        /// 注意不能直接 <c>Path.Combine(root, "/a")</c>：第二个参数是"根路径"时
        /// <see cref="Path.Combine(string, string)"/> 会丢弃第一个参数，
        /// 结果会变成当前盘符下的 /a，而不是 &lt;root&gt;/a。
        /// </summary>
        /// <param name="rootPath">根目录的真实路径。</param>
        /// <param name="virtualPath">虚拟路径，例如 "/a/b" 或 "/../x"。</param>
        /// <returns>换算后的真实绝对路径（".." 已被解析）。</returns>
        public static string CombineWithRoot(string rootPath, string virtualPath)
        {
            var relative = (virtualPath ?? "")
                .Replace('\\', '/')
                .TrimStart('/')
                .Replace('/', Path.DirectorySeparatorChar);

            return Path.GetFullPath(Path.Combine(rootPath, relative));
        }

        /// <summary>
        /// 判断路径是否带"卷标 / UNC"这种明确的绝对路径记号（如 <c>D:\</c>、<c>\\srv\share</c>）。
        /// 单独一个 "/" 或 "\" 不算——那正是我们要当作"相对根目录"的写法。
        /// </summary>
        /// <param name="path">待判断的路径。</param>
        /// <returns>带盘符或 UNC 前缀返回 true。</returns>
        private static bool HasVolumeRoot(string path)
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return false;

            return root.IndexOf(':') >= 0 ||
                   root.StartsWith("\\\\", StringComparison.Ordinal) ||
                   root.StartsWith("//", StringComparison.Ordinal);
        }

        /// <summary>
        /// 查询真实路径的类型：文件、目录，还是不存在。
        /// 所有"路径不存在 / 无权访问 / 参数非法"的异常都被归一成
        /// <see cref="PathKind.NotFound"/>，避免调用方到处写 try-catch。
        /// </summary>
        /// <param name="path">待查询的路径。</param>
        /// <returns>路径类型。</returns>
        public static PathKind GetKind(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return PathKind.NotFound;

            try
            {
                // File.GetAttributes 对文件和目录都有效，是判断类型的统一入口。
                var attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.Directory) == FileAttributes.Directory
                    ? PathKind.Directory
                    : PathKind.File;
            }
            catch (FileNotFoundException)
            {
                return PathKind.NotFound;
            }
            catch (DirectoryNotFoundException)
            {
                return PathKind.NotFound;
            }
            catch (IOException)
            {
                // 磁盘不可用、访问被拒绝等，无法确定类型，一律当"不存在"处理。
                return PathKind.NotFound;
            }
            catch (UnauthorizedAccessException)
            {
                return PathKind.NotFound;
            }
            catch (ArgumentException)
            {
                // 路径里含非法字符或格式不对。
                return PathKind.NotFound;
            }
        }
    }
}
