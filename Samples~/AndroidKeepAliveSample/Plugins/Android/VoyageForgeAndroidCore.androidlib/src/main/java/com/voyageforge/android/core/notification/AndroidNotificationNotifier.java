package com.voyageforge.android.core.notification;

import android.Manifest;
import android.app.Activity;
import android.app.AlarmManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.media.AudioAttributes;
import android.media.MediaPlayer;
import android.media.RingtoneManager;
import android.net.Uri;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.provider.Settings;

import java.io.IOException;
import java.util.concurrent.TimeUnit;

import androidx.work.BackoffPolicy;
import androidx.work.Configuration;
import androidx.work.ExistingWorkPolicy;
import androidx.work.OneTimeWorkRequest;
import androidx.work.WorkManager;

import com.voyageforge.android.core.diagnostics.VoyageForgeAndroidLogger;
import com.voyageforge.android.core.keepalive.CrucibleKeepAliveService;

/**
 * Android 通知栏通知器，负责给 Unity 提供即时通知、静音通知和可取消的定时通知能力。
 */
public final class AndroidNotificationNotifier {
    /**
     * 当前类写入诊断日志时使用的业务标签。
     */
    private static final String LOG_TAG = "NotificationNotifier";

    /**
     * 定时通知广播 Action。
     */
    public static final String SCHEDULE_ACTION = "com.voyageforge.android.core.notification.SHOW_SCHEDULED_NOTIFICATION";

    /**
     * WorkManager 定时通知兜底任务唯一名称。
     */
    private static final String SCHEDULE_WORK_NAME = "voyageforge_android_core_scheduled_notification_work";

    /**
     * 默认有声通知渠道 ID，使用新 ID 避免旧渠道被系统缓存为静音。
     */
    private static final String AUDIBLE_CHANNEL_ID = "voyageforge_android_core_audible_v6";

    /**
     * 有声通知使用的 raw 资源名称，不包含扩展名。
     */
    private static final String AUDIBLE_SOUND_RESOURCE_NAME = "ding";

    /**
     * 默认有声通知渠道名称。
     */
    private static final String AUDIBLE_CHANNEL_NAME = "VoyageForge 有声通知";

    /**
     * 默认有声通知渠道描述。
     */
    private static final String AUDIBLE_CHANNEL_DESCRIPTION = "VoyageForge Android Core 普通有声通知";

    /**
     * 默认无声通知渠道 ID。
     */
    private static final String SILENT_CHANNEL_ID = "voyageforge_android_core_silent_v2";

    /**
     * 旧版通知渠道 ID 列表，用于自动清理曾经被系统或用户缓存成错误声音策略的渠道。
     */
    private static final String[] LEGACY_CHANNEL_IDS = new String[]{
            "voyageforge_android_core_audible",
            "voyageforge_android_core_audible_v2",
            "voyageforge_android_core_audible_v3",
            "voyageforge_android_core_audible_v4",
            "voyageforge_android_core_audible_v5",
            "voyageforge_android_core_silent"
    };

    /**
     * 当前通知渠道 ID 列表，用于主动重置当前版本渠道。
     */
    private static final String[] CURRENT_CHANNEL_IDS = new String[]{
            AUDIBLE_CHANNEL_ID,
            SILENT_CHANNEL_ID
    };

    /**
     * 默认无声通知渠道名称。
     */
    private static final String SILENT_CHANNEL_NAME = "VoyageForge 无声通知";

    /**
     * 默认无声通知渠道描述。
     */
    private static final String SILENT_CHANNEL_DESCRIPTION = "VoyageForge Android Core 普通无声通知";

    /**
     * 定时通知配置 SharedPreferences 名称。
     */
    private static final String SCHEDULE_PREFS_NAME = "voyageforge_android_core_scheduled_notification";

    /**
     * 定时通知是否开启的配置键。
     */
    private static final String KEY_SCHEDULE_ENABLED = "schedule_enabled";

    /**
     * 定时通知 ID 的配置键。
     */
    private static final String KEY_SCHEDULE_NOTIFICATION_ID = "schedule_notification_id";

    /**
     * 定时通知标题的配置键。
     */
    private static final String KEY_SCHEDULE_TITLE = "schedule_title";

    /**
     * 定时通知正文的配置键。
     */
    private static final String KEY_SCHEDULE_CONTENT = "schedule_content";

    /**
     * 定时通知间隔毫秒数的配置键。
     */
    private static final String KEY_SCHEDULE_INTERVAL_MILLIS = "schedule_interval_millis";

    /**
     * 定时通知是否播放声音的配置键。
     */
    private static final String KEY_SCHEDULE_PLAY_SOUND = "schedule_play_sound";

    /**
     * 定时通知下一次触发 Unix 毫秒时间戳的配置键。
     */
    private static final String KEY_SCHEDULE_NEXT_TRIGGER_UNIX_MILLIS = "schedule_next_trigger_unix_millis";

    /**
     * 默认定时通知 ID。
     */
    private static final int DEFAULT_SCHEDULE_NOTIFICATION_ID = 2001;

    /**
     * 定时通知闹钟在系统闹钟入口中使用的展示 PendingIntent 请求码。
     */
    private static final int ALARM_CLOCK_SHOW_REQUEST_CODE = 2002;

    /**
     * 默认定时通知间隔毫秒数。
     */
    private static final long DEFAULT_SCHEDULE_INTERVAL_MILLIS = 5L * 60L * 1000L;

    /**
     * 允许设置的最小定时通知间隔毫秒数。
     */
    private static final long MIN_SCHEDULE_INTERVAL_MILLIS = 15L * 1000L;

    /**
     * 默认震动节奏。
     */
    private static final long[] DEFAULT_VIBRATION_PATTERN = new long[]{0L, 180L, 80L, 180L};

    /**
     * 私有构造函数，防止工具类被实例化。
     */
    private AndroidNotificationNotifier() {
    }

    /**
     * 发送一条默认有声 Android 通知。
     *
     * @param activity 当前 Unity Activity。
     * @param notificationId 通知 ID，相同 ID 会覆盖旧通知。
     * @param title 通知标题。
     * @param content 通知正文。
     * @return 通知提交到系统时返回 true。
     */
    public static boolean showNotification(Activity activity, int notificationId, String title, String content) {
        return showNotification(activity, notificationId, title, content, true);
    }

    /**
     * 发送一条 Android 通知，可选择有声或无声。
     *
     * @param activity 当前 Unity Activity。
     * @param notificationId 通知 ID，相同 ID 会覆盖旧通知。
     * @param title 通知标题。
     * @param content 通知正文。
     * @param playSound 是否使用有声通知渠道。
     * @return 通知提交到系统时返回 true。
     */
    public static boolean showNotification(Activity activity, int notificationId, String title, String content, boolean playSound) {
        return showNotificationInternal(activity, notificationId, title, content, playSound);
    }

    /**
     * 开启一组周期定时通知。
     *
     * @param activity 当前 Unity Activity。
     * @param notificationId 通知 ID，相同 ID 会覆盖旧通知。
     * @param title 通知标题。
     * @param content 通知正文。
     * @param intervalMillis 定时通知间隔毫秒数。
     * @param playSound 是否使用有声通知渠道。
     * @return 定时通知成功交给 AlarmManager 时返回 true。
     */
    public static boolean startScheduledNotification(
            Activity activity,
            int notificationId,
            String title,
            String content,
            long intervalMillis,
            boolean playSound) {
        if (activity == null || notificationId <= 0) {
            VoyageForgeAndroidLogger.warn(activity, LOG_TAG, "startScheduledNotification：参数无效，notificationId=" + notificationId);
            return false;
        }

        long safeIntervalMillis = clampScheduleInterval(intervalMillis);
        VoyageForgeAndroidLogger.info(
                activity,
                LOG_TAG,
                "startScheduledNotification：保存定时通知配置，notificationId=" + notificationId
                        + ", intervalMillis=" + safeIntervalMillis
                        + ", playSound=" + playSound);
        SharedPreferences preferences = getSchedulePreferences(activity);
        preferences.edit()
                .putBoolean(KEY_SCHEDULE_ENABLED, true)
                .putInt(KEY_SCHEDULE_NOTIFICATION_ID, notificationId)
                .putString(KEY_SCHEDULE_TITLE, safeString(title))
                .putString(KEY_SCHEDULE_CONTENT, safeString(content))
                .putLong(KEY_SCHEDULE_INTERVAL_MILLIS, safeIntervalMillis)
                .putBoolean(KEY_SCHEDULE_PLAY_SOUND, playSound)
                .commit();

        boolean scheduled = scheduleNext(activity, notificationId, safeIntervalMillis);
        boolean workScheduled = scheduleScheduledNotificationWorkFallback(activity, safeIntervalMillis);
        VoyageForgeAndroidLogger.info(
                activity,
                LOG_TAG,
                "startScheduledNotification：AlarmManager 下一次闹钟是否提交成功=" + scheduled
                        + "，WorkManager 兜底任务是否提交成功=" + workScheduled);
        if (scheduled) {
            CrucibleKeepAliveService.setKeepAliveSwitchEnabled(activity, true);
            CrucibleKeepAliveService.startFromContext(activity);
        }

        return scheduled;
    }

    /**
     * 关闭当前保存的定时通知。
     *
     * @param activity 当前 Unity Activity。
     * @param notificationId 通知 ID。
     * @return 取消请求成功提交时返回 true。
     */
    public static boolean cancelScheduledNotification(Activity activity, int notificationId) {
        if (activity == null) {
            return false;
        }

        int safeNotificationId = notificationId > 0 ? notificationId : getSavedNotificationId(activity);
        VoyageForgeAndroidLogger.info(activity, LOG_TAG, "cancelScheduledNotification：取消定时通知，notificationId=" + safeNotificationId);
        cancelAlarm(activity, safeNotificationId);
        cancelScheduledNotificationWorkFallback(activity);
        getSchedulePreferences(activity).edit()
                .putBoolean(KEY_SCHEDULE_ENABLED, false)
                .putLong(KEY_SCHEDULE_NEXT_TRIGGER_UNIX_MILLIS, 0L)
                .commit();
        return true;
    }

    /**
     * 查询当前是否开启了定时通知。
     *
     * @param activity 当前 Unity Activity。
     * @return 定时通知已开启时返回 true。
     */
    public static boolean isScheduledNotificationEnabled(Activity activity) {
        return activity != null && getSchedulePreferences(activity).getBoolean(KEY_SCHEDULE_ENABLED, false);
    }

    /**
     * 查询当前上下文是否开启了定时通知，供前台服务读取保存配置。
     *
     * @param context Android 上下文。
     * @return 定时通知已开启时返回 true。
     */
    public static boolean isScheduledNotificationEnabledFromContext(Context context) {
        return context != null && getSchedulePreferences(context).getBoolean(KEY_SCHEDULE_ENABLED, false);
    }

    /**
     * 获取当前保存的定时通知有声配置。
     *
     * @param activity 当前 Unity Activity。
     * @return 定时通知配置为有声时返回 true。
     */
    public static boolean isScheduledNotificationSoundEnabled(Activity activity) {
        if (activity == null) {
            return false;
        }

        return getSchedulePreferences(activity).getBoolean(KEY_SCHEDULE_PLAY_SOUND, false);
    }

    /**
     * 单独更新当前保存的定时通知声音开关，不重置下一次闹钟时间。
     *
     * @param activity 当前 Unity Activity。
     * @param playSound 定时通知是否使用有声模式。
     * @return 声音开关成功写入本地配置时返回 true。
     */
    public static boolean setScheduledNotificationSoundEnabled(Activity activity, boolean playSound) {
        if (activity == null) {
            return false;
        }

        boolean saved = getSchedulePreferences(activity).edit()
                .putBoolean(KEY_SCHEDULE_PLAY_SOUND, playSound)
                .commit();
        VoyageForgeAndroidLogger.info(
                activity,
                LOG_TAG,
                "setScheduledNotificationSoundEnabled：已保存定时通知声音开关，playSound=" + playSound
                        + "，保存是否成功=" + saved);
        return saved;
    }

    /**
     * 获取当前保存的定时通知间隔毫秒数。
     *
     * @param activity 当前 Unity Activity。
     * @return 定时通知间隔毫秒数。
     */
    public static long getScheduledNotificationIntervalMillis(Activity activity) {
        if (activity == null) {
            return 0L;
        }

        return getSchedulePreferences(activity).getLong(KEY_SCHEDULE_INTERVAL_MILLIS, DEFAULT_SCHEDULE_INTERVAL_MILLIS);
    }

    /**
     * 获取当前保存的定时通知间隔毫秒数，供前台服务计算下一次检查时间。
     *
     * @param context Android 上下文。
     * @return 定时通知间隔毫秒数。
     */
    public static long getScheduledNotificationIntervalMillisFromContext(Context context) {
        if (context == null) {
            return 0L;
        }

        return getSchedulePreferences(context).getLong(KEY_SCHEDULE_INTERVAL_MILLIS, DEFAULT_SCHEDULE_INTERVAL_MILLIS);
    }

    /**
     * 获取当前保存的定时通知下一次触发时间戳。
     *
     * @param activity 当前 Unity Activity。
     * @return 下一次触发时间戳，单位为 Unix 毫秒。
     */
    public static long getScheduledNotificationNextTriggerUnixMillis(Activity activity) {
        if (activity == null) {
            return 0L;
        }

        return getSchedulePreferences(activity).getLong(KEY_SCHEDULE_NEXT_TRIGGER_UNIX_MILLIS, 0L);
    }

    /**
     * 获取当前保存的定时通知下一次触发时间戳，供前台服务计算下一次检查时间。
     *
     * @param context Android 上下文。
     * @return 下一次触发时间戳，单位为 Unix 毫秒。
     */
    public static long getScheduledNotificationNextTriggerUnixMillisFromContext(Context context) {
        if (context == null) {
            return 0L;
        }

        return getSchedulePreferences(context).getLong(KEY_SCHEDULE_NEXT_TRIGGER_UNIX_MILLIS, 0L);
    }

    /**
     * 主动检查当前定时通知是否已经到期，并在到期时立即补发。
     *
     * @param activity 当前 Unity Activity。
     * @return 已经补发到期通知时返回 true。
     */
    public static boolean maybeDispatchScheduledNotification(Activity activity) {
        if (activity == null) {
            return false;
        }

        return maybeDispatchScheduledNotification((Context) activity);
    }

    /**
     * 重置当前插件管理的通知渠道，让有声和无声通知按代码默认策略重新创建。
     *
     * @param activity 当前 Unity Activity。
     * @return 渠道重置请求执行完成时返回 true。
     */
    public static boolean resetNotificationChannels(Activity activity) {
        if (activity == null) {
            return false;
        }

        NotificationManager notificationManager =
                (NotificationManager) activity.getSystemService(Context.NOTIFICATION_SERVICE);
        if (notificationManager == null) {
            return false;
        }

        deleteChannels(notificationManager, LEGACY_CHANNEL_IDS);
        deleteChannels(notificationManager, CURRENT_CHANNEL_IDS);
        ensureChannels(activity, notificationManager);
        return true;
    }

    /**
     * 按已保存的定时通知配置重新安装下一次闹钟。
     *
     * @param activity 当前 Unity Activity。
     * @return 定时通知已开启并成功重新安装闹钟时返回 true。
     */
    public static boolean ensureScheduledNotification(Activity activity) {
        if (activity == null) {
            return false;
        }

        return ensureScheduledNotification((Context) activity);
    }

    /**
     * 按已保存的定时通知配置重新安装下一次闹钟，供前台保活服务启动时恢复计划。
     *
     * @param context Android 上下文。
     * @return 定时通知已开启并成功重新安装闹钟时返回 true。
     */
    public static boolean ensureScheduledNotification(Context context) {
        if (context == null) {
            return false;
        }

        boolean dispatched = maybeDispatchScheduledNotification(context);
        boolean alarmScheduled = ensureScheduledNotificationAlarm(context);
        boolean workScheduled = ensureScheduledNotificationWorkFallback(context);
        VoyageForgeAndroidLogger.info(
                context,
                LOG_TAG,
                "ensureScheduledNotification：恢复计划完成，是否先补发到期通知=" + dispatched
                        + "，AlarmManager 闹钟是否提交成功=" + alarmScheduled
                        + "，WorkManager 兜底任务是否提交成功=" + workScheduled);
        return dispatched || alarmScheduled;
    }

    /**
     * 按已保存的定时通知配置重新安排 AlarmManager 闹钟，不额外提交 WorkManager 任务。
     *
     * @param context Android 上下文。
     * @return 定时通知已开启并成功重新安装闹钟时返回 true。
     */
    public static boolean ensureScheduledNotificationAlarm(Context context) {
        if (context == null) {
            return false;
        }

        SharedPreferences preferences = getSchedulePreferences(context);
        if (!preferences.getBoolean(KEY_SCHEDULE_ENABLED, false)) {
            return false;
        }

        int notificationId = preferences.getInt(KEY_SCHEDULE_NOTIFICATION_ID, DEFAULT_SCHEDULE_NOTIFICATION_ID);
        long intervalMillis = clampScheduleInterval(preferences.getLong(KEY_SCHEDULE_INTERVAL_MILLIS, DEFAULT_SCHEDULE_INTERVAL_MILLIS));
        long nextTriggerUnixMillis = preferences.getLong(KEY_SCHEDULE_NEXT_TRIGGER_UNIX_MILLIS, 0L);
        long currentTimeMillis = System.currentTimeMillis();
        if (nextTriggerUnixMillis <= 0L) {
            VoyageForgeAndroidLogger.info(
                    context,
                    LOG_TAG,
                    "ensureScheduledNotificationAlarm：没有保存下一次触发时间，按当前间隔新建闹钟。");
            return scheduleNext(context, notificationId, intervalMillis);
        }

        if (nextTriggerUnixMillis <= currentTimeMillis) {
            VoyageForgeAndroidLogger.info(
                    context,
                    LOG_TAG,
                    "ensureScheduledNotificationAlarm：保存的下一次触发时间已经到期，不在恢复阶段重置倒计时，等待补发逻辑处理。"
                            + " nextTriggerUnixMillis=" + nextTriggerUnixMillis
                            + ", currentTimeMillis=" + currentTimeMillis);
            return false;
        }

        long safeNextTriggerUnixMillis = nextTriggerUnixMillis;
        boolean alarmScheduled = scheduleAt(context, notificationId, safeNextTriggerUnixMillis);
        VoyageForgeAndroidLogger.info(
                context,
                LOG_TAG,
                "ensureScheduledNotificationAlarm：AlarmManager 恢复完成，闹钟是否提交成功=" + alarmScheduled
                        + "，安全的下一次触发时间戳=" + safeNextTriggerUnixMillis);
        return alarmScheduled;
    }

    /**
     * 查询当前应用是否可以发送通知。
     *
     * @param activity 当前 Unity Activity。
     * @return 当前应用具备通知权限时返回 true。
     */
    public static boolean canPostNotifications(Activity activity) {
        return canPostNotifications((Context) activity);
    }

    /**
     * 查询当前应用是否可以安排精确闹钟。
     *
     * @param activity 当前 Unity Activity。
     * @return 当前应用可以安排精确闹钟时返回 true。
     */
    public static boolean canScheduleExactAlarms(Activity activity) {
        if (activity == null || Build.VERSION.SDK_INT < Build.VERSION_CODES.S) {
            return true;
        }

        AlarmManager alarmManager = (AlarmManager) activity.getSystemService(Context.ALARM_SERVICE);
        return alarmManager != null && alarmManager.canScheduleExactAlarms();
    }

    /**
     * 打开系统精确闹钟授权页面，供用户允许划掉应用后的定时唤醒。
     *
     * @param activity 当前 Unity Activity。
     * @return 已提交系统设置跳转时返回 true。
     */
    public static boolean requestScheduleExactAlarmPermission(Activity activity) {
        if (activity == null || canScheduleExactAlarms(activity)) {
            return false;
        }

        Intent intent = new Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM);
        intent.setData(Uri.parse("package:" + activity.getPackageName()));
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        activity.startActivity(intent);
        return true;
    }

    /**
     * 定时广播触发后发送通知并安排下一次触发。
     *
     * @param context Android 上下文。
     */
    static void handleScheduledNotification(Context context) {
        if (context == null) {
            return;
        }

        SharedPreferences preferences = getSchedulePreferences(context);
        if (!preferences.getBoolean(KEY_SCHEDULE_ENABLED, false)) {
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "handleScheduledNotification：定时通知未开启，忽略本次触发。");
            return;
        }

        int notificationId = preferences.getInt(KEY_SCHEDULE_NOTIFICATION_ID, DEFAULT_SCHEDULE_NOTIFICATION_ID);
        String title = preferences.getString(KEY_SCHEDULE_TITLE, "");
        String content = preferences.getString(KEY_SCHEDULE_CONTENT, "");
        long intervalMillis = clampScheduleInterval(preferences.getLong(KEY_SCHEDULE_INTERVAL_MILLIS, DEFAULT_SCHEDULE_INTERVAL_MILLIS));
        boolean playSound = preferences.getBoolean(KEY_SCHEDULE_PLAY_SOUND, true);

        VoyageForgeAndroidLogger.info(
                context,
                LOG_TAG,
                "handleScheduledNotification：准备发送通知，notificationId=" + notificationId
                        + ", intervalMillis=" + intervalMillis
                        + ", playSound=" + playSound);
        boolean shown = showNotificationInternal(context, notificationId, title, content, playSound);
        boolean scheduled = scheduleNext(context, notificationId, intervalMillis);
        boolean workScheduled = scheduleScheduledNotificationWorkFallback(context, intervalMillis);
        VoyageForgeAndroidLogger.info(
                context,
                LOG_TAG,
                "handleScheduledNotification：本次处理结束，通知是否提交成功=" + shown
                        + "，下一次 AlarmManager 闹钟是否提交成功=" + scheduled
                        + "，WorkManager 兜底任务是否提交成功=" + workScheduled);
    }

    /**
     * 设备重启后恢复下一次定时通知。
     *
     * @param context Android 上下文。
     */
    static void restoreScheduledNotification(Context context) {
        if (context == null) {
            return;
        }

        SharedPreferences preferences = getSchedulePreferences(context);
        if (!preferences.getBoolean(KEY_SCHEDULE_ENABLED, false)) {
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "restoreScheduledNotification：定时通知未开启，无需恢复。");
            return;
        }

        int notificationId = preferences.getInt(KEY_SCHEDULE_NOTIFICATION_ID, DEFAULT_SCHEDULE_NOTIFICATION_ID);
        long intervalMillis = clampScheduleInterval(preferences.getLong(KEY_SCHEDULE_INTERVAL_MILLIS, DEFAULT_SCHEDULE_INTERVAL_MILLIS));
        boolean scheduled = scheduleNext(context, notificationId, intervalMillis);
        boolean workScheduled = scheduleScheduledNotificationWorkFallback(context, intervalMillis);
        VoyageForgeAndroidLogger.info(
                context,
                LOG_TAG,
                "restoreScheduledNotification：恢复下一次定时通知，notificationId=" + notificationId
                        + ", intervalMillis=" + intervalMillis
                        + "，AlarmManager 闹钟是否提交成功=" + scheduled
                        + "，WorkManager 兜底任务是否提交成功=" + workScheduled);
    }

    /**
     * 由前台保活服务定时任务调用，在系统广播被厂商系统拦截时补发到期的定时通知。
     *
     * @param context Android 上下文。
     * @return 已经补发到期通知时返回 true。
     */
    public static boolean maybeDispatchScheduledNotificationFromService(Context context) {
        return maybeDispatchScheduledNotification(context);
    }

    /**
     * 主动检查当前定时通知是否已经到期，并在到期时立即补发。
     *
     * @param context Android 上下文。
     * @return 已经补发到期通知时返回 true。
     */
    public static boolean maybeDispatchScheduledNotification(Context context) {
        if (context == null) {
            return false;
        }

        SharedPreferences preferences = getSchedulePreferences(context);
        if (!preferences.getBoolean(KEY_SCHEDULE_ENABLED, false)) {
            return false;
        }

        long nextTriggerUnixMillis = preferences.getLong(KEY_SCHEDULE_NEXT_TRIGGER_UNIX_MILLIS, 0L);
        if (nextTriggerUnixMillis <= 0L || System.currentTimeMillis() < nextTriggerUnixMillis) {
            return false;
        }

        VoyageForgeAndroidLogger.info(
                context,
                LOG_TAG,
                "maybeDispatchScheduledNotification：发现定时通知到期，nextTriggerUnixMillis=" + nextTriggerUnixMillis);
        handleScheduledNotification(context);
        return true;
    }

    /**
     * 按已保存的定时通知间隔重新安排 WorkManager 兜底任务。
     *
     * @param context Android 上下文。
     * @return WorkManager 兜底任务成功提交时返回 true。
     */
    public static boolean ensureScheduledNotificationWorkFallback(Context context) {
        if (context == null) {
            return false;
        }

        SharedPreferences preferences = getSchedulePreferences(context);
        if (!preferences.getBoolean(KEY_SCHEDULE_ENABLED, false)) {
            cancelScheduledNotificationWorkFallback(context);
            return false;
        }

        long intervalMillis = clampScheduleInterval(preferences.getLong(KEY_SCHEDULE_INTERVAL_MILLIS, DEFAULT_SCHEDULE_INTERVAL_MILLIS));
        long fallbackDelayMillis = calculateSavedScheduleDelayMillis(preferences, intervalMillis);
        return scheduleScheduledNotificationWorkFallback(context, fallbackDelayMillis);
    }

    /**
     * 取消 WorkManager 定时通知兜底任务。
     *
     * @param context Android 上下文。
     */
    public static void cancelScheduledNotificationWorkFallback(Context context) {
        if (context == null) {
            return;
        }

        try {
            WorkManager workManager = resolveWorkManager(context);
            if (workManager == null) {
                return;
            }

            workManager.cancelUniqueWork(SCHEDULE_WORK_NAME);
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "cancelScheduledNotificationWorkFallback：已取消 WorkManager 兜底任务。");
        } catch (RuntimeException exception) {
            VoyageForgeAndroidLogger.error(context, LOG_TAG, "cancelScheduledNotificationWorkFallback：取消 WorkManager 兜底任务失败。", exception);
        }
    }

    /**
     * 安排一次 WorkManager 兜底任务，测试阶段可跟随较短间隔，正式周期任务仍应以系统允许的较长间隔为主。
     *
     * @param context Android 上下文。
     * @param intervalMillis 兜底任务延迟毫秒数。
     * @return WorkManager 兜底任务成功提交时返回 true。
     */
    private static boolean scheduleScheduledNotificationWorkFallback(Context context, long intervalMillis) {
        if (context == null) {
            return false;
        }

        long safeIntervalMillis = clampScheduleInterval(intervalMillis);
        try {
            WorkManager workManager = resolveWorkManager(context);
            if (workManager == null) {
                return false;
            }

            OneTimeWorkRequest workRequest = new OneTimeWorkRequest.Builder(VoyageForgeScheduledNotificationWorker.class)
                    .setInitialDelay(safeIntervalMillis, TimeUnit.MILLISECONDS)
                    .setBackoffCriteria(BackoffPolicy.LINEAR, MIN_SCHEDULE_INTERVAL_MILLIS, TimeUnit.MILLISECONDS)
                    .build();
            workManager.enqueueUniqueWork(SCHEDULE_WORK_NAME, ExistingWorkPolicy.REPLACE, workRequest);
            VoyageForgeAndroidLogger.info(
                    context,
                    LOG_TAG,
                    "scheduleScheduledNotificationWorkFallback：已提交 WorkManager 兜底任务"
                            + "，延迟毫秒数=" + safeIntervalMillis
                            + "，任务名称=" + SCHEDULE_WORK_NAME);
            return true;
        } catch (RuntimeException exception) {
            VoyageForgeAndroidLogger.error(context, LOG_TAG, "scheduleScheduledNotificationWorkFallback：提交 WorkManager 兜底任务失败。", exception);
            return false;
        }
    }

    /**
     * 根据保存的下一次触发时间计算 WorkManager 兜底延迟，避免应用回到前台时把兜底任务重新推迟一个完整周期。
     *
     * @param preferences 定时通知配置。
     * @param intervalMillis 定时通知间隔毫秒数。
     * @return 用于 WorkManager 的安全延迟毫秒数。
     */
    private static long calculateSavedScheduleDelayMillis(SharedPreferences preferences, long intervalMillis) {
        long nextTriggerUnixMillis = preferences.getLong(KEY_SCHEDULE_NEXT_TRIGGER_UNIX_MILLIS, 0L);
        if (nextTriggerUnixMillis <= 0L) {
            return intervalMillis;
        }

        long remainingMillis = nextTriggerUnixMillis - System.currentTimeMillis();
        return remainingMillis > 0L ? Math.max(MIN_SCHEDULE_INTERVAL_MILLIS, remainingMillis) : intervalMillis;
    }

    /**
     * 获取 WorkManager 实例；在独立保活进程中自动初始化失败时，使用代码方式补一次初始化。
     *
     * @param context Android 上下文。
     * @return 可用的 WorkManager 实例；仍然不可用时返回 null。
     */
    private static WorkManager resolveWorkManager(Context context) {
        Context applicationContext = context.getApplicationContext();
        try {
            return WorkManager.getInstance(applicationContext);
        } catch (IllegalStateException exception) {
            try {
                WorkManager.initialize(applicationContext, new Configuration.Builder().build());
                VoyageForgeAndroidLogger.info(context, LOG_TAG, "resolveWorkManager：WorkManager 未自动初始化，已在当前进程手动初始化。");
                return WorkManager.getInstance(applicationContext);
            } catch (RuntimeException initializeException) {
                VoyageForgeAndroidLogger.error(context, LOG_TAG, "resolveWorkManager：手动初始化 WorkManager 失败。", initializeException);
                return null;
            }
        } catch (RuntimeException exception) {
            VoyageForgeAndroidLogger.error(context, LOG_TAG, "resolveWorkManager：获取 WorkManager 实例失败。", exception);
            return null;
        }
    }

    /**
     * 发送一条 Android 通知的内部实现，会负责权限检查、通知渠道创建、震动和小米系声音兜底。
     *
     * @param context Android 上下文。
     * @param notificationId 通知 ID。
     * @param title 通知标题。
     * @param content 通知正文。
     * @param playSound 是否使用有声通知渠道。
     * @return 通知提交到系统时返回 true。
     */
    private static boolean showNotificationInternal(Context context, int notificationId, String title, String content, boolean playSound) {
        if (context == null || !canPostNotifications(context)) {
            VoyageForgeAndroidLogger.warn(context, LOG_TAG, "showNotificationInternal：上下文为空或通知权限未授予。");
            return false;
        }

        NotificationManager notificationManager =
                (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (notificationManager == null) {
            VoyageForgeAndroidLogger.warn(context, LOG_TAG, "showNotificationInternal：NotificationManager 为空。");
            return false;
        }

        ensureChannels(context, notificationManager);

        String safeTitle = title == null || title.length() == 0
                ? context.getApplicationInfo().loadLabel(context.getPackageManager()).toString()
                : title;
        String safeContent = content == null ? "" : content;
        Notification.Builder builder = createBuilder(context, playSound)
                .setSmallIcon(resolveSmallIcon(context))
                .setContentTitle(safeTitle)
                .setContentText(safeContent)
                .setStyle(new Notification.BigTextStyle().bigText(safeContent))
                .setContentIntent(createLaunchPendingIntent(context))
                .setAutoCancel(true)
                .setShowWhen(true)
                .setWhen(System.currentTimeMillis());

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            builder.setCategory(Notification.CATEGORY_STATUS);
        }

        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            if (playSound) {
                builder.setDefaults(Notification.DEFAULT_SOUND | Notification.DEFAULT_VIBRATE)
                        .setPriority(Notification.PRIORITY_HIGH);
            } else {
                builder.setDefaults(0)
                        .setSound(null)
                        .setVibrate(null)
                        .setPriority(Notification.PRIORITY_LOW);
            }
        }

        try {
            notificationManager.notify(notificationId, builder.build());
            VoyageForgeAndroidLogger.info(
                    context,
                    LOG_TAG,
                    "showNotificationInternal：通知已提交，notificationId=" + notificationId + ", playSound=" + playSound);
            if (playSound) {
                vibrateAudibleNotification(context);
                playAudibleSoundFallback(context);
            }
            return true;
        } catch (SecurityException exception) {
            VoyageForgeAndroidLogger.error(context, LOG_TAG, "showNotificationInternal：系统拒绝发送通知。", exception);
            return false;
        }
    }

    /**
     * 查询当前上下文是否具备通知权限。
     *
     * @param context Android 上下文。
     * @return 当前应用具备通知权限时返回 true。
     */
    private static boolean canPostNotifications(Context context) {
        if (context == null) {
            return false;
        }

        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
            return true;
        }

        return context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED;
    }

    /**
     * 创建适配当前 Android 版本的通知构建器。
     *
     * @param context Android 上下文。
     * @param playSound 是否使用有声通知渠道。
     * @return Android 通知构建器。
     */
    private static Notification.Builder createBuilder(Context context, boolean playSound) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            return new Notification.Builder(context, playSound ? AUDIBLE_CHANNEL_ID : SILENT_CHANNEL_ID);
        }

        return new Notification.Builder(context);
    }

    /**
     * 确保有声和无声两个通知渠道都已经创建。
     *
     * @param notificationManager Android 通知管理器。
     */
    private static void ensureChannels(Context context, NotificationManager notificationManager) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return;
        }

        deleteChannels(notificationManager, LEGACY_CHANNEL_IDS);

        Uri soundUri = resolveAudibleSoundUri(context);
        AudioAttributes audioAttributes = createAudibleAudioAttributes();

        NotificationChannel audibleChannel = new NotificationChannel(
                AUDIBLE_CHANNEL_ID,
                AUDIBLE_CHANNEL_NAME,
                NotificationManager.IMPORTANCE_HIGH);
        audibleChannel.setDescription(AUDIBLE_CHANNEL_DESCRIPTION);
        audibleChannel.setSound(soundUri, audioAttributes);
        audibleChannel.enableVibration(true);
        audibleChannel.setVibrationPattern(DEFAULT_VIBRATION_PATTERN);
        notificationManager.createNotificationChannel(audibleChannel);

        NotificationChannel silentChannel = new NotificationChannel(
                SILENT_CHANNEL_ID,
                SILENT_CHANNEL_NAME,
                NotificationManager.IMPORTANCE_DEFAULT);
        silentChannel.setDescription(SILENT_CHANNEL_DESCRIPTION);
        silentChannel.setSound(null, null);
        silentChannel.enableVibration(false);
        notificationManager.createNotificationChannel(silentChannel);
    }

    /**
     * 安排下一次定时通知。
     *
     * @param context Android 上下文。
     * @param notificationId 通知 ID。
     * @param intervalMillis 定时通知间隔毫秒数。
     * @return 闹钟提交成功时返回 true。
     */
    private static boolean scheduleNext(Context context, int notificationId, long intervalMillis) {
        AlarmManager alarmManager = (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
        if (alarmManager == null) {
            VoyageForgeAndroidLogger.warn(context, LOG_TAG, "scheduleNext：AlarmManager 为空，无法安排下一次通知。");
            return false;
        }

        long safeIntervalMillis = clampScheduleInterval(intervalMillis);
        long triggerAtMillis = System.currentTimeMillis() + safeIntervalMillis;
        VoyageForgeAndroidLogger.info(
                context,
                LOG_TAG,
                "scheduleNext：准备安排下一次通知，notificationId=" + notificationId
                        + ", intervalMillis=" + safeIntervalMillis
                        + ", triggerAtMillis=" + triggerAtMillis);
        return scheduleAt(context, notificationId, triggerAtMillis);
    }

    /**
     * 主动触发有声通知对应的震动，避免红米通知渠道震动被系统策略吞掉。
     *
     * @param context Android 上下文。
     */
    private static void vibrateAudibleNotification(Context context) {
        if (context == null) {
            return;
        }

        Vibrator vibrator = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
        if (vibrator == null || !vibrator.hasVibrator()) {
            return;
        }

        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vibrator.vibrate(VibrationEffect.createWaveform(DEFAULT_VIBRATION_PATTERN, -1));
            } else {
                vibrator.vibrate(DEFAULT_VIBRATION_PATTERN, -1);
            }
        } catch (RuntimeException exception) {
            // 系统或厂商策略拒绝震动时保持通知流程继续。
        }
    }

    /**
     * 创建有声通知和兜底播放共用的音频属性。
     *
     * @return Android 音频属性。
     */
    private static AudioAttributes createAudibleAudioAttributes() {
        return new AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_ALARM)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                .build();
    }

    /**
     * 在小米、红米和 POCO 设备上直接播放 raw 声音，绕开 MIUI 对通知渠道声音的额外静音策略。
     *
     * @param context Android 上下文。
     */
    private static void playAudibleSoundFallback(Context context) {
        if (context == null || !isXiaomiFamilyDevice()) {
            return;
        }

        MediaPlayer mediaPlayer = new MediaPlayer();
        try {
            mediaPlayer.setAudioAttributes(createAudibleAudioAttributes());
            mediaPlayer.setDataSource(context, resolveAudibleSoundUri(context));
            mediaPlayer.setOnCompletionListener(MediaPlayer::release);
            mediaPlayer.setOnErrorListener((player, what, extra) -> {
                player.release();
                return true;
            });
            mediaPlayer.prepare();
            mediaPlayer.start();
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "playAudibleSoundFallback：小米系设备 raw 声音兜底播放已启动。");
        } catch (IOException | RuntimeException exception) {
            mediaPlayer.release();
            VoyageForgeAndroidLogger.error(context, LOG_TAG, "playAudibleSoundFallback：raw 声音兜底播放失败。", exception);
        }
    }

    /**
     * 判断当前设备是否属于小米、红米或 POCO 系列。
     *
     * @return 当前设备属于小米系设备时返回 true。
     */
    private static boolean isXiaomiFamilyDevice() {
        String manufacturer = Build.MANUFACTURER == null ? "" : Build.MANUFACTURER.toLowerCase();
        String brand = Build.BRAND == null ? "" : Build.BRAND.toLowerCase();
        return manufacturer.contains("xiaomi")
                || manufacturer.contains("redmi")
                || manufacturer.contains("poco")
                || brand.contains("xiaomi")
                || brand.contains("redmi")
                || brand.contains("poco");
    }

    private static void deleteChannels(NotificationManager notificationManager, String[] channelIds) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O || notificationManager == null || channelIds == null) {
            return;
        }

        for (String channelId : channelIds) {
            if (channelId == null || channelId.length() == 0) {
                continue;
            }

            notificationManager.deleteNotificationChannel(channelId);
        }
    }

    /**
     * 解析有声通知的 raw 音频资源 URI，资源不存在时回退到系统默认通知音。
     *
     * @param context Android 上下文。
     * @return 可用于通知渠道的声音 URI。
     */
    private static Uri resolveAudibleSoundUri(Context context) {
        if (context == null) {
            return RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION);
        }

        int soundResourceId = context.getResources().getIdentifier(
                AUDIBLE_SOUND_RESOURCE_NAME,
                "raw",
                context.getPackageName());
        if (soundResourceId == 0) {
            VoyageForgeAndroidLogger.warn(context, LOG_TAG, "resolveAudibleSoundUri：未找到 raw/" + AUDIBLE_SOUND_RESOURCE_NAME + "，回退系统默认通知音。");
            return RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION);
        }

        return Uri.parse("android.resource://" + context.getPackageName() + "/raw/" + AUDIBLE_SOUND_RESOURCE_NAME);
    }

    /**
     * 在指定 Unix 毫秒时间戳安装下一次定时通知。
     *
     * @param context Android 上下文。
     * @param notificationId 通知 ID。
     * @param triggerAtMillis 触发时间戳，单位为 Unix 毫秒。
     * @return 闹钟提交成功时返回 true。
     */
    private static boolean scheduleAt(Context context, int notificationId, long triggerAtMillis) {
    
        AlarmManager alarmManager = (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
        
        if (alarmManager == null) {
                VoyageForgeAndroidLogger.warn(context, LOG_TAG, "scheduleAt：AlarmManager 为空，无法安排闹钟。");
                return false;
            }
        
            // ↓↓↓ 这行不能丢，也不能放在后面 ↓↓↓
            PendingIntent pendingIntent = createSchedulePendingIntent(context, notificationId);
            
            getSchedulePreferences(context).edit()
                        .putLong(KEY_SCHEDULE_NEXT_TRIGGER_UNIX_MILLIS, triggerAtMillis)
                        .apply();
        
        if (tryScheduleAlarmClock(context, alarmManager, triggerAtMillis, pendingIntent)) {
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "scheduleAt：已使用 setAlarmClock 安排定时通知，triggerAtMillis=" + triggerAtMillis);
            return true;
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && alarmManager.canScheduleExactAlarms()) {
            alarmManager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, triggerAtMillis, pendingIntent);
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "scheduleAt：已使用 setExactAndAllowWhileIdle 安排定时通知。");
        } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            alarmManager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, triggerAtMillis, pendingIntent);
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "scheduleAt：已使用 setAndAllowWhileIdle 安排定时通知。");
        } else {
            alarmManager.set(AlarmManager.RTC_WAKEUP, triggerAtMillis, pendingIntent);
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "scheduleAt：已使用 set 安排定时通知。");
        }

        return true;
    }

    /**
     * 尝试使用闹钟级别的精确唤醒安排定时通知，提升划掉应用后的触发概率。
     *
     * @param context Android 上下文。
     * @param alarmManager Android 闹钟管理器。
     * @param triggerAtMillis 触发时间戳，单位为 Unix 毫秒。
     * @param pendingIntent 到点需要执行的广播 PendingIntent。
     * @return 已成功使用 AlarmClock 安排闹钟时返回 true。
     */
    private static boolean tryScheduleAlarmClock(
            Context context,
            AlarmManager alarmManager,
            long triggerAtMillis,
            PendingIntent pendingIntent) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.LOLLIPOP) {
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "tryScheduleAlarmClock：系统版本低于 Android 5.0，跳过 setAlarmClock。");
            return false;
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && !alarmManager.canScheduleExactAlarms()) {
            VoyageForgeAndroidLogger.warn(context, LOG_TAG, "tryScheduleAlarmClock：没有精确闹钟权限，跳过 setAlarmClock。");
            return false;
        }

        try {
            AlarmManager.AlarmClockInfo alarmClockInfo = new AlarmManager.AlarmClockInfo(
                    triggerAtMillis,
                    createAlarmClockShowPendingIntent(context));
            alarmManager.setAlarmClock(alarmClockInfo, pendingIntent);
            return true;
        } catch (SecurityException exception) {
            VoyageForgeAndroidLogger.error(context, LOG_TAG, "tryScheduleAlarmClock：系统拒绝 setAlarmClock。", exception);
            return false;
        }
    }

    /**
     * 创建用户点击系统闹钟入口时打开应用的 PendingIntent。
     *
     * @param context Android 上下文。
     * @return 指向当前应用入口的 PendingIntent。
     */
    private static PendingIntent createAlarmClockShowPendingIntent(Context context) {
        Intent launchIntent = context.getPackageManager().getLaunchIntentForPackage(context.getPackageName());
        if (launchIntent == null) {
            launchIntent = new Intent();
            launchIntent.setPackage(context.getPackageName());
        }

        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            flags |= PendingIntent.FLAG_IMMUTABLE;
        }

        launchIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        return PendingIntent.getActivity(
                context.getApplicationContext(),
                ALARM_CLOCK_SHOW_REQUEST_CODE,
                launchIntent,
                flags);
    }

    /**
     * 取消指定 ID 对应的定时通知闹钟。
     *
     * @param context Android 上下文。
     * @param notificationId 通知 ID。
     */
    private static void cancelAlarm(Context context, int notificationId) {
        AlarmManager alarmManager = (AlarmManager) context.getSystemService(Context.ALARM_SERVICE);
        if (alarmManager == null) {
            return;
        }

        alarmManager.cancel(createSchedulePendingIntent(context, notificationId));
    }

    /**
     * 创建定时通知广播 PendingIntent。
     *
     * @param context Android 上下文。
     * @param notificationId 通知 ID。
     * @return 用于触发定时通知广播的 PendingIntent。
     */
    private static PendingIntent createSchedulePendingIntent(Context context, int notificationId) {
        Intent intent = new Intent(context, ScheduledNotificationReceiver.class);
        intent.setAction(SCHEDULE_ACTION);
        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            flags |= PendingIntent.FLAG_IMMUTABLE;
        }

        return PendingIntent.getBroadcast(context, notificationId, intent, flags);
    }

    /**
     * 获取通知小图标资源 ID。
     *
     * @param context Android 上下文。
     * @return 可用于通知栏的小图标资源 ID。
     */
    private static int resolveSmallIcon(Context context) {
        ApplicationInfo applicationInfo = context.getApplicationInfo();
        if (applicationInfo.icon != 0) {
            return applicationInfo.icon;
        }

        return android.R.drawable.ic_dialog_info;
    }

    /**
     * 创建点击通知后回到应用的 PendingIntent。
     *
     * @param context Android 上下文。
     * @return 启动当前应用的 PendingIntent。
     */
    private static PendingIntent createLaunchPendingIntent(Context context) {
        Intent launchIntent = context.getPackageManager().getLaunchIntentForPackage(context.getPackageName());
        if (launchIntent == null) {
            return null;
        }

        launchIntent.addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            flags |= PendingIntent.FLAG_IMMUTABLE;
        }

        return PendingIntent.getActivity(context, 0, launchIntent, flags);
    }

    /**
     * 获取定时通知配置。
     *
     * @param context Android 上下文。
     * @return 定时通知 SharedPreferences。
     */
    private static SharedPreferences getSchedulePreferences(Context context) {
        return context.getSharedPreferences(SCHEDULE_PREFS_NAME, Context.MODE_PRIVATE);
    }

    /**
     * 获取已保存的通知 ID。
     *
     * @param context Android 上下文。
     * @return 已保存的通知 ID。
     */
    private static int getSavedNotificationId(Context context) {
        return getSchedulePreferences(context).getInt(KEY_SCHEDULE_NOTIFICATION_ID, DEFAULT_SCHEDULE_NOTIFICATION_ID);
    }

    /**
     * 清理空字符串。
     *
     * @param value 原始字符串。
     * @return 非空字符串。
     */
    private static String safeString(String value) {
        return value == null ? "" : value;
    }

    /**
     * 限制定时通知间隔，避免过小间隔导致系统拒绝或频繁唤醒。
     *
     * @param intervalMillis 原始间隔毫秒数。
     * @return 安全的间隔毫秒数。
     */
    private static long clampScheduleInterval(long intervalMillis) {
        return Math.max(intervalMillis, MIN_SCHEDULE_INTERVAL_MILLIS);
    }
}
