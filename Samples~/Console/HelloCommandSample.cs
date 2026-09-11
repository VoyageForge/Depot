using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Scripting;
using VoyageForge.Depot.Runtime.Console;

namespace VoyageForge.Depot.Samples.Console
{
    /// <summary>
    /// 参数补全示例（简单）：hello &lt;name&gt;。
    ///
    /// 用法：输入 "hello " 后命令名已完整，开始输入参数（人名）时，
    /// 会提示固定的人名列表，并按已输入前缀过滤。例如输入 "hello T" 会提示 Tom、Tony。
    /// 只有一个参数，第一个参数填完后（args 非空）不再提示。
    /// </summary>
    [Preserve]
    public sealed class HelloCommandSample : ConsoleCommand
    {
        // 可选的人名列表（固定）
        private static readonly string[] Names = { "Alice", "Bob", "Tom", "Tony" };

        /// <inheritdoc />
        public override string Name => "hello";

        /// <inheritdoc />
        public override string Usage => "hello <name>";

        /// <inheritdoc />
        public override string Description => "向指定人名打招呼（参数补全简单示例）";

        /// <inheritdoc />
        public override void Execute(string[] args)
        {
            string name = args.Length > 0 ? args[0] : "world";
            Debug.Log($"[Hello] Hi, {name}!");
        }

        /// <summary>
        /// 简单参数补全：返回固定的人名列表，按正在输入的参数片段做前缀过滤。
        /// </summary>
        /// <param name="args">已完成的参数。本命令只有一个参数，args 非空表示已填完，不再提示。</param>
        /// <param name="currentInput">正在输入的参数片段。</param>
        /// <returns>匹配的人名列表。</returns>
        public override IReadOnlyList<string> GetArgumentCompletions(string[] args, string currentInput)
        {
            // 只有一个参数：第一个参数填完后（args 非空）不再提示
            if (args.Length > 0)
            {
                return Array.Empty<string>();
            }

            return Names
                .Where(n => n.StartsWith(currentInput, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
