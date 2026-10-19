using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VoyageForge.Depot.Editor;

namespace VoyageForge.Depot.Tests
{
    /// <summary>
    /// 锁定 .forge~ 的编码与行尾风格。
    ///
    /// 背景：序列化器原先用 StreamWriter.WriteLine，行尾取 Environment.NewLine，
    /// 于是同一份元数据在 Windows 上被写成 CRLF、在 macOS/Linux 上写成 LF；
    /// 编码又用了 Encoding.UTF8，会额外写入 UTF-8 BOM。
    /// 后果是跨平台协作时整文件每一行都变，diff 全是无意义的行尾噪音，
    /// 而 BOM 会让不吃 BOM 的解析器把首行键读成 "\uFEFFv"。
    ///
    /// 现在约定：无论什么平台，写入一律 LF + 无 BOM。
    /// 这两个约定与 Unity 自己产出的文本文件（.meta / .unity / manifest.json）保持一致。
    /// 这些测试就是这个约定的护栏。
    /// 测试只走公开 API（ForgeMetaDatabase），不直接碰 internal 的序列化器，
    /// 这样断言的是对外可观测的行为，序列化器内部怎么实现都可以重构。
    /// </summary>
    [TestFixture]
    public class ForgeMetaDatabaseLineEndingTests
    {
        /// <summary>测试专用资源目录，TearDown 里整个删掉，不留垃圾。</summary>
        private const string Folder = "Assets/__ForgeMetaLineEndingTests";

        /// <summary>测试用资源路径。sidecar 会生成在它旁边，名字是「资源名 + .forge~」。</summary>
        private const string AssetPath = Folder + "/Target.txt";

        /// <summary>测试用的元数据键；用独立命名空间，避免和真实业务字段撞车。</summary>
        private const string FieldKey = "com.voyageforge.tests.lineEnding";

        /// <summary>
        /// 每个用例前重建一份干净的资源：
        /// 新建资源会拿到新的 GUID，等于同时把 ForgeMetaDatabase 按 GUID 的缓存也隔离了。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", "__ForgeMetaLineEndingTests");
            }

            File.WriteAllText(ToAbsolutePath(AssetPath), "target");
            AssetDatabase.Refresh();

            Assert.IsNotEmpty(AssetDatabase.AssetPathToGUID(AssetPath), "测试资源没有成功导入，后续断言无意义");
        }

        /// <summary>
        /// 删除资源即可连带清掉 sidecar（由 ForgeMetaAssetHandler 的 postprocessor 负责），
        /// 所以这里不需要额外删 .forge~ 文件；最后再删掉空目录。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(AssetPath) != null)
            {
                AssetDatabase.DeleteAsset(AssetPath);
            }

            if (AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        /// <summary>
        /// 单条写入必须只产出 LF，不能出现任何 CR。
        /// 这是本次修复的核心回归点。
        /// </summary>
        [Test]
        public void SetNestedField_WritesLfLineEndingsOnly()
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetPath);

            ForgeMetaDatabase.SetNestedField(guid, FieldKey, "lf");

            byte[] bytes = ReadSidecarBytes();
            Assert.Greater(bytes.Length, 0, "没有生成 .forge~ 文件");

            string content = Encoding.UTF8.GetString(bytes);
            StringAssert.Contains("flag: lf", content.Replace("lineEnding", "flag"),
                "写入内容不符合预期，后续行尾断言没有意义");

            Assert.AreEqual(0, CountByte(bytes, 0x0D),
                ".forge~ 里不应出现 CR（0x0D）：行尾必须是 LF，否则跨平台 diff 会被行尾变化污染");

            Assert.IsFalse(HasUtf8Bom(bytes),
                ".forge~ 不应带 UTF-8 BOM：Unity 自己的文本文件都不带，且不吃 BOM 的解析器会把首行键读成 \\uFEFFv");
        }

        /// <summary>
        /// 批量写入走的是同一套序列化路径，同样必须只产出 LF。
        /// 单独覆盖一次，是因为批量入口内部会绕开逐条刷新，属于另一条代码路径。
        /// </summary>
        [Test]
        public void SetNestedFieldBatch_WritesLfLineEndingsOnly()
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetPath);

            var writes = new[]
            {
                new ForgeMetaWrite(guid, FieldKey, "batch-lf")
            };

            int applied = ForgeMetaDatabase.SetNestedFieldBatch(writes);
            Assert.AreEqual(1, applied, "批量写入应当报告 1 条实际改动");

            byte[] bytes = ReadSidecarBytes();
            Assert.Greater(bytes.Length, 0, "没有生成 .forge~ 文件");
            Assert.AreEqual(0, CountByte(bytes, 0x0D),
                "批量写入同样只能产出 LF 行尾");

            Assert.IsFalse(HasUtf8Bom(bytes),
                "批量写入同样不能带 UTF-8 BOM");
        }

        /// <summary>
        /// 反向兼容：历史上已经写成 CRLF（还带 BOM）的 sidecar 必须仍然能读出来。
        ///
        /// 这条测试保证「写入改成 LF」不会让老项目里现存的 CRLF 文件变成读不出来，
        /// 否则升级 Depot 就等于把已有别名全部丢掉。
        /// </summary>
        [Test]
        public void TryGetNestedField_ReadsLegacyCrLfFile()
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetPath);

            // 手工构造旧格式文件：BOM + CRLF，并写入一个真实字段。
            string legacy = "v: 1\r\n" +
                            "guid: " + guid + "\r\n" +
                            "com:\r\n" +
                            "  voyageforge:\r\n" +
                            "    tests:\r\n" +
                            "      lineEnding: 旧格式\r\n";
            File.WriteAllText(ToAbsolutePath(SidecarPathOf(AssetPath)), legacy, new UTF8Encoding(true));

            string value;
            bool found = ForgeMetaDatabase.TryGetNestedField(guid, FieldKey, out value);

            Assert.IsTrue(found, "CRLF 格式的历史 sidecar 必须仍然可读");
            Assert.AreEqual("旧格式", value);
        }

        /// <summary>取本用例资源的 sidecar 字节，路径用绝对路径，避免依赖编辑器进程的当前目录。</summary>
        private static byte[] ReadSidecarBytes()
        {
            string path = ToAbsolutePath(SidecarPathOf(AssetPath));
            Assert.IsTrue(File.Exists(path), "期望存在 sidecar：" + path);
            return File.ReadAllBytes(path);
        }

        /// <summary>按 Depot 的命名约定推导 sidecar 的项目相对路径。</summary>
        private static string SidecarPathOf(string assetPath)
        {
            return assetPath + ".forge~";
        }

        /// <summary>
        /// 把项目相对路径转成绝对路径。
        /// Application.dataPath 固定是 &lt;项目&gt;/Assets，用它推导比依赖当前工作目录可靠。
        /// </summary>
        private static string ToAbsolutePath(string projectRelativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>判断字节序列是否以 UTF-8 BOM（EF BB BF）开头。</summary>
        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }

        /// <summary>统计字节序列里某个值出现的次数，用于数 CR。</summary>
        private static int CountByte(byte[] bytes, byte value)
        {
            int count = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == value)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
