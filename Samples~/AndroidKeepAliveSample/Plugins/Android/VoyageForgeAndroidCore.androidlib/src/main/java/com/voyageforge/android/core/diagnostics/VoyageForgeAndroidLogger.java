package com.voyageforge.android.core.diagnostics;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.util.Log;

import java.io.File;
import java.io.FileWriter;
import java.io.IOException;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

/**
 * VoyageForge Android Core 文件日志工具，用于在应用进入后台、被划掉或被厂商系统限制时保留原生链路证据。
 */
public final class VoyageForgeAndroidLogger {
    /**
     * Logcat 中使用的统一标签。
     */
    private static final String LOGCAT_TAG = "VoyageForgeAndroid";

    /**
     * App 专属外部目录下保存日志的子目录名。
     */
    private static final String LOG_DIRECTORY_NAME = "voyageforge_logs";

    /**
     * 当前日志文件名。
     */
    private static final String LOG_FILE_NAME = "voyageforge_android_core.log";

    /**
     * 上一份轮转日志文件名。
     */
    private static final String OLD_LOG_FILE_NAME = "voyageforge_android_core.old.log";

    /**
     * 单个日志文件允许的最大字节数，超过后会轮转，避免长时间运行把存储写满。
     */
    private static final long MAX_LOG_FILE_BYTES = 1024L * 1024L;

    /**
     * 日志时间戳格式，精确到毫秒，便于和手机操作时间对齐。
     */
    private static final SimpleDateFormat LOG_TIME_FORMAT =
            new SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US);

    /**
     * 私有构造函数，防止工具类被实例化。
     */
    private VoyageForgeAndroidLogger() {
    }

    /**
     * 写入一条调试级日志，同时输出到 Logcat 和文件。
     *
     * @param context Android 上下文，可以是 Activity、Service 或 Receiver 的 Context。
     * @param tag 业务模块标签。
     * @param message 日志正文。
     */
    public static void debug(Context context, String tag, String message) {
        write(context, "DEBUG", tag, message, null);
    }

    /**
     * 写入一条信息级日志，同时输出到 Logcat 和文件。
     *
     * @param context Android 上下文，可以是 Activity、Service 或 Receiver 的 Context。
     * @param tag 业务模块标签。
     * @param message 日志正文。
     */
    public static void info(Context context, String tag, String message) {
        write(context, "INFO", tag, message, null);
    }

    /**
     * 写入一条警告级日志，同时输出到 Logcat 和文件。
     *
     * @param context Android 上下文，可以是 Activity、Service 或 Receiver 的 Context。
     * @param tag 业务模块标签。
     * @param message 日志正文。
     */
    public static void warn(Context context, String tag, String message) {
        write(context, "WARN", tag, message, null);
    }

    /**
     * 写入一条错误级日志，同时输出到 Logcat 和文件。
     *
     * @param context Android 上下文，可以是 Activity、Service 或 Receiver 的 Context。
     * @param tag 业务模块标签。
     * @param message 日志正文。
     * @param throwable 关联异常。
     */
    public static void error(Context context, String tag, String message, Throwable throwable) {
        write(context, "ERROR", tag, message, throwable);
    }

    /**
     * 获取当前日志文件绝对路径，供 Unity UI 或调试输出展示。
     *
     * @param activity 当前 Unity Activity。
     * @return 日志文件路径；无法获取目录时返回空字符串。
     */
    public static String getLogFilePath(Activity activity) {
        File logFile = resolveLogFile(activity);
        return logFile == null ? "" : logFile.getAbsolutePath();
    }

    /**
     * 获取当前日志目录绝对路径，供 Unity UI 展示或引导用户手动打开目录。
     *
     * @param activity 当前 Unity Activity。
     * @return 日志目录路径；无法获取目录时返回空字符串。
     */
    public static String getLogFolderPath(Activity activity) {
        File logDirectory = resolveLogDirectory(activity);
        return logDirectory == null ? "" : logDirectory.getAbsolutePath();
    }

    /**
     * 清空当前日志和上一份轮转日志，便于重新复现一次后台通知问题。
     *
     * @param activity 当前 Unity Activity。
     * @return 删除请求成功处理时返回 true。
     */
    public static boolean clearLogFiles(Activity activity) {
        File logFile = resolveLogFile(activity);
        if (logFile == null) {
            return false;
        }

        File oldLogFile = new File(logFile.getParentFile(), OLD_LOG_FILE_NAME);
        boolean currentDeleted = !logFile.exists() || logFile.delete();
        boolean oldDeleted = !oldLogFile.exists() || oldLogFile.delete();
        return currentDeleted && oldDeleted;
    }

    /**
     * 尝试打开日志目录，便于用户从手机文件管理器中导出日志。
     *
     * @param activity 当前 Unity Activity。
     * @return 已成功提交打开目录请求时返回 true。
     */
    public static boolean openLogFolder(Activity activity) {
        File logDirectory = resolveLogDirectory(activity);
        if (activity == null || logDirectory == null) {
            return false;
        }

        info(activity, "Diagnostics", "请求打开日志目录，path=" + logDirectory.getAbsolutePath());
        if (tryOpenFolderDirectly(activity, logDirectory)) {
            return true;
        }

        return tryOpenFolderPicker(activity);
    }

    /**
     * 写入 Unity 生命周期事件，记录界面打开、关闭、获得焦点和失去焦点时间。
     *
     * @param activity 当前 Unity Activity。
     * @param eventName Unity 生命周期事件名称。
     */
    public static void logUnityLifecycleEvent(Activity activity, String eventName) {
        String safeEventName = eventName == null || eventName.length() == 0 ? "未知生命周期事件" : eventName;
        info(activity, "UnityLifecycle", "Unity 生命周期事件：" + safeEventName
                + ", eventTime=" + LOG_TIME_FORMAT.format(new Date()));
    }

    /**
     * 执行真实日志写入，内部会吞掉所有文件异常，避免诊断功能影响业务逻辑。
     *
     * @param context Android 上下文。
     * @param level 日志等级。
     * @param tag 业务模块标签。
     * @param message 日志正文。
     * @param throwable 关联异常。
     */
    private static synchronized void write(
            Context context,
            String level,
            String tag,
            String message,
            Throwable throwable) {
        String safeTag = tag == null || tag.length() == 0 ? "Core" : tag;
        String safeMessage = message == null ? "" : message;
        String logcatMessage = "[" + safeTag + "] " + safeMessage;

        if ("ERROR".equals(level)) {
            Log.e(LOGCAT_TAG, logcatMessage, throwable);
        } else if ("WARN".equals(level)) {
            Log.w(LOGCAT_TAG, logcatMessage);
        } else {
            Log.d(LOGCAT_TAG, logcatMessage);
        }

        File logFile = resolveLogFile(context);
        if (logFile == null) {
            return;
        }

        try {
            rotateIfNeeded(logFile);
            FileWriter fileWriter = new FileWriter(logFile, true);
            if (logFile.length() == 0L) {
                String creationTime = LOG_TIME_FORMAT.format(new Date());
                fileWriter.write(buildLogLine(
                        "INFO",
                        "Diagnostics",
                        "日志文件创建时间=" + creationTime + ", path=" + logFile.getAbsolutePath(),
                        null));
            }
            fileWriter.write(buildLogLine(level, safeTag, safeMessage, throwable));
            fileWriter.close();
        } catch (IOException | RuntimeException ignored) {
            // 日志系统不能影响通知、保活或安装等真实业务流程。
        }
    }

    /**
     * 拼装单行文件日志内容。
     *
     * @param level 日志等级。
     * @param tag 业务模块标签。
     * @param message 日志正文。
     * @param throwable 关联异常。
     * @return 可直接追加到文件的日志文本。
     */
    private static String buildLogLine(String level, String tag, String message, Throwable throwable) {
        StringBuilder builder = new StringBuilder();
        builder.append(LOG_TIME_FORMAT.format(new Date()))
                .append(" [")
                .append(level)
                .append("] [")
                .append(tag)
                .append("] ")
                .append(message);

        if (throwable != null) {
            builder.append(" | exception=")
                    .append(throwable.getClass().getName())
                    .append(": ")
                    .append(throwable.getMessage());
        }

        builder.append('\n');
        return builder.toString();
    }

    /**
     * 解析日志文件路径，优先使用 App 专属外部目录，失败时回退到内部 files 目录。
     *
     * @param context Android 上下文。
     * @return 日志文件对象；无法创建目录时返回 null。
     */
    private static File resolveLogFile(Context context) {
        File logDirectory = resolveLogDirectory(context);
        return logDirectory == null ? null : new File(logDirectory, LOG_FILE_NAME);
    }

    /**
     * 解析日志目录路径，优先使用 App 专属外部目录，失败时回退到内部 files 目录。
     *
     * @param context Android 上下文。
     * @return 日志目录对象；无法创建目录时返回 null。
     */
    private static File resolveLogDirectory(Context context) {
        if (context == null) {
            return null;
        }

        Context applicationContext = context.getApplicationContext();
        File logDirectory = applicationContext.getExternalFilesDir(LOG_DIRECTORY_NAME);
        if (logDirectory == null) {
            logDirectory = new File(applicationContext.getFilesDir(), LOG_DIRECTORY_NAME);
        }

        if (!logDirectory.exists() && !logDirectory.mkdirs()) {
            return null;
        }

        return logDirectory;
    }

    /**
     * 直接请求系统文件管理器打开指定目录。
     *
     * @param activity 当前 Unity Activity。
     * @param logDirectory 日志目录。
     * @return 系统接受打开目录请求时返回 true。
     */
    private static boolean tryOpenFolderDirectly(Activity activity, File logDirectory) {
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(Uri.parse(logDirectory.toURI().toString()), "resource/folder");
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(intent);
            return true;
        } catch (RuntimeException exception) {
            warn(activity, "Diagnostics", "直接打开日志目录失败，准备回退到文件夹选择器。");
            return false;
        }
    }

    /**
     * 打开系统文件夹选择器作为目录打开失败时的兜底。
     *
     * @param activity 当前 Unity Activity。
     * @return 系统接受文件夹选择器请求时返回 true。
     */
    private static boolean tryOpenFolderPicker(Activity activity) {
        try {
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(intent);
            return true;
        } catch (RuntimeException exception) {
            error(activity, "Diagnostics", "打开系统文件夹选择器失败。", exception);
            return false;
        }
    }

    /**
     * 日志文件超过阈值时进行一次简单轮转，只保留当前文件和上一份旧文件。
     *
     * @param logFile 当前日志文件。
     */
    private static void rotateIfNeeded(File logFile) {
        if (!logFile.exists() || logFile.length() < MAX_LOG_FILE_BYTES) {
            return;
        }

        File oldLogFile = new File(logFile.getParentFile(), OLD_LOG_FILE_NAME);
        if (oldLogFile.exists()) {
            oldLogFile.delete();
        }

        logFile.renameTo(oldLogFile);
    }
}
