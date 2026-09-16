using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine.Scripting;

namespace VoyageForge.Depot.Runtime.Console
{
    /// <summary>
    /// 内置命令：help —— 列出所有已注册命令及其用法与描述（每个命令占一行）。
    /// 标注 [Preserve] 防止 IL2CPP 代码剥离，确保反射自动发现能注册本命令。
    /// </summary>
    [Preserve]
    public sealed class HelpCommand : ConsoleCommand
    {
        /// <inheritdoc />
        public override string Name => "help";

        /// <inheritdoc />
        public override string Description => "列出所有可用命令";

        /// <summary>
        /// 收集所有已注册命令，按名称排序后每行输出一个命令。
        /// </summary>
        /// <param name="args">本命令忽略参数。</param>
        public override void Execute(string[] args)
        {
            // 按命令名（忽略大小写）排序，便于阅读
            List<ConsoleCommand> commands = ConsoleCommandRegistry.RegisteredCommands
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 计算最长用法字符串长度，用于把每行的描述列对齐
            int width = commands.Count == 0 ? 0 : commands.Max(c => c.Usage.Length);

            StringBuilder sb = new StringBuilder("[Console] 可用命令：");
            foreach (ConsoleCommand command in commands)
            {
                // 每个命令一行：缩进 + 左对齐用法 + 描述
                sb.Append('\n')
                  .Append("  ")
                  .Append(command.Usage.PadRight(width))
                  .Append("  ")
                  .Append(command.Description);
            }

            RuntimeConsole.WriteDirect(sb.ToString());
        }
    }
}
