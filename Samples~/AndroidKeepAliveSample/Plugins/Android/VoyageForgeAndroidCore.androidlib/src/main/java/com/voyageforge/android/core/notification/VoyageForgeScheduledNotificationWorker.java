package com.voyageforge.android.core.notification;

import android.content.Context;

import androidx.annotation.NonNull;
import androidx.work.Worker;
import androidx.work.WorkerParameters;

import com.voyageforge.android.core.diagnostics.VoyageForgeAndroidLogger;
import com.voyageforge.android.core.keepalive.CrucibleKeepAliveService;

/**
 * WorkManager 定时通知兜底 Worker，用于在系统允许时恢复保活服务、补发到期通知并续约下一次后台任务。
 */
public final class VoyageForgeScheduledNotificationWorker extends Worker {
    /**
     * 当前类写入诊断日志时使用的业务标签。
     */
    private static final String LOG_TAG = "ScheduledNotificationWork";

    /**
     * 创建 WorkManager Worker 实例。
     *
     * @param context Android 应用上下文。
     * @param workerParameters WorkManager 传入的任务参数。
     */
    public VoyageForgeScheduledNotificationWorker(
            @NonNull Context context,
            @NonNull WorkerParameters workerParameters) {
        super(context, workerParameters);
    }

    /**
     * 执行一次后台兜底检查，完成后由通知工具类重新安排下一次 WorkManager 任务。
     *
     * @return 本次后台任务的执行结果。
     */
    @NonNull
    @Override
    public Result doWork() {
        Context context = getApplicationContext();
        try {
            boolean scheduleEnabled = AndroidNotificationNotifier.isScheduledNotificationEnabledFromContext(context);
            boolean keepAliveEnabled = CrucibleKeepAliveService.isKeepAliveSwitchEnabled(context);
            boolean manualStopped = CrucibleKeepAliveService.isManualStopped(context);
            VoyageForgeAndroidLogger.info(
                    context,
                    LOG_TAG,
                    "doWork：WorkManager 兜底被系统唤醒，定时通知是否开启=" + scheduleEnabled
                            + "，保活开关是否开启=" + keepAliveEnabled
                            + "，是否为用户手动停止=" + manualStopped
                            + "，任务编号=" + getId());

            if (!scheduleEnabled) {
                AndroidNotificationNotifier.cancelScheduledNotificationWorkFallback(context);
                VoyageForgeAndroidLogger.info(context, LOG_TAG, "doWork：定时通知已关闭，取消 WorkManager 兜底任务。");
                return Result.success();
            }

            if (keepAliveEnabled && !manualStopped) {
                boolean serviceStarted = CrucibleKeepAliveService.startForScheduledNotification(context);
                VoyageForgeAndroidLogger.info(context, LOG_TAG, "doWork：尝试通过 WorkManager 拉起保活服务，服务启动请求是否成功=" + serviceStarted);
            }

            boolean dispatched = AndroidNotificationNotifier.maybeDispatchScheduledNotification(context);
            boolean alarmEnsured = AndroidNotificationNotifier.ensureScheduledNotificationAlarm(context);
            boolean workScheduled = AndroidNotificationNotifier.ensureScheduledNotificationWorkFallback(context);
            VoyageForgeAndroidLogger.info(
                    context,
                    LOG_TAG,
                    "doWork：兜底处理完成，是否补发了到期通知=" + dispatched
                            + "，AlarmManager 闹钟是否恢复成功=" + alarmEnsured
                            + "，下一次 WorkManager 兜底是否提交成功=" + workScheduled);
            return Result.success();
        } catch (Throwable throwable) {
            VoyageForgeAndroidLogger.error(context, LOG_TAG, "doWork：WorkManager 兜底执行失败，交给系统稍后重试。", throwable);
            return Result.retry();
        }
    }
}
