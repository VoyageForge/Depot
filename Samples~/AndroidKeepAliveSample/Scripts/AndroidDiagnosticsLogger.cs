using UnityEngine;

namespace VoyageForge.Depot.Samples.Android
{
    /// <summary>
    /// Android 原生日志诊断封装器，负责从 Unity 读取 VoyageForge Android Core 写入的文件日志路径并清理旧日志。
    /// </summary>
    public static class AndroidDiagnosticsLogger
    {
        /// <summary>
        /// Android 原生诊断日志工具类名。
        /// </summary>
        private const string AndroidLoggerClassName = "com.voyageforge.android.core.diagnostics.VoyageForgeAndroidLogger";

        /// <summary>
        /// Android UnityPlayer Java 类名。
        /// </summary>
        private const string UnityPlayerClassName = "com.unity3d.player.UnityPlayer";

        /// <summary>
        /// 获取 Android 原生日志文件路径。
        /// </summary>
        /// <returns>Android 真机上的日志文件绝对路径；编辑器中返回说明文本。</returns>
        public static string GetLogFilePath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var loggerClass = new AndroidJavaClass(AndroidLoggerClassName))
            using (var activity = GetCurrentActivity())
            {
                return loggerClass.CallStatic<string>("getLogFilePath", activity);
            }
#else
            return "Android 真机运行时才会生成 VoyageForge Android Core 文件日志。";
#endif
        }

        /// <summary>
        /// 获取 Android 原生日志目录路径。
        /// </summary>
        /// <returns>Android 真机上的日志目录绝对路径；编辑器中返回说明文本。</returns>
        public static string GetLogFolderPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var loggerClass = new AndroidJavaClass(AndroidLoggerClassName))
            using (var activity = GetCurrentActivity())
            {
                return loggerClass.CallStatic<string>("getLogFolderPath", activity);
            }
#else
            return "Android 真机运行时才会生成 VoyageForge Android Core 文件日志目录。";
#endif
        }

        /// <summary>
        /// 清空 Android 原生日志文件，便于重新复现一次后台问题。
        /// </summary>
        /// <returns>日志清理请求成功提交时返回 true。</returns>
        public static bool ClearLogFiles()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var loggerClass = new AndroidJavaClass(AndroidLoggerClassName))
            using (var activity = GetCurrentActivity())
            {
                return loggerClass.CallStatic<bool>("clearLogFiles", activity);
            }
#else
            Debug.Log("编辑器环境模拟清空 VoyageForge Android Core 文件日志。");
            return true;
#endif
        }

        /// <summary>
        /// 尝试打开 Android 原生日志目录。
        /// </summary>
        /// <returns>打开目录请求成功提交时返回 true。</returns>
        public static bool OpenLogFolder()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var loggerClass = new AndroidJavaClass(AndroidLoggerClassName))
            using (var activity = GetCurrentActivity())
            {
                return loggerClass.CallStatic<bool>("openLogFolder", activity);
            }
#else
            Debug.Log("编辑器环境无法打开 Android 真机日志目录。");
            return false;
#endif
        }

        /// <summary>
        /// 写入 Unity 生命周期事件到 Android 原生日志。
        /// </summary>
        /// <param name="eventName">生命周期事件名称。</param>
        public static void LogUnityLifecycleEvent(string eventName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var loggerClass = new AndroidJavaClass(AndroidLoggerClassName))
            using (var activity = GetCurrentActivity())
            {
                loggerClass.CallStatic("logUnityLifecycleEvent", activity, eventName ?? string.Empty);
            }
#else
            Debug.Log("编辑器环境模拟 Android 生命周期日志：" + eventName);
#endif
        }

        /// <summary>
        /// 获取当前 Unity Activity。
        /// </summary>
        /// <returns>当前 Android Activity。</returns>
        private static AndroidJavaObject GetCurrentActivity()
        {
            using (var unityPlayer = new AndroidJavaClass(UnityPlayerClassName))
            {
                return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }
    }
}
