using UnityEngine;

namespace VoyageForge.Depot.Samples.Android
{
    /// <summary>
    /// Android APK 安装桥接器，负责从 Unity 调用 VoyageForge Android Core 插件的安装能力。
    /// </summary>
    public static class AndroidPackageInstaller
    {
        /// <summary>
        /// Android 原生 APK 安装工具类名。
        /// </summary>
        private const string AndroidPackageInstallerClassName = "com.voyageforge.android.core.installer.AndroidPackageInstaller";

        /// <summary>
        /// Android UnityPlayer Java 类名。
        /// </summary>
        private const string UnityPlayerClassName = "com.unity3d.player.UnityPlayer";

        /// <summary>
        /// 查询当前应用是否可以请求安装 APK 包。
        /// </summary>
        public static bool CanRequestPackageInstalls()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var installerClass = new AndroidJavaClass(AndroidPackageInstallerClassName))
            using (var activity = GetCurrentActivity())
            {
                return installerClass.CallStatic<bool>("canRequestPackageInstalls", activity);
            }
#else
            return true;
#endif
        }

        /// <summary>
        /// 安装指定路径的 APK 文件；若缺少未知来源安装权限，会先打开授权设置页。
        /// </summary>
        /// <param name="apkPath">APK 文件路径。</param>
        /// <returns>已拉起安装界面或授权设置页时返回 true。</returns>
        public static bool InstallApk(string apkPath)
        {
            if (string.IsNullOrWhiteSpace(apkPath))
            {
                Debug.LogWarning("APK 安装路径为空，已取消安装请求。");
                return false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            using (var installerClass = new AndroidJavaClass(AndroidPackageInstallerClassName))
            using (var activity = GetCurrentActivity())
            {
                return installerClass.CallStatic<bool>("installApk", activity, apkPath);
            }
#else
            Debug.Log("编辑器环境跳过 APK 安装：" + apkPath);
            return false;
#endif
        }

        /// <summary>
        /// 继续安装此前因未知来源安装授权而挂起的 APK。
        /// </summary>
        /// <returns>存在待安装 APK 且已处理时返回 true。</returns>
        public static bool InstallPendingApk()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var installerClass = new AndroidJavaClass(AndroidPackageInstallerClassName))
            using (var activity = GetCurrentActivity())
            {
                return installerClass.CallStatic<bool>("installPendingApk", activity);
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 查询是否存在等待继续安装的 APK。
        /// </summary>
        /// <returns>存在待安装 APK 时返回 true。</returns>
        public static bool HasPendingApk()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var installerClass = new AndroidJavaClass(AndroidPackageInstallerClassName))
            {
                return installerClass.CallStatic<bool>("hasPendingApk");
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 获取 Unity 当前 Android Activity。
        /// </summary>
        private static AndroidJavaObject GetCurrentActivity()
        {
            using (var unityPlayer = new AndroidJavaClass(UnityPlayerClassName))
            {
                return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }
    }
}
