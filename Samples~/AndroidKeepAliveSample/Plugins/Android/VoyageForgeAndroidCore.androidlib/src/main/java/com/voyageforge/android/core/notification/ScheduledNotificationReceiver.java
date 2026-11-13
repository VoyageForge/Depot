package com.voyageforge.android.core.notification;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

import com.voyageforge.android.core.diagnostics.VoyageForgeAndroidLogger;
import com.voyageforge.android.core.keepalive.CrucibleKeepAliveService;

/**
 * 定时通知广播接收器，负责在 AlarmManager 或系统重启广播到达时恢复通知计划。
 */
public final class ScheduledNotificationReceiver extends BroadcastReceiver {
    /**
     * 当前类写入诊断日志时使用的业务标签。
     */
    private static final String LOG_TAG = "ScheduledNotificationReceiver";

    /**
     * 收到广播时处理定时通知或重启恢复。
     *
     * @param context Android 上下文。
     * @param intent 收到的广播意图。
     */
    @Override
    public void onReceive(Context context, Intent intent) {
        if (context == null || intent == null) {
            return;
        }

        String action = intent.getAction();
        VoyageForgeAndroidLogger.info(context, LOG_TAG, "收到广播，action=" + action);
        if (AndroidNotificationNotifier.SCHEDULE_ACTION.equals(action)) {
            boolean serviceStarted = CrucibleKeepAliveService.startForScheduledNotification(context);
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "定时通知广播尝试拉起服务，serviceStarted=" + serviceStarted);
            if (!serviceStarted) {
                VoyageForgeAndroidLogger.warn(context, LOG_TAG, "系统拒绝从广播拉起服务，改为 Receiver 直接派发通知兜底。");
                AndroidNotificationNotifier.handleScheduledNotification(context);
            }
            return;
        }

        if (Intent.ACTION_BOOT_COMPLETED.equals(action) || "android.intent.action.QUICKBOOT_POWERON".equals(action)) {
            VoyageForgeAndroidLogger.info(context, LOG_TAG, "收到开机广播，准备恢复定时通知计划。");
            AndroidNotificationNotifier.restoreScheduledNotification(context);
            CrucibleKeepAliveService.startForScheduledNotification(context);
            return;
        }

        VoyageForgeAndroidLogger.warn(context, LOG_TAG, "收到未处理广播，action=" + action);
    }
}
