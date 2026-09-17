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
    }
}
