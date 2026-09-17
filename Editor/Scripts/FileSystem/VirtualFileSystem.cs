using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 内存中的虚拟文件系统：一棵以 "/" 为根、由
    /// <see cref="VfsDirectory"/> / <see cref="VfsFile"/> 组成的树，
    /// 并提供类 POSIX 的查找、创建、读取、删除、移动、复制操作。
    ///
    /// 职责边界：本类只负责【树结构与节点操作】。
    ///   · 路径的合法性规则          → <see cref="PathValidator"/> / <see cref="VfsPath"/>
    ///   · 路径拼接与相对路径计算    → <see cref="PathCombiner"/>
    ///   · 二进制镜像的读写          → <see cref="VfsImageSerializer"/>
    ///   · 子树深拷贝                → <see cref="VfsNodeCloner"/>
    ///   · 统计信息                  → <see cref="VfsStatistics"/>
    ///
    /// 所有 public 方法都接受"字符串路径"或"已解析的 <see cref="VfsPath"/>"：
    /// 前者方便直接对接 UI 输入，后者适合循环内复用、避免重复解析。
    /// </summary>
    public sealed class VirtualFileSystem
    {
        /// <summary>根目录。根目录的 Name 为空串，FullPath 恒为 "/"。</summary>
        private readonly VfsDirectory _root = new VfsDirectory { Name = "" };

        /// <summary>根目录节点，供遍历与序列化使用。</summary>
        public VfsDirectory Root => _root;

        // ════════════════════════════════════════════════════════════
        //  A. 查找与查询
        // ════════════════════════════════════════════════════════════

        /// <summary>按已解析的路径查找节点；不存在返回 null。</summary>
        /// <param name="path">已解析的路径。</param>
        /// <returns>命中的节点，或 null。</returns>
        public VfsNode Find(VfsPath path)
        {
            if (path is null) return null;

            VfsNode current = _root;
            foreach (var segment in path.Segments)
            {
                // 中途遇到文件，说明路径不可能继续往下走。
                var dir = current as VfsDirectory;
                if (dir is null) return null;

                var child = dir.Find(segment);
                if (child is null) return null;
                current = child;
            }
            return current;
        }

        /// <summary>
        /// 按字符串路径查找节点。
        /// 注意：路径非法时会抛异常，适合"路径来自程序内部、非法即为 bug"的场景；
        /// 若路径来自用户输入，请用 <see cref="Exists"/> 或 <see cref="TryResolve"/>。
        /// </summary>
        /// <param name="rawPath">字符串路径。</param>
        /// <returns>命中的节点，或 null。</returns>
        /// <exception cref="ArgumentException">路径非法时抛出。</exception>
        public VfsNode Find(string rawPath) => Find(VfsPath.Parse(rawPath));

        /// <summary>
        /// 判断路径是否存在。
        /// 这里用 TryParse 而不是 Parse：非法路径直接视作"不存在"，而不是崩溃，
        /// 因为本方法正是给"校验用户输入"的场景用的。
        /// </summary>
        /// <param name="rawPath">字符串路径。</param>
        /// <returns>存在返回 true。</returns>
        public bool Exists(string rawPath)
        {
            VfsPath path;
            if (!VfsPath.TryParse(rawPath, out path, out _)) return false;
            return Find(path) != null;
        }

        /// <summary>
        /// 判断一个路径指向的是文件、目录，还是不存在。
        ///
        /// ★ 核心原理：
        ///   路径只是"导航地址"，它本身不含类型信息。
        ///   必须先在树中定位到节点，再读节点的 Type 属性，才能得出类型。
        ///   这就是为什么 Linux 下 `ls -l` 必须真正访问 inode。
        /// </summary>
        /// <param name="rawPath">字符串路径。</param>
        /// <returns>路径类型；不存在或路径非法时返回 <see cref="PathKind.NotFound"/>。</returns>
        public PathKind GetPathKind(string rawPath)
        {
            VfsPath path;
            if (!VfsPath.TryParse(rawPath, out path, out _))
                return PathKind.NotFound;

            var node = Find(path);
            if (node is null) return PathKind.NotFound;

            return node.Type == NodeType.File ? PathKind.File : PathKind.Directory;
        }

        /// <summary>
        /// 与 <see cref="GetPathKind"/> 相同，但同时把节点引用带出来，
        /// 避免调用方为了拿到节点而重复查找一次。
        /// </summary>
        /// <param name="rawPath">字符串路径。</param>
        /// <param name="node">成功时输出命中的节点。</param>
        /// <param name="kind">成功时输出路径类型。</param>
        /// <returns>路径存在返回 true。</returns>
        public bool TryResolve(string rawPath, out VfsNode node, out PathKind kind)
        {
            node = null;
            kind = PathKind.NotFound;

            VfsPath path;
            if (!VfsPath.TryParse(rawPath, out path, out _))
                return false;

            node = Find(path);
            if (node is null) return false;

            kind = node.Type == NodeType.File ? PathKind.File : PathKind.Directory;
            return true;
        }

        // ════════════════════════════════════════════════════════════
        //  B. 意图推断
        // ════════════════════════════════════════════════════════════

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
        /// <param name="path">已解析的路径。</param>
        /// <returns>推断出的意图。</returns>
        public static PathIntent GuessIntent(VfsPath path)
        {
            if (path != null && path.HasTrailingSeparator)
                return PathIntent.WantDirectory;

            return PathIntent.Unknown;
        }

        /// <summary>
        /// 【把上面的能力串起来】按意图创建节点：
        ///   ① 路径非法 → 抛异常（由 <see cref="VfsPath.Parse"/> 完成校验）；
        ///   ② 路径已存在 → 直接返回已有节点；
        ///   ③ 路径不存在 → 按语法推断意图，推断不出来就用调用方给的默认值。
        /// </summary>
        /// <param name="rawPath">用户输入的路径。</param>
        /// <param name="fallback">语法推断不出意图时的兜底选择。</param>
        /// <param name="content">按文件创建时的初始内容。</param>
        /// <returns>已存在或新建的节点。</returns>
        /// <exception cref="ArgumentException">路径非法时抛出。</exception>
        public VfsNode CreateByIntent(string rawPath,
                                      PathIntent fallback = PathIntent.Unknown,
                                      byte[] content = null)
        {
            // ① 校验并规范化（非法路径在这里就会抛 ArgumentException）
            var path = VfsPath.Parse(rawPath);

            // ② 已存在就直接返回
            var existing = Find(path);
            if (existing != null) return existing;

            // ③ 推断意图；推断不出来就用调用方的兜底值
            var intent = GuessIntent(path);
            if (intent == PathIntent.Unknown) intent = fallback;

            // ④ 按意图创建
            if (intent == PathIntent.WantDirectory)
                return CreateDirectory(path);

            return CreateFile(path, content);
        }

        // ════════════════════════════════════════════════════════════
        //  C. 创建
        // ════════════════════════════════════════════════════════════

        /// <summary>逐级创建目录，行为类似 mkdir -p；已存在的目录会被复用。</summary>
        /// <param name="path">要创建的目录路径。</param>
        /// <returns>最终那一级目录节点。</returns>
        /// <exception cref="IOException">路径中间存在同名文件时抛出。</exception>
        public VfsDirectory CreateDirectory(VfsPath path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));

            var current = _root;
            foreach (var segment in path.Segments)
            {
                var child = current.Find(segment);
                if (child != null)
                {
                    // 同名节点存在，但它必须是目录才能继续往下走。
                    var childDir = child as VfsDirectory;
                    if (childDir is null)
                        throw new IOException(
                            $"路径中间存在同名文件，无法创建目录: {child.FullPath}");

                    current = childDir;
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
        /// <param name="rawPath">要创建的目录路径。</param>
        /// <returns>最终那一级目录节点。</returns>
        public VfsDirectory CreateDirectory(string rawPath)
            => CreateDirectory(VfsPath.Parse(rawPath));

        /// <summary>创建文件；父目录必须已存在，且目标位置不能已有同名节点。</summary>
        /// <param name="path">要创建的文件路径。</param>
        /// <param name="content">初始内容；为 null 表示空文件。</param>
        /// <returns>新建的文件节点。</returns>
        /// <exception cref="IOException">目标是根目录，或已存在同名节点时抛出。</exception>
        /// <exception cref="DirectoryNotFoundException">父目录不存在时抛出。</exception>
        public VfsFile CreateFile(VfsPath path, byte[] content = null)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));

            // 根目录不能变成文件。
            if (path.IsRoot)
                throw new IOException("根目录不能作为文件");

            // 父目录必须存在，且必须是目录。
            var parent = Find(path.ParentPath) as VfsDirectory;
            if (parent is null)
                throw new DirectoryNotFoundException($"父目录不存在: {path.ParentPath}");

            // 目标位置不能已有同名节点。
            if (parent.Find(path.Name) != null)
                throw new IOException($"已存在同名节点: {path.Normalized}");

            var file = new VfsFile { Name = path.Name, Parent = parent };
            if (content != null) file.WriteAllBytes(content);

            parent.Attach(file);
            parent.ModifiedAt = DateTime.UtcNow;
            return file;
        }

        /// <summary>创建文件（字符串重载）。</summary>
        /// <param name="rawPath">要创建的文件路径。</param>
        /// <param name="content">初始内容；为 null 表示空文件。</param>
        /// <returns>新建的文件节点。</returns>
        public VfsFile CreateFile(string rawPath, byte[] content = null)
            => CreateFile(VfsPath.Parse(rawPath), content);

        /// <summary>创建文本文件（按指定编码，默认 UTF-8 转成字节）。</summary>
        /// <param name="rawPath">要创建的文件路径。</param>
        /// <param name="text">文本内容。</param>
        /// <param name="encoding">文本编码；为 null 时使用 UTF-8。</param>
        /// <returns>新建的文件节点。</returns>
        public VfsFile CreateFile(string rawPath, string text, Encoding encoding = null)
            => CreateFile(VfsPath.Parse(rawPath),
                          (encoding ?? Encoding.UTF8).GetBytes(text));

        // ════════════════════════════════════════════════════════════
        //  D. 读取与遍历
        // ════════════════════════════════════════════════════════════

        /// <summary>读取文本文件内容。</summary>
        /// <param name="rawPath">文件路径。</param>
        /// <param name="encoding">文本编码；为 null 时使用 UTF-8。</param>
        /// <returns>文件文本内容。</returns>
        /// <exception cref="FileNotFoundException">路径不是文件或不存在时抛出。</exception>
        public string ReadAllText(string rawPath, Encoding encoding = null)
        {
            var path = VfsPath.Parse(rawPath);
            var file = Find(path) as VfsFile;
            if (file is null)
                throw new FileNotFoundException($"不是文件或不存在: {path.Normalized}");
            return file.ReadAllText(encoding);
        }

        /// <summary>列出某个目录的直接子节点。</summary>
        /// <param name="rawPath">目录路径，默认根目录。</param>
        /// <returns>直接子节点集合。</returns>
        /// <exception cref="DirectoryNotFoundException">路径不是目录或不存在时抛出。</exception>
        public IReadOnlyCollection<VfsNode> List(string rawPath = "/")
        {
            var path = VfsPath.Parse(rawPath);
            var dir = Find(path) as VfsDirectory;
            if (dir is null)
                throw new DirectoryNotFoundException($"不是目录或不存在: {path.Normalized}");
            return dir.Children;
        }

        /// <summary>
        /// 深度优先遍历某个目录下的所有节点。
        /// 返回的是惰性序列，遍历过程中的修改会影响枚举结果，调用方需自行注意。
        /// </summary>
        /// <param name="rawPath">起始目录路径，默认根目录。</param>
        /// <param name="recursive">是否递归进入子目录。</param>
        /// <returns>节点序列（先根顺序）。</returns>
        /// <exception cref="DirectoryNotFoundException">路径不是目录或不存在时抛出。</exception>
        public IEnumerable<VfsNode> Walk(string rawPath = "/", bool recursive = true)
        {
            var path = VfsPath.Parse(rawPath);
            var dir = Find(path) as VfsDirectory;
            if (dir is null)
                throw new DirectoryNotFoundException($"不是目录或不存在: {path.Normalized}");

            return WalkCore(dir, recursive);
        }

        /// <summary>Walk 的递归实现。</summary>
        /// <param name="dir">当前目录。</param>
        /// <param name="recursive">是否递归进入子目录。</param>
        /// <returns>节点序列。</returns>
        private static IEnumerable<VfsNode> WalkCore(VfsDirectory dir, bool recursive)
        {
            foreach (var child in dir.Children)
            {
                yield return child;

                var sub = child as VfsDirectory;
                if (recursive && sub != null)
                {
                    foreach (var node in WalkCore(sub, true))
                        yield return node;
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  E. 删除 / 移动 / 复制
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 删除一个节点。目录会被整棵摘除（子节点随父节点一起脱离树，由 GC 回收）。
        /// </summary>
        /// <param name="rawPath">要删除的路径。</param>
        /// <param name="recursive">目录非空时是否允许删除。</param>
        /// <returns>确实删除了返回 true；路径不存在返回 false。</returns>
        /// <exception cref="IOException">试图删除根目录，或目录非空且不允许递归时抛出。</exception>
        public bool Delete(string rawPath, bool recursive = true)
        {
            var path = VfsPath.Parse(rawPath);

            if (path.IsRoot)
                throw new IOException("不能删除根目录");

            var node = Find(path);
            if (node is null) return false;

            // 非递归删除时，目录必须为空。
            var dir = node as VfsDirectory;
            if (dir != null && dir.Count > 0 && !recursive)
                throw new IOException($"目录非空，无法删除: {path.Normalized}");

            var parent = node.Parent;
            parent.Detach(node.Name);
            parent.ModifiedAt = DateTime.UtcNow;

            // 断开父指针，让被删的子树成为一个孤立的、不会再被误用的片段。
            node.Parent = null;
            return true;
        }

        /// <summary>移动 / 重命名一个节点。</summary>
        /// <param name="rawSource">源路径。</param>
        /// <param name="rawDestination">目标路径（含新的名字）。</param>
        /// <returns>移动后的节点。</returns>
        /// <exception cref="FileNotFoundException">源不存在时抛出。</exception>
        /// <exception cref="DirectoryNotFoundException">目标父目录不存在时抛出。</exception>
        /// <exception cref="IOException">目标是根目录、目标已存在，或试图把目录移入自身子树时抛出。</exception>
        public VfsNode Move(string rawSource, string rawDestination)
        {
            var src = VfsPath.Parse(rawSource);
            var dst = VfsPath.Parse(rawDestination);

            if (src.IsRoot)
                throw new IOException("不能移动根目录");

            if (dst.IsRoot)
                throw new IOException("目标不能是根目录");

            // 先确认源存在：这样下面的"原地不动"捷径才不会返回 null。
            var node = Find(src);
            if (node is null)
                throw new FileNotFoundException($"源不存在: {src.Normalized}");

            // 源和目标规范化后相同 → 等同于原地不动。
            if (string.Equals(src.Normalized, dst.Normalized, StringComparison.OrdinalIgnoreCase))
                return node;

            var destDir = Find(dst.ParentPath) as VfsDirectory;
            if (destDir is null)
                throw new DirectoryNotFoundException($"目标目录不存在: {dst.ParentPath}");

            var existing = destDir.Find(dst.Name);
            if (existing != null)
            {
                if (ReferenceEquals(existing, node)) return node;
                throw new IOException($"目标已存在: {dst.Normalized}");
            }

            // 防止把目录移动到自己的子树中，否则会形成环。
            if (node.IsSelfOrAncestorOf(destDir))
                throw new IOException("不能把目录移动到它自身或其子目录中");

            var oldParent = node.Parent;
            oldParent.Detach(node.Name);

            node.Name = dst.Name;
            node.Parent = destDir;
            node.ModifiedAt = DateTime.UtcNow;
            destDir.Attach(node);

            oldParent.ModifiedAt = DateTime.UtcNow;
            destDir.ModifiedAt = DateTime.UtcNow;
            return node;
        }

        /// <summary>复制一个节点（目录会连同整棵子树深拷贝）。</summary>
        /// <param name="rawSource">源路径。</param>
        /// <param name="rawDestination">目标路径（含新的名字）。</param>
        /// <returns>复制出来的新节点。</returns>
        /// <exception cref="FileNotFoundException">源不存在时抛出。</exception>
        /// <exception cref="DirectoryNotFoundException">目标父目录不存在时抛出。</exception>
        /// <exception cref="IOException">目标已存在，或试图把目录复制进自身子树时抛出。</exception>
        public VfsNode Copy(string rawSource, string rawDestination)
        {
            var src = VfsPath.Parse(rawSource);
            var dst = VfsPath.Parse(rawDestination);

            if (dst.IsRoot)
                throw new IOException("目标不能是根目录");

            var node = Find(src);
            if (node is null)
                throw new FileNotFoundException($"源不存在: {src.Normalized}");

            var destDir = Find(dst.ParentPath) as VfsDirectory;
            if (destDir is null)
                throw new DirectoryNotFoundException($"目标目录不存在: {dst.ParentPath}");

            if (destDir.Find(dst.Name) != null)
                throw new IOException($"目标已存在: {dst.Normalized}");

            // 复制到自己的子树里会导致"边拷边变"的无穷递归，必须拦住。
            if (node.IsSelfOrAncestorOf(destDir))
                throw new IOException("不能把目录复制到它自己的子目录中");

            var clone = VfsNodeCloner.Clone(node, dst.Name);
            clone.Parent = destDir;
            destDir.Attach(clone);
            destDir.ModifiedAt = DateTime.UtcNow;
            return clone;
        }
    }
}
