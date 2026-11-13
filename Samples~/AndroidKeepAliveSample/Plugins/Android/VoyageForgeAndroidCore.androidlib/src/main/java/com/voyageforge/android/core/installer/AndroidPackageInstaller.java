package com.voyageforge.android.core.installer;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import androidx.core.content.FileProvider;

import java.io.File;

/**
 * Android APK 安装器，负责从 Unity 侧传入 APK 路径并拉起 Android 系统安装界面。
 */
public final class AndroidPackageInstaller {
    /**
     * 等待用户授权未知来源安装后继续处理的 APK 路径。
     */
    public static String pendingApkPath;

    /**
     * 私有构造函数，防止工具类被实例化。
     */
    private AndroidPackageInstaller() {
    }

    /**
     * 安装指定路径的 APK 文件；若缺少未知来源安装权限，会先打开授权设置页。
     *
     * @param activity 当前 Unity Activity。
     * @param apkPath APK 文件路径。
     * @return 已拉起安装界面或授权设置页时返回 true，否则返回 false。
     */
    public static boolean installApk(Activity activity, String apkPath) {
        if (activity == null || apkPath == null || apkPath.length() == 0) {
            return false;
        }

        File apkFile = new File(apkPath);
        if (!apkFile.exists()) {
            return false;
        }

        if (!canRequestPackageInstalls(activity)) {
            pendingApkPath = apkPath;
            openUnknownAppSourcesSettings(activity);
            return true;
        }

        installInternal(activity, apkFile);
        return true;
    }

    /**
     * 在用户从未知来源安装授权页返回后，继续安装此前记录的 APK。
     *
     * @param activity 当前 Unity Activity。
     * @return 存在待安装 APK 且已处理时返回 true，否则返回 false。
     */
    public static boolean installPendingApk(Activity activity) {
        if (pendingApkPath == null || pendingApkPath.length() == 0) {
            return false;
        }

        String apkPath = pendingApkPath;
        pendingApkPath = null;
        return installApk(activity, apkPath);
    }

    /**
     * 查询当前是否存在等待继续安装的 APK 路径。
     *
     * @return 存在待安装路径时返回 true。
     */
    public static boolean hasPendingApk() {
        return pendingApkPath != null && pendingApkPath.length() > 0;
    }

    /**
     * 查询当前应用是否可以请求安装 APK 包。
     *
     * @param activity 当前 Unity Activity。
     * @return 已允许未知来源安装或系统版本无需授权时返回 true。
     */
    public static boolean canRequestPackageInstalls(Activity activity) {
        if (activity == null) {
            return false;
        }

        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return true;
        }

        return activity.getPackageManager().canRequestPackageInstalls();
    }

    /**
     * 打开当前应用的未知来源安装授权设置页。
     *
     * @param activity 当前 Unity Activity。
     */
    private static void openUnknownAppSourcesSettings(Activity activity) {
        Intent intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES);
        intent.setData(Uri.parse("package:" + activity.getPackageName()));
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        activity.startActivity(intent);
    }

    /**
     * 使用系统安装器打开 APK 文件。
     *
     * @param activity 当前 Unity Activity。
     * @param apkFile 需要安装的 APK 文件。
     */
    private static void installInternal(Activity activity, File apkFile) {
        Intent intent = new Intent(Intent.ACTION_VIEW);
        intent.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK);

        Uri uri;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            uri = FileProvider.getUriForFile(
                    activity,
                    activity.getPackageName() + ".fileprovider",
                    apkFile);
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        } else {
            uri = Uri.fromFile(apkFile);
        }

        intent.setDataAndType(uri, "application/vnd.android.package-archive");
        activity.startActivity(intent);
    }
}
