package com.voyageforge.android.core.keepalive;

import android.app.AlarmManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.os.Build;
import android.os.IBinder;
import android.os.PowerManager;
import android.os.SystemClock;
import android.provider.Settings;

import com.voyageforge.android.core.diagnostics.VoyageForgeAndroidLogger;
import com.voyageforge.android.core.notification.AndroidNotificationNotifier;
import com.voyageforge.android.core.task.VoyageForgeScheduledTaskService;

/**
 * Android 前台保活服务，用常驻通知、心跳记录、WakeLock 和任务移除恢复来尽量维持后台运行。
 */
public final class CrucibleKeepAliveService extends VoyageForgeScheduledTaskService {
    /**
     * 当前类写入诊断日志时使用的业务标签。
     */
    private static final String LOG_TAG = "KeepAliveService";

    /**
     * 启动服务的 Intent 动作名。
     */
    public static final String ACTION_START = "com.voyageforge.android.core.keepalive.START_KEEP_ALIVE";

    /**
     * 停止服务的 Intent 动作名。
     */
    public static final String ACTION_STOP = "com.voyageforge.android.core.keepalive.STOP_KEEP_ALIVE";

    /**
     * 恢复服务的 Intent 动作名。
     */
    public static final String ACTION_RESTART = "com.voyageforge.android.core.keepalive.RESTART_KEEP_ALIVE";

    /**
     * 由定时通知闹钟触发服务派发检查的 Intent 动作名。
     */
    public static final String ACTION_DISPATCH_SCHEDULED_NOTIFICATION =
            "com.voyageforge.android.core.keepalive.DISPATCH_SCHEDULED_NOTIFICATION";

    /**
     * SharedPreferences 文件名。
     */
    private static final String PREFS_NAME = "crucible_keep_alive";

    /**
     * 服务运行状态存储键。
     */
    private static final String KEY_RUNNING = "running";

    /**
     * 用户手动停止状态存储键。
     */
    private static final String KEY_MANUAL_STOPPED = "manual_stopped";

    /**
     * 用户期望保活开关状态的存储键。
     */
    private static final String KEY_SWITCH_ENABLED = "switch_enabled";

    /**
     * 服务启动 Unix 毫秒时间戳存储键。
     */
    private static final String KEY_START_UNIX_MILLIS = "start_unix_millis";

    /**
     * 服务最近心跳 Unix 毫秒时间戳存储键。
     */
    private static final String KEY_LAST_HEARTBEAT_UNIX_MILLIS = "last_heartbeat_unix_millis";

    /**
     * 通知渠道 ID。
     */
    private static final String CHANNEL_ID = "crucible_keep_alive";

    /**
     * 前台服务通知 ID。
     */
    private static final int NOTIFICATION_ID = 7301;

    /**
     * 服务恢复闹钟请求码。
     */
    private static final int RESTART_REQUEST_CODE = 7302;

    /**
     * Android 14 specialUse 前台服务类型常量值，避免低版本编译 SDK 缺少字段。
     */
    private static final int FOREGROUND_SERVICE_TYPE_SPECIAL_USE = 0x40000000;

    /**
     * 心跳刷新间隔毫秒数。
     */
    private static final long HEARTBEAT_INTERVAL_MILLIS = 1000L;

    /**
     * 通知文本刷新间隔毫秒数。
     */
    private static final long NOTIFICATION_REFRESH_INTERVAL_MILLIS = 10000L;

    /**
     * 定时通知任务最短检查延迟毫秒数，避免 Handler 过于频繁地重复入队。
     */
    private static final long SCHEDULED_NOTIFICATION_MIN_CHECK_DELAY_MILLIS = 1000L;

    /**
     * 定时通知任务最长检查延迟毫秒数，避免系统闹钟被厂商拦截后长时间无人兜底。
     */
    private static final long SCHEDULED_NOTIFICATION_MAX_CHECK_DELAY_MILLIS = 60000L;

    /**
     * 最近任务列表被划掉后的恢复延迟毫秒数。
     */
    private static final long TASK_REMOVED_RESTART_DELAY_MILLIS = 2000L;

    /**
     * 服务异常销毁后的恢复延迟毫秒数。
     */
    private static final long DESTROY_RESTART_DELAY_MILLIS = 3000L;

    /**
     * 心跳定时任务 ID。
     */
    private static final String HEARTBEAT_TASK_ID = "keep_alive_heartbeat";

    /**
     * 定时通知派发任务 ID。
     */
    private static final String SCHEDULED_NOTIFICATION_TASK_ID = "scheduled_notification_dispatch";

    /**
     * 服务启动后的本地耗时基准。
     */
    private long startElapsedRealtime;

    /**
     * 最近一次刷新通知的本地耗时。
     */
    private long lastNotificationRefreshElapsedRealtime;

    /**
     * 当前服务是否正在执行手动停止流程。
     */
    private boolean stoppingManually;

    /**
     * 用于降低休眠期间 CPU 被挂起概率的局部 WakeLock。
     */
    private PowerManager.WakeLock wakeLock;

    /**
     * 服务创建时初始化通知渠道。
     */
    @Override
    public void onCreate() {
        super.onCreate();
        VoyageForgeAndroidLogger.info(this, LOG_TAG, "onCreate：保活服务创建，准备初始化通知渠道和定时任务。");
        createNotificationChannel();
        registerKeepAliveScheduledTasks();
    }

    /**
     * 注册保活服务需要长期运行的定时任务。
     */
    private void registerKeepAliveScheduledTasks() {
        registerScheduledTask(HEARTBEAT_TASK_ID, new ScheduledTask() {
            /**
             * 写入一次保活心跳并刷新前台服务通知。
             *
             * @return 下一次心跳执行延迟毫秒数。
             */
            @Override
            public long runScheduledTask() {
                writeHeartbeat();
                maybeRefreshNotification();
                return HEARTBEAT_INTERVAL_MILLIS;
            }
        });

        registerScheduledTask(SCHEDULED_NOTIFICATION_TASK_ID, new ScheduledTask() {
            /**
             * 检查一次定时通知是否到期，并返回下一次检查延迟。
             *
             * @return 下一次定时通知检查延迟毫秒数。
             */
            @Override
            public long runScheduledTask() {
                return dispatchScheduledNotificationFromService();
            }
        });
    }

    /**
     * 处理启动、恢复或停止服务的 Intent。
     */
    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent == null ? ACTION_START : intent.getAction();
        VoyageForgeAndroidLogger.info(
                this,
                LOG_TAG,
                "onStartCommand：action=" + action + ", flags=" + flags + ", startId=" + startId);
        if (ACTION_STOP.equals(action)) {
            VoyageForgeAndroidLogger.info(this, LOG_TAG, "收到手动停止 action，准备停止保活服务。");
            stopKeepAlive(true);
            return START_NOT_STICKY;
        }

        startKeepAlive();
        if (ACTION_DISPATCH_SCHEDULED_NOTIFICATION.equals(action)) {
            VoyageForgeAndroidLogger.info(this, LOG_TAG, "收到定时通知派发 action，立即执行定时通知任务。");
            startScheduledTask(SCHEDULED_NOTIFICATION_TASK_ID);
        }

        return START_STICKY;
    }

    /**
     * 绑定接口未使用，返回空。
     */
    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    /**
     * 最近任务列表被划掉时安排恢复，尽量抵消用户移除任务导致的服务停止。
     */
    @Override
    public void onTaskRemoved(Intent rootIntent) {
        super.onTaskRemoved(rootIntent);
        VoyageForgeAndroidLogger.warn(this, LOG_TAG, "onTaskRemoved：最近任务列表被划掉，manualStopped=" + isManualStopped(this));
        if (!isManualStopped(this)) {
            scheduleRestart(this, TASK_REMOVED_RESTART_DELAY_MILLIS);
        }
    }

    /**
     * 服务销毁时释放运行期资源，并在非手动停止时安排恢复。
     */
    @Override
    public void onDestroy() {
        VoyageForgeAndroidLogger.warn(
                this,
                LOG_TAG,
                "onDestroy：服务销毁，stoppingManually=" + stoppingManually
                        + ", running=" + isServiceRunning(this)
                        + ", manualStopped=" + isManualStopped(this));
        cleanupRuntimeResources();
        if (!stoppingManually && isServiceRunning(this) && !isManualStopped(this)) {
            scheduleRestart(this, DESTROY_RESTART_DELAY_MILLIS);
        }
        super.onDestroy();
    }

    /**
     * 查询保活服务当前是否记录为运行中。
     */
    public static boolean isServiceRunning(Context context) {
        return getPreferences(context).getBoolean(KEY_RUNNING, false);
    }

    /**
     * 查询保活服务是否被用户手动停止。
     */
    public static boolean isManualStopped(Context context) {
        return getPreferences(context).getBoolean(KEY_MANUAL_STOPPED, false);
    }

    /**
     * 查询用户上次保存的保活开关是否开启。
     */
    public static boolean isKeepAliveSwitchEnabled(Context context) {
        return getPreferences(context).getBoolean(KEY_SWITCH_ENABLED, false);
    }

    /**
     * 保存用户期望的保活开关状态。
     */
    public static void setKeepAliveSwitchEnabled(Context context, boolean enabled) {
        getPreferences(context).edit()
                .putBoolean(KEY_SWITCH_ENABLED, enabled)
                .putBoolean(KEY_MANUAL_STOPPED, !enabled)
                .commit();
    }

    /**
     * 查询保活服务启动 Unix 毫秒时间戳。
     */
    public static long getStartUnixMillis(Context context) {
        return getPreferences(context).getLong(KEY_START_UNIX_MILLIS, 0L);
    }

    /**
     * 查询保活服务最近心跳 Unix 毫秒时间戳。
     */
    public static long getLastHeartbeatUnixMillis(Context context) {
        return getPreferences(context).getLong(KEY_LAST_HEARTBEAT_UNIX_MILLIS, 0L);
    }

    /**
     * 按 SharedPreferences 中保存的开关状态恢复前台保活服务。
     */
    public static boolean ensureServiceFromSavedState(Context context) {
        if (!isKeepAliveSwitchEnabled(context)) {
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "ensureServiceFromSavedState：保存的保活开关为关闭，不启动服务。");
            return false;
        }

        boolean started = startFromContext(context);
        VoyageForgeAndroidLogger.info(context, LOG_TAG, "ensureServiceFromSavedState：按保存状态启动服务，started=" + started);
        return started;
    }

    /**
     * 从指定 Context 启动或恢复前台保活服务。
     */
    public static boolean startFromContext(Context context) {
        if (context == null) {
            return false;
        }

        Intent intent = new Intent(context, CrucibleKeepAliveService.class);
        intent.setAction(ACTION_RESTART);
        boolean started = startServiceIntent(context, intent);
        VoyageForgeAndroidLogger.info(context, LOG_TAG, "startFromContext：action=" + ACTION_RESTART + ", started=" + started);
        return started;
    }

    /**
     * 从定时通知广播启动前台服务，并让服务负责检查与派发到期通知。
     *
     * @param context Android 上下文。
     * @return 服务启动请求成功提交时返回 true。
     */
    public static boolean startForScheduledNotification(Context context) {
        if (context == null) {
            return false;
        }

        Intent intent = new Intent(context, CrucibleKeepAliveService.class);
        intent.setAction(ACTION_DISPATCH_SCHEDULED_NOTIFICATION);
        boolean started = startServiceIntent(context, intent);
        VoyageForgeAndroidLogger.info(
                context,
                LOG_TAG,
                "startForScheduledNotification：action=" + ACTION_DISPATCH_SCHEDULED_NOTIFICATION + ", started=" + started);
        return started;
    }

    /**
     * 按当前 Android 版本启动服务，并捕获厂商或系统后台启动限制导致的异常。
     *
     * @param context Android 上下文。
     * @param intent 服务启动 Intent。
     * @return 服务启动请求成功提交时返回 true。
     */
    private static boolean startServiceIntent(Context context, Intent intent) {
        if (context == null || intent == null) {
            return false;
        }

        try {
            Context applicationContext = context.getApplicationContext();
            if (Build.VERSION.SDK_INT >= 26) {
                applicationContext.startForegroundService(intent);
            } else {
                applicationContext.startService(intent);
            }
        } catch (Exception exception) {
            VoyageForgeAndroidLogger.error(context, LOG_TAG, "startServiceIntent：系统拒绝启动服务。", exception);
            return false;
        }

        return true;
    }

    /**
     * 安排一次延迟恢复保活服务。
     *
     * @param context Android 上下文。
     * @param delayMillis 延迟恢复毫秒数。
     */
    public static void scheduleRestart(Context context, long delayMillis) {
        if (context == null) {
            return;
        }

        long safeDelayMillis = Math.max(1000L, delayMillis);
        long nowElapsedRealtime = SystemClock.elapsedRealtime();
        long triggerAtMillis = nowElapsedRealtime + safeDelayMillis;
        String alarmApiName = Build.VERSION.SDK_INT >= 23 ? "setAndAllowWhileIdle" : "set";
        VoyageForgeAndroidLogger.info(
                context,
                LOG_TAG,
                "scheduleRestart：准备安排服务恢复，delayMillis=" + delayMillis
                        + ", safeDelayMillis=" + safeDelayMillis
                        + ", nowElapsedRealtime=" + nowElapsedRealtime
                        + ", triggerElapsedRealtime=" + triggerAtMillis
                        + ", alarmApi=" + alarmApiName
                        + ", sdkInt=" + Build.VERSION.SDK_INT
                        + ", keepAliveSwitchEnabled=" + isKeepAliveSwitchEnabled(context)
                        + ", manualStopped=" + isManualStopped(context)
                        + ", serviceRunning=" + isServiceRunning(context));

        Intent intent = new Intent(context, KeepAliveRestartReceiver.class);
        intent.setAction(ACTION_RESTART);
        intent.putExtra("restart_reason", "keep_alive_recovery");
        intent.putExtra("created_elapsed_realtime", nowElapsedRealtime);
        intent.putExtra("trigger_elapsed_realtime", triggerAtMillis);

        PendingIntent pendingIntent;
        try {
            pendingIntent = PendingIntent.getBroadcast(
                    context.getApplicationContext(),
                    RESTART_REQUEST_CODE,
                    intent,
                    pendingIntentFlags());
            VoyageForgeAndroidLogger.info(
                    context,
                    LOG_TAG,
                    "scheduleRestart：PendingIntent 已创建，requestCode=" + RESTART_REQUEST_CODE
                            + ", flags=" + pendingIntentFlags()
                            + ", isNull=" + (pendingIntent == null));
        } catch (RuntimeException exception) {
            VoyageForgeAndroidLogger.error(context, LOG_TAG, "scheduleRestart：创建 PendingIntent 失败。", exception);
            return;
        }

        if (pendingIntent == null) {
            VoyageForgeAndroidLogger.warn(context, LOG_TAG, "scheduleRestart：PendingIntent 为空，无法安排服务恢复。");
            return;
        }

        AlarmManager alarmManager = (AlarmManager)context.getSystemService(Context.ALARM_SERVICE);
        if (alarmManager == null) {
            VoyageForgeAndroidLogger.warn(context, LOG_TAG, "scheduleRestart：AlarmManager 为空，无法安排服务恢复。");
            return;
        }

        try {
            if (Build.VERSION.SDK_INT >= 23) {
                alarmManager.setAndAllowWhileIdle(AlarmManager.ELAPSED_REALTIME_WAKEUP, triggerAtMillis, pendingIntent);
            } else {
                alarmManager.set(AlarmManager.ELAPSED_REALTIME_WAKEUP, triggerAtMillis, pendingIntent);
            }
        } catch (RuntimeException exception) {
            VoyageForgeAndroidLogger.error(context, LOG_TAG, "scheduleRestart：提交恢复闹钟失败。", exception);
            return;
        }

        VoyageForgeAndroidLogger.info(
                context,
                LOG_TAG,
                "scheduleRestart：已提交恢复闹钟，alarmApi=" + alarmApiName
                        + ", triggerElapsedRealtime=" + triggerAtMillis
                        + ", remainingMillis=" + Math.max(0L, triggerAtMillis - SystemClock.elapsedRealtime()));
    }

    /**
     * 请求用户开启厂商自启动权限；Android 没有标准授权弹窗，只能跳转到厂商设置页让用户手动开启。
     *
     * @param context Android 上下文。
     * @return 已成功打开某个设置页时返回 true。
     */
    public static boolean requestAutoStartPermission(Context context) {
        if (context == null) {
            return false;
        }

        Context applicationContext = context.getApplicationContext();
        String packageName = applicationContext.getPackageName();
        VoyageForgeAndroidLogger.info(
                applicationContext,
                LOG_TAG,
                "requestAutoStartPermission：准备请求用户开启自启动权限，packageName=" + packageName
                        + ", manufacturer=" + Build.MANUFACTURER
                        + ", brand=" + Build.BRAND);

        if (tryStartSettingsIntent(applicationContext, createXiaomiAutoStartIntent())) {
            VoyageForgeAndroidLogger.info(applicationContext, LOG_TAG, "requestAutoStartPermission：已打开小米/红米自启动管理页。");
            return true;
        }

        Intent appDetailsIntent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
        appDetailsIntent.setData(Uri.parse("package:" + packageName));
        if (tryStartSettingsIntent(applicationContext, appDetailsIntent)) {
            VoyageForgeAndroidLogger.info(applicationContext, LOG_TAG, "requestAutoStartPermission：自启动页不可用，已回退到应用详情页。");
            return true;
        }

        VoyageForgeAndroidLogger.warn(applicationContext, LOG_TAG, "requestAutoStartPermission：无法打开自启动或应用详情设置页。");
        return false;
    }

    /**
     * 打开厂商后台运行或应用详情设置，便于用户允许划掉应用后继续自启动和后台运行。
     *
     * @param context Android 上下文。
     * @return 已成功打开某个设置页时返回 true。
     */
    public static boolean openBackgroundRunSettings(Context context) {
        if (context == null) {
            return false;
        }

        VoyageForgeAndroidLogger.info(context, LOG_TAG, "openBackgroundRunSettings：准备打开厂商后台运行设置。");
        Context applicationContext = context.getApplicationContext();
        String packageName = applicationContext.getPackageName();
        if (tryStartSettingsIntent(applicationContext, createXiaomiAutoStartIntent())) {
            return true;
        }

        if (tryStartSettingsIntent(applicationContext, createXiaomiPowerKeeperIntent(applicationContext, packageName))) {
            return true;
        }

        Intent appDetailsIntent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
        appDetailsIntent.setData(Uri.parse("package:" + packageName));
        if (tryStartSettingsIntent(applicationContext, appDetailsIntent)) {
            return true;
        }

        Intent batteryIntent = new Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS);
        return tryStartSettingsIntent(applicationContext, batteryIntent);
    }

    /**
     * 创建小米/红米自启动管理页 Intent。
     *
     * @return 小米/红米自启动管理页 Intent。
     */
    private static Intent createXiaomiAutoStartIntent() {
        Intent intent = new Intent();
        intent.setComponent(new ComponentName(
                "com.miui.securitycenter",
                "com.miui.permcenter.autostart.AutoStartManagementActivity"));
        return intent;
    }

    /**
     * 创建小米/红米后台省电策略页 Intent。
     *
     * @param context Android 上下文。
     * @param packageName 当前应用包名。
     * @return 小米/红米后台省电策略页 Intent。
     */
    private static Intent createXiaomiPowerKeeperIntent(Context context, String packageName) {
        Intent intent = new Intent();
        intent.setComponent(new ComponentName(
                "com.miui.powerkeeper",
                "com.miui.powerkeeper.ui.HiddenAppsConfigActivity"));
        intent.putExtra("package_name", packageName);
        intent.putExtra("package_label", context.getApplicationInfo().loadLabel(context.getPackageManager()).toString());
        return intent;
    }

    /**
     * 尝试打开系统设置 Intent。
     *
     * @param context Android 上下文。
     * @param intent 设置页 Intent。
     * @return 设置页成功打开时返回 true。
     */
    private static boolean tryStartSettingsIntent(Context context, Intent intent) {
        try {
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(intent);
            return true;
        } catch (Exception exception) {
            VoyageForgeAndroidLogger.warn(context, LOG_TAG, "tryStartSettingsIntent：打开设置失败，intent=" + intent);
            return false;
        }
    }

    /**
     * 启动前台服务、记录启动时间并启动心跳和定时通知任务。
     */
    private void startKeepAlive() {
        VoyageForgeAndroidLogger.info(this, LOG_TAG, "startKeepAlive：开始启动或刷新前台保活服务。");
        stoppingManually = false;
        startElapsedRealtime = SystemClock.elapsedRealtime();
        lastNotificationRefreshElapsedRealtime = 0L;

        long now = System.currentTimeMillis();
        long startUnixMillis = getStartUnixMillis(this);
        if (startUnixMillis <= 0L || isManualStopped(this)) {
            startUnixMillis = now;
        }

        getPreferences(this).edit()
                .putBoolean(KEY_RUNNING, true)
                .putBoolean(KEY_MANUAL_STOPPED, false)
                .putBoolean(KEY_SWITCH_ENABLED, true)
                .putLong(KEY_START_UNIX_MILLIS, startUnixMillis)
                .putLong(KEY_LAST_HEARTBEAT_UNIX_MILLIS, now)
                .apply();

        Notification notification = buildNotification(Math.max(0L, now - startUnixMillis));
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(NOTIFICATION_ID, notification, FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        } else {
            startForeground(NOTIFICATION_ID, notification);
        }
        VoyageForgeAndroidLogger.info(this, LOG_TAG, "startKeepAlive：startForeground 已执行，startUnixMillis=" + startUnixMillis);

        acquireWakeLock();
        AndroidNotificationNotifier.ensureScheduledNotification(this);
        stopAllScheduledTasks();
        startAllScheduledTasks();
        VoyageForgeAndroidLogger.info(this, LOG_TAG, "startKeepAlive：心跳和定时通知任务已启动。");
    }

    /**
     * 停止前台服务、清理运行状态并释放资源。
     */
    private void stopKeepAlive(boolean manualStop) {
        VoyageForgeAndroidLogger.info(this, LOG_TAG, "stopKeepAlive：准备停止服务，manualStop=" + manualStop);
        stoppingManually = manualStop;
        cleanupRuntimeResources();
        getPreferences(this).edit()
                .putBoolean(KEY_RUNNING, false)
                .putBoolean(KEY_MANUAL_STOPPED, manualStop)
                .putBoolean(KEY_SWITCH_ENABLED, !manualStop)
                .apply();
        if (Build.VERSION.SDK_INT >= 24) {
            stopForeground(STOP_FOREGROUND_REMOVE);
        } else {
            stopForeground(true);
        }
        stopSelf();
        VoyageForgeAndroidLogger.info(this, LOG_TAG, "stopKeepAlive：stopSelf 已提交。");
    }

    /**
     * 清理心跳任务和 WakeLock。
     */
    private void cleanupRuntimeResources() {
        stopAllScheduledTasks();
        releaseWakeLock();
    }

    /**
     * 写入一次当前服务心跳时间。
     */
    private void writeHeartbeat() {
        getPreferences(this).edit()
                .putBoolean(KEY_RUNNING, true)
                .putLong(KEY_LAST_HEARTBEAT_UNIX_MILLIS, System.currentTimeMillis())
                .apply();
    }

    /**
     * 在前台服务内检查并派发到期的定时通知。
     *
     * @return 下一次检查应该延迟的毫秒数。
     */
    private long dispatchScheduledNotificationFromService() {
        if (!AndroidNotificationNotifier.isScheduledNotificationEnabledFromContext(this)) {
            VoyageForgeAndroidLogger.info(this, LOG_TAG, "dispatchScheduledNotificationFromService：定时通知未开启。");
            return SCHEDULED_NOTIFICATION_MAX_CHECK_DELAY_MILLIS;
        }

        boolean dispatched = AndroidNotificationNotifier.maybeDispatchScheduledNotificationFromService(this);
        long nextTriggerUnixMillis = AndroidNotificationNotifier.getScheduledNotificationNextTriggerUnixMillisFromContext(this);
        long intervalMillis = AndroidNotificationNotifier.getScheduledNotificationIntervalMillisFromContext(this);
        long nextDelayMillis = nextTriggerUnixMillis > 0L
                ? nextTriggerUnixMillis - System.currentTimeMillis()
                : intervalMillis;
        long safeNextDelayMillis = clampScheduledNotificationCheckDelay(nextDelayMillis);
        VoyageForgeAndroidLogger.info(
                this,
                LOG_TAG,
                "dispatchScheduledNotificationFromService：检查完成，dispatched=" + dispatched
                        + ", nextTriggerUnixMillis=" + nextTriggerUnixMillis
                        + ", intervalMillis=" + intervalMillis
                        + ", nextDelayMillis=" + safeNextDelayMillis);
        return safeNextDelayMillis;
    }

    /**
     * 限制定时通知检查延迟，兼顾及时触发和后台功耗。
     *
     * @param delayMillis 原始延迟毫秒数。
     * @return 限制后的延迟毫秒数。
     */
    private static long clampScheduledNotificationCheckDelay(long delayMillis) {
        long safeDelayMillis = Math.max(SCHEDULED_NOTIFICATION_MIN_CHECK_DELAY_MILLIS, delayMillis);
        return Math.min(safeDelayMillis, SCHEDULED_NOTIFICATION_MAX_CHECK_DELAY_MILLIS);
    }

    /**
     * 到达刷新间隔时更新通知文本。
     */
    private void maybeRefreshNotification() {
        long nowElapsedRealtime = SystemClock.elapsedRealtime();
        if (nowElapsedRealtime - lastNotificationRefreshElapsedRealtime < NOTIFICATION_REFRESH_INTERVAL_MILLIS) {
            return;
        }

        lastNotificationRefreshElapsedRealtime = nowElapsedRealtime;
        long aliveMillis = Math.max(0L, System.currentTimeMillis() - getStartUnixMillis(this));
        NotificationManager notificationManager = (NotificationManager)getSystemService(Context.NOTIFICATION_SERVICE);
        if (notificationManager != null) {
            notificationManager.notify(NOTIFICATION_ID, buildNotification(aliveMillis));
        } else {
            VoyageForgeAndroidLogger.warn(this, LOG_TAG, "maybeRefreshNotification：NotificationManager 为空，无法刷新前台通知。");
        }
    }

    /**
     * 创建前台服务通知。
     */
    private Notification buildNotification(long aliveMillis) {
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
                ? new Notification.Builder(this, CHANNEL_ID)
                : new Notification.Builder(this);

        builder.setContentTitle("Crucible 后台保活运行中")
                .setContentText("已存活 " + formatDuration(aliveMillis))
                .setSmallIcon(getApplicationInfo().icon)
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setShowWhen(false);

        if (Build.VERSION.SDK_INT >= 21) {
            builder.setCategory(Notification.CATEGORY_SERVICE);
        }

        return builder.build();
    }

    /**
     * 创建 Android 8 及以上需要的通知渠道。
     */
    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT < 26) {
            return;
        }

        NotificationChannel channel = new NotificationChannel(
                CHANNEL_ID,
                "Crucible 后台保活",
                NotificationManager.IMPORTANCE_LOW);
        channel.setDescription("显示 Crucible 后台保活服务的运行状态");
        channel.setShowBadge(false);

        NotificationManager notificationManager = (NotificationManager)getSystemService(Context.NOTIFICATION_SERVICE);
        if (notificationManager != null) {
            notificationManager.createNotificationChannel(channel);
            VoyageForgeAndroidLogger.info(this, LOG_TAG, "createNotificationChannel：前台服务通知渠道已创建。");
        } else {
            VoyageForgeAndroidLogger.warn(this, LOG_TAG, "createNotificationChannel：NotificationManager 为空。");
        }
    }

    /**
     * 获取或创建局部 WakeLock。
     */
    private void acquireWakeLock() {
        if (wakeLock != null && wakeLock.isHeld()) {
            return;
        }

        PowerManager powerManager = (PowerManager)getSystemService(Context.POWER_SERVICE);
        if (powerManager == null) {
            VoyageForgeAndroidLogger.warn(this, LOG_TAG, "acquireWakeLock：PowerManager 为空。");
            return;
        }

        wakeLock = powerManager.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "Crucible:KeepAlive");
        wakeLock.setReferenceCounted(false);
        wakeLock.acquire();
        VoyageForgeAndroidLogger.info(this, LOG_TAG, "acquireWakeLock：PARTIAL_WAKE_LOCK 已获取。");
    }

    /**
     * 释放局部 WakeLock。
     */
    private void releaseWakeLock() {
        if (wakeLock != null && wakeLock.isHeld()) {
            wakeLock.release();
            VoyageForgeAndroidLogger.info(this, LOG_TAG, "releaseWakeLock：WakeLock 已释放。");
        }
        wakeLock = null;
    }

    /**
     * 创建 PendingIntent 需要的兼容标记。
     */
    private static int pendingIntentFlags() {
        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= 23) {
            flags |= PendingIntent.FLAG_IMMUTABLE;
        }
        return flags;
    }

    /**
     * 获取 SharedPreferences 实例。
     */
    private static SharedPreferences getPreferences(Context context) {
        return context.getApplicationContext().getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
    }

    /**
     * 格式化毫秒时长为天时分秒文本。
     */
    private static String formatDuration(long millis) {
        long totalSeconds = Math.max(0L, millis / 1000L);
        long days = totalSeconds / 86400L;
        long hours = (totalSeconds % 86400L) / 3600L;
        long minutes = (totalSeconds % 3600L) / 60L;
        long seconds = totalSeconds % 60L;
        if (days > 0L) {
            return days + "天 " + twoDigits(hours) + ":" + twoDigits(minutes) + ":" + twoDigits(seconds);
        }
        return twoDigits(hours) + ":" + twoDigits(minutes) + ":" + twoDigits(seconds);
    }

    /**
     * 格式化两位数字文本。
     */
    private static String twoDigits(long value) {
        return value < 10L ? "0" + value : Long.toString(value);
    }
}
