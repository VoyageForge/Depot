package com.voyageforge.android.core.keepalive;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.os.SystemClock;

import com.voyageforge.android.core.diagnostics.VoyageForgeAndroidLogger;

/**
 * 保活恢复广播接收器，用于任务被移除、闹钟触发和开机完成后重新拉起前台服务。
 */
public final class KeepAliveRestartReceiver extends BroadcastReceiver {
    /**
     * 当前类写入诊断日志时使用的业务标签。
     */
    private static final String LOG_TAG = "KeepAliveRestartReceiver";

    /**
     * 接收恢复广播并按当前持久化状态决定是否重新启动前台服务。
     *
     * @param context Android 广播上下文。
     * @param intent 系统传入的广播 Intent。
     */
    @Override
    public void onReceive(Context context, Intent intent) {
        if (context == null) {
            return;
        }

        String action = intent == null ? CrucibleKeepAliveService.ACTION_RESTART : intent.getAction();
        long receiveElapsedRealtime = SystemClock.elapsedRealtime();
        long createdElapsedRealtime = intent == null ? -1L : intent.getLongExtra("created_elapsed_realtime", -1L);
        long triggerElapsedRealtime = intent == null ? -1L : intent.getLongExtra("trigger_elapsed_realtime", -1L);
        boolean isKeepAliveSwitchEnabled = CrucibleKeepAliveService.isKeepAliveSwitchEnabled(context);
        boolean isManualStopped = CrucibleKeepAliveService.isManualStopped(context);
        boolean isServiceRunning = CrucibleKeepAliveService.isServiceRunning(context);
        VoyageForgeAndroidLogger.info(
                context,
                LOG_TAG,
                "onReceive：收到保活恢复广播，action=" + action
                        + ", receiveElapsedRealtime=" + receiveElapsedRealtime
                        + ", createdElapsedRealtime=" + createdElapsedRealtime
                        + ", triggerElapsedRealtime=" + triggerElapsedRealtime
                        + ", triggerDelayMillis=" + (triggerElapsedRealtime > 0L ? receiveElapsedRealtime - triggerElapsedRealtime : -1L)
                        + ", keepAliveSwitchEnabled=" + isKeepAliveSwitchEnabled
                        + ", manualStopped=" + isManualStopped
                        + ", serviceRunning=" + isServiceRunning);

        if (!isKeepAliveSwitchEnabled || isManualStopped) {
            VoyageForgeAndroidLogger.warn(
                    context,
                    LOG_TAG,
                    "onReceive：恢复广播被忽略，keepAliveSwitchEnabled=" + isKeepAliveSwitchEnabled
                            + ", manualStopped=" + isManualStopped);
            return;
        }

        if (Intent.ACTION_BOOT_COMPLETED.equals(action)
                || "android.intent.action.QUICKBOOT_POWERON".equals(action)
                || CrucibleKeepAliveService.ACTION_RESTART.equals(action)
                || isServiceRunning) {
            boolean started = CrucibleKeepAliveService.startFromContext(context);
            VoyageForgeAndroidLogger.info(
                    context,
                    LOG_TAG,
                    "onReceive：已请求启动保活服务，action=" + action + ", started=" + started);
        } else {
            VoyageForgeAndroidLogger.warn(context, LOG_TAG, "onReceive：未知 action，未启动服务，action=" + action);
        }
    }
}
