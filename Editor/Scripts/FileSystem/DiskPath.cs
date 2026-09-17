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
