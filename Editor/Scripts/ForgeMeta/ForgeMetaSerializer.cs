using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VoyageForge.Depot.Editor
{
    /// <summary>
    /// 轻量级 YAML 序列化器，将 ForgeMetadata 对象写入/读取为 YAML 格式文本。
    /// 支持嵌套字典，缩进使用两个空格。序列化采用迭代（栈）方式，避免递归。
    /// </summary>
    internal static class ForgeMetaSerializer
    {
        /// <summary>将元数据序列化到指定文件路径（迭代实现）</summary>
        public static void Serialize(string filePath, ForgeMetadata data)
        {
            // 编码与行尾都固定下来，保证同一个 ForgeMetadata 在任何平台、任何一次写入都得到完全相同的字节。
            // 1. 编码用不带 BOM 的 UTF-8。Encoding.UTF8 会写入 EF BB BF 三个字节，而 Unity 自己产出的
            //    .meta / .unity / manifest.json 等文本文件都不带 BOM；保持一致，也避免不吃 BOM 的
            //    第三方解析器把首行键读成 "\uFEFFv"。
            // 2. 行尾固定 LF。StreamWriter 默认取 Environment.NewLine，Windows 上会写成 CRLF，
            //    跨平台协作时整文件每一行都会变，git diff 里全是行尾噪音。
            using var writer = new StreamWriter(filePath, false, new UTF8Encoding(false)) { NewLine = "\n" };
            // 写入版本和 GUID 作为顶层键
            writer.WriteLine($"v: {data.version}");
            writer.WriteLine($"guid: {data.guid ?? ""}");

            // 使用栈进行深度优先遍历
            var stack = new Stack<(int indent, string key, object value)>();
            PushDictionaryItems(stack, data.fields, 0);

            while (stack.Count > 0)
            {
                var (indent, key, value) = stack.Pop();

                if (value is Dictionary<string, object> nested)
                {
                    writer.WriteLine($"{new string(' ', indent * 2)}{key}:");
                    PushDictionaryItems(stack, nested, indent + 1);
                }
                else
                {
                    writer.WriteLine($"{new string(' ', indent * 2)}{key}: {value}");
                }
            }
        }

        /// <summary>将字典的所有键值对按倒序压入栈，以保证顺序</summary>
        private static void PushDictionaryItems(Stack<(int indent, string key, object value)> stack,
            Dictionary<string, object> dict, int indent)
        {
            var items = new List<(string key, object value)>(dict.Count);
            foreach (var kv in dict)
                items.Add((kv.Key, kv.Value));

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var (key, value) = items[i];
                stack.Push((indent, key, value));
            }
        }

        /// <summary>从文件反序列化元数据（迭代实现）</summary>
        public static ForgeMetadata Deserialize(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            var data = new ForgeMetadata();
            var root = data.fields;
            var stack = new Stack<Dictionary<string, object>>();
            stack.Push(root);
            var indentLevels = new Stack<int>();
            indentLevels.Push(-1);

            bool versionParsed = false, guidParsed = false;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int indent = 0;
                while (indent < line.Length && line[indent] == ' ')
                    indent++;

                string trimmed = line.TrimStart();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                int colonIndex = trimmed.IndexOf(':');
                if (colonIndex == -1)
                    continue;

                string key = trimmed.Substring(0, colonIndex).Trim();
                string value = trimmed.Substring(colonIndex + 1).Trim();

                if (!versionParsed && key == "v")
                {
                    int.TryParse(value, out data.version);
                    versionParsed = true;
                    continue;
                }

                if (!guidParsed && key == "guid")
                {
                    data.guid = value;
                    guidParsed = true;
                    continue;
                }

                while (stack.Count > 1 && indentLevels.Peek() >= indent)
                {
                    stack.Pop();
                    indentLevels.Pop();
                }

                var currentDict = stack.Peek();

                if (string.IsNullOrEmpty(value))
                {
                    bool hasChildren = false;
                    int currentIndex = System.Array.IndexOf(lines, line);
                    for (int i = currentIndex + 1; i < lines.Length; i++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[i]))
                            continue;
                        int nextIndent = 0;
                        while (nextIndent < lines[i].Length && lines[i][nextIndent] == ' ')
                            nextIndent++;
                        if (nextIndent > indent)
                        {
                            hasChildren = true;
                            break;
                        }
                        else
                            break;
                    }

                    if (hasChildren)
                    {
                        var nested = new Dictionary<string, object>();
                        currentDict[key] = nested;
                        stack.Push(nested);
                        indentLevels.Push(indent);
                    }
                    else
                    {
                        currentDict[key] = "";
                    }
                }
                else
                {
                    currentDict[key] = value;
                }
            }

            return data;
        }
    }
}