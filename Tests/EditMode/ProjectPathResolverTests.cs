using System.IO;
using NUnit.Framework;
using VoyageForge.Depot.Editor.FileSystem;

namespace VoyageForge.Depot.Tests
{
    /// <summary>
    /// <see cref="ProjectPathResolver"/> 的测试：把用户输入解析成"工程内的目录"。
    ///
    /// 这些用例原本散落在 PathInputField.Analysis 里（而且无法单测），
    /// 抽成独立的、不依赖 UnityEngine 的纯函数后就能直接跑。
    /// 测试用一个临时目录模拟工程根目录（真实场景里它就是 Application.dataPath）。
    /// </summary>
    [TestFixture]
    public class ProjectPathResolverTests
    {
        /// <summary>模拟的工程根目录（相当于 Assets）。</summary>
        private string _projectRoot;

        /// <summary>工程内的子目录。</summary>
        private string _subDir;

        /// <summary>工程内的另一个子目录，用于验证 ".." 归一化。</summary>
        private string _otherDir;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(),
                "ProjectPathResolverTests_" + System.Guid.NewGuid().ToString("N"));
            _subDir = Path.Combine(_projectRoot, "Sub");
            _otherDir = Path.Combine(_projectRoot, "Other");

            Directory.CreateDirectory(_subDir);
            Directory.CreateDirectory(_otherDir);
            File.WriteAllText(Path.Combine(_subDir, "a.txt"), "x");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
                Directory.Delete(_projectRoot, recursive: true);
        }

        // ---- 成功路径 ----

        /// <summary>工程根目录自身应表达为 "./"。</summary>
        [Test]
        public void TryResolve_工程根目录_返回默认相对路径()
        {
            string absolute;
            string relative;

            var ok = ProjectPathResolver.TryResolve(_projectRoot, _projectRoot,
                                                    out absolute, out relative);

            Assert.IsTrue(ok);
            Assert.AreEqual(ProjectPathResolver.RootRelativePath, relative);
            Assert.AreEqual("./", relative);
            Assert.AreEqual(_projectRoot, absolute);
        }

        /// <summary>工程内的子目录应表达为 "./子目录"。</summary>
        [Test]
        public void TryResolve_子目录_带相对前缀()
        {
            string absolute;
            string relative;

            var ok = ProjectPathResolver.TryResolve(_subDir, _projectRoot,
                                                    out absolute, out relative);

            Assert.IsTrue(ok);
            Assert.AreEqual("./Sub", relative);
            Assert.AreEqual(_subDir, absolute);
        }

        /// <summary>尾随分隔符应被裁掉，相对路径里不出现多余的分隔符。</summary>
        [Test]
        public void TryResolve_尾随分隔符_被裁掉()
        {
            string absolute;
            string relative;

            var ok = ProjectPathResolver.TryResolve(
                _subDir + Path.DirectorySeparatorChar, _projectRoot,
                out absolute, out relative);

            Assert.IsTrue(ok);
            Assert.AreEqual("./Sub", relative);
        }

        /// <summary>输入是文件时，地址栏应停在它所在的目录。</summary>
        [Test]
        public void TryResolve_文件路径_回退到所在目录()
        {
            string absolute;
            string relative;

            var ok = ProjectPathResolver.TryResolve(Path.Combine(_subDir, "a.txt"),
                                                    _projectRoot,
                                                    out absolute, out relative);

            Assert.IsTrue(ok);
            Assert.AreEqual("./Sub", relative);
            Assert.AreEqual(_subDir, absolute);
        }

        /// <summary>相对路径应以工程根目录为基准补全。</summary>
        [Test]
        public void TryResolve_相对路径_以工程根为基准()
        {
            string absolute;
            string relative;

            var ok = ProjectPathResolver.TryResolve("Sub", _projectRoot,
                                                    out absolute, out relative);

            Assert.IsTrue(ok);
            Assert.AreEqual("./Sub", relative);
        }

        /// <summary>路径中的 "." 与 ".." 应被归一化后再判定。</summary>
        [Test]
        public void TryResolve_含上级记号_归一化后仍在工程内()
        {
            string absolute;
            string relative;

            var ok = ProjectPathResolver.TryResolve(
                "Sub" + Path.DirectorySeparatorChar + ".." +
                Path.DirectorySeparatorChar + "Other",
                _projectRoot, out absolute, out relative);

            Assert.IsTrue(ok);
            Assert.AreEqual("./Other", relative);
            Assert.AreEqual(_otherDir, absolute);
        }

        // ---- 失败路径 ----

        /// <summary>空输入应直接失败。</summary>
        [Test]
        public void TryResolve_空输入_返回false()
        {
            string absolute;
            string relative;

            Assert.IsFalse(ProjectPathResolver.TryResolve(null, _projectRoot,
                                                          out absolute, out relative));
            Assert.IsFalse(ProjectPathResolver.TryResolve("", _projectRoot,
                                                          out absolute, out relative));
            Assert.IsFalse(ProjectPathResolver.TryResolve("   ", _projectRoot,
                                                          out absolute, out relative));
        }

        /// <summary>不存在的路径应失败（地址栏只接受真实存在的目录）。</summary>
        [Test]
        public void TryResolve_路径不存在_返回false()
        {
            string absolute;
            string relative;

            var ok = ProjectPathResolver.TryResolve(
                Path.Combine(_projectRoot, "missing"), _projectRoot,
                out absolute, out relative);

            Assert.IsFalse(ok);
            Assert.AreEqual("", relative);
            Assert.AreEqual("", absolute);
        }

        /// <summary>工程目录之外的路径必须被拒绝。</summary>
        [Test]
        public void TryResolve_工程目录之外_返回false()
        {
            string absolute;
            string relative;

            // 工程根目录的上一级
            var outside = Path.GetDirectoryName(_projectRoot);
            Assert.IsFalse(ProjectPathResolver.TryResolve(outside, _projectRoot,
                                                          out absolute, out relative));

            // 同级但不是下级的目录
            Assert.IsFalse(ProjectPathResolver.TryResolve(_otherDir + "_sibling", _projectRoot,
                                                          out absolute, out relative));
        }

        /// <summary>用 ".." 试图爬出工程目录时必须被拒绝。</summary>
        [Test]
        public void TryResolve_用上级记号爬出工程_返回false()
        {
            string absolute;
            string relative;

            var escaped = Path.Combine(_subDir, "..", "..");

            Assert.IsFalse(ProjectPathResolver.TryResolve(escaped, _projectRoot,
                                                          out absolute, out relative));
        }

        /// <summary>工程根目录参数为空时应失败，避免把相对路径解析到进程当前目录。</summary>
        [Test]
        public void TryResolve_工程根为空_返回false()
        {
            string absolute;
            string relative;

            Assert.IsFalse(ProjectPathResolver.TryResolve(_subDir, null,
                                                          out absolute, out relative));
            Assert.IsFalse(ProjectPathResolver.TryResolve(_subDir, "",
                                                          out absolute, out relative));
        }
    }
}
