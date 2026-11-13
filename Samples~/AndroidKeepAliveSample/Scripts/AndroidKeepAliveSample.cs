using System;
using UnityEngine;

namespace VoyageForge.Depot.Samples.Android
{
    /// <summary>
    /// Android 保活示例脚本，演示前台保活服务、自启动权限引导、电池优化白名单、开关持久化和存活时长查询的 C# 调用方式。
    /// </summary>
    public sealed class AndroidKeepAliveSample : MonoBehaviour
    {
        /// <summary>
        /// 是否在组件启用时自动启动前台保活服务。
        /// </summary>
        [SerializeField]
        private bool startServiceOnEnable;

        /// <summary>
        /// 是否在组件禁用时自动停止前台保活服务。
        /// </summary>
        [SerializeField]
        private bool stopServiceOnDisable;

        /// <summary>
        /// 是否在控制台打印保活状态。
        /// </summary>
        [SerializeField]
        private bool logStatusToConsole;

        /// <summary>
        /// 控制台打印状态的间隔秒数。
        /// </summary>
        [SerializeField]
        private float logIntervalSeconds = 5f;

        /// <summary>
        /// 下一次打印状态的 Unity 时间。
        /// </summary>
        private float nextLogTime;

        /// <summary>
        /// 当前 Android API 等级。
        /// </summary>
        public int SdkInt => AndroidKeepAliveService.SdkInt;

        /// <summary>
        /// 当前应用包名。
        /// </summary>
        public string PackageName => AndroidKeepAliveService.PackageName;

        /// <summary>
        /// 当前前台保活服务是否记录为运行中。
        /// </summary>
        public bool IsServiceRunning => AndroidKeepAliveService.IsServiceRunning();

        /// <summary>
        /// 用户保存的保活服务开关是否为开启状态。
        /// </summary>
        public bool IsKeepAliveSwitchEnabled => AndroidKeepAliveService.IsKeepAliveSwitchEnabled();

        /// <summary>
        /// 当前应用是否已经加入电池优化白名单。
        /// </summary>
        public bool IsIgnoringBatteryOptimizations => AndroidKeepAliveService.IsIgnoringBatteryOptimizations();

        /// <summary>
        /// 当前前台保活服务的存活时长。
        /// </summary>
        public TimeSpan AliveDuration
        {
            get
            {
                var startUnixMillis = AndroidKeepAliveService.GetServiceStartUnixMillis();
                if (!IsServiceRunning || startUnixMillis <= 0)
                {
                    return TimeSpan.Zero;
                }

                return DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(startUnixMillis);
            }
        }

        /// <summary>
        /// 组件启用时按配置启动或恢复前台保活服务。
        /// </summary>
        private void OnEnable()
        {
            if (startServiceOnEnable)
            {
                StartKeepAliveService();
            }
            else
            {
                EnsureKeepAliveServiceFromSavedState();
            }
        }

        /// <summary>
        /// 组件禁用时按配置停止前台保活服务。
        /// </summary>
        private void OnDisable()
        {
            if (stopServiceOnDisable)
            {
                StopKeepAliveService();
            }
        }

        /// <summary>
        /// 每帧按配置打印保活状态。
        /// </summary>
        private void Update()
        {
            if (!logStatusToConsole || Time.unscaledTime < nextLogTime)
            {
                return;
            }

            nextLogTime = Time.unscaledTime + Mathf.Max(1f, logIntervalSeconds);
            LogKeepAliveStatus();
        }

        /// <summary>
        /// 请求通知权限并启动 Android 前台保活服务。
        /// </summary>
        public void StartKeepAliveService()
        {
            AndroidKeepAliveService.SetKeepAliveSwitchEnabled(true);
            AndroidKeepAliveService.RequestNotificationPermission();
            AndroidKeepAliveService.StartService();
        }

        /// <summary>
        /// 停止 Android 前台保活服务。
        /// </summary>
        public void StopKeepAliveService()
        {
            AndroidKeepAliveService.SetKeepAliveSwitchEnabled(false);
            AndroidKeepAliveService.StopService();
        }

        /// <summary>
        /// 按 SharedPreferences 中保存的开关状态恢复 Android 前台保活服务。
        /// </summary>
        /// <returns>保存的开关状态是否要求恢复服务。</returns>
        public bool EnsureKeepAliveServiceFromSavedState()
        {
            return AndroidKeepAliveService.EnsureServiceFromSavedState();
        }

        /// <summary>
        /// 请求系统把当前应用加入电池优化白名单。
        /// </summary>
        public void RequestIgnoreBatteryOptimizations()
        {
            AndroidKeepAliveService.RequestIgnoreBatteryOptimizations();
        }

        /// <summary>
        /// 请求打开厂商自启动权限设置页；用户需要在系统页面中手动开启。
        /// </summary>
        /// <returns>设置页跳转请求是否成功提交。</returns>
        public bool RequestAutoStartPermission()
        {
            return AndroidKeepAliveService.RequestAutoStartPermission();
        }

        /// <summary>
        /// 打开系统电池优化设置页，引导用户手动处理厂商后台限制。
        /// </summary>
        public void OpenBatteryOptimizationSettings()
        {
            AndroidKeepAliveService.OpenBatteryOptimizationSettings();
        }

        /// <summary>
        /// 获取服务启动时间戳。
        /// </summary>
        /// <returns>服务启动时间戳，单位为 Unix 毫秒。</returns>
        public long GetServiceStartUnixMillis()
        {
            return AndroidKeepAliveService.GetServiceStartUnixMillis();
        }

        /// <summary>
        /// 获取最近一次服务心跳时间戳。
        /// </summary>
        /// <returns>最近一次服务心跳时间戳，单位为 Unix 毫秒。</returns>
        public long GetLastHeartbeatUnixMillis()
        {
            return AndroidKeepAliveService.GetLastHeartbeatUnixMillis();
        }

        /// <summary>
        /// 把当前保活状态打印到 Unity 控制台。
        /// </summary>
        public void LogKeepAliveStatus()
        {
            Debug.Log(
                $"Android 保活状态：开关={IsKeepAliveSwitchEnabled}，运行={IsServiceRunning}，白名单={IsIgnoringBatteryOptimizations}，存活={FormatDuration(AliveDuration)}，包名={PackageName}，API={SdkInt}");
        }

        /// <summary>
        /// 把持续时间格式化为天时分秒字符串。
        /// </summary>
        /// <param name="duration">需要格式化的持续时间。</param>
        /// <returns>格式化后的持续时间字符串。</returns>
        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalSeconds < 0)
            {
                duration = TimeSpan.Zero;
            }

            return duration.TotalDays >= 1d
                ? $"{(int)duration.TotalDays}天 {duration.Hours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Hours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        }
    }
}
