package com.voyageforge.android.core.task;

import android.app.Service;
import android.os.Handler;
import android.os.Looper;

import java.util.LinkedHashMap;
import java.util.Map;

import com.voyageforge.android.core.diagnostics.VoyageForgeAndroidLogger;

/**
 * VoyageForge Android Core 通用定时任务服务基类，负责在 Service 存活期间统一调度多个周期任务。
 */
public abstract class VoyageForgeScheduledTaskService extends Service {
    /**
     * 当前类写入诊断日志时使用的业务标签。
     */
    private static final String LOG_TAG = "ScheduledTaskService";

    /**
     * 主线程任务调度 Handler。
     */
    private final Handler scheduledTaskHandler = new Handler(Looper.getMainLooper());

    /**
     * 已注册的定时任务记录表。
     */
    private final Map<String, ScheduledTaskRecord> scheduledTaskRecords = new LinkedHashMap<>();

    /**
     * 可被服务调度的定时任务接口。
     */
    protected interface ScheduledTask {
        /**
         * 执行一次定时任务，并返回下一次执行的延迟毫秒数。
         *
         * @return 下一次执行延迟毫秒数；返回负数时表示任务不再自动续约。
         */
        long runScheduledTask();
    }

    /**
     * 服务销毁时停止所有定时任务。
     */
    @Override
    public void onDestroy() {
        stopAllScheduledTasks();
        super.onDestroy();
    }

    /**
     * 注册一个可重复调度的定时任务。
     *
     * @param taskId 定时任务 ID。
     * @param scheduledTask 定时任务实例。
     */
    protected final void registerScheduledTask(String taskId, ScheduledTask scheduledTask) {
        if (taskId == null || taskId.length() == 0 || scheduledTask == null || scheduledTaskRecords.containsKey(taskId)) {
            VoyageForgeAndroidLogger.warn(this, LOG_TAG, "注册定时任务被忽略，taskId=" + taskId);
            return;
        }

        scheduledTaskRecords.put(taskId, new ScheduledTaskRecord(taskId, scheduledTask));
        VoyageForgeAndroidLogger.info(this, LOG_TAG, "已注册定时任务，taskId=" + taskId);
    }

    /**
     * 注销一个已注册的定时任务。
     *
     * @param taskId 定时任务 ID。
     */
    protected final void unregisterScheduledTask(String taskId) {
        ScheduledTaskRecord scheduledTaskRecord = scheduledTaskRecords.remove(taskId);
        if (scheduledTaskRecord != null) {
            scheduledTaskHandler.removeCallbacks(scheduledTaskRecord);
            VoyageForgeAndroidLogger.info(this, LOG_TAG, "已注销定时任务，taskId=" + taskId);
        }
    }

    /**
     * 立即启动所有已注册的定时任务。
     */
    protected final void startAllScheduledTasks() {
        for (ScheduledTaskRecord scheduledTaskRecord : scheduledTaskRecords.values()) {
            startScheduledTask(scheduledTaskRecord.taskId);
        }
    }

    /**
     * 立即启动指定定时任务。
     *
     * @param taskId 定时任务 ID。
     */
    protected final void startScheduledTask(String taskId) {
        ScheduledTaskRecord scheduledTaskRecord = scheduledTaskRecords.get(taskId);
        if (scheduledTaskRecord == null) {
            VoyageForgeAndroidLogger.warn(this, LOG_TAG, "启动定时任务失败，未找到 taskId=" + taskId);
            return;
        }

        scheduledTaskHandler.removeCallbacks(scheduledTaskRecord);
        scheduledTaskHandler.post(scheduledTaskRecord);
        VoyageForgeAndroidLogger.info(this, LOG_TAG, "已提交定时任务立即执行，taskId=" + taskId);
    }

    /**
     * 停止所有已注册定时任务的待执行回调。
     */
    protected final void stopAllScheduledTasks() {
        for (ScheduledTaskRecord scheduledTaskRecord : scheduledTaskRecords.values()) {
            scheduledTaskHandler.removeCallbacks(scheduledTaskRecord);
        }
        VoyageForgeAndroidLogger.info(this, LOG_TAG, "已停止全部定时任务，count=" + scheduledTaskRecords.size());
    }

    /**
     * 单个定时任务的调度记录。
     */
    private final class ScheduledTaskRecord implements Runnable {
        /**
         * 定时任务 ID。
         */
        private final String taskId;

        /**
         * 定时任务实例。
         */
        private final ScheduledTask scheduledTask;

        /**
         * 创建定时任务调度记录。
         *
         * @param taskId 定时任务 ID。
         * @param scheduledTask 定时任务实例。
         */
        private ScheduledTaskRecord(String taskId, ScheduledTask scheduledTask) {
            this.taskId = taskId;
            this.scheduledTask = scheduledTask;
        }

        /**
         * 执行一次任务，并按照返回延迟安排下一次执行。
         */
        @Override
        public void run() {
            long nextDelayMillis = scheduledTask.runScheduledTask();
            if (!scheduledTaskRecords.containsKey(taskId) || nextDelayMillis < 0L) {
                VoyageForgeAndroidLogger.info(
                        VoyageForgeScheduledTaskService.this,
                        LOG_TAG,
                        "定时任务执行后停止续约，taskId=" + taskId + ", nextDelayMillis=" + nextDelayMillis);
                return;
            }

            scheduledTaskHandler.postDelayed(this, nextDelayMillis);
        }
    }
}
