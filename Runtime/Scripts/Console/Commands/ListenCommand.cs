using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace VoyageForge.Depot.Runtime.Console
{
    /// <summary>
    /// 内置命令：listen —— 监听 Unity 日志并决定是否转发到 RuntimeConsole。
    ///
    /// 职责：
    /// 本命令独占「Unity 日志 → RuntimeConsole」的桥接职责：自己订阅 Application.logMessageReceived，
    /// 把日志转发给 RuntimeConsole.WriteLogEntry；开关状态与自启动设置持久化在 PlayerPrefs。
    ///
    /// 启动早期缓存：
    /// - 自启动开启时，<see cref="EarlyInit"/>（BeforeSceneLoad）会提前订阅缓存 handler，
    ///   把启动窗口（其它 [RuntimeInitializeOnLoadMethod] 打印）的日志先缓存起来；
    /// - <see cref="OnCreate"/> 在命令注册后（RuntimeConsole 实例已创建）把缓存填充进控制台，
    ///   之后切换到实时转发。
    ///
    /// 用法：
    ///   listen                        查看当前监听状态与自启动状态；
    ///   listen on / off               开启 / 关闭日志监听（持久化到 PlayerPrefs，off 会立即丢弃缓存）；
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

        // 启动窗口缓存上限，与日志缓冲上限对齐
        private const int MaxPendingLogs = 300;

        // 启动窗口缓存：早于命令实例存在（静态），仅自启动开启时积累
        private static readonly List<ConsoleLogEntry> PendingLogs = new List<ConsoleLogEntry>();

        /// <summary>当前是否已订阅 Unity 日志回调（实例状态）。</summary>
        private bool _listening;

        /// <inheritdoc />
        public override string Name => "listen";

        /// <inheritdoc />
        public override string Description => "控制日志监听开关与自启动设置";

        /// <inheritdoc />
        public override string Usage => "listen [on|off|autostart on|autostart off]";

        /// <summary>是否自启动监听（持久化到 PlayerPrefs）。</summary>
        private static bool AutoStartListening
        {
            get => PlayerPrefs.GetInt(AutoStartListeningPrefKey, 0) == 1;
            set => PlayerPrefs.SetInt(AutoStartListeningPrefKey, value ? 1 : 0);
        }

        /// <summary>
        /// 启动早期订阅（BeforeSceneLoad，早于所有 AfterSceneLoad）：
        /// 只有自启动开启时才订阅缓存 handler，把启动窗口的日志先缓存起来，避免漏掉其它
        /// [RuntimeInitializeOnLoadMethod] 打印的日志。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EarlyInit()
        {
            // 防御：清理可能的残留（如关闭域重载 + 上次强退遗留），再按自启动决定是否缓存
            PendingLogs.Clear();

            if (AutoStartListening)
            {
                Application.logMessageReceived += CacheLog;
            }
        }

        /// <summary>缓存 Unity 日志（有上限），待命令注册后填充进控制台。</summary>
        /// <param name="condition">日志正文。</param>
        /// <param name="stackTrace">调用堆栈。</param>
        /// <param name="type">日志类型。</param>
        private static void CacheLog(string condition, string stackTrace, LogType type)
        {
            PendingLogs.Add(new ConsoleLogEntry(condition, stackTrace, type, DateTime.Now.ToString("HH:mm:ss.fff")));

            if (PendingLogs.Count > MaxPendingLogs)
            {
                PendingLogs.RemoveAt(0);
            }
        }

        /// <summary>
        /// 命令注册成功后调用（主线程，此时 RuntimeConsole 实例已创建）：
        /// 把启动窗口缓存填充进控制台，然后切换为实时转发。
        /// 注：OnCreate 由 PollCommandScan（Update 中）触发，必然在 RuntimeConsole.Instance 创建完成之后。
        /// </summary>
        public override void OnCreate()
        {
            bool listen = AutoStartListening || PlayerPrefs.GetInt(ListenLogsPrefKey, 0) == 1;

            // 填充启动窗口缓存（保留原始时间戳）
            foreach (ConsoleLogEntry entry in PendingLogs)
            {
                RuntimeConsole.Instance.WriteLogEntry(entry);
            }
            PendingLogs.Clear();

            // 取消缓存 handler，切换到最终状态
            Application.logMessageReceived -= CacheLog;

            _listening = false;
            if (listen)
            {
                Application.logMessageReceived += HandleUnityLog;
                _listening = true;
            }
        }

        /// <summary>命令被移除时调用（主线程）：清理订阅与缓存。</summary>
        public override void OnDestroy()
        {
            Application.logMessageReceived -= CacheLog;
            Application.logMessageReceived -= HandleUnityLog;
            PendingLogs.Clear();
            _listening = false;
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
                    $"[Console] 日志监听：{(_listening ? "开启" : "关闭")}，自启动：{(AutoStartListening ? "开启" : "关闭")}");
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "on":
                    SetListening(true, persist: true);
                    RuntimeConsole.WriteDirect("[Console] 日志监听已开启。");
                    break;

                case "off":
                    SetListening(false, persist: true);
                    PendingLogs.Clear();  // 立即丢弃缓存
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

        /// <summary>设置监听状态（幂等），并按需持久化。</summary>
        /// <param name="listen">true 开始监听；false 停止监听。</param>
        /// <param name="persist">是否把“是否监听”写入 PlayerPrefs。</param>
        private void SetListening(bool listen, bool persist)
        {
            if (persist)
            {
                PlayerPrefs.SetInt(ListenLogsPrefKey, listen ? 1 : 0);
            }

            if (_listening == listen)
            {
                return;
            }

            _listening = listen;

            if (listen)
            {
                Application.logMessageReceived += HandleUnityLog;
            }
            else
            {
                Application.logMessageReceived -= HandleUnityLog;
            }
        }

        /// <summary>Unity 日志回调：实时转发到 RuntimeConsole（决定是否打印到控制台）。</summary>
        /// <param name="condition">日志正文。</param>
        /// <param name="stackTrace">调用堆栈。</param>
        /// <param name="type">日志类型。</param>
        private void HandleUnityLog(string condition, string stackTrace, LogType type)
        {
            // 仅当 RuntimeConsole 单例存在时转发
            if (RuntimeConsole.HasInstance)
            {
                RuntimeConsole.Instance.WriteLogEntry(condition, stackTrace, type);
            }
        }

        /// <summary>处理 autostart 子命令：切换自启动开关（只持久化，不影响当前会话）。</summary>
        /// <param name="args">命令参数（args[0] 为 "autostart"）。</param>
        private void HandleAutoStart(string[] args)
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
