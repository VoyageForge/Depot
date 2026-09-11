using UnityEngine;
using UnityEngine.UIElements;
using VoyageForge.Depot.Runtime.Console;

namespace VoyageForge.Depot.Samples.Console
{
    /// <summary>
    /// 运行时控制台示例引导器。
    ///
    /// 演示推荐用法：
    /// 1. 在 [RuntimeInitializeOnLoadMethod] 中调用 RuntimeConsole.Initialize() 完成初始化；
    /// 2. 初始化只会创建实例、构建面板，但不会显示；
    /// 3. 运行后连按 3 次 Tab 键唤醒/隐藏控制台。
    ///
    /// 中文字体说明：
    /// 运行时默认字体不含中文字形，会导致控制台中文不显示。本 Sample 的 Fonts 目录
    /// 提供了 Noto Sans SC 字体资源。使用时请把 "UITK Text Settings.asset" 放入你自己
    /// 项目的 Resources 目录，并在下方 TextSettingsResourcePath 填写对应路径。
    /// </summary>
    public static class RuntimeConsoleBootstrap
    {
        /// <summary>
        /// 中文字体文本设置的 Resources 路径（可选）。
        /// 例如把 Fonts 目录下的 "UITK Text Settings.asset" 放入 Resources/Depot/Console 后，
        /// 填写 "Depot/Console/UITK Text Settings"。留空则不加载自定义字体。
        /// </summary>
        private const string TextSettingsResourcePath = "";

        /// <summary>
        /// 应用启动后自动执行，完成运行时控制台的初始化（默认隐藏）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeConsole()
        {
            // 可选：加载自定义中文字体（运行时默认字体不含中文字形，会导致中文不显示）
            if (!string.IsNullOrEmpty(TextSettingsResourcePath))
            {
                PanelTextSettings textSettings = Resources.Load<PanelTextSettings>(TextSettingsResourcePath);
                if (textSettings != null)
                {
                    RuntimeConsole.SetTextSettings(textSettings);
                }
            }

            // 创建单例并完成面板构建；默认不显示，等待三连 Tab 唤醒
            RuntimeConsole.Initialize();

            // 打印一条日志，唤醒控制台后即可看到，用于验证日志捕获链路
            Debug.Log("Depot 运行时控制台已初始化（隐藏中），连按 3 次 Tab 键唤醒。");
        }
    }
}
