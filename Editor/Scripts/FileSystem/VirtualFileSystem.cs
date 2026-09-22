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
    ///
    /// 根目录通过构造函数注入：默认构造会新建一个空目录当根；
    /// 也可以把一个已存在的目录接管进来，从而复用或共享一整棵树
    /// （见 <see cref="VirtualFileSystem(VfsDirectory, string, RootEscapeMode)"/>）。
    ///
    /// 当根目录是一个真实路径时，本类提供一对【双向】换算：
    ///   · 虚拟 → 真实：<see cref="GetRealPath(string)"/>
    ///   · 真实 → 虚拟：<see cref="GetVirtualPath(string)"/>
    /// 两者都只是纯词法换算（本类不读写磁盘），合起来是一对互逆映射，
    /// 对应生态里的 Zio <c>ConvertPathToInternal</c> / <c>ConvertPathFromInternal</c>。
    ///
    /// 大小写策略（刻意分开）：
    ///   · 【虚拟路径空间】区分大小写（<see cref="System.StringComparer.Ordinal"/>），
    ///     这样行为在 Windows / Linux / macOS 上完全一致；
    ///   · 【真实层的包含性判断】沿用操作系统的语义（Windows 上不区分大小写），
    ///     因为那是在问"这条真实路径在不在这个真实根目录之下"，
    ///     若强行按 Ordinal 判断，Windows 上会把 "D:\PROJ\ASSETS\x"
    ///     这种合法输入误判成"根外"。
    /// </summary>
    public sealed class VirtualFileSystem
    {
        /// <summary>根目录。根目录的 Name 为空串，FullPath 恒为 "/"。</summary>
        private readonly VfsDirectory _root;

        /// <summary>
        /// 根目录对应的真实磁盘路径（已用 <see cref="Path.GetFullPath(string)"/> 规范化）。
        /// 纯虚拟模式（构造时没给根路径）下为空串。
        ///
        /// 本类【不】读写磁盘：这个路径只用来把输入路径解析、校验成
        /// "必须在根目录之内"的虚拟路径，以及反向换算出真实路径交给调用方。
        /// 所以路径指向的目录即使不存在也不影响使用。
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// ".." 想要越过 <see cref="RootPath"/> 时的处理方式，见 <see cref="RootEscapeMode"/>。
        ///
        /// 默认是 <see cref="RootEscapeMode.Clamp"/>（夹取），因为根目录被当作
        /// "伪磁盘根"使用，而真实操作系统在到顶时正是夹取而非报错：
        ///   <c>C:\..</c> → <c>C:\</c>；<c>C:\..\..\Windows</c> → <c>C:\Windows</c>。
        ///
        /// 因此默认情况下：
        ///   · "/.."     → "/"
        ///   · "../../x" → "/x"
        ///   · 任何解析成功的路径都保证落在根目录之内，<see cref="VfsPath.EscapesRoot"/> 恒为 false。
        ///
        /// 若换成 <see cref="RootEscapeMode.Error"/>，越界的 ".." 会报
        /// <see cref="PathError.EscapeRoot"/>；换成 <see cref="RootEscapeMode.Escape"/>
        /// 则会保留越界记号（<see cref="VfsPath.EscapesRoot"/> 为 true），
        /// 从而可以用 <see cref="TryGetRealPath(string, out string, out PathError)"/>
        /// 换算出根目录之外的真实路径。
        ///
        /// 注意：本属性只影响"用 .. 往上爬"。对于**直接给出根外绝对路径**
        /// （如根是 <c>D:\Proj\Assets</c> 却输入 <c>D:\Other\x</c>），
        /// 只有 <see cref="RootEscapeMode.Escape"/> 才接受，其余模式一律报
        /// <see cref="PathError.OutsideRoot"/>——那种情况不会被"夹取"，
        /// 因为静默改写成根目录会丢掉调用方真正想表达的位置。
        /// </summary>
        public RootEscapeMode EscapeMode { get; }

        // ════════════════════════════════════════════════════════════
        //  构造
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 构造一个【纯虚拟】文件系统：不关联真实目录，根就是虚拟路径空间里的 "/"。
        /// 行为与最早版本一致，适合纯内存的树操作与单测。
        /// </summary>
        public VirtualFileSystem()
            : this(new VfsDirectory { Name = "" }, null, RootEscapeMode.Clamp)
        {
        }

        /// <summary>
        /// 构造一个以【指定真实目录】为根目录的文件系统。
        ///
        /// 这是最常用的用法：传入一个真实路径（如 <c>D:\Proj\Assets</c>），
        /// 它就成为整个文件系统的根，相当于把它【伪装成一个磁盘根】。
        /// 之后所有传入的字符串路径都会：
        ///   ① 被解析成"相对于根目录"的虚拟路径；
        ///   ② 校验必须落在根目录之内——直接给出根外绝对路径报
        ///      <see cref="PathError.OutsideRoot"/>；用 ".." 往上爬则按
        ///      <paramref name="escapeMode"/> 处理。
        ///
        /// <paramref name="escapeMode"/> 默认 <see cref="RootEscapeMode.Clamp"/>，
        /// 即与 Windows 卷根一致地夹取（"/.." → "/"），
        /// 也就是说默认情况下解析成功的路径一定在根目录之内。
        /// 需要严格报警或需要拿到根外路径时再显式改（见 <see cref="EscapeMode"/>）。
        ///
        /// 注意：本类不访问磁盘，所以不检查该目录是否存在；
        /// 传入的路径只做规范化，非法字符会抛 <see cref="ArgumentException"/>。
        /// </summary>
        /// <param name="rootPath">根目录的真实路径，例如 <c>D:\Proj\Assets</c>。</param>
        /// <param name="escapeMode">".." 越过根目录时的处理方式，默认夹取。</param>
        /// <exception cref="ArgumentException">rootPath 为空或不是合法路径时抛出。</exception>
        public VirtualFileSystem(string rootPath, RootEscapeMode escapeMode = RootEscapeMode.Clamp)
            : this(new VfsDirectory { Name = "" },
                   RequireRootPath(rootPath), escapeMode)
        {
        }

        /// <summary>
        /// 校验"走字符串构造函数"时传入的根路径不能为空。
        /// 空路径在这里是明确的调用错误：想要纯虚拟模式请用无参构造，
        /// 而不是传一个空字符串——否则 <c>new VirtualFileSystem(null)</c>
        /// 会悄悄退化成纯虚拟模式，把调用方的笔误藏起来。
        /// </summary>
        /// <param name="rootPath">待校验的根路径。</param>
        /// <returns>原样返回 <paramref name="rootPath"/>。</returns>
        /// <exception cref="ArgumentException">为空或纯空白时抛出。</exception>
        private static string RequireRootPath(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException(
                    "根目录路径不能为空；若要使用纯虚拟模式，请改用无参构造。",
                    nameof(rootPath));

            return rootPath;
        }

        /// <summary>
        /// 构造一个文件系统，同时指定【承载树的根节点】与【根目录的真实路径】。
        ///
        /// 使用场景：
        ///   · 接管一棵已经建好的树（例如另一个文件系统的 <see cref="Root"/>、
        ///     或是从镜像读出来之后继续组装的目录），不必再逐级重建；
        ///   · 让多个文件系统实例共享同一棵树——它们会共享同一份状态，
        ///     在任一实例上的增删改，另一个实例立刻能看到；
        ///   · 给内存里的树贴上一个真实目录路径，从而获得路径校验能力。
        ///
        /// 约束：传入的目录必须是"没有父节点"的独立目录。
        ///   <see cref="VfsNode.FullPath"/> 是靠父链回溯算出来的，
        ///   如果传入的目录还挂在别的树上，它的后代算出来的绝对路径会指回原来那棵树，
        ///   与本文件系统里的规范化路径对不上；与其默默接受再产生难以排查的路径错乱，
        ///   不如在这里直接拒绝。
        ///
        /// 注意：根节点自身的 <see cref="VfsNode.Name"/> 不参与路径计算
        ///   （根的 FullPath 恒为 "/"），所以传入的根叫什么名字都不影响行为。
        /// </summary>
        /// <param name="root">作为根的目录节点，必须没有父节点。</param>
        /// <param name="rootPath">根目录的真实路径；为 null 表示纯虚拟模式。</param>
        /// <param name="escapeMode">".." 越过根目录时的处理方式，默认夹取。</param>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="root"/> 仍然挂在别的父节点下，
        /// 或 <paramref name="rootPath"/> 不是合法路径时抛出。
        /// </exception>
        public VirtualFileSystem(VfsDirectory root, string rootPath = null,
                                 RootEscapeMode escapeMode = RootEscapeMode.Clamp)
        {
            if (root is null)
                throw new ArgumentNullException(nameof(root));

            if (root.Parent != null)
                throw new ArgumentException(
                    "作为根传入的目录必须没有父节点（Parent 为 null）；" +
                    "若它原本挂在别的树上，请先把它从原树中摘除。", nameof(root));

            _root = root;
            EscapeMode = escapeMode;

            // 纯虚拟模式：没有真实根路径，路径空间就是 "/"。
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                RootPath = "";
            }
            else
            {
                try
                {
                    RootPath = Path.GetFullPath(rootPath.Trim());
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException(
                        $"根目录路径不合法: \"{rootPath}\"", nameof(rootPath), ex);
                }
                catch (NotSupportedException ex)
                {
                    throw new ArgumentException(
                        $"根目录路径不合法: \"{rootPath}\"", nameof(rootPath), ex);
                }
                catch (PathTooLongException ex)
                {
                    throw new ArgumentException(
                        $"根目录路径过长: \"{rootPath}\"", nameof(rootPath), ex);
                }
            }
        }

        /// <summary>根目录节点，供遍历与序列化使用。</summary>
        public VfsDirectory Root => _root;

        /// <summary>是否指定了真实根目录路径；为 false 表示纯虚拟模式。</summary>
        public bool HasRootPath => RootPath.Length > 0;

        // ════════════════════════════════════════════════════════════
        //  A0. 字符串路径 → 虚拟路径（根目录校验的唯一入口）
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 把一条【字符串路径】解析成虚拟路径，并完成根目录校验。
        /// 本类型所有接受 <c>string rawPath</c> 的方法都走这里，规则只此一处。
        ///
        /// 两步走：
        ///   ① 有根路径时，先用
        ///      <see cref="DiskPath.TryMakeRelativeToRoot(string, string, RootEscapeMode, out string, out PathError)"/>
        ///      把输入换算成"相对根目录"的写法——这一步负责处理
        ///      "直接给出根目录之外绝对路径"的情况（默认报
        ///      <see cref="PathError.OutsideRoot"/>）；
        ///   ② 再交给 <see cref="VfsPath.TryParse(string, RootEscapeMode, out VfsPath, out PathError)"/>
        ///      按虚拟路径空间规范化——这一步负责处理"用 .. 往上爬"的情况，
        ///      行为由 <see cref="EscapeMode"/> 决定
        ///      （默认 <see cref="RootEscapeMode.Clamp"/>，即夹取到根目录）。
        /// </summary>
        /// <param name="rawPath">用户输入的原始路径。</param>
        /// <param name="path">成功时输出解析结果。</param>
        /// <param name="error">失败时输出原因。</param>
        /// <returns>解析成功返回 true。</returns>
        public bool TryParsePath(string rawPath, out VfsPath path, out PathError error)
        {
            path = null;

            // 纯虚拟模式：没有根路径可校验，直接在虚拟路径空间里解析。
            if (!HasRootPath)
                return VfsPath.TryParse(rawPath, EscapeMode, out path, out error);

            // 先换算成相对根目录的写法，再做根内的规范化与越界判断。
            string rootRelative;
            if (!DiskPath.TryMakeRelativeToRoot(RootPath, rawPath, EscapeMode,
                                                out rootRelative, out error))
                return false;

            return VfsPath.TryParse(rootRelative, EscapeMode, out path, out error);
        }

        /// <summary>
        /// <see cref="TryParsePath"/> 的抛异常版本：路径非法即为程序 bug 时使用。
        /// </summary>
        /// <param name="rawPath">用户输入的原始路径。</param>
        /// <returns>解析结果。</returns>
        /// <exception cref="ArgumentException">路径非法时抛出，消息为中文错误说明。</exception>
        public VfsPath ParsePath(string rawPath)
        {
            if (!TryParsePath(rawPath, out var path, out var error))
                throw new ArgumentException(PathValidator.Describe(error), nameof(rawPath));
            return path;
        }

        // ════════════════════════════════════════════════════════════
        //  A1. 虚拟路径 ↔ 真实磁盘路径
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 把一条虚拟路径换算成真实磁盘路径。
        ///
        /// 当 <see cref="EscapeMode"/> 为 <see cref="RootEscapeMode.Escape"/> 时，
        /// 路径可能带越界的 ".."（<see cref="VfsPath.EscapesRoot"/> 为 true），
        /// 换算结果会真的落在 <see cref="RootPath"/> 之外——这正是"越过 root 取路径"
        /// 的落点。本方法只做换算，不访问磁盘。
        /// </summary>
        /// <param name="path">已解析的虚拟路径。</param>
        /// <param name="realPath">成功时输出真实绝对路径。</param>
        /// <param name="error">失败时输出原因。</param>
        /// <returns>换算成功返回 true。</returns>
        public bool TryGetRealPath(VfsPath path, out string realPath, out PathError error)
        {
            realPath = "";

            if (path is null)
            { error = PathError.Empty; return false; }

            if (!HasRootPath)
            { error = PathError.NoRootPath; return false; }

            realPath = DiskPath.CombineWithRoot(RootPath, path.Normalized);
            error = PathError.None;
            return true;
        }

        /// <summary>
        /// 把一条【字符串路径】解析后换算成真实磁盘路径。
        /// 解析阶段同样会做根目录校验，所以路径非法或越界时会直接失败。
        /// </summary>
        /// <param name="rawPath">用户输入的原始路径。</param>
        /// <param name="realPath">成功时输出真实绝对路径。</param>
        /// <param name="error">失败时输出原因。</param>
        /// <returns>换算成功返回 true。</returns>
        public bool TryGetRealPath(string rawPath, out string realPath, out PathError error)
        {
            realPath = "";

            VfsPath path;
            if (!TryParsePath(rawPath, out path, out error))
                return false;

            return TryGetRealPath(path, out realPath, out error);
        }

        /// <summary>
        /// <see cref="TryGetRealPath(string, out string, out PathError)"/> 的抛异常版本。
        /// </summary>
        /// <param name="rawPath">用户输入的原始路径。</param>
        /// <returns>真实绝对路径。</returns>
        /// <exception cref="ArgumentException">
        /// 路径非法、越界，或当前处于纯虚拟模式（<see cref="PathError.NoRootPath"/>）时抛出。
        /// </exception>
        public string GetRealPath(string rawPath)
        {
            string realPath;
            PathError error;
            if (!TryGetRealPath(rawPath, out realPath, out error))
                throw new ArgumentException(PathValidator.Describe(error), nameof(rawPath));
            return realPath;
        }

        /// <summary>
        /// <see cref="TryGetRealPath(VfsPath, out string, out PathError)"/> 的抛异常版本。
        /// 已经有解析好的 <see cref="VfsPath"/> 时用这个，省掉一次重复解析。
        /// </summary>
        /// <param name="path">已解析的虚拟路径。</param>
        /// <returns>真实绝对路径。</returns>
        /// <exception cref="ArgumentException">
        /// 路径为 null，或当前处于纯虚拟模式（<see cref="PathError.NoRootPath"/>）时抛出。
        /// </exception>
        public string GetRealPath(VfsPath path)
        {
            string realPath;
            PathError error;
            if (!TryGetRealPath(path, out realPath, out error))
                throw new ArgumentException(PathValidator.Describe(error), nameof(path));
            return realPath;
        }

        // ════════════════════════════════════════════════════════════
        //  A2. 真实磁盘路径 → 虚拟路径（与 A1 互为反向）
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 把一条【真实磁盘路径】换算成虚拟路径——也就是
        /// <see cref="TryGetRealPath(string, out string, out PathError)"/> 的反向操作。
        ///
        /// 两者合起来构成一对双向映射（生态里的对应物是 Zio 的
        /// <c>ConvertPathToInternal</c> / <c>ConvertPathFromInternal</c>）：
        /// <code>
        ///   GetRealPath("/docs/a.txt")                        → "D:\Proj\Assets\docs\a.txt"
        ///   GetVirtualPath(@"D:\Proj\Assets\docs\a.txt")      → "/docs/a.txt"
        /// </c>
        /// </code>
        ///
        /// 语义与 <see cref="TryParsePath"/> 完全一致（事实上就是它的语义化别名）：
        /// 会做根目录校验，根外的绝对路径报 <see cref="PathError.OutsideRoot"/>，
        /// ".." 爬出根的行为由 <see cref="EscapeMode"/> 决定。
        /// 之所以单独提供这个名字，是因为"我手上有一条真实路径，我要虚拟路径"
        /// 是这个类最主要的用法之一，不该逼调用方去猜 <c>TryParsePath</c> 就是它。
        ///
        /// 关于大小写（重要）：
        ///   本方法只做【词法】换算，不访问磁盘，因此无法把段名的大小写
        ///   纠正成磁盘上的真实写法。返回的虚拟路径保留输入的大小写，
        ///   只有根目录那一段会被规范成 <see cref="RootPath"/> 的写法。
        ///   而虚拟节点表是【区分大小写】的（见 <see cref="VfsDirectory"/>），
        ///   所以在 Windows 上大小写写错时，路径本身合法、但可能查不到节点。
        /// </summary>
        /// <param name="realPath">真实磁盘路径（也可以是根相对写法，规则同 <see cref="TryParsePath"/>）。</param>
        /// <param name="virtualPath">成功时输出解析好的虚拟路径。</param>
        /// <param name="error">失败时输出原因。</param>
        /// <returns>换算成功返回 true。</returns>
        public bool TryGetVirtualPath(string realPath, out VfsPath virtualPath,
                                      out PathError error)
            => TryParsePath(realPath, out virtualPath, out error);

        /// <summary>
        /// <see cref="TryGetVirtualPath(string, out VfsPath, out PathError)"/> 的抛异常版本。
        /// </summary>
        /// <param name="realPath">真实磁盘路径。</param>
        /// <returns>换算后的虚拟路径。</returns>
        /// <exception cref="ArgumentException">路径非法或越界时抛出。</exception>
        public VfsPath GetVirtualPath(string realPath) => ParsePath(realPath);

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
        public VfsNode Find(string rawPath) => Find(ParsePath(rawPath));

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
            if (!TryParsePath(rawPath, out path, out _)) return false;
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
            if (!TryParsePath(rawPath, out path, out _))
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
            if (!TryParsePath(rawPath, out path, out _))
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
            var path = ParsePath(rawPath);

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
        /// <exception cref="IOException">
        /// 路径中间存在同名文件，或路径落在根目录之外（含越界的 ".."）时抛出。
        /// </exception>
        public VfsDirectory CreateDirectory(VfsPath path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));

            var current = _root;
            foreach (var segment in path.Segments)
            {
                // "." / ".." 是路径记号而不是真实的目录名。
                // 它们只会在 RootEscapeMode.Escape 时出现在段列表里，代表"根目录之外"，
                // 而内存树只覆盖根目录之内，所以这里必须拒绝——
                // 否则会凭空造出一个名叫 ".." 的目录节点，把树弄脏。
                // （Clamp 模式下越界的 ".." 已经被吃掉，不会走到这里。）
                if (segment == "." || segment == "..")
                    throw new IOException(
                        $"不能在根目录之外创建目录: {path.Normalized}");

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
            => CreateDirectory(ParsePath(rawPath));

        /// <summary>创建文件；父目录必须已存在，且目标位置不能已有同名节点。</summary>
        /// <param name="path">要创建的文件路径。</param>
        /// <param name="content">初始内容；为 null 表示空文件。</param>
        /// <returns>新建的文件节点。</returns>
        /// <exception cref="IOException">
        /// 目标是根目录、已存在同名节点，或路径落在根目录之外（含越界的 ".."）时抛出。
        /// </exception>
        /// <exception cref="DirectoryNotFoundException">父目录不存在时抛出。</exception>
        public VfsFile CreateFile(VfsPath path, byte[] content = null)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));

            // 根目录不能变成文件。
            if (path.IsRoot)
                throw new IOException("根目录不能作为文件");

            // 带越界 ".." 的路径指向根目录之外，内存树里没有那块地方，明确拒绝。
            if (path.EscapesRoot)
                throw new IOException(
                    $"不能在根目录之外创建文件: {path.Normalized}");

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
            => CreateFile(ParsePath(rawPath), content);

        /// <summary>创建文本文件（按指定编码，默认 UTF-8 转成字节）。</summary>
        /// <param name="rawPath">要创建的文件路径。</param>
        /// <param name="text">文本内容。</param>
        /// <param name="encoding">文本编码；为 null 时使用 UTF-8。</param>
        /// <returns>新建的文件节点。</returns>
        public VfsFile CreateFile(string rawPath, string text, Encoding encoding = null)
            => CreateFile(ParsePath(rawPath),
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
            var path = ParsePath(rawPath);
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
            var path = ParsePath(rawPath);
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
            var path = ParsePath(rawPath);
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
            var path = ParsePath(rawPath);

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
            var src = ParsePath(rawSource);
            var dst = ParsePath(rawDestination);

            if (src.IsRoot)
                throw new IOException("不能移动根目录");

            if (dst.IsRoot)
                throw new IOException("目标不能是根目录");

            // 先确认源存在：这样下面的"原地不动"捷径才不会返回 null。
            var node = Find(src);
            if (node is null)
                throw new FileNotFoundException($"源不存在: {src.Normalized}");

            // 源和目标规范化后相同 → 等同于原地不动。
            // 按 Ordinal 比较，与虚拟节点表的"区分大小写"策略一致：
            // "/a" 与 "/A" 是两个不同的路径，不能当成"原地不动"。
            if (string.Equals(src.Normalized, dst.Normalized, StringComparison.Ordinal))
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
            var src = ParsePath(rawSource);
            var dst = ParsePath(rawDestination);

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
