using System;
using UnityEngine;

namespace VoyageForge.Depot.Samples.Android
{
    /// <summary>
    /// Android 通知声音模式。
    /// </summary>
    public enum AndroidNotificationSoundMode
    {
        /// <summary>
        /// 使用 Android 有声通知渠道。
        /// </summary>
        Audible = 1,

        /// <summary>
        /// 使用 Android 无声通知渠道。
        /// </summary>
        Silent = 0
    }

    /// <summary>
    /// Android 通知栏通知封装器，负责从 Unity 调用 VoyageForge Android Core 的即时通知和定时通知能力。
    /// </summary>
    public static class AndroidNotificationNotifier
    {
        /// <summary>
        /// Android 原生通知工具类名。
        /// </summary>
        private const string AndroidNotificationNotifierClassName = "com.voyageforge.android.core.notification.AndroidNotificationNotifier";

        /// <summary>
        /// Android UnityPlayer Java 类名。
        /// </summary>
        private const string UnityPlayerClassName = "com.unity3d.player.UnityPlayer";

        /// <summary>
        /// Android 通知权限名称，Android 13 及以上需要用户授权。
        /// </summary>
        private const string PostNotificationsPermission = "android.permission.POST_NOTIFICATIONS";

        /// <summary>
        /// Android 13 的 API 等级。
        /// </summary>
        private const int AndroidApiTiramisu = 33;

        /// <summary>
        /// 编辑器环境下模拟定时通知是否已开启。
        /// </summary>
        private static bool editorScheduledNotificationEnabled;

        /// <summary>
        /// 编辑器环境下模拟定时通知间隔毫秒数。
        /// </summary>
        private static long editorScheduledNotificationIntervalMillis;

        /// <summary>
        /// 编辑器环境下模拟定时通知下一次触发 Unix 毫秒时间戳。
        /// </summary>
        private static long editorScheduledNotificationNextTriggerUnixMillis;

        /// <summary>
        /// 编辑器环境下模拟定时通知是否使用有声模式。
        /// </summary>
        private static bool editorScheduledNotificationSoundEnabled;

        /// <summary>
        /// 查询当前应用是否可以发送 Android 通知。
        /// </summary>
        /// <returns>当前应用具备通知权限时返回 true。</returns>
        public static bool CanPostNotifications()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>("canPostNotifications", activity);
            }
#else
            return true;
#endif
        }

        /// <summary>
        /// 请求 Android 通知权限，Android 13 以下系统会直接视为无需请求。
        /// </summary>
        /// <param name="callbacks">通知权限请求回调。</param>
        public static void RequestPostNotificationsPermission(UnityEngine.Android.PermissionCallbacks callbacks = null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (AndroidKeepAliveService.SdkInt < AndroidApiTiramisu)
            {
                return;
            }

            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(PostNotificationsPermission))
            {
                return;
            }

            if (callbacks == null)
            {
                UnityEngine.Android.Permission.RequestUserPermission(PostNotificationsPermission);
                return;
            }

            UnityEngine.Android.Permission.RequestUserPermission(PostNotificationsPermission, callbacks);
#else
#endif
        }

        /// <summary>
        /// 查询当前应用是否可以安排精确闹钟。
        /// </summary>
        /// <returns>可以安排精确闹钟时返回 true。</returns>
        public static bool CanScheduleExactAlarms()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>("canScheduleExactAlarms", activity);
            }
#else
            return true;
#endif
        }

        /// <summary>
        /// 请求系统允许当前应用安排精确闹钟。
        /// </summary>
        /// <returns>已提交系统设置跳转时返回 true。</returns>
        public static bool RequestScheduleExactAlarmPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>("requestScheduleExactAlarmPermission", activity);
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 重置插件管理的 Android 通知渠道，让有声和无声通知按代码默认策略重新创建。
        /// </summary>
        /// <returns>渠道重置请求成功提交时返回 true。</returns>
        public static bool ResetNotificationChannels()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>("resetNotificationChannels", activity);
            }
#else
            Debug.Log("编辑器环境模拟重置 Android 通知渠道。");
            return true;
#endif
        }

        public static bool ShowNotification(int notificationId, string title, string content)
        {
            return ShowNotification(notificationId, title, content, AndroidNotificationSoundMode.Audible);
        }

        /// <summary>
        /// 发送一条有声 Android 通知。
        /// </summary>
        /// <param name="notificationId">通知 ID，相同 ID 会覆盖旧通知。</param>
        /// <param name="title">通知标题。</param>
        /// <param name="content">通知正文。</param>
        /// <returns>通知成功提交到系统时返回 true。</returns>
        public static bool ShowAudibleNotification(int notificationId, string title, string content)
        {
            return ShowNotification(notificationId, title, content, AndroidNotificationSoundMode.Audible);
        }

        /// <summary>
        /// 发送一条无声 Android 通知。
        /// </summary>
        /// <param name="notificationId">通知 ID，相同 ID 会覆盖旧通知。</param>
        /// <param name="title">通知标题。</param>
        /// <param name="content">通知正文。</param>
        /// <returns>通知成功提交到系统时返回 true。</returns>
        public static bool ShowSilentNotification(int notificationId, string title, string content)
        {
            return ShowNotification(notificationId, title, content, AndroidNotificationSoundMode.Silent);
        }

        /// <summary>
        /// 按指定声音模式发送一条 Android 通知。
        /// </summary>
        /// <param name="notificationId">通知 ID，相同 ID 会覆盖旧通知。</param>
        /// <param name="title">通知标题。</param>
        /// <param name="content">通知正文。</param>
        /// <param name="soundMode">通知声音模式。</param>
        /// <returns>通知成功提交到系统时返回 true。</returns>
        public static bool ShowNotification(int notificationId, string title, string content, AndroidNotificationSoundMode soundMode)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                Debug.LogWarning("Android 通知标题为空，已取消通知请求。");
                return false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>(
                    "showNotification",
                    activity,
                    notificationId,
                    title,
                    content ?? string.Empty,
                    soundMode == AndroidNotificationSoundMode.Audible);
            }
#else
            Debug.Log($"编辑器环境模拟 Android {(soundMode == AndroidNotificationSoundMode.Audible ? "有声" : "无声")}通知：{title} - {content}");
            return true;
#endif
        }

        /// <summary>
        /// 开启周期定时通知。
        /// </summary>
        /// <param name="notificationId">通知 ID，相同 ID 会覆盖旧通知。</param>
        /// <param name="title">通知标题。</param>
        /// <param name="content">通知正文。</param>
        /// <param name="interval">定时通知间隔。</param>
        /// <param name="soundMode">通知声音模式。</param>
        /// <returns>定时通知成功交给 Android 系统时返回 true。</returns>
        public static bool StartScheduledNotification(
            int notificationId,
            string title,
            string content,
            TimeSpan interval,
            AndroidNotificationSoundMode soundMode)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                Debug.LogWarning("Android 定时通知标题为空，已取消定时通知请求。");
                return false;
            }

            var intervalMillis = Math.Max(1L, (long)interval.TotalMilliseconds);
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>(
                    "startScheduledNotification",
                    activity,
                    notificationId,
                    title,
                    content ?? string.Empty,
                    intervalMillis,
                    soundMode == AndroidNotificationSoundMode.Audible);
            }
#else
            editorScheduledNotificationEnabled = true;
            editorScheduledNotificationIntervalMillis = intervalMillis;
            editorScheduledNotificationNextTriggerUnixMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + intervalMillis;
            editorScheduledNotificationSoundEnabled = soundMode == AndroidNotificationSoundMode.Audible;
            Debug.Log($"编辑器环境模拟开启 Android 定时通知：{title}，间隔 {interval.TotalSeconds:0} 秒，模式 {soundMode}");
            return true;
#endif
        }

        /// <summary>
        /// 关闭周期定时通知。
        /// </summary>
        /// <param name="notificationId">通知 ID。</param>
        /// <returns>关闭请求成功提交时返回 true。</returns>
        public static bool CancelScheduledNotification(int notificationId)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>("cancelScheduledNotification", activity, notificationId);
            }
#else
            editorScheduledNotificationEnabled = false;
            editorScheduledNotificationNextTriggerUnixMillis = 0L;
            Debug.Log("编辑器环境模拟关闭 Android 定时通知。");
            return true;
#endif
        }

        /// <summary>
        /// 查询周期定时通知是否已开启。
        /// </summary>
        /// <returns>定时通知已开启时返回 true。</returns>
        public static bool IsScheduledNotificationEnabled()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>("isScheduledNotificationEnabled", activity);
            }
#else
            return editorScheduledNotificationEnabled;
#endif
        }

        /// <summary>
        /// 查询当前周期定时通知是否保存为有声模式。
        /// </summary>
        /// <returns>定时通知保存为有声模式时返回 true。</returns>
        public static bool IsScheduledNotificationSoundEnabled()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>("isScheduledNotificationSoundEnabled", activity);
            }
#else
            return editorScheduledNotificationSoundEnabled;
#endif
        }

        /// <summary>
        /// 保存当前周期定时通知是否使用有声模式，不改变下一次闹钟触发时间。
        /// </summary>
        /// <param name="isSoundEnabled">定时通知是否使用有声模式。</param>
        /// <returns>声音状态成功保存时返回 true。</returns>
        public static bool SetScheduledNotificationSoundEnabled(bool isSoundEnabled)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>("setScheduledNotificationSoundEnabled", activity, isSoundEnabled);
            }
#else
            editorScheduledNotificationSoundEnabled = isSoundEnabled;
            Debug.Log($"编辑器环境模拟保存 Android 定时通知声音状态：{isSoundEnabled}");
            return true;
#endif
        }

        /// <summary>
        /// 按已保存的定时通知配置重新安装下一次 Android 闹钟。
        /// </summary>
        /// <returns>定时通知已开启并成功重新安装闹钟时返回 true。</returns>
        public static bool EnsureScheduledNotification()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>("ensureScheduledNotification", activity);
            }
#else
            if (!editorScheduledNotificationEnabled)
            {
                return false;
            }

            if (editorScheduledNotificationNextTriggerUnixMillis <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                editorScheduledNotificationNextTriggerUnixMillis =
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Math.Max(1L, editorScheduledNotificationIntervalMillis);
            }

            return true;
#endif
        }

        /// <summary>
        /// 主动检查定时通知是否已经到期，并在到期时立即补发。
        /// </summary>
        /// <returns>已经补发到期通知时返回 true。</returns>
        public static bool MaybeDispatchScheduledNotification()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<bool>("maybeDispatchScheduledNotification", activity);
            }
#else
            if (!editorScheduledNotificationEnabled)
            {
                return false;
            }

            var nowUnixMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (editorScheduledNotificationNextTriggerUnixMillis <= 0L ||
                nowUnixMillis < editorScheduledNotificationNextTriggerUnixMillis)
            {
                return false;
            }

            editorScheduledNotificationNextTriggerUnixMillis =
                nowUnixMillis + Math.Max(1L, editorScheduledNotificationIntervalMillis);
            return true;
#endif
        }

        /// <summary>
        /// 获取当前定时通知间隔毫秒数。
        /// </summary>
        /// <returns>当前定时通知间隔毫秒数。</returns>
        public static long GetScheduledNotificationIntervalMillis()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<long>("getScheduledNotificationIntervalMillis", activity);
            }
#else
            return editorScheduledNotificationIntervalMillis;
#endif
        }

        /// <summary>
        /// 获取当前定时通知下一次触发 Unix 毫秒时间戳。
        /// </summary>
        /// <returns>下一次触发时间戳，单位为 Unix 毫秒。</returns>
        public static long GetScheduledNotificationNextTriggerUnixMillis()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var notifierClass = new AndroidJavaClass(AndroidNotificationNotifierClassName))
            using (var activity = GetCurrentActivity())
            {
                return notifierClass.CallStatic<long>("getScheduledNotificationNextTriggerUnixMillis", activity);
            }
#else
            return editorScheduledNotificationNextTriggerUnixMillis;
#endif
        }

        /// <summary>
        /// 获取 Unity 当前 Android Activity。
        /// </summary>
        /// <returns>当前 Unity Android Activity。</returns>
        private static AndroidJavaObject GetCurrentActivity()
        {
            using (var unityPlayer = new AndroidJavaClass(UnityPlayerClassName))
            {
                return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }
    }
}
