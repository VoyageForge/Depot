using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace VoyageForge.Depot.Runtime.Console
{
    /// <summary>
    /// 内置命令：listen —— 控制日志监听开关与自启动设置。
    ///
    /// 职责说明：
    /// 日志桥接（订阅 Application.logMessageReceived 把 Unity 日志转发到 RuntimeConsole）的
    /// 开关状态与 PlayerPrefs 持久化都收敛在本命令中，主类 <see cref="RuntimeConsole"/> 只提供
    /// 底层机制 <see cref="RuntimeConsole.SetLogListening"/>，避免污染主类。
    ///
    /// 用法：
    ///   listen                        查看当前监听状态与自启动状态；
    ///   listen on / off               开启 / 关闭日志监听（持久化到 PlayerPrefs）；
    ///   listen autostart on / off     开启 / 关闭自启动（持久化，影响下次初始化）。
    ///
    /// 标注 [Preserve] 防止 IL2CPP 代码剥离，确保反射自动发现能注册本命令。
    /// </summary>
    [Preserve]
    public sealed class ListenCommand : ConsoleCommand
    {
        // PlayerPrefs 键：日志监听开关与自启动开关
        private const string ListenLogsPrefKey = "Depot.Console.ListenLogs";
        private const string AutoStartListeningPrefKey = "Depot.Console.AutoStartListening";

        /// <inheritdoc />
        public override string Name => "listen";

        /// <inheritdoc />
        public override string Description => "控制日志监听开关与自启动设置";

        /// <inheritdoc />
        public override string Usage => "listen [on|off|autostart on|autostart off]";

        /// <summary>
        /// 是否自启动监听（持久化到 PlayerPrefs）。
        /// 开启后，RuntimeConsole 初始化时会自动开始监听 Unity 日志。
        /// </summary>
        public static bool AutoStartListening
        {
            get => PlayerPrefs.GetInt(AutoStartListeningPrefKey, 0) == 1;
            set => PlayerPrefs.SetInt(AutoStartListeningPrefKey, value ? 1 : 0);
        }

        /// <summary>
        /// 设置日志监听开关并持久化（供 listen on/off 命令调用）。
        /// </summary>
        /// <param name="listen">true 开始监听；false 停止监听。</param>
        public static void SetListening(bool listen)
        {
            PlayerPrefs.SetInt(ListenLogsPrefKey, listen ? 1 : 0);
            RuntimeConsole.SetLogListening(listen);
        }

        /// <summary>
        /// 按持久化的自启动/监听设置，应用日志监听状态。
        /// 由 <see cref="RuntimeConsole"/> 初始化完成后调用。
        /// </summary>
        public static void ApplyAutoStart()
        {
            bool listen = AutoStartListening || PlayerPrefs.GetInt(ListenLogsPrefKey, 0) == 1;
            RuntimeConsole.SetLogListening(listen);
        }

        /// <summary>
        /// 执行命令：无参数显示状态；on/off 切换监听；autostart on/off 切换自启动。
        /// 输出统一走 <see cref="RuntimeConsole.WriteDirect"/>，即使监听已关闭也能在控制台看到提示。
        /// </summary>
        /// <param name="args">命令参数。</param>
        public override void Execute(string[] args)
        {
            // 无参数：显示当前状态
            if (args.Length == 0)
            {
                RuntimeConsole.WriteDirect(
                    $"[Console] 日志监听：{(RuntimeConsole.IsListening ? "开启" : "关闭")}，自启动：{(AutoStartListening ? "开启" : "关闭")}");
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "on":
                    SetListening(true);
                    RuntimeConsole.WriteDirect("[Console] 日志监听已开启。");
                    break;

                case "off":
                    SetListening(false);
                    RuntimeConsole.WriteDirect("[Console] 日志监听已关闭。");
                    break;

                case "autostart":
                    HandleAutoStart(args);
                    break;

                default:
                    RuntimeConsole.WriteDirect($"[Console] 未知参数：{args[0]}。用法：{Usage}", LogType.Warning);
                    break;
            }
        }

        /// <summary>处理 autostart 子命令：切换自启动开关（只持久化，不影响当前会话）。</summary>
        /// <param name="args">命令参数（args[0] 为 "autostart"）。</param>
        private static void HandleAutoStart(string[] args)
        {
            // 缺少第二个参数：显示自启动状态
            if (args.Length < 2)
            {
                RuntimeConsole.WriteDirect($"[Console] 自启动：{(AutoStartListening ? "开启" : "关闭")}。用法：listen autostart on/off");
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "on":
                    AutoStartListening = true;
                    RuntimeConsole.WriteDirect("[Console] 自启动监听已开启（下次初始化时自动开始监听）。");
                    break;

                case "off":
                    AutoStartListening = false;
                    RuntimeConsole.WriteDirect("[Console] 自启动监听已关闭（下次初始化时不再自动监听）。");
                    break;

                default:
                    RuntimeConsole.WriteDirect($"[Console] 未知参数：{args[1]}。用法：listen autostart on/off", LogType.Warning);
                    break;
            }
        }

        /// <summary>
        /// 参数补全：第一个参数提示 on / off / autostart；输入 autostart 后第二个参数提示 on / off。
        /// </summary>
        /// <param name="args">已完成的参数。</param>
        /// <param name="currentInput">正在输入的参数片段。</param>
        /// <returns>补全建议列表。</returns>
        public override IReadOnlyList<string> GetArgumentCompletions(string[] args, string currentInput)
        {
            // 正在输入第一个参数
            if (args.Length == 0)
            {
                return new[] { "on", "off", "autostart" };
            }

            // 第一个参数是 autostart，正在输入第二个参数
            if (args.Length == 1 && string.Equals(args[0], "autostart", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "on", "off" };
            }

            return Array.Empty<string>();
        }
    }
}
