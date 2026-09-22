using System.IO;
using NUnit.Framework;
using VoyageForge.Depot.Editor.FileSystem;

namespace VoyageForge.Depot.Tests
{
    /// <summary>
    /// <see cref="DiskPath"/> 的测试：真实磁盘上的路径判断。
    /// 需要在磁盘上真实建目录 / 文件，因此用临时目录做沙箱，
    /// 并在 TearDown 里清理，避免污染工程。
    /// </summary>
    [TestFixture]
    public class DiskPathTests
    {
        /// <summary>本次测试专属的临时根目录。</summary>
        private string _sandbox;

        [SetUp]
        public void SetUp()
        {
            _sandbox = Path.Combine(Path.GetTempPath(), "DiskPathTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_sandbox);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_sandbox))
                Directory.Delete(_sandbox, recursive: true);
        }

        // ---- IsFullyAbsolute ----

        /// <summary>由 GetFullPath 产出的路径一定是绝对路径。</summary>
        [Test]
        public void IsFullyAbsolute_绝对路径_返回true()
        {
            Assert.IsTrue(DiskPath.IsFullyAbsolute(Path.GetFullPath(_sandbox)));
        }

        /// <summary>相对路径与空值都不算绝对路径。</summary>
        [Test]
        public void IsFullyAbsolute_相对路径与空值_返回false()
        {
            Assert.IsFalse(DiskPath.IsFullyAbsolute("relative" + Path.DirectorySeparatorChar + "sub"));
            Assert.IsFalse(DiskPath.IsFullyAbsolute(""));
            Assert.IsFalse(DiskPath.IsFullyAbsolute("   "));
            Assert.IsFalse(DiskPath.IsFullyAbsolute(null));
        }

        // ---- IsSubPathOf ----

        /// <summary>下级路径应被判定为子路径，并算出相对路径。</summary>
        [Test]
        public void IsSubPathOf_下级路径_返回true与相对路径()
        {
            var child = Path.Combine(_sandbox, "a", "b");
            string relative;

            var result = DiskPath.IsSubPathOf(child, _sandbox, out relative);

            Assert.IsTrue(result);
            Assert.AreEqual(Path.Combine("a", "b"), relative);
        }

        /// <summary>路径与父路径相同时相对结果为 "."，也算作"在其下"。</summary>
        [Test]
        public void IsSubPathOf_路径相同_返回true与当前目录()
        {
            string relative;

            var result = DiskPath.IsSubPathOf(_sandbox, _sandbox, out relative);

            Assert.IsTrue(result);
            Assert.AreEqual(".", relative);
        }

        /// <summary>父路径之外（含父路径的上级）应返回 false。</summary>
        [Test]
        public void IsSubPathOf_路径之外_返回false()
        {
            var parent = Path.Combine(_sandbox, "a");
            var outside = Path.Combine(_sandbox, "b");
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(outside);

            string relative;

            Assert.IsFalse(DiskPath.IsSubPathOf(outside, parent, out relative));
            Assert.IsFalse(DiskPath.IsSubPathOf(_sandbox, parent, out relative));
        }

        /// <summary>任一参数为空时应返回 false，不抛异常。</summary>
        [Test]
        public void IsSubPathOf_空参数_返回false()
        {
            string relative;

            Assert.IsFalse(DiskPath.IsSubPathOf("", _sandbox, out relative));
            Assert.IsFalse(DiskPath.IsSubPathOf(_sandbox, "", out relative));
            Assert.IsFalse(DiskPath.IsSubPathOf(null, null, out relative));
        }

        // ---- GetKind ----

        /// <summary>能正确区分目录、文件与不存在。</summary>
        [Test]
        public void GetKind_区分目录文件与不存在()
        {
            var dir = Path.Combine(_sandbox, "dir");
            var file = Path.Combine(_sandbox, "file.txt");
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "x");

            Assert.AreEqual(PathKind.Directory, DiskPath.GetKind(dir));
            Assert.AreEqual(PathKind.File, DiskPath.GetKind(file));
            Assert.AreEqual(PathKind.NotFound, DiskPath.GetKind(Path.Combine(_sandbox, "missing")));
        }

        /// <summary>空值与含非法字符的路径都应归一成 NotFound，而不是抛异常。</summary>
        [Test]
        public void GetKind_空值与非法字符_返回NotFound()
        {
            Assert.AreEqual(PathKind.NotFound, DiskPath.GetKind(""));
            Assert.AreEqual(PathKind.NotFound, DiskPath.GetKind("   "));
            Assert.AreEqual(PathKind.NotFound, DiskPath.GetKind(null));
            // '\0' 会让底层 API 抛 ArgumentException，这里必须被兜住
            Assert.AreEqual(PathKind.NotFound, DiskPath.GetKind(_sandbox + "\0bad"));
        }

        /// <summary>ToAbsolute 应把相对路径解析成绝对路径。</summary>
        [Test]
        public void ToAbsolute_相对路径_解析成功()
        {
            var absolute = DiskPath.ToAbsolute(".");

            Assert.IsTrue(Path.IsPathRooted(absolute));
        }

        // ---- 相对根目录的换算（TryMakeRelativeToRoot）----

        /// <summary>"/" 开头的写法视为"相对根目录"，而不是当前盘符根目录。</summary>
        [Test]
        public void TryMakeRelativeToRoot_斜杠开头_视为相对根目录()
        {
            string relative;

            Assert.IsTrue(DiskPath.TryMakeRelativeToRoot(_sandbox, "/a/b.txt",
                                                         out relative, out _));
            Assert.AreEqual(Path.Combine("a", "b.txt"), relative);
        }

        /// <summary>普通相对写法原样保留。</summary>
        [Test]
        public void TryMakeRelativeToRoot_相对写法_原样保留()
        {
            string relative;

            Assert.IsTrue(DiskPath.TryMakeRelativeToRoot(_sandbox, "a/b.txt",
                                                         out relative, out _));
            Assert.AreEqual(Path.Combine("a", "b.txt"), relative);
        }

        /// <summary>".." 记号要保留下来，交给虚拟路径层去弹栈。</summary>
        [Test]
        public void TryMakeRelativeToRoot_保留上级记号()
        {
            string relative;

            Assert.IsTrue(DiskPath.TryMakeRelativeToRoot(_sandbox, "../x",
                                                         out relative, out _));
            Assert.AreEqual(Path.Combine("..", "x"), relative);
        }

        /// <summary>根目录之内的绝对路径应换算成相对写法。</summary>
        [Test]
        public void TryMakeRelativeToRoot_根内绝对路径_换算为相对()
        {
            var file = Path.Combine(_sandbox, "a", "b.txt");
            string relative;

            Assert.IsTrue(DiskPath.TryMakeRelativeToRoot(_sandbox, file,
                                                         out relative, out _));
            Assert.AreEqual(Path.Combine("a", "b.txt"), relative);
        }

        /// <summary>输入就是根目录自身时（绝对路径写法）输出 "."。</summary>
        [Test]
        public void TryMakeRelativeToRoot_根目录自身_换算为当前目录()
        {
            string relative;

            Assert.IsTrue(DiskPath.TryMakeRelativeToRoot(_sandbox, _sandbox,
                                                         out relative, out _));
            Assert.AreEqual(".", relative);
        }

        /// <summary>单独一个 "/" 也表示根目录自身。</summary>
        [Test]
        public void TryMakeRelativeToRoot_斜杠_表示根目录()
        {
            string relative;

            Assert.IsTrue(DiskPath.TryMakeRelativeToRoot(_sandbox, "/",
                                                         out relative, out _));
            Assert.AreEqual(".", relative);
        }

        /// <summary>根目录之外的绝对路径默认应报 OutsideRoot。</summary>
        [Test]
        public void TryMakeRelativeToRoot_根外绝对路径_报_OutsideRoot()
        {
            var outside = Path.Combine(Path.GetDirectoryName(_sandbox), "elsewhere");
            string relative;
            PathError error;

            Assert.IsFalse(DiskPath.TryMakeRelativeToRoot(_sandbox, outside,
                                                          out relative, out error));
            Assert.AreEqual(PathError.OutsideRoot, error);
        }

        /// <summary>允许越界时，根外绝对路径应换算成"从根退出去"的相对写法。</summary>
        [Test]
        public void TryMakeRelativeToRoot_允许越界_换算为退出去的写法()
        {
            var outside = Path.Combine(Path.GetDirectoryName(_sandbox), "elsewhere");
            string relative;
            PathError error;

            var ok = DiskPath.TryMakeRelativeToRoot(_sandbox, outside,
                                                    RootEscapeMode.Escape,
                                                    out relative, out error);

            Assert.IsTrue(ok);
            Assert.AreEqual(PathError.None, error);
            StringAssert.StartsWith("..", relative);
        }

        /// <summary>任一参数为空时失败，不抛异常。</summary>
        [Test]
        public void TryMakeRelativeToRoot_空参数_返回false()
        {
            string relative;
            PathError error;

            Assert.IsFalse(DiskPath.TryMakeRelativeToRoot("", "/a", out relative, out error));
            Assert.AreEqual(PathError.Empty, error);

            Assert.IsFalse(DiskPath.TryMakeRelativeToRoot(_sandbox, "", out relative, out error));
            Assert.AreEqual(PathError.Empty, error);
        }

        // ---- 拼接真实路径（CombineWithRoot）----

        /// <summary>
        /// 关键回归点：<c>Path.Combine(root, "/a")</c> 会因为第二个参数是"根路径"
        /// 而丢弃第一个参数；CombineWithRoot 必须先剥掉前导分隔符。
        /// </summary>
        [Test]
        public void CombineWithRoot_不会因前导斜杠丢掉根()
        {
            var real = DiskPath.CombineWithRoot(_sandbox, "/a/b.txt");

            Assert.AreEqual(Path.GetFullPath(Path.Combine(_sandbox, "a", "b.txt")), real);
            Assert.IsTrue(DiskPath.IsSubPathOf(real, _sandbox, out _));
        }

        /// <summary>根路径自身应原样换算回来。</summary>
        [Test]
        public void CombineWithRoot_根路径自身()
        {
            Assert.AreEqual(Path.GetFullPath(_sandbox), DiskPath.CombineWithRoot(_sandbox, "/"));
            Assert.AreEqual(Path.GetFullPath(_sandbox), DiskPath.CombineWithRoot(_sandbox, ""));
        }

        /// <summary>越界的 ".." 应被真正解析到根目录之外。</summary>
        [Test]
        public void CombineWithRoot_解析越界的上级记号()
        {
            var real = DiskPath.CombineWithRoot(_sandbox, "/../x.txt");

            Assert.AreEqual(Path.GetFullPath(Path.Combine(_sandbox, "..", "x.txt")), real);
            Assert.IsFalse(DiskPath.IsSubPathOf(real, _sandbox, out _));
        }
    }
}
