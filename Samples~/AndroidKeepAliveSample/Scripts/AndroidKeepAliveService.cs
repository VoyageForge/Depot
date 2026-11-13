using System;
using UnityEngine;

namespace VoyageForge.Depot.Samples.Android
{
    /// <summary>
    /// Android 后台保活前台服务封装器，负责把 Unity 调用转发到 VoyageForge Android Core 原生服务。
    /// </summary>
    public static class AndroidKeepAliveService
    {
        /// <summary>
        /// 原生 Android 保活服务的完整类名。
        /// </summary>
        private const string ServiceClassName = "com.voyageforge.android.core.keepalive.CrucibleKeepAliveService";

        /// <summary>
        /// Android Intent 启动服务动作名。
        /// </summary>
        private const string StartAction = "com.voyageforge.android.core.keepalive.START_KEEP_ALIVE";

        /// <summary>
        /// Android Intent 停止服务动作名。
        /// </summary>
        private const string StopAction = "com.voyageforge.android.core.keepalive.STOP_KEEP_ALIVE";

        /// <summary>
        /// Android 通知权限名称，Android 13 及以上需要用户授权。
        /// </summary>
        private const string PostNotificationsPermission = "android.permission.POST_NOTIFICATIONS";

        /// <summary>
        /// Android 13 的 API 等级。
        /// </summary>
        private const int AndroidApiTiramisu = 33;

        /// <summary>
        /// Android UnityPlayer Java 类名。
        /// </summary>
        private const string UnityPlayerClassName = "com.unity3d.player.UnityPlayer";

        /// <summary>
        /// Android Context Java 类名。
        /// </summary>
        private const string ContextClassName = "android.content.Context";

        /// <summary>
        /// Android Intent Java 类名。
        /// </summary>
        private const string IntentClassName = "android.content.Intent";

        /// <summary>
        /// Android Uri Java 类名。
        /// </summary>
        private const string UriClassName = "android.net.Uri";

        /// <summary>
        /// Android Settings Java 类名。
        /// </summary>
        private const string SettingsClassName = "android.provider.Settings";

        /// <summary>
        /// Android Build.VERSION Java 类名。
        /// </summary>
        private const string BuildVersionClassName = "android.os.Build$VERSION";

        /// <summary>
        /// 当前设备的 Android API 等级。
        /// </summary>
        public static int SdkInt
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var versionClass = new AndroidJavaClass(BuildVersionClassName))
                {
                    return versionClass.GetStatic<int>("SDK_INT");
                }
#else
                return 0;
#endif
            }
        }

        /// <summary>
        /// 当前应用包名。
        /// </summary>
        public static string PackageName
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var activity = GetCurrentActivity())
                {
                    return activity.Call<string>("getPackageName");
                }
#else
                return Application.identifier;
#endif
            }
        }

        /// <summary>
        /// 请求通知权限，确保前台服务通知能在 Android 13 及以上显示。
        /// </summary>
        public static void RequestNotificationPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (SdkInt < AndroidApiTiramisu)
            {
                return;
            }

            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(PostNotificationsPermission))
            {
                UnityEngine.Android.Permission.RequestUserPermission(PostNotificationsPermission);
            }
#endif
        }

        /// <summary>
        /// 启动 Android 前台保活服务。
        /// </summary>
        public static void StartService()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var activity = GetCurrentActivity())
            using (var intent = CreateServiceIntent(activity, StartAction))
            {
                if (SdkInt >= 26)
                {
                    activity.Call<AndroidJavaObject>("startForegroundService", intent);
                }
                else
                {
                    activity.Call<AndroidJavaObject>("startService", intent);
                }
            }
#endif
        }

        /// <summary>
        /// 停止 Android 前台保活服务。
        /// </summary>
        public static void StopService()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var activity = GetCurrentActivity())
            using (var intent = CreateServiceIntent(activity, StopAction))
            {
                activity.Call<AndroidJavaObject>("startService", intent);
            }
#endif
        }

        /// <summary>
        /// 保存用户期望的保活服务开关状态。
        /// </summary>
        /// <param name="isEnabled">用户是否希望保活服务保持开启。</param>
        public static void SetKeepAliveSwitchEnabled(bool isEnabled)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var serviceClass = new AndroidJavaClass(ServiceClassName))
            using (var activity = GetCurrentActivity())
            {
                serviceClass.CallStatic("setKeepAliveSwitchEnabled", activity, isEnabled);
            }
#endif
        }

        /// <summary>
        /// 读取用户上次保存的保活服务开关状态。
        /// </summary>
        public static bool IsKeepAliveSwitchEnabled()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var serviceClass = new AndroidJavaClass(ServiceClassName))
            using (var activity = GetCurrentActivity())
            {
                return serviceClass.CallStatic<bool>("isKeepAliveSwitchEnabled", activity);
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 如果 SharedPreferences 中保存的保活开关为开启，则按保存状态恢复前台服务。
        /// </summary>
        public static bool EnsureServiceFromSavedState()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var serviceClass = new AndroidJavaClass(ServiceClassName))
            using (var activity = GetCurrentActivity())
            {
                return serviceClass.CallStatic<bool>("ensureServiceFromSavedState", activity);
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 判断 Android 前台保活服务当前是否记录为运行中。
        /// </summary>
        public static bool IsServiceRunning()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var serviceClass = new AndroidJavaClass(ServiceClassName))
            using (var activity = GetCurrentActivity())
            {
                return serviceClass.CallStatic<bool>("isServiceRunning", activity);
            }
#else
            return Application.isPlaying;
#endif
        }

        /// <summary>
        /// 获取 Android 前台保活服务的启动时间戳，单位为 Unix 毫秒。
        /// </summary>
        public static long GetServiceStartUnixMillis()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var serviceClass = new AndroidJavaClass(ServiceClassName))
            using (var activity = GetCurrentActivity())
            {
                return serviceClass.CallStatic<long>("getStartUnixMillis", activity);
            }
#else
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
#endif
        }

        /// <summary>
        /// 获取 Android 前台保活服务的最近心跳时间戳，单位为 Unix 毫秒。
        /// </summary>
        public static long GetLastHeartbeatUnixMillis()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var serviceClass = new AndroidJavaClass(ServiceClassName))
            using (var activity = GetCurrentActivity())
            {
                return serviceClass.CallStatic<long>("getLastHeartbeatUnixMillis", activity);
            }
#else
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
#endif
        }

        /// <summary>
        /// 判断当前应用是否已被加入电池优化白名单。
        /// </summary>
        public static bool IsIgnoringBatteryOptimizations()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (SdkInt < 23)
            {
                return true;
            }

            using (var activity = GetCurrentActivity())
            using (var contextClass = new AndroidJavaClass(ContextClassName))
            using (var powerManager = activity.Call<AndroidJavaObject>("getSystemService", contextClass.GetStatic<string>("POWER_SERVICE")))
            {
                return powerManager.Call<bool>("isIgnoringBatteryOptimizations", activity.Call<string>("getPackageName"));
            }
#else
            return true;
#endif
        }

        /// <summary>
        /// 请求系统把当前应用加入电池优化白名单。
        /// </summary>
        public static void RequestIgnoreBatteryOptimizations()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (SdkInt < 23 || IsIgnoringBatteryOptimizations())
            {
                return;
            }

            using (var activity = GetCurrentActivity())
            using (var settingsClass = new AndroidJavaClass(SettingsClassName))
            using (var uri = BuildPackageUri(activity))
            using (var intent = new AndroidJavaObject(IntentClassName, settingsClass.GetStatic<string>("ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS"), uri))
            {
                activity.Call("startActivity", intent);
            }
#endif
        }

        /// <summary>
        /// 打开系统电池优化设置页，便于用户在厂商系统中手动处理白名单。
        /// </summary>
        public static void OpenBatteryOptimizationSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var activity = GetCurrentActivity())
            using (var settingsClass = new AndroidJavaClass(SettingsClassName))
            using (var intent = new AndroidJavaObject(IntentClassName, settingsClass.GetStatic<string>("ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS")))
            {
                activity.Call("startActivity", intent);
            }
#endif
        }

        /// <summary>
        /// 请求打开厂商自启动权限设置页，用户需要在系统页面中手动开启。
        /// </summary>
        /// <returns>已成功提交系统设置跳转时返回 true。</returns>
        public static bool RequestAutoStartPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var serviceClass = new AndroidJavaClass(ServiceClassName))
            using (var activity = GetCurrentActivity())
            {
                return serviceClass.CallStatic<bool>("requestAutoStartPermission", activity);
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 打开厂商后台运行、自启动或应用详情设置页。
        /// </summary>
        /// <returns>已成功提交系统设置跳转时返回 true。</returns>
        public static bool OpenBackgroundRunSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var serviceClass = new AndroidJavaClass(ServiceClassName))
            using (var activity = GetCurrentActivity())
            {
                return serviceClass.CallStatic<bool>("openBackgroundRunSettings", activity);
            }
#else
            return false;
#endif
        }

        private static AndroidJavaObject GetCurrentActivity()
        {
            using (var unityPlayer = new AndroidJavaClass(UnityPlayerClassName))
            {
                return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }

        /// <summary>
        /// 创建指向保活服务的 Android Intent。
        /// </summary>
        /// <param name="activity">当前 Unity Activity。</param>
        /// <param name="action">需要设置到 Intent 上的动作名。</param>
        private static AndroidJavaObject CreateServiceIntent(AndroidJavaObject activity, string action)
        {
            var intent = new AndroidJavaObject(IntentClassName, activity, new AndroidJavaClass(ServiceClassName));
            intent.Call<AndroidJavaObject>("setAction", action);
            return intent;
        }

        /// <summary>
        /// 创建当前应用包名对应的 Android package Uri。
        /// </summary>
        /// <param name="activity">当前 Unity Activity。</param>
        private static AndroidJavaObject BuildPackageUri(AndroidJavaObject activity)
        {
            using (var uriClass = new AndroidJavaClass(UriClassName))
            {
                return uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + activity.Call<string>("getPackageName"));
            }
        }
    }
}
