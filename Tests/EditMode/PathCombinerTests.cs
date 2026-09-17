using NUnit.Framework;
using VoyageForge.Depot.Editor.FileSystem;

namespace VoyageForge.Depot.Tests
{
    /// <summary>
    /// <see cref="PathCombiner"/> 的测试：三种拼接场景（AppendName / Resolve / Combine）
    /// 以及反向的 TryGetRelative。
    /// </summary>
    [TestFixture]
    public class PathCombinerTests
    {
        // ---- 场景 A：父目录 + 单个子名 ----

        /// <summary>子名附加到父路径后应得到完整的规范化路径。</summary>
        [Test]
        public void AppendName_合法子名_拼出完整路径()
        {
            var parent = VfsPath.Parse("/docs");
            var result = PathCombiner.AppendName(parent, "readme.md");

            Assert.AreEqual("/docs/readme.md", result.Normalized);
            Assert.AreEqual("readme.md", result.Name);
            Assert.AreEqual("/docs", result.ParentPath);
        }

        /// <summary>根目录作为父路径时应拼出单级路径，不能出现 "//"。</summary>
        [Test]
        public void AppendName_父路径是根_拼出单级路径()
        {
            var result = PathCombiner.AppendName(VfsPath.Parse("/"), "a");

            Assert.AreEqual("/a", result.Normalized);
        }

        /// <summary>子名含分隔符时属于调用错误，应报 InvalidSegment（提示改用 Combine）。</summary>
        [Test]
        public void AppendName_子名含分隔符_报_InvalidSegment()
        {
            VfsPath result;
            PathError error;
            var ok = PathCombiner.AppendName(VfsPath.Parse("/docs"), "a/b", out result, out error);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
            Assert.AreEqual(PathError.InvalidSegment, error);
        }

        /// <summary>子名为空或纯空白应报 Empty。</summary>
        [Test]
        public void AppendName_子名为空_报_Empty()
        {
            VfsPath result;
            PathError error;

            Assert.IsFalse(PathCombiner.AppendName(VfsPath.Parse("/docs"), "", out result, out error));
            Assert.AreEqual(PathError.Empty, error);

            Assert.IsFalse(PathCombiner.AppendName(VfsPath.Parse("/docs"), "   ", out result, out error));
            Assert.AreEqual(PathError.Empty, error);
        }

        /// <summary>子名非法时错误码应透传，且能定位到该片段。</summary>
        [Test]
        public void AppendName_子名非法_透传错误码()
        {
            VfsPath result;
            PathError error;
            var ok = PathCombiner.AppendName(VfsPath.Parse("/docs"), "NUL", out result, out error);

            Assert.IsFalse(ok);
            Assert.AreEqual(PathError.ReservedName, error);
        }

        /// <summary>抛异常重载在失败时应抛 ArgumentException，消息里带上出错的片段。</summary>
        [Test]
        public void AppendName_失败重载_抛出并带片段信息()
        {
            var ex = Assert.Throws<System.ArgumentException>(
                () => PathCombiner.AppendName(VfsPath.Parse("/docs"), "a/b"));

            StringAssert.Contains("a/b", ex.Message);
        }

        // ---- 场景 B：基准路径 + 相对路径 ----

        /// <summary>".." 应相对基准路径回退。</summary>
        [Test]
        public void Resolve_相对回退_基于基准路径()
        {
            Assert.AreEqual("/docs/a.txt",
                PathCombiner.Resolve("/docs/sub", "../a.txt").Normalized);
            Assert.AreEqual("/docs/b/c",
                PathCombiner.Resolve("/docs", "./b/c").Normalized);
            Assert.AreEqual("/a",
                PathCombiner.Resolve("/", "a").Normalized);
        }

        /// <summary>相对路径以分隔符开头时等价于绝对路径，基准路径被忽略。</summary>
        [Test]
        public void Resolve_绝对相对路径_覆盖基准()
        {
            Assert.AreEqual("/etc/x",
                PathCombiner.Resolve("/docs", "/etc/x").Normalized);
        }

        /// <summary>基准路径非法时应直接失败，不进入相对路径处理。</summary>
        [Test]
        public void Resolve_基准非法_失败()
        {
            VfsPath result;
            PathError error;
            var ok = PathCombiner.Resolve("/a/CON", "b", out result, out error);

            Assert.IsFalse(ok);
            Assert.AreEqual(PathError.ReservedName, error);
        }

        /// <summary>相对路径为空时报 Empty。</summary>
        [Test]
        public void Resolve_相对路径为空_报_Empty()
        {
            VfsPath result;
            PathError error;
            var ok = PathCombiner.Resolve("/docs", "   ", out result, out error);

            Assert.IsFalse(ok);
            Assert.AreEqual(PathError.Empty, error);
        }

        /// <summary>从根目录再往上走应报 EscapeRoot。</summary>
        [Test]
        public void Resolve_从根继续向上_报_EscapeRoot()
        {
            VfsPath result;
            PathError error;
            var ok = PathCombiner.Resolve("/", "../x", out result, out error);

            Assert.IsFalse(ok);
            Assert.AreEqual(PathError.EscapeRoot, error);
        }

        // ---- 场景 C：多片段拼接 ----

        /// <summary>多个普通片段按顺序拼接。</summary>
        [Test]
        public void Combine_多片段_按顺序拼接()
        {
            Assert.AreEqual("/root/user/f.txt",
                PathCombiner.Combine("/root", "user", "f.txt").Normalized);
        }

        /// <summary>出现绝对片段时，之前拼好的部分被丢弃（与 Path.Combine 语义一致）。</summary>
        [Test]
        public void Combine_绝对片段_重置结果()
        {
            Assert.AreEqual("/b/c",
                PathCombiner.Combine("/a", "/b", "c").Normalized);
        }

        /// <summary>空片段与 "." 应被忽略，不应产生 "//"。</summary>
        [Test]
        public void Combine_空片段与当前目录_被忽略()
        {
            Assert.AreEqual("/a/b",
                PathCombiner.Combine("/a", "", ".", "b").Normalized);
            Assert.AreEqual("/",
                PathCombiner.Combine("", "", "").Normalized);
        }

        /// <summary>".." 弹栈；栈空时越根应报 EscapeRoot。</summary>
        [Test]
        public void Combine_越根_报_EscapeRoot()
        {
            VfsPath result;
            PathError error;
            var ok = PathCombiner.Combine(out result, out error, "/", "..", "x");

            Assert.IsFalse(ok);
            Assert.AreEqual(PathError.EscapeRoot, error);

            Assert.AreEqual("/c",
                PathCombiner.Combine("/a", "..", "c").Normalized);
        }

        /// <summary>没有任何片段时应报 Empty。</summary>
        [Test]
        public void Combine_没有片段_报_Empty()
        {
            VfsPath result;
            PathError error;

            Assert.IsFalse(PathCombiner.Combine(out result, out error));
            Assert.AreEqual(PathError.Empty, error);
        }

        // ---- 反向：求相对路径 ----

        /// <summary>目标是起点的下级时，相对路径就是剩余的层级，不含 ".."。</summary>
        [Test]
        public void TryGetRelative_目标在起点之下_不含上级记号()
        {
            string relative;
            PathError error;
            var ok = PathCombiner.TryGetRelative("/docs/sub", "/docs/sub/a.txt",
                                                 out relative, out error);

            Assert.IsTrue(ok);
            Assert.AreEqual("a.txt", relative);
            Assert.AreEqual(PathError.None, error);
        }

        /// <summary>
        /// 起点被当作"要从中出发的目录"，所以兄弟节点之间必须先回退一级。
        /// 这条也顺带覆盖了旧注释里写错的示例（曾写成 "b/c.txt"）。
        /// </summary>
        [Test]
        public void TryGetRelative_兄弟节点_先回退一级()
        {
            string relative;
            PathCombiner.TryGetRelative("/docs/a", "/docs/b/c.txt", out relative, out _);

            Assert.AreEqual("../b/c.txt", relative);
        }

        /// <summary>需要向上回退时应补出足够的 ".."。</summary>
        [Test]
        public void TryGetRelative_需要回退_补出上级记号()
        {
            string relative;
            PathCombiner.TryGetRelative("/a/b/c", "/a/x", out relative, out _);

            Assert.AreEqual("../../x", relative);
        }

        /// <summary>两端相同时返回 "." 表示同级。</summary>
        [Test]
        public void TryGetRelative_路径相同_返回当前目录()
        {
            string relative;
            PathCombiner.TryGetRelative("/docs", "/docs", out relative, out _);

            Assert.AreEqual(".", relative);
        }

        /// <summary>比较段名时忽略大小写，与虚拟文件系统的查找策略一致。</summary>
        [Test]
        public void TryGetRelative_段名大小写不同_视为同一层()
        {
            string relative;
            PathCombiner.TryGetRelative("/Docs/Sub", "/docs/Sub/b.txt", out relative, out _);

            // "Docs/Sub" 与 "docs/Sub" 视为同一层，所以只剩最后一截是新层级。
            Assert.AreEqual("b.txt", relative);
        }

        /// <summary>任一端非法时应失败，并透传错误码。</summary>
        [Test]
        public void TryGetRelative_端点非法_失败()
        {
            string relative;
            PathError error;
            var ok = PathCombiner.TryGetRelative("/docs", "/a/CON", out relative, out error);

            Assert.IsFalse(ok);
            Assert.AreEqual(PathError.ReservedName, error);
        }
    }
}
