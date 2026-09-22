using System;
using System.IO;
using NUnit.Framework;
using VoyageForge.Depot.Editor.FileSystem;

namespace VoyageForge.Depot.Tests
{
    /// <summary>
    /// <see cref="VirtualFileSystem"/> 的【根目录（沙箱 / 伪磁盘根）】行为测试：
    /// 以真实目录路径为根时的路径解析、根内校验，以及
    /// <see cref="VirtualFileSystem.EscapeMode"/> 三种取值
    /// （<see cref="RootEscapeMode.Clamp"/> 夹取 / <see cref="RootEscapeMode.Error"/> 报错 /
    /// <see cref="RootEscapeMode.Escape"/> 真穿出去）的差异。
    ///
    /// 注意：被当作根的那个目录【不需要真实存在】——文件系统只在内存里建树，
    /// 根路径只用于解析与换算，不访问磁盘。
    /// </summary>
    [TestFixture]
    public class RootedFileSystemTests
    {
        /// <summary>沙箱根目录（一个真实路径，但不要求真的存在）。</summary>
        private string _root;

        /// <summary>与根目录同级、但不在根之内的路径，用于验证越界拦截。</summary>
        private string _outside;

        /// <summary>默认（不允许越界）的根文件系统。</summary>
        private VirtualFileSystem _fs;

        [SetUp]
        public void SetUp()
        {
            var temp = Path.GetTempPath();
            _root = Path.Combine(temp, "DepotRoot_" + Guid.NewGuid().ToString("N"));
            _outside = Path.Combine(temp, "DepotOutside_" + Guid.NewGuid().ToString("N"));

            _fs = new VirtualFileSystem(_root);
        }

        // ════════════════════════════════════════════════════════════
        //  根路径本身
        // ════════════════════════════════════════════════════════════

        /// <summary>构造函数应把根路径规范化为绝对路径，越界模式默认为夹取。</summary>
        [Test]
        public void 构造_记录规范化的根路径()
        {
            Assert.IsTrue(_fs.HasRootPath);
            Assert.AreEqual(Path.GetFullPath(_root), _fs.RootPath);
            Assert.AreEqual(RootEscapeMode.Clamp, _fs.EscapeMode);
        }

        /// <summary>无参构造是纯虚拟模式：没有根路径；越界模式同样是默认的夹取。</summary>
        [Test]
        public void 构造_无参_纯虚拟模式()
        {
            var virtualFs = new VirtualFileSystem();

            Assert.IsFalse(virtualFs.HasRootPath);
            Assert.AreEqual("", virtualFs.RootPath);
            Assert.AreEqual(RootEscapeMode.Clamp, virtualFs.EscapeMode);
        }

        /// <summary>三种越界模式都应被原样记录。</summary>
        [Test]
        public void 构造_记录越界模式()
        {
            Assert.AreEqual(RootEscapeMode.Clamp,
                new VirtualFileSystem(_root).EscapeMode);
            Assert.AreEqual(RootEscapeMode.Error,
                new VirtualFileSystem(_root, RootEscapeMode.Error).EscapeMode);
            Assert.AreEqual(RootEscapeMode.Escape,
                new VirtualFileSystem(_root, RootEscapeMode.Escape).EscapeMode);
        }

        // ════════════════════════════════════════════════════════════
        //  根内路径：各种写法都要落到同一批节点上
        // ════════════════════════════════════════════════════════════

        /// <summary>相对写法与 "/" 开头的写法都相对根目录解析，指向同一处。</summary>
        [Test]
        public void 相对写法与斜杠开头写法_指向同一节点()
        {
            _fs.CreateDirectory("docs");
            _fs.CreateFile("/docs/readme.md", "hello");

            Assert.IsTrue(_fs.Exists("docs"));
            Assert.IsTrue(_fs.Exists("/docs"));
            Assert.AreEqual("hello", _fs.ReadAllText("docs/readme.md"));
            Assert.AreEqual("hello", _fs.ReadAllText("/docs/readme.md"));
        }

        /// <summary>根目录之内的真实绝对路径应被换算成根内的虚拟路径。</summary>
        [Test]
        public void 根内的真实绝对路径_换算为根内虚拟路径()
        {
            _fs.CreateDirectory("docs");

            var realFile = Path.Combine(_root, "docs", "a.txt");
            _fs.CreateFile(realFile, "content");

            Assert.IsTrue(_fs.Exists("/docs/a.txt"));
            Assert.AreEqual("content", _fs.ReadAllText("/docs/a.txt"));
            Assert.AreEqual(realFile, _fs.GetRealPath("/docs/a.txt"));
        }

        /// <summary>根内路径里出现的 ".." 应正常回退，不算越界。</summary>
        [Test]
        public void 根内使用上级记号_正常回退()
        {
            _fs.CreateDirectory("a/b");
            _fs.CreateFile("/a/c.txt", "y");

            Assert.IsTrue(_fs.Exists("a/b/../c.txt"));
            Assert.AreEqual("y", _fs.ReadAllText("/a/b/../../a/c.txt"));
        }

        /// <summary>整条路径首尾的空白仍然被裁掉，不会误判成非法段名。</summary>
        [Test]
        public void 带空白的输入_仍能解析()
        {
            _fs.CreateDirectory("docs");

            Assert.IsTrue(_fs.Exists("  /docs  "));
        }

        // ════════════════════════════════════════════════════════════
        //  默认模式 = 夹取（Clamp）：与 Windows 卷根行为一致
        // ════════════════════════════════════════════════════════════

        /// <summary>默认模式必须是夹取，这样根才像"伪磁盘根"。</summary>
        [Test]
        public void 默认模式_是夹取()
        {
            Assert.AreEqual(RootEscapeMode.Clamp, _fs.EscapeMode);
            Assert.AreEqual(RootEscapeMode.Clamp,
                            new VirtualFileSystem().EscapeMode);
        }

        /// <summary>
        /// 根目录处的 ".." 停在根目录——与 <c>C:\..</c> → <c>C:\</c> 同构，不报错。
        /// </summary>
        [Test]
        public void 夹取_根目录处的上级记号停在根()
        {
            AssertClamped("/..", "/");
            AssertClamped("../..", "/");
            AssertClamped("/../../../..", "/");
        }

        /// <summary>先夹取再往下走：对应 <c>C:\..\..\Windows</c> → <c>C:\Windows</c>。</summary>
        [Test]
        public void 夹取_越界后再往下走()
        {
            AssertClamped("../../x", "/x");
            AssertClamped("/../docs/a.txt", "/docs/a.txt");
        }

        /// <summary>根内回退到根之后，再 ".." 同样被夹取。</summary>
        [Test]
        public void 夹取_根内回退后再夹取()
        {
            AssertClamped("docs/sub/../..", "/");
            AssertClamped("docs/../../x", "/x");
        }

        /// <summary>
        /// 夹取的语义是"解析成一个根内的路径"，所以真能查到根内的同名节点：
        /// 建了 /x 之后，"../x" 与 "/x" 指向同一个节点。
        /// </summary>
        [Test]
        public void 夹取_与根内同名路径指向同一节点()
        {
            _fs.CreateFile("/x", "content");

            Assert.IsTrue(_fs.Exists("../x"));
            Assert.AreEqual("content", _fs.ReadAllText("../x"));
            Assert.AreEqual(_fs.GetRealPath("/x"), _fs.GetRealPath("../x"));
        }

        /// <summary>任意多条 ".." 都不会抛异常，也不会越界。</summary>
        [Test]
        public void 夹取_永不抛异常且永不越界()
        {
            foreach (var raw in new[] { "..", "../..", "/..", "../../../x" })
            {
                VfsPath path;
                PathError error;

                Assert.IsTrue(_fs.TryParsePath(raw, out path, out error),
                              $"\"{raw}\" 在夹取模式下应当成功");
                Assert.AreEqual(PathError.None, error);
                Assert.IsFalse(path.EscapesRoot, $"\"{raw}\" 不应越界");
            }
        }

        /// <summary>
        /// 夹取只管"用 .. 往上爬"；指名道姓给出根外的绝对路径是另一回事，
        /// 静默改写成根目录会丢掉调用方真正想表达的位置，所以仍然报错。
        /// </summary>
        [Test]
        public void 夹取_根外绝对路径仍报_OutsideRoot()
        {
            VfsPath path;
            PathError error;

            Assert.IsFalse(_fs.TryParsePath(_outside, out path, out error));
            Assert.IsNull(path);
            Assert.AreEqual(PathError.OutsideRoot, error);

            var deep = Path.Combine(_outside, "a", "b.txt");
            Assert.IsFalse(_fs.TryParsePath(deep, out path, out error));
            Assert.AreEqual(PathError.OutsideRoot, error);
        }

        /// <summary>用 ".." 先退出根、再进入的绝对路径，规范化后同样落在根外。</summary>
        [Test]
        public void 夹取_先退出根再进入的绝对路径_报_OutsideRoot()
        {
            VfsPath path;
            PathError error;

            var sneaky = Path.Combine(_root, "..", "x.txt");
            Assert.IsFalse(_fs.TryParsePath(sneaky, out path, out error));
            Assert.AreEqual(PathError.OutsideRoot, error);
        }

        // ════════════════════════════════════════════════════════════
        //  Error 模式 = 严格沙箱
        // ════════════════════════════════════════════════════════════

        /// <summary>严格模式下，用 ".." 爬出根目录直接报 EscapeRoot。</summary>
        [Test]
        public void Error模式_用上级记号爬出根_报_EscapeRoot()
        {
            var fs = new VirtualFileSystem(_root, RootEscapeMode.Error);

            AssertFails(fs, "../x", PathError.EscapeRoot);
            AssertFails(fs, "/../x", PathError.EscapeRoot);
            AssertFails(fs, "a/../../x", PathError.EscapeRoot);
        }

        /// <summary>严格模式下，根外的绝对路径依旧报 OutsideRoot。</summary>
        [Test]
        public void Error模式_根外绝对路径_报_OutsideRoot()
        {
            var fs = new VirtualFileSystem(_root, RootEscapeMode.Error);

            AssertFails(fs, _outside, PathError.OutsideRoot);
        }

        /// <summary>严格模式下根内的 ".." 回退仍然正常，不是一刀切禁止。</summary>
        [Test]
        public void Error模式_根内回退_正常()
        {
            var fs = new VirtualFileSystem(_root, RootEscapeMode.Error);
            fs.CreateDirectory("a/b");
            fs.CreateFile("/a/c.txt", "y");

            Assert.IsTrue(fs.Exists("a/b/../c.txt"));
        }

        // ════════════════════════════════════════════════════════════
        //  越界输入在各种入口上的一致性
        // ════════════════════════════════════════════════════════════

        /// <summary>根外绝对路径在"查询类"入口上表现为"不存在"，而不是抛异常。</summary>
        [Test]
        public void 根外路径_在查询入口上表现为不存在()
        {
            Assert.IsFalse(_fs.Exists(_outside));
            Assert.AreEqual(PathKind.NotFound, _fs.GetPathKind(_outside));

            VfsNode node;
            PathKind kind;
            Assert.IsFalse(_fs.TryResolve(_outside, out node, out kind));
            Assert.IsNull(node);
            Assert.AreEqual(PathKind.NotFound, kind);
        }

        /// <summary>
        /// 需要"路径非法即为 bug"的入口（Find / ParsePath / 各类变更操作）
        /// 对根外的绝对路径应抛异常——它是"非法路径"，不是"合法的空路径"。
        /// </summary>
        [Test]
        public void 根外路径_在严格入口上抛异常()
        {
            Assert.Throws<ArgumentException>(() => { _fs.Find(_outside); });
            Assert.Throws<ArgumentException>(() => { _fs.ParsePath(_outside); });
            Assert.Throws<ArgumentException>(() => { _fs.List(_outside); });
            Assert.Throws<ArgumentException>(() => { _fs.Walk(_outside); });
            Assert.Throws<ArgumentException>(() => { _fs.Delete(_outside); });
            Assert.Throws<ArgumentException>(() => { _fs.ReadAllText(_outside); });
            Assert.Throws<ArgumentException>(() => { _fs.CreateDirectory(_outside); });
            Assert.Throws<ArgumentException>(() => { _fs.CreateFile(_outside, "x"); });
            Assert.Throws<ArgumentException>(() => { _fs.Move(_outside, "/x"); });
            Assert.Throws<ArgumentException>(() => { _fs.Copy(_outside, "/x"); });
        }

        // ════════════════════════════════════════════════════════════
        //  Escape 模式：真的能拿到 root 之外的路径
        // ════════════════════════════════════════════════════════════

        /// <summary>允许越界后，".." 会被保留下来，并能换算出根之外的真实路径。</summary>
        [Test]
        public void 允许越界_可表达并换算出根外路径()
        {
            var fs = new VirtualFileSystem(_root, RootEscapeMode.Escape);

            VfsPath path;
            PathError error;
            Assert.IsTrue(fs.TryParsePath("../x.txt", out path, out error));
            Assert.AreEqual(PathError.None, error);
            Assert.IsTrue(path.EscapesRoot);
            Assert.AreEqual("/../x.txt", path.Normalized);
            CollectionAssert.AreEqual(new[] { "..", "x.txt" }, path.Segments);

            string realPath;
            Assert.IsTrue(fs.TryGetRealPath(path, out realPath, out error));
            Assert.AreEqual(Path.GetFullPath(Path.Combine(_root, "..", "x.txt")), realPath);

            // 换算结果确实不在根目录之内
            string relative;
            Assert.IsFalse(DiskPath.IsSubPathOf(realPath, fs.RootPath, out relative));
        }

        /// <summary>多层 ".." 的层数不会丢，能一路退到根之外。</summary>
        [Test]
        public void 允许越界_多层上级记号不丢失()
        {
            var fs = new VirtualFileSystem(_root, RootEscapeMode.Escape);

            VfsPath path;
            fs.TryParsePath("../../a/../b.txt", out path, out _);

            CollectionAssert.AreEqual(new[] { "..", "..", "b.txt" }, path.Segments);
        }

        /// <summary>根外绝对路径在允许越界时，会被换算成"退回根再往外走"的相对写法。</summary>
        [Test]
        public void 允许越界_根外绝对路径也能解析()
        {
            var fs = new VirtualFileSystem(_root, RootEscapeMode.Escape);

            VfsPath path;
            PathError error;
            var deep = Path.Combine(_outside, "a.txt");

            Assert.IsTrue(fs.TryParsePath(deep, out path, out error));
            Assert.AreEqual(PathError.None, error);
            Assert.IsTrue(path.EscapesRoot);

            string realPath;
            Assert.IsTrue(fs.TryGetRealPath(path, out realPath, out error));
            Assert.AreEqual(Path.GetFullPath(deep), realPath);
        }

        /// <summary>越界路径在树里查不到——树只覆盖根目录之内。</summary>
        [Test]
        public void 允许越界_但树内依然查不到()
        {
            var fs = new VirtualFileSystem(_root, RootEscapeMode.Escape);

            Assert.IsFalse(fs.Exists("../x.txt"));
            Assert.AreEqual(PathKind.NotFound, fs.GetPathKind("../x.txt"));
        }

        /// <summary>
        /// 越界只影响"路径的表达"，不能让内存树长出根之外的节点；
        /// 否则会凭空造出名叫 ".." 的目录，把树弄脏。
        /// </summary>
        [Test]
        public void 允许越界_不能在根外创建节点()
        {
            var fs = new VirtualFileSystem(_root, RootEscapeMode.Escape);

            Assert.Throws<IOException>(() => { fs.CreateDirectory("../newdir/"); });
            Assert.Throws<IOException>(() => { fs.CreateFile("../newfile.txt", "x"); });

            // 根仍然是空的，也没有被塞进名叫 ".." 的节点
            Assert.AreEqual(0, fs.List("/").Count);
            Assert.AreEqual(0, fs.Root.Count);
        }

        // ════════════════════════════════════════════════════════════
        //  双向映射：真实 → 虚拟（GetVirtualPath）
        // ════════════════════════════════════════════════════════════

        /// <summary>根目录之内的真实路径应换算成虚拟路径。</summary>
        [Test]
        public void 真实转虚拟_根内绝对路径()
        {
            var real = Path.Combine(_root, "docs", "a.txt");

            var virtualPath = _fs.GetVirtualPath(real);

            Assert.AreEqual("/docs/a.txt", virtualPath.Normalized);
            CollectionAssert.AreEqual(new[] { "docs", "a.txt" }, virtualPath.Segments);
        }

        /// <summary>根目录自身换算成虚拟根 "/"。</summary>
        [Test]
        public void 真实转虚拟_根目录自身()
        {
            Assert.AreEqual("/", _fs.GetVirtualPath(_root).Normalized);
        }

        /// <summary>TryGetVirtualPath 失败时给出错误码，不抛异常。</summary>
        [Test]
        public void 真实转虚拟_根外路径_报_OutsideRoot()
        {
            VfsPath virtualPath;
            PathError error;

            Assert.IsFalse(_fs.TryGetVirtualPath(_outside, out virtualPath, out error));
            Assert.IsNull(virtualPath);
            Assert.AreEqual(PathError.OutsideRoot, error);
        }

        /// <summary>严格模式下越界失败；默认夹取模式下，规范化后仍在根内就正常换算。</summary>
        [Test]
        public void 真实转虚拟_越界按模式处理()
        {
            var strict = new VirtualFileSystem(_root, RootEscapeMode.Error);

            VfsPath virtualPath;
            PathError error;
            Assert.IsFalse(strict.TryGetVirtualPath(Path.Combine(_root, "..", "x"),
                                                    out virtualPath, out error));
            Assert.AreEqual(PathError.OutsideRoot, error);

            Assert.AreEqual("/docs/a.txt",
                _fs.GetVirtualPath(Path.Combine(_root, "docs", "..", "docs", "a.txt"))
                   .Normalized);
        }

        /// <summary>非法路径抛异常（GetVirtualPath 是抛异常版本）。</summary>
        [Test]
        public void 真实转虚拟_非法路径_抛异常()
        {
            Assert.Throws<ArgumentException>(() => { _fs.GetVirtualPath(_outside); });
            Assert.Throws<ArgumentException>(() => { _fs.GetVirtualPath("   "); });
        }

        // ════════════════════════════════════════════════════════════
        //  双向映射的往返一致性
        // ════════════════════════════════════════════════════════════

        /// <summary>虚拟 → 真实 → 虚拟 应当回到同一个虚拟路径。</summary>
        [Test]
        public void 往返_虚拟到真实再回来()
        {
            _fs.CreateDirectory("docs/sub");
            _fs.CreateFile("/docs/sub/a.txt", "x");

            var virtualPath = _fs.GetVirtualPath(Path.Combine(_root, "docs", "sub", "a.txt"));
            var realPath = _fs.GetRealPath(virtualPath);

            Assert.AreEqual(Path.GetFullPath(Path.Combine(_root, "docs", "sub", "a.txt")),
                            realPath);
            Assert.AreEqual(virtualPath.Normalized,
                            _fs.GetVirtualPath(realPath).Normalized);
        }

        /// <summary>
        /// 真实 → 虚拟 → 真实 保留输入各段的大小写（虚拟层区分大小写），
        /// 只有"根"那一段被规范成 rootPath 的写法——因为包含性判断走的是平台语义。
        /// </summary>
        [Test]
        public void 往返_真实到虚拟再回来_保留段名大小写()
        {
            var mixedCase = Path.Combine(_root, "Docs", "A.TXT");

            var virtualPath = _fs.GetVirtualPath(mixedCase);
            Assert.AreEqual("/Docs/A.TXT", virtualPath.Normalized);

            // 虚拟层区分大小写：换小写查不到
            Assert.IsFalse(_fs.Exists("/docs/a.txt"));

            var realPath = _fs.GetRealPath(virtualPath);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(_root, "Docs", "A.TXT")), realPath);
        }

        /// <summary>
        /// 真实层的包含性判断沿用操作系统语义：Windows 上大小写写错仍算"在根内"，
        /// 否则会把 "D:\PROJ\ASSETS\x" 这种合法输入误判成根外。
        /// （Linux 上路径本身区分大小写，因此这条会走另一分支。）
        /// </summary>
        [Test]
        public void 包含性判断_沿用操作系统语义()
        {
            var differentlyCased = _root.ToUpperInvariant() +
                                   Path.DirectorySeparatorChar + "docs";

            VfsPath virtualPath;
            PathError error;
            var ok = _fs.TryGetVirtualPath(differentlyCased, out virtualPath, out error);

            if (IsFileSystemCaseSensitive())
            {
                Assert.IsFalse(ok, "区分大小写的系统上，大小写不同就是另一条路径");
                Assert.AreEqual(PathError.OutsideRoot, error);
            }
            else
            {
                Assert.IsTrue(ok, $"不区分大小写的系统上应当被接受，实际错误：{error}");
                Assert.AreEqual("/docs", virtualPath.Normalized);
            }
        }

        /// <summary>探测当前文件系统是否区分大小写：拿临时目录的换大小写写法去问磁盘。</summary>
        /// <returns>区分大小写返回 true。</returns>
        private static bool IsFileSystemCaseSensitive()
        {
            var probe = Path.Combine(Path.GetTempPath(),
                                     "DepotCaseProbe_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(probe);
            try
            {
                return !Directory.Exists(probe.ToUpperInvariant());
            }
            finally
            {
                if (Directory.Exists(probe)) Directory.Delete(probe, recursive: true);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  真实路径换算
        // ════════════════════════════════════════════════════════════

        /// <summary>根目录自身应换算回根路径。</summary>
        [Test]
        public void 真实路径换算_根目录自身()
        {
            Assert.AreEqual(Path.GetFullPath(_root), _fs.GetRealPath("/"));
            Assert.AreEqual(Path.GetFullPath(_root), _fs.GetRealPath("."));
        }

        /// <summary>多层子目录应逐级拼到根路径上。</summary>
        [Test]
        public void 真实路径换算_多级子目录()
        {
            _fs.CreateDirectory("a/b/c");

            Assert.AreEqual(Path.GetFullPath(Path.Combine(_root, "a", "b", "c")),
                            _fs.GetRealPath("/a/b/c"));
        }

        /// <summary>纯虚拟模式没有真实路径可换算，应报 NoRootPath。</summary>
        [Test]
        public void 真实路径换算_纯虚拟模式_报_NoRootPath()
        {
            var virtualFs = new VirtualFileSystem();

            string realPath;
            PathError error;
            Assert.IsFalse(virtualFs.TryGetRealPath("/a", out realPath, out error));
            Assert.AreEqual("", realPath);
            Assert.AreEqual(PathError.NoRootPath, error);
        }

        /// <summary>
        /// 换算真实路径时，非法输入应失败而不是产出奇怪的路径。
        /// 注意 ".." 在默认夹取模式下是合法的（会夹回根内），所以这里用 Error 模式验证。
        /// </summary>
        [Test]
        public void 真实路径换算_非法路径_失败()
        {
            string realPath;
            PathError error;

            Assert.IsFalse(_fs.TryGetRealPath(_outside, out realPath, out error));
            Assert.AreEqual(PathError.OutsideRoot, error);

            var strict = new VirtualFileSystem(_root, RootEscapeMode.Error);
            Assert.IsFalse(strict.TryGetRealPath("../x", out realPath, out error));
            Assert.AreEqual(PathError.EscapeRoot, error);
        }

        /// <summary>夹取模式下，"../x" 换算出的真实路径就是根内 /x 的位置。</summary>
        [Test]
        public void 真实路径换算_夹取后落在根内()
        {
            var realPath = _fs.GetRealPath("../x");

            Assert.AreEqual(Path.GetFullPath(Path.Combine(_root, "x")), realPath);
            Assert.IsTrue(DiskPath.IsSubPathOf(realPath, _fs.RootPath, out _));
        }

        /// <summary>Escape 模式下，"../x" 换算出的真实路径真的在根之外。</summary>
        [Test]
        public void 真实路径换算_越界后落在根外()
        {
            var escaped = new VirtualFileSystem(_root, RootEscapeMode.Escape);
            var realPath = escaped.GetRealPath("../x");

            Assert.AreEqual(Path.GetFullPath(Path.Combine(_root, "..", "x")), realPath);
            Assert.IsFalse(DiskPath.IsSubPathOf(realPath, escaped.RootPath, out _));
        }

        // ════════════════════════════════════════════════════════════
        //  与真实目录共存（根路径指向一个真实存在的目录也要能用）
        // ════════════════════════════════════════════════════════════

        /// <summary>根路径指向真实存在的目录时，行为与不存在时一致（本类不读盘）。</summary>
        [Test]
        public void 根路径指向真实目录_行为一致()
        {
            Directory.CreateDirectory(_root);
            try
            {
                var fs = new VirtualFileSystem(_root);
                fs.CreateDirectory("docs");
                fs.CreateFile("/docs/a.txt", "x");

                Assert.IsTrue(fs.Exists("docs/a.txt"));
                Assert.AreEqual(Path.Combine(_root, "docs", "a.txt"),
                                fs.GetRealPath("docs/a.txt"));
            }
            finally
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  断言辅助
        // ════════════════════════════════════════════════════════════

        /// <summary>断言某条输入路径在指定文件系统上解析失败，且错误码符合预期。</summary>
        /// <param name="fs">目标文件系统。</param>
        /// <param name="rawPath">原始路径。</param>
        /// <param name="expected">期望的错误码。</param>
        private static void AssertFails(VirtualFileSystem fs, string rawPath,
                                        PathError expected)
        {
            VfsPath path;
            PathError error;
            var ok = fs.TryParsePath(rawPath, out path, out error);

            Assert.IsFalse(ok, $"\"{rawPath}\" 应当解析失败");
            Assert.IsNull(path);
            Assert.AreEqual(expected, error, $"\"{rawPath}\" 的错误码不符合预期");
        }

        /// <summary>
        /// 断言在默认（夹取）模式的文件系统上，输入解析成期望的规范化路径且不越界。
        /// </summary>
        /// <param name="rawPath">原始路径。</param>
        /// <param name="expected">期望的规范化路径。</param>
        private void AssertClamped(string rawPath, string expected)
        {
            VfsPath path;
            PathError error;
            var ok = _fs.TryParsePath(rawPath, out path, out error);

            Assert.IsTrue(ok, $"\"{rawPath}\" 应当解析成功，实际错误：{error}");
            Assert.AreEqual(expected, path.Normalized);
            Assert.IsFalse(path.EscapesRoot, $"\"{rawPath}\" 不应越界");
        }
    }
}
