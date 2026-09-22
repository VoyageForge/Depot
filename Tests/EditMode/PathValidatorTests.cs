using NUnit.Framework;
using VoyageForge.Depot.Editor.FileSystem;

namespace VoyageForge.Depot.Tests
{
    /// <summary>
    /// <see cref="PathValidator"/> 的单段规则测试：
    /// 只验证"一个目录名 / 文件名"是否合法，不涉及整条路径的结构。
    /// </summary>
    [TestFixture]
    public class PathValidatorTests
    {
        /// <summary>普通名字、带扩展名的名字、隐藏文件都应通过。</summary>
        [Test]
        public void IsLegalSegment_常规名字_通过()
        {
            AssertSegmentLegal("readme.txt");
            AssertSegmentLegal("images");
            AssertSegmentLegal("a b c");        // 中间空格合法（只有结尾空格才禁止）
            AssertSegmentLegal(".gitignore");   // 隐藏文件，主名为空，不命中保留名
            AssertSegmentLegal("CONX");         // 与保留名相似但不是
            AssertSegmentLegal("中文目录名");   // 非 ASCII 名字应放行
        }

        /// <summary>空段、"."、".." 是"写法记号"而非非法名字，必须放行。</summary>
        [Test]
        public void IsLegalSegment_空段与相对记号_通过()
        {
            AssertSegmentLegal("");
            AssertSegmentLegal(".");
            AssertSegmentLegal("..");
        }

        /// <summary>Windows 保留设备名（含带扩展名的变体、不同大小写）必须被拒绝。</summary>
        [Test]
        public void IsLegalSegment_保留设备名_拒绝()
        {
            AssertSegmentIllegal("CON", PathError.ReservedName);
            AssertSegmentIllegal("con", PathError.ReservedName);
            AssertSegmentIllegal("NUL", PathError.ReservedName);
            AssertSegmentIllegal("COM1", PathError.ReservedName);
            AssertSegmentIllegal("LPT9", PathError.ReservedName);
            AssertSegmentIllegal("CON.txt", PathError.ReservedName);   // 带扩展名同样命中
            AssertSegmentIllegal("CON.a.b.c", PathError.ReservedName);
        }

        /// <summary>
        /// 控制台设备名 CONIN$ / CONOUT$ 也属于保留名
        /// （写出来像普通文件，实际会落到控制台设备上）。
        /// </summary>
        [Test]
        public void IsLegalSegment_控制台设备名_拒绝()
        {
            AssertSegmentIllegal("CONIN$", PathError.ReservedName);
            AssertSegmentIllegal("CONOUT$", PathError.ReservedName);
            AssertSegmentIllegal("conout$", PathError.ReservedName);       // 大小写不敏感
            AssertSegmentIllegal("CONOUT$.txt", PathError.ReservedName);   // 带扩展名同样命中
            // 只是以它开头的普通名字应放行
            AssertSegmentLegal("CONOUT$x");
            AssertSegmentLegal("MYCONOUT$");
        }

        /// <summary>
        /// 保留名的判断刻意【保持】不区分大小写，与虚拟路径空间的区分大小写策略相反：
        /// Windows 判断设备名本来就不区分大小写，"con.txt" 同样创建不出来，
        /// 所以不能因为改成 Ordinal 就把它放行。
        /// </summary>
        [Test]
        public void IsLegalSegment_保留名判断_不区分大小写()
        {
            AssertSegmentIllegal("CoN", PathError.ReservedName);
            AssertSegmentIllegal("cOnOuT$", PathError.ReservedName);
        }

        /// <summary>非法字符必须被拒绝，并回报 IllegalChar。</summary>
        [Test]
        public void IsLegalSegment_非法字符_拒绝()
        {
            AssertSegmentIllegal("na|me", PathError.IllegalChar);
            AssertSegmentIllegal("a?b", PathError.IllegalChar);
            AssertSegmentIllegal("a*b", PathError.IllegalChar);
            AssertSegmentIllegal("a:b", PathError.IllegalChar);
            AssertSegmentIllegal("a<b", PathError.IllegalChar);
            AssertSegmentIllegal("a>b", PathError.IllegalChar);
            AssertSegmentIllegal("a\"b", PathError.IllegalChar);
            AssertSegmentIllegal("a\0b", PathError.IllegalChar);
        }

        /// <summary>以空格或英文句点结尾的名字必须被拒绝（Windows 会静默去掉）。</summary>
        [Test]
        public void IsLegalSegment_尾随空格或点_拒绝()
        {
            AssertSegmentIllegal("name ", PathError.TrailingSpaceOrDot);
            AssertSegmentIllegal("name.", PathError.TrailingSpaceOrDot);
            AssertSegmentIllegal(" ", PathError.TrailingSpaceOrDot);
        }

        /// <summary>超过 MaxSegmentLength 的段名必须被拒绝。</summary>
        [Test]
        public void IsLegalSegment_段名超长_拒绝()
        {
            var tooLong = new string('a', PathValidator.MaxSegmentLength + 1);

            AssertSegmentIllegal(tooLong, PathError.SegmentTooLong);
            AssertSegmentLegal(new string('a', PathValidator.MaxSegmentLength));  // 边界值放行
        }

        /// <summary>成功的调用必须把 error 置为 None，避免调用方读到脏值。</summary>
        [Test]
        public void IsLegalSegment_成功时_error_为_None()
        {
            PathError error;
            var legal = PathValidator.IsLegalSegment("ok.txt", out error);

            Assert.IsTrue(legal);
            Assert.AreEqual(PathError.None, error);
        }

        /// <summary>Describe 应把每个错误码翻译成非空的中文说明。</summary>
        [Test]
        public void Describe_所有错误码_都有中文说明()
        {
            foreach (PathError error in System.Enum.GetValues(typeof(PathError)))
            {
                var text = PathValidator.Describe(error);

                Assert.IsNotNull(text);
                Assert.IsNotEmpty(text);
                Assert.AreNotEqual("未知错误", text, $"错误码 {error} 没有对应的说明文案");
            }
        }

        /// <summary>断言某个段名合法。</summary>
        /// <param name="segment">待校验的段名。</param>
        private static void AssertSegmentLegal(string segment)
        {
            PathError error;
            var legal = PathValidator.IsLegalSegment(segment, out error);

            Assert.IsTrue(legal, $"\"{segment}\" 应当合法，实际错误：{error}");
        }

        /// <summary>断言某个段名非法，且错误码符合预期。</summary>
        /// <param name="segment">待校验的段名。</param>
        /// <param name="expected">期望的错误码。</param>
        private static void AssertSegmentIllegal(string segment, PathError expected)
        {
            PathError error;
            var legal = PathValidator.IsLegalSegment(segment, out error);

            Assert.IsFalse(legal, $"\"{segment}\" 应当非法");
            Assert.AreEqual(expected, error, $"\"{segment}\" 的错误码不符合预期");
        }
    }
}
