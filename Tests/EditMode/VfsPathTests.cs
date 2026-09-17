using NUnit.Framework;
using VoyageForge.Depot.Editor.FileSystem;

namespace VoyageForge.Depot.Tests
{
    /// <summary>
    /// <see cref="VfsPath"/> 的整条路径测试：规范化、".." 弹栈、越根拒绝、
    /// 尾随分隔符识别，以及 Name / ParentPath / IsRoot 这些派生属性。
    /// </summary>
    [TestFixture]
    public class VfsPathTests
    {
        // ---- 规范化 ----

        /// <summary>多余的斜杠、"." 段都应被折叠掉。</summary>
        [Test]
        public void TryParse_冗余斜杠与当前目录_被规范化()
        {
            AssertNormalized("/a//b/./c", "/a/b/c");
            AssertNormalized("///a///b///", "/a/b");
            AssertNormalized("/././.", "/");
        }

        /// <summary>".." 应回退一级；退到根就停在根。</summary>
        [Test]
        public void TryParse_上一级目录_正确弹栈()
        {
            AssertNormalized("/a/b/../c", "/a/c");
            AssertNormalized("/a/b/../../c", "/c");
            AssertNormalized("/a/..", "/");
            AssertNormalized("/a/b/..", "/a");
        }

        /// <summary>反斜杠应被当作正斜杠处理，两条写法得到同一结果。</summary>
        [Test]
        public void TryParse_反斜杠分隔符_与正斜杠等价()
        {
            AssertNormalized(@"a\b\c", "/a/b/c");
            AssertNormalized(@"\a\b\", "/a/b");
        }

        /// <summary>整条路径首尾多余的空白应被裁掉，段名不应因此变成非法。</summary>
        [Test]
        public void TryParse_首尾空白_被裁掉()
        {
            AssertNormalized("  /a/b  ", "/a/b");
            AssertNormalized("\t/a/b\r\n", "/a/b");
        }

        // ---- 失败情形 ----

        /// <summary>null、空串、纯空白都应报 Empty。</summary>
        [Test]
        public void TryParse_空输入_报_Empty()
        {
            AssertFails(null, PathError.Empty);
            AssertFails("", PathError.Empty);
            AssertFails("   ", PathError.Empty);
        }

        /// <summary>越过根目录的 ".." 应被拒绝。</summary>
        [Test]
        public void TryParse_越过根目录_报_EscapeRoot()
        {
            AssertFails("/../escape", PathError.EscapeRoot);
            AssertFails("../x", PathError.EscapeRoot);
            AssertFails("/a/../../b", PathError.EscapeRoot);
        }

        /// <summary>段名非法时，错误码应透传自 PathValidator。</summary>
        [Test]
        public void TryParse_段名非法_透传错误码()
        {
            AssertFails("/a/CON.txt", PathError.ReservedName);
            AssertFails("/a/b.", PathError.TrailingSpaceOrDot);
            AssertFails("/a/na|me", PathError.IllegalChar);
        }

        /// <summary>超过总长度上限的路径应报 TooLong。</summary>
        [Test]
        public void TryParse_总长度超限_报_TooLong()
        {
            // 长度检查发生在拆段之前，所以只要原文够长就会先报 TooLong，
            // 不会再往下走到"段名超长"的规则。
            var tooLong = "/" + new string('a', PathValidator.MaxPathLength + 1);

            AssertFails(tooLong, PathError.TooLong);
        }

        // ---- 尾随分隔符 ----

        /// <summary>尾随分隔符是"用户想要目录"的语法线索，必须被准确记录。</summary>
        [Test]
        public void HasTrailingSeparator_反映原始写法()
        {
            Assert.IsTrue(VfsPath.Parse("/a/b/").HasTrailingSeparator);
            Assert.IsTrue(VfsPath.Parse(@"a\b\").HasTrailingSeparator);
            Assert.IsTrue(VfsPath.Parse("docs/").HasTrailingSeparator);

            // 根目录写作 "/"，字面上就是"以分隔符结尾"；而且根本来就一定是目录，
            // 所以这里为 true 是符合语义的（推断意图时会得到 WantDirectory）。
            Assert.IsTrue(VfsPath.Parse("/").HasTrailingSeparator);

            Assert.IsFalse(VfsPath.Parse("/a/b").HasTrailingSeparator);
            // 结尾是空格而不是分隔符，不算尾随分隔符（空白会被裁掉）
            Assert.IsFalse(VfsPath.Parse("/a/b ").HasTrailingSeparator);
        }

        // ---- 派生属性 ----

        /// <summary>根目录的 Name 为空串、IsRoot 为 true、父目录仍是自己。</summary>
        [Test]
        public void 根目录属性_符合约定()
        {
            var root = VfsPath.Parse("/");

            Assert.IsTrue(root.IsRoot);
            Assert.AreEqual("", root.Name);
            Assert.AreEqual("/", root.ParentPath);
            Assert.AreEqual("/", root.Normalized);
            Assert.AreEqual(0, root.Segments.Count);
        }

        /// <summary>Name 取最后一段，ParentPath 取去掉最后一段的结果。</summary>
        [Test]
        public void Name与ParentPath_取正确的层级()
        {
            var path = VfsPath.Parse("/a/b/c.txt");

            Assert.AreEqual("c.txt", path.Name);
            Assert.AreEqual("/a/b", path.ParentPath);
            Assert.IsFalse(path.IsRoot);
            CollectionAssert.AreEqual(new[] { "a", "b", "c.txt" }, path.Segments);

            // 只有一级时，父目录就是根
            Assert.AreEqual("/", VfsPath.Parse("/a").ParentPath);
        }

        /// <summary>Original 保留用户原话，Normalized 是规范化结果。</summary>
        [Test]
        public void Original_保留原始写法()
        {
            var path = VfsPath.Parse("/a//b/");

            Assert.AreEqual("/a//b/", path.Original);
            Assert.AreEqual("/a/b", path.Normalized);
            Assert.AreEqual("/a/b", path.ToString());
        }

        /// <summary>Parse 在路径非法时应抛 ArgumentException。</summary>
        [Test]
        public void Parse_非法路径_抛异常()
        {
            Assert.Throws<System.ArgumentException>(() => VfsPath.Parse("/a/CON"));
            Assert.Throws<System.ArgumentException>(() => VfsPath.Parse("   "));
            Assert.Throws<System.ArgumentException>(() => VfsPath.Parse("/../x"));
        }

        // ---- 断言辅助 ----

        /// <summary>断言解析成功且规范化结果符合预期。</summary>
        /// <param name="raw">原始路径。</param>
        /// <param name="expected">期望的规范化路径。</param>
        private static void AssertNormalized(string raw, string expected)
        {
            VfsPath path;
            PathError error;
            var ok = VfsPath.TryParse(raw, out path, out error);

            Assert.IsTrue(ok, $"\"{raw}\" 应当解析成功，实际错误：{error}");
            Assert.AreEqual(expected, path.Normalized);
        }

        /// <summary>断言解析失败且错误码符合预期。</summary>
        /// <param name="raw">原始路径。</param>
        /// <param name="expected">期望的错误码。</param>
        private static void AssertFails(string raw, PathError expected)
        {
            VfsPath path;
            PathError error;
            var ok = VfsPath.TryParse(raw, out path, out error);

            Assert.IsFalse(ok, $"\"{raw}\" 应当解析失败");
            Assert.IsNull(path);
            Assert.AreEqual(expected, error);
        }
    }
}
