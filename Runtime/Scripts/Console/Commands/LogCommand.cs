using UnityEngine.Scripting;

namespace VoyageForge.Depot.Runtime.Console
{
    /// <summary>
    /// 内置命令：log —— 打印一段文本（把参数拼回一句话）。
    /// 标注 [Preserve] 防止 IL2CPP 代码剥离，确保反射自动发现能注册本命令。
    /// </summary>
    [Preserve]
    public sealed class LogCommand : ConsoleCommand
    {
        /// <inheritdoc />
        public override string Name => "log";

        /// <inheritdoc />
        public override string Description => "打印一段文本";

        /// <inheritdoc />
        public override string Usage => "log <message>";

        /// <summary>
        /// 把所有参数用空格拼回一句话输出。
        /// </summary>
        /// <param name="args">要打印的参数。</param>
        public override void Execute(string[] args)
        {
            // 用空格把参数重新拼成一句话，直接写入控制台（不再依赖 Unity 日志桥接）
            RuntimeConsole.WriteDirect($"[Console] {string.Join(" ", args)}");
        }
    }
}
