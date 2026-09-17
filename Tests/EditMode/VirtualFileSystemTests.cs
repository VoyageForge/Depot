using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using VoyageForge.Depot.Editor.FileSystem;

namespace VoyageForge.Depot.Tests
{
    /// <summary>
    /// <see cref="VirtualFileSystem"/> 的测试：查找、类型判定、意图推断、
    /// 增删改查、移动复制，以及 <see cref="VfsImageSerializer"/> 的镜像往返。
    /// 这些测试替代了原先堆在源码文件末尾的 Program 演示类。
    /// </summary>
    [TestFixture]
    public class VirtualFileSystemTests
    {
        /// <summary>每个测试用一个全新的空文件系统，避免相互污染。</summary>
        private VirtualFileSystem _fs;

        [SetUp]
        public void SetUp()
        {
            _fs = new VirtualFileSystem();
        }

        // ════════════════════════════════════════════════════════════
        //  创建 / 查找 / 读取
        // ════════════════════════════════════════════════════════════

        /// <summary>CreateDirectory 应逐级创建（类似 mkdir -p）。</summary>
        [Test]
        public void CreateDirectory_多级路径_逐级创建()
        {
            _fs.CreateDirectory("/docs/images/2024");

            Assert.IsTrue(_fs.Exists("/docs"));
            Assert.IsTrue(_fs.Exists("/docs/images"));
            Assert.IsTrue(_fs.Exists("/docs/images/2024"));
            Assert.AreEqual(PathKind.Directory, _fs.GetPathKind("/docs/images"));
        }

        /// <summary>重复创建同一目录应幂等，返回同一个节点。</summary>
        [Test]
        public void CreateDirectory_已存在_幂等()
        {
            var first = _fs.CreateDirectory("/docs");
            var second = _fs.CreateDirectory("/docs");

            Assert.AreSame(first, second);
        }

        /// <summary>路径中间是文件时，无法继续往下建目录。</summary>
        [Test]
        public void CreateDirectory_中间是文件_抛异常()
        {
            _fs.CreateFile("/a", "x");

            Assert.Throws<IOException>(() => _fs.CreateDirectory("/a/b"));
        }

        /// <summary>创建文件后应能读回原文；UTF-8 中文也要正常。</summary>
        [Test]
        public void CreateFile_写文本_可原样读回()
        {
            _fs.CreateDirectory("/docs");
            _fs.CreateFile("/docs/readme.md", "你好，虚拟文件系统！");

            Assert.AreEqual("你好，虚拟文件系统！", _fs.ReadAllText("/docs/readme.md"));
        }

        /// <summary>父目录不存在时创建文件应报 DirectoryNotFoundException。</summary>
        [Test]
        public void CreateFile_父目录不存在_抛异常()
        {
            Assert.Throws<DirectoryNotFoundException>(
                () => _fs.CreateFile("/nope/a.txt", "x"));
        }

        /// <summary>同名节点已存在时不能覆盖。</summary>
        [Test]
        public void CreateFile_已存在同名节点_抛异常()
        {
            _fs.CreateDirectory("/docs");
            _fs.CreateFile("/docs/a.txt", "x");

            Assert.Throws<IOException>(() => _fs.CreateFile("/docs/a.txt", "y"));
        }

        /// <summary>根目录不能被当成文件。</summary>
        [Test]
        public void CreateFile_根目录_抛异常()
        {
            Assert.Throws<IOException>(() => _fs.CreateFile("/", "x"));
        }

        /// <summary>读取一个目录（或不存在的东西）应报 FileNotFoundException。</summary>
        [Test]
        public void ReadAllText_不是文件_抛异常()
        {
            _fs.CreateDirectory("/docs");

            Assert.Throws<FileNotFoundException>(() => _fs.ReadAllText("/docs"));
            Assert.Throws<FileNotFoundException>(() => _fs.ReadAllText("/docs/missing.txt"));
        }

        /// <summary>按字节写入的文件长度应准确。</summary>
        [Test]
        public void CreateFile_按字节写入_长度正确()
        {
            _fs.CreateDirectory("/bin");
            var file = _fs.CreateFile("/bin/a.bin", new byte[] { 1, 2, 3, 4, 5 });

            Assert.AreEqual(5, file.Length);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, file.ReadAllBytes());
        }

        // ════════════════════════════════════════════════════════════
        //  路径类型判定
        // ════════════════════════════════════════════════════════════

        /// <summary>类型必须靠"定位到节点再读 Type"得出。</summary>
        [Test]
        public void GetPathKind_区分文件目录与不存在()
        {
            _fs.CreateDirectory("/docs/images");
            _fs.CreateFile("/docs/readme.md", "hello");

            Assert.AreEqual(PathKind.Directory, _fs.GetPathKind("/docs"));
            Assert.AreEqual(PathKind.Directory, _fs.GetPathKind("/docs/images"));
            Assert.AreEqual(PathKind.File, _fs.GetPathKind("/docs/readme.md"));
            Assert.AreEqual(PathKind.NotFound, _fs.GetPathKind("/docs/nothing"));
            // 拿文件当目录继续往下走，应当当作"不存在"
            Assert.AreEqual(PathKind.NotFound, _fs.GetPathKind("/docs/readme.md/x"));
        }

        /// <summary>非法路径在查询接口里应表现为"不存在"，而不是抛异常。</summary>
        [Test]
        public void GetPathKind_非法路径_当作不存在()
        {
            Assert.AreEqual(PathKind.NotFound, _fs.GetPathKind("/a/CON"));
            Assert.AreEqual(PathKind.NotFound, _fs.GetPathKind("   "));
            Assert.AreEqual(PathKind.NotFound, _fs.GetPathKind("/../escape"));
        }

        /// <summary>TryResolve 应一次拿到节点引用和类型，省掉第二次查找。</summary>
        [Test]
        public void TryResolve_同时给出节点与类型()
        {
            _fs.CreateDirectory("/docs");
            _fs.CreateFile("/docs/readme.md", "content");

            VfsNode node;
            PathKind kind;
            var ok = _fs.TryResolve("/docs/readme.md", out node, out kind);

            Assert.IsTrue(ok);
            Assert.AreEqual(PathKind.File, kind);
            Assert.IsInstanceOf<VfsFile>(node);
            Assert.AreEqual("content", ((VfsFile)node).ReadAllText());
            Assert.AreEqual("/docs/readme.md", node.FullPath);
        }

        /// <summary>Exists 对非法路径与不存在的路径都返回 false，不抛异常。</summary>
        [Test]
        public void Exists_非法或不存在_返回false()
        {
            Assert.IsFalse(_fs.Exists("/nope"));
            Assert.IsFalse(_fs.Exists("/a/CON"));
            Assert.IsFalse(_fs.Exists("   "));
        }

        /// <summary>Find(string) 走的是 Parse，非法路径应当抛异常。</summary>
        [Test]
        public void Find_字符串重载_非法路径抛异常()
        {
            Assert.Throws<System.ArgumentException>(() => _fs.Find("/a/CON"));
        }

        // ════════════════════════════════════════════════════════════
        //  意图推断
        // ════════════════════════════════════════════════════════════

        /// <summary>只有尾随分隔符是可靠线索；扩展名不算。</summary>
        [Test]
        public void GuessIntent_只看尾随分隔符()
        {
            Assert.AreEqual(PathIntent.WantDirectory,
                VirtualFileSystem.GuessIntent(VfsPath.Parse("/docs/sub/")));
            Assert.AreEqual(PathIntent.Unknown,
                VirtualFileSystem.GuessIntent(VfsPath.Parse("/docs/a.txt")));
            Assert.AreEqual(PathIntent.Unknown,
                VirtualFileSystem.GuessIntent(VfsPath.Parse("/docs/plain")));
        }

        /// <summary>CreateByIntent 应把校验、推断、创建串成一条龙。</summary>
        [Test]
        public void CreateByIntent_按写法创建目录或文件()
        {
            // 尾随斜杠 → 建目录
            var dir = _fs.CreateByIntent("/logs/");
            Assert.IsInstanceOf<VfsDirectory>(dir);

            // 无尾随斜杠 + 兜底 Unknown → 按文件处理
            var file = _fs.CreateByIntent("/logs/archive");
            Assert.IsInstanceOf<VfsFile>(file);
        }

        /// <summary>兜底值可以强制把"没有语法线索"的路径建成目录。</summary>
        [Test]
        public void CreateByIntent_兜底值_决定类型()
        {
            var node = _fs.CreateByIntent("/data", PathIntent.WantDirectory);

            Assert.IsInstanceOf<VfsDirectory>(node);
        }

        /// <summary>路径已存在时应直接返回已有节点，不重复创建。</summary>
        [Test]
        public void CreateByIntent_已存在_返回已有节点()
        {
            var first = _fs.CreateByIntent("/logs/");
            var second = _fs.CreateByIntent("/logs/");

            Assert.AreSame(first, second);
        }

        /// <summary>非法路径应抛异常，由 VfsPath.Parse 负责把关。</summary>
        [Test]
        public void CreateByIntent_非法路径_抛异常()
        {
            Assert.Throws<System.ArgumentException>(() => _fs.CreateByIntent("/a/CON.txt"));
            Assert.Throws<System.ArgumentException>(() => _fs.CreateByIntent("   "));
        }

        // ════════════════════════════════════════════════════════════
        //  列出与遍历
        // ════════════════════════════════════════════════════════════

        /// <summary>List 只给出直接子节点。</summary>
        [Test]
        public void List_只返回直接子节点()
        {
            _fs.CreateDirectory("/docs/images");
            _fs.CreateFile("/docs/readme.md", "x");

            IReadOnlyCollection<VfsNode> children = _fs.List("/docs");

            Assert.AreEqual(2, children.Count);
            CollectionAssert.AreEquivalent(new[] { "images", "readme.md" },
                                           children.Select(c => c.Name).ToArray());
        }

        /// <summary>对文件调用 List 应报 DirectoryNotFoundException。</summary>
        [Test]
        public void List_不是目录_抛异常()
        {
            _fs.CreateDirectory("/docs");
            _fs.CreateFile("/docs/a.txt", "x");

            Assert.Throws<DirectoryNotFoundException>(() => _fs.List("/docs/a.txt"));
            Assert.Throws<DirectoryNotFoundException>(() => _fs.List("/missing"));
        }

        /// <summary>Walk 默认递归给出整棵子树的所有节点（不含起点自身）。</summary>
        [Test]
        public void Walk_递归_给出整棵子树()
        {
            _fs.CreateDirectory("/a/b/c");
            _fs.CreateFile("/a/b/one.txt", "1");
            _fs.CreateFile("/a/two.txt", "2");

            List<string> paths = _fs.Walk("/").Select(n => n.FullPath).ToList();

            CollectionAssert.AreEquivalent(
                new[] { "/a", "/a/b", "/a/b/c", "/a/b/one.txt", "/a/two.txt" },
                paths);
        }

        /// <summary>recursive = false 时只遍历一层。</summary>
        [Test]
        public void Walk_非递归_只遍历一层()
        {
            _fs.CreateDirectory("/a/b/c");
            _fs.CreateFile("/a/two.txt", "2");

            List<string> paths = _fs.Walk("/a", recursive: false)
                                    .Select(n => n.FullPath).ToList();

            CollectionAssert.AreEquivalent(new[] { "/a/b", "/a/two.txt" }, paths);
        }

        // ════════════════════════════════════════════════════════════
        //  删除
        // ════════════════════════════════════════════════════════════

        /// <summary>删除文件后不应再查到。</summary>
        [Test]
        public void Delete_文件_移除成功()
        {
            _fs.CreateDirectory("/docs");
            _fs.CreateFile("/docs/a.txt", "x");

            Assert.IsTrue(_fs.Delete("/docs/a.txt"));
            Assert.IsFalse(_fs.Exists("/docs/a.txt"));
        }

        /// <summary>删除不存在的路径返回 false，不抛异常。</summary>
        [Test]
        public void Delete_不存在_返回false()
        {
            Assert.IsFalse(_fs.Delete("/nope"));
        }

        /// <summary>递归删除应把整棵子树摘掉。</summary>
        [Test]
        public void Delete_目录递归_整棵移除()
        {
            _fs.CreateDirectory("/docs/images/2024");
            _fs.CreateFile("/docs/readme.md", "x");

            Assert.IsTrue(_fs.Delete("/docs"));

            Assert.IsFalse(_fs.Exists("/docs"));
            Assert.IsFalse(_fs.Exists("/docs/images/2024"));
            Assert.IsFalse(_fs.Exists("/docs/readme.md"));
        }

        /// <summary>非递归删除非空目录必须失败。</summary>
        [Test]
        public void Delete_非空目录且不递归_抛异常()
        {
            _fs.CreateDirectory("/docs/images");
            _fs.CreateFile("/docs/a.txt", "x");

            Assert.Throws<IOException>(() => _fs.Delete("/docs", recursive: false));

            // 失败后结构应保持完整
            Assert.IsTrue(_fs.Exists("/docs/images"));
            Assert.IsTrue(_fs.Exists("/docs/a.txt"));
        }

        /// <summary>空目录即使非递归也能删掉。</summary>
        [Test]
        public void Delete_空目录且不递归_成功()
        {
            _fs.CreateDirectory("/docs/empty");

            Assert.IsTrue(_fs.Delete("/docs/empty", recursive: false));
            Assert.IsFalse(_fs.Exists("/docs/empty"));
        }

        /// <summary>根目录不允许删除。</summary>
        [Test]
        public void Delete_根目录_抛异常()
        {
            Assert.Throws<IOException>(() => _fs.Delete("/"));
        }

        // ════════════════════════════════════════════════════════════
        //  移动
        // ════════════════════════════════════════════════════════════

        /// <summary>移动文件等价于重命名，父指针和 FullPath 都要跟着更新。</summary>
        [Test]
        public void Move_重命名文件_路径同步更新()
        {
            _fs.CreateDirectory("/dst");
            _fs.CreateFile("/a.txt", "x");

            var node = _fs.Move("/a.txt", "/dst/b.txt");

            Assert.IsFalse(_fs.Exists("/a.txt"));
            Assert.AreEqual("/dst/b.txt", node.FullPath);
            Assert.AreEqual("x", _fs.ReadAllText("/dst/b.txt"));
        }

        /// <summary>移动目录时，整棵子树的 FullPath 都应随之改变。</summary>
        [Test]
        public void Move_目录_子树路径全部更新()
        {
            _fs.CreateDirectory("/src/inner");
            _fs.CreateFile("/src/inner/a.txt", "x");
            _fs.CreateDirectory("/dst");

            var node = _fs.Move("/src", "/dst/src");

            Assert.AreEqual("/dst/src", node.FullPath);
            Assert.AreEqual("/dst/src/inner/a.txt",
                            ((VfsFile)_fs.Find("/dst/src/inner/a.txt")).FullPath);
        }

        /// <summary>源和目标相同时应原地返回同一个节点。</summary>
        [Test]
        public void Move_源与目标相同_返回原节点()
        {
            _fs.CreateDirectory("/docs");
            _fs.CreateFile("/docs/a.txt", "x");

            var node = _fs.Move("/docs/a.txt", "/docs/a.txt");

            Assert.AreEqual("/docs/a.txt", node.FullPath);
            Assert.IsTrue(_fs.Exists("/docs/a.txt"));
        }

        /// <summary>源不存在应报 FileNotFoundException。</summary>
        [Test]
        public void Move_源不存在_抛异常()
        {
            _fs.CreateDirectory("/dst");

            Assert.Throws<FileNotFoundException>(() => _fs.Move("/nope", "/dst/x"));
        }

        /// <summary>目标父目录不存在应报 DirectoryNotFoundException。</summary>
        [Test]
        public void Move_目标目录不存在_抛异常()
        {
            _fs.CreateFile("/a.txt", "x");

            Assert.Throws<DirectoryNotFoundException>(() => _fs.Move("/a.txt", "/nope/a.txt"));
        }

        /// <summary>目标已有同名节点时应拒绝，避免静默覆盖。</summary>
        [Test]
        public void Move_目标已存在_抛异常()
        {
            _fs.CreateDirectory("/dst");
            _fs.CreateFile("/a.txt", "x");
            _fs.CreateFile("/dst/a.txt", "y");

            Assert.Throws<IOException>(() => _fs.Move("/a.txt", "/dst/a.txt"));
        }

        /// <summary>把目录移进自己的子树会形成环，必须拦住。</summary>
        [Test]
        public void Move_移入自身子树_抛异常()
        {
            _fs.CreateDirectory("/a/b");

            Assert.Throws<IOException>(() => _fs.Move("/a", "/a/b/c"));
        }

        /// <summary>根目录不能作为移动的源或目标。</summary>
        [Test]
        public void Move_根目录_抛异常()
        {
            _fs.CreateDirectory("/a");

            Assert.Throws<IOException>(() => _fs.Move("/", "/a"));
            // 目标为根会让节点名字变成空串、把树弄坏，必须拒绝
            Assert.Throws<IOException>(() => _fs.Move("/a", "/"));
        }

        // ════════════════════════════════════════════════════════════
        //  复制
        // ════════════════════════════════════════════════════════════

        /// <summary>复制文件必须是真的复制，改副本不能影响原件。</summary>
        [Test]
        public void Copy_文件_内容相互独立()
        {
            _fs.CreateDirectory("/dst");
            _fs.CreateFile("/a.txt", "original");

            var copy = _fs.Copy("/a.txt", "/dst/a.txt");

            var original = (VfsFile)_fs.Find("/a.txt");
            Assert.AreNotSame(original, copy);
            Assert.AreEqual("original", _fs.ReadAllText("/dst/a.txt"));

            ((VfsFile)copy).WriteAllText("changed");

            Assert.AreEqual("original", original.ReadAllText());
            Assert.AreEqual("changed", _fs.ReadAllText("/dst/a.txt"));
        }

        /// <summary>复制目录应深拷贝整棵子树，且父子关系正确。</summary>
        [Test]
        public void Copy_目录_深拷贝整棵子树()
        {
            _fs.CreateDirectory("/src/inner");
            _fs.CreateFile("/src/inner/a.txt", "x");
            _fs.CreateFile("/src/top.txt", "y");

            _fs.Copy("/src", "/src2");

            Assert.AreEqual(2, _fs.List("/src2").Count);
            Assert.AreEqual("x", _fs.ReadAllText("/src2/inner/a.txt"));
            Assert.AreEqual("/src2/inner/a.txt",
                            _fs.Find("/src2/inner/a.txt").FullPath);
        }

        /// <summary>目标已存在时应拒绝。</summary>
        [Test]
        public void Copy_目标已存在_抛异常()
        {
            _fs.CreateDirectory("/dst");
            _fs.CreateFile("/a.txt", "x");
            _fs.CreateFile("/dst/a.txt", "y");

            Assert.Throws<IOException>(() => _fs.Copy("/a.txt", "/dst/a.txt"));
        }

        /// <summary>复制到自己的子树会导致无穷递归，必须拦住。</summary>
        [Test]
        public void Copy_复制到自身子树_抛异常()
        {
            _fs.CreateDirectory("/a/b");

            Assert.Throws<IOException>(() => _fs.Copy("/a", "/a/b/c"));
        }

        /// <summary>目标为根会让节点名字变成空串，必须拒绝。</summary>
        [Test]
        public void Copy_目标为根_抛异常()
        {
            _fs.CreateFile("/a.txt", "x");

            Assert.Throws<IOException>(() => _fs.Copy("/a.txt", "/"));
        }

        // ════════════════════════════════════════════════════════════
        //  统计
        // ════════════════════════════════════════════════════════════

        /// <summary>统计应给出文件数、目录数与字节总数，且不把根目录算进去。</summary>
        [Test]
        public void VfsStatistics_统计文件目录与字节()
        {
            _fs.CreateDirectory("/docs/images");
            _fs.CreateFile("/docs/readme.md", "12345");        // 5 字节
            _fs.CreateFile("/docs/images/logo.png", "1234567"); // 7 字节

            var stats = VfsStatistics.From(_fs);

            Assert.AreEqual(2, stats.FileCount);
            Assert.AreEqual(2, stats.DirectoryCount);
            Assert.AreEqual(12, stats.TotalBytes);
        }

        /// <summary>空文件系统的统计应全为零。</summary>
        [Test]
        public void VfsStatistics_空文件系统_全为零()
        {
            var stats = VfsStatistics.From(_fs);

            Assert.AreEqual(0, stats.FileCount);
            Assert.AreEqual(0, stats.DirectoryCount);
            Assert.AreEqual(0, stats.TotalBytes);
        }

        // ════════════════════════════════════════════════════════════
        //  持久化
        // ════════════════════════════════════════════════════════════

        /// <summary>保存再加载应还原出完全一致的目录树、内容与时间戳。</summary>
        [Test]
        public void 镜像往返_结构与内容一致()
        {
            _fs.CreateDirectory("/data/logs");
            _fs.CreateFile("/data/logs/app.log", "2024-01-01 启动");
            _fs.CreateFile("/data/readme.md", "示例文件");

            var originalLog = (VfsFile)_fs.Find("/data/logs/app.log");

            VirtualFileSystem reloaded;
            using (var stream = new MemoryStream())
            {
                VfsImageSerializer.Save(_fs, stream);
                stream.Position = 0;
                reloaded = VfsImageSerializer.Load(stream);
            }

            Assert.AreEqual("2024-01-01 启动", reloaded.ReadAllText("/data/logs/app.log"));
            Assert.AreEqual("示例文件", reloaded.ReadAllText("/data/readme.md"));
            CollectionAssert.AreEquivalent(
                _fs.Walk("/").Select(n => n.FullPath).ToList(),
                reloaded.Walk("/").Select(n => n.FullPath).ToList());

            // 时间戳应精确保留，而不是被"刚写入"覆盖
            var reloadedLog = (VfsFile)reloaded.Find("/data/logs/app.log");
            Assert.AreEqual(originalLog.CreatedAt, reloadedLog.CreatedAt);
            Assert.AreEqual(originalLog.ModifiedAt, reloadedLog.ModifiedAt);
        }

        /// <summary>二进制内容必须逐字节还原。</summary>
        [Test]
        public void 镜像往返_二进制内容一致()
        {
            var payload = new byte[] { 0, 1, 2, 255, 254, 128, 64 };
            _fs.CreateDirectory("/bin");
            _fs.CreateFile("/bin/blob.dat", payload);

            using (var stream = new MemoryStream())
            {
                VfsImageSerializer.Save(_fs, stream);
                stream.Position = 0;
                var reloaded = VfsImageSerializer.Load(stream);

                CollectionAssert.AreEqual(payload,
                    ((VfsFile)reloaded.Find("/bin/blob.dat")).ReadAllBytes());
            }
        }

        /// <summary>魔数不匹配的流应被拒绝。</summary>
        [Test]
        public void 加载_魔数错误_抛异常()
        {
            using (var stream = new MemoryStream())
            {
                VfsImageSerializer.Save(_fs, stream);
                var bytes = stream.ToArray();
                bytes[0] = (byte)(bytes[0] ^ 0xFF);   // 破坏魔数首字节

                Assert.Throws<InvalidDataException>(
                    () => VfsImageSerializer.Load(new MemoryStream(bytes)));
            }
        }

        /// <summary>版本号不匹配时应拒绝，避免用旧规则误解析新格式。</summary>
        [Test]
        public void 加载_版本不支持_抛异常()
        {
            using (var stream = new MemoryStream())
            {
                VfsImageSerializer.Save(_fs, stream);
                var bytes = stream.ToArray();

                // 布局：Magic(4) + Version(4)，小端写入，版本低字节在下标 4
                bytes[4] = 99;

                Assert.Throws<InvalidDataException>(
                    () => VfsImageSerializer.Load(new MemoryStream(bytes)));
            }
        }

        /// <summary>用 UTF-8 编码写入的文本在往返后仍能正确解码。</summary>
        [Test]
        public void 镜像往返_编码为UTF8()
        {
            _fs.CreateDirectory("/i18n");
            _fs.CreateFile("/i18n/zh.txt", "中文", Encoding.UTF8);

            using (var stream = new MemoryStream())
            {
                VfsImageSerializer.Save(_fs, stream);
                stream.Position = 0;
                var reloaded = VfsImageSerializer.Load(stream);

                Assert.AreEqual("中文", reloaded.ReadAllText("/i18n/zh.txt"));
            }
        }
    }
}
