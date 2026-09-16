# Depot

## 简介
Depot 是 VoyageForge 的基础工具仓库，定位为 Unity 开发过程中的公共物资库。

它收拢项目中高频复用的运行时工具、编辑器辅助能力和通用扩展，让常用能力有统一的存放位置、命名方式和维护边界。

## 当前内容
- 运行时通用能力，例如数学工具、单例基类、场景引用与状态机辅助。
- 基于 UI Toolkit 的运行时控制台，支持直接输出日志、按需桥接 Unity 日志、按类型过滤与执行命令。
- Android 原生能力封装，例如后台保活、APK 安装、系统设置跳转与后续可扩展的移动端桥接能力。
- 编辑器辅助能力，例如只读属性绘制、Project Settings 配置、构建前自动版本处理与启动项控制。
- 面向包开发的基础设施，例如程序集划分、打包元数据与工作流配置。

## 目录说明
- `Runtime/Scripts/Utilities`
  Depot 的运行时通用工具目录。
- `Runtime/Scripts/Attributes`
  运行时可用的特性定义。
- `Runtime/Scripts/Console`
  基于 UI Toolkit（UXML + USS）的运行时控制台实现。
- `Runtime/Resources/Depot/Console`
  运行时控制台的 UXML 与 USS 资源。
- `Runtime/Scripts/Android`
  Unity C# 侧的 Android 原生能力调用封装。
- `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib`
  VoyageForge Android Core 原生插件源码目录。
- `Runtime/Plugins/Android/core-1.12.0.aar`
  AndroidX Core 依赖，用于 `FileProvider` 等 AndroidX 能力。
- `Samples~/Android`
  Android 原生能力的 Unity 调用示例，导入示例后可挂到场景对象或按钮事件上测试。
- `Editor/Scripts/Utilities`
  Depot 的编辑器工具与 Project Settings 相关实现。
  其中 `DepotSettingsProvider.cs` 是 Depot 的 Project Settings 面板入口，负责展示自动版本号和启动项相关配置。
- `Editor/Scripts/Inspector`
  自定义 Inspector 与编辑器界面相关实现。

## 设计目标
- 把零散的共用工具统一沉淀到一个稳定的仓库模块中。
- 尽量让运行时工具与编辑器工具边界清晰，便于裁剪与维护。
- 为其他包提供可复用的底层支持，而不是把通用能力散落到业务代码中。

## 运行时 Console

`RuntimeConsole` 是基于 UI Toolkit（UXML + USS）实现的运行时控制台。默认不监听 Unity 日志，推荐用 `RuntimeConsole.Log / Warning / Error` 直接输出；如需把 `Debug.Log` / `LogWarning` / `LogError` / `Exception` 转发进来，执行 `listen on` 命令开启桥接。

### 使用方式

`RuntimeConsole` 继承自 Depot 的 `MonoSingleton`，可被继承。推荐在业务侧用 `[RuntimeInitializeOnLoadMethod]` 初始化——此时只创建实例、**不显示**面板，运行后连按 3 次 `Tab` 键唤醒/切换显隐。

```csharp
using UnityEngine;
using VoyageForge.Depot.Runtime.Console;

public static class ConsoleBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        // 创建单例并初始化（默认隐藏），不会显示面板
        RuntimeConsole.Initialize();
    }
}
```

也可以直接把 `RuntimeConsole` 挂到场景中的 GameObject 上（会自动添加 `UIDocument`），或通过 `RuntimeConsole.Instance` 惰性创建。

```csharp
// 显示 / 隐藏 / 切换
RuntimeConsole.ShowInstance();
RuntimeConsole.HideInstance();
RuntimeConsole.ToggleInstance();

// 清空日志
RuntimeConsole.Instance.Clear();
```

### 直接输出日志

`RuntimeConsole.Log / Warning / Error` 直接写入控制台缓冲并自动捕获调用方栈；在编辑器非播放模式下仅打印到 Unity 控制台：

```csharp
RuntimeConsole.Log("普通日志");
RuntimeConsole.Warning("警告");
RuntimeConsole.Error("错误");
```

自定义命令只需继承 `ConsoleCommand` 并标注 `[Preserve]`（防止 IL2CPP 代码剥离），无需手动注册，会被自动发现：

```csharp
using UnityEngine.Scripting;
using VoyageForge.Depot.Runtime.Console;

[Preserve]
public sealed class HelloCommand : ConsoleCommand
{
    public override string Name => "hello";
    public override string Usage => "hello <name>";
    public override string Description => "打印一句问候";

    public override void Execute(string[] args)
    {
        RuntimeConsole.WriteDirect("Hello " + string.Join(" ", args));
    }
}
```

### 内置命令

| 命令 | 用法 | 说明 |
| --- | --- | --- |
| `help` | `help` | 列出所有已注册命令及其用法、描述。 |
| `clear` | `clear` | 清空日志缓冲与计数。 |
| `log` | `log <message>` | 打印一段文本（参数拼回一句话）。 |
| `listen` | `listen [on\|off\|autostart on\|off]` | 控制是否监听 Unity 日志并转发到控制台，支持自启动，状态持久化到 PlayerPrefs。 |
| `exit` | `exit` | 退出 / 隐藏控制台（等价于点击关闭按钮）。 |

`ConsoleCommand` 基类成员：

| 成员 | 说明 |
| --- | --- |
| `Name` | 抽象属性，命令名（不区分大小写，全局唯一）。 |
| `Usage` | 用法字符串，用于 `help` 展示；默认等于 `Name`。 |
| `Description` | 简短描述，用于 `help` 展示；默认空字符串。 |
| `Execute(string[] args)` | 抽象方法，命令执行体；`args` 为空格分隔的参数数组。 |
| `GetArgumentCompletions(string[] args, string currentInput)` | 参数补全建议（可选，默认返回空）。 |
| `OnCreate()` | 命令注册成功后（主线程）调用，可订阅事件、初始化状态。 |
| `OnDestroy()` | 命令被移除时（主线程）调用，可清理资源、取消订阅。 |

### 生命周期

`RuntimeConsole` 复用 `MonoSingleton` 的生命周期，并额外提供控制台专属钩子：

| 钩子 | 说明 |
| --- | --- |
| `OnInitialize()` | MonoSingleton 初始化回调（Awake 阶段调用），完成面板构建并启动命令后台扫描；派生类重写时需调用 `base.OnInitialize()`。 |
| `OnConsoleInitialized()` | 面板构建完成、命令后台扫描已启动后调用。 |
| `OnVisibilityChanged(bool)` | 显隐状态变化时调用。 |
| `OnLogReceived(ConsoleLogEntry)` | 每条日志写入缓冲后调用。 |
| `OnAwake()` / `OnApplicationQuitting()` / `OnDestroying()` | 继承自 MonoSingleton，可按需重写。 |

### 交互说明

| 操作 | 行为 |
| --- | --- |
| 连按 3 次 `Tab` | 唤醒 / 切换控制台显隐。 |
| 打开控制台 | 命令输入框自动获得焦点，可直接输入。 |
| 输入命令名 | 自动弹出前缀匹配的补全建议。 |
| 上下方向键 | 在补全建议间移动选中项。 |
| `Tab`（有补全建议） | 把选中项填入输入框。 |
| 双击 `Tab`（无补全建议） | 全选输入框内容。 |
| 回车 | 执行命令，执行后自动恢复焦点、光标回到开头。 |
| 关闭按钮（×） | 退出控制台（走 `exit` 命令）。 |

> 命令扫描在后台线程进行，扫描期间输入框禁用并显示 loading，不阻塞主线程；命令输入框聚焦时 `Tab` 不再作为唤醒键，优先用于补全 / 全选。

### 面板能力

| 能力 | 说明 |
| --- | --- |
| 唤醒 | 连按 3 次 `Tab` 键切换显隐（次数与间隔窗口可配置）。 |
| 日志捕获 | 默认不监听 Unity 日志；用 `RuntimeConsole.Log / Warning / Error` 直写，或执行 `listen on` 桥接 Unity 日志。缓冲最近 300 条（`_maxEntries` 可调），列表采用虚拟化 `ListView` 渲染。 |
| 类型过滤 | All / Log / Warning / Error 四档过滤。 |
| 堆栈展开 | 点击日志条目展开/收起调用堆栈。 |
| 拖拽 | 拖动顶部标题栏移动面板。 |
| 折叠 | 点击标题栏 `-` 按钮折叠/展开面板。 |
| 清空 | 点击 `Clear` 按钮或执行 `clear` 命令。 |
| 命令输入 | 底部输入框回车执行命令，内置 `help`、`clear`、`log`、`exit`（退出）。 |
| 命令补全 | 输入命令名时弹出前缀匹配建议，上下方向键选择、`Tab` 或点击补全；无建议时双击 `Tab` 全选输入。 |
| 后台扫描 | 命令在后台线程反射发现，不阻塞主线程；扫描期间禁用输入并显示 loading。 |

### 目录

| 路径 | 说明 |
| --- | --- |
| `Runtime/Scripts/Console/ConsoleLogEntry.cs` | 日志条目模型与过滤类型。 |
| `Runtime/Scripts/Console/ConsoleCommand.cs` | 命令抽象基类（派生类标注 [Preserve] 后自动注册）。 |
| `Runtime/Scripts/Console/ConsoleCommandRegistry.cs` | 命令注册表（反射发现、注册、查询与补全建议）。 |
| `Runtime/Scripts/Console/Commands/ClearCommand.cs` | 内置 `clear` 命令。 |
| `Runtime/Scripts/Console/Commands/HelpCommand.cs` | 内置 `help` 命令。 |
| `Runtime/Scripts/Console/Commands/LogCommand.cs` | 内置 `log` 命令。 |
| `Runtime/Scripts/Console/Commands/ListenCommand.cs` | 内置 `listen` 命令（监听 Unity 日志并转发）。 |
| `Runtime/Scripts/Console/Commands/ExitCommand.cs` | 内置 `exit` 命令（退出控制台）。 |
| `Runtime/Scripts/Console/RuntimeConsole.cs` | 运行时控制台组件。 |
| `Runtime/Resources/Depot/Console/RuntimeConsole.uxml` | 控制台布局。 |
| `Runtime/Resources/Depot/Console/RuntimeConsole.uss` | 控制台样式。 |

> 组件优先加载 `Runtime/Resources/Depot/Console/RuntimeConsole.asset`（PanelSettings，参考分辨率 1200×800）作为面板设置；若该资源缺失，会回退到运行时创建的默认 `ScaleWithScreenSize` 设置（1920×1080）。若为 `UIDocument` 指定了自己的 `PanelSettings`，则优先使用已指定的设置。

## Android 原生插件

`Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib` 是 VoyageForge Android Core 原生插件，用来承载 Unity 调用 Android 原生能力的通用实现。当前包含后台保活、通知栏通知与 APK 安装模块，后续权限、通知、电池、系统设置等 Android 能力也应继续收敛到这里。

### 插件命名空间

- Gradle namespace：`com.voyageforge.android.core`
- KeepAlive Java package：`com.voyageforge.android.core.keepalive`
- Notification Java package：`com.voyageforge.android.core.notification`
- Installer Java package：`com.voyageforge.android.core.installer`

### 原生插件结构

| 路径 | 说明 |
| --- | --- |
| `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib/AndroidManifest.xml` | 声明权限、前台服务、重启广播接收器和 FileProvider。 |
| `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib/build.gradle` | Android Library 编译配置，AndroidX Core 与 WorkManager 依赖统一从 Google Maven 解析。 |
| `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib/src/main/java/com/voyageforge/android/core/keepalive/CrucibleKeepAliveService.java` | 前台保活服务实现。 |
| `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib/src/main/java/com/voyageforge/android/core/keepalive/KeepAliveRestartReceiver.java` | 开机、闹钟和恢复广播接收器。 |
| `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib/src/main/java/com/voyageforge/android/core/task/VoyageForgeScheduledTaskService.java` | Android Service 内通用定时任务调度基类，供保活、通知和后续 Android 模块复用。 |
| `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib/src/main/java/com/voyageforge/android/core/diagnostics/VoyageForgeAndroidLogger.java` | Android 原生文件日志工具，用于诊断后台、广播、Service 和通知链路。 |
| `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib/src/main/java/com/voyageforge/android/core/notification/AndroidNotificationNotifier.java` | Android 下拉通知栏即时通知与定时通知调度实现。 |
| `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib/src/main/java/com/voyageforge/android/core/notification/ScheduledNotificationReceiver.java` | 定时通知广播接收器，用于 AlarmManager 触发和开机后恢复计划。 |
| `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib/src/main/java/com/voyageforge/android/core/notification/VoyageForgeScheduledNotificationWorker.java` | WorkManager 定时通知兜底任务，用于测试和系统允许时的补偿恢复。 |
| `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib/src/main/java/com/voyageforge/android/core/installer/AndroidPackageInstaller.java` | APK 安装器实现。 |
| `Runtime/Plugins/Android/VoyageForgeAndroidCore.androidlib/res/xml/voyageforge_android_core_file_paths.xml` | FileProvider 可共享文件路径配置。 |

### Unity C# 封装

Unity 业务层应通过 `Runtime/Scripts/Android` 下的 C# 封装调用 Android 原生能力：

| 能力 | C# 封装 | 原生入口 |
| --- | --- | --- |
| 后台保活 | `VoyageForge.Depot.Runtime.Android.AndroidKeepAliveService` | `com.voyageforge.android.core.keepalive.CrucibleKeepAliveService` |
| 通知栏通知 | `VoyageForge.Depot.Runtime.Android.AndroidNotificationNotifier` | `com.voyageforge.android.core.notification.AndroidNotificationNotifier` |
| 原生日志诊断 | `VoyageForge.Depot.Runtime.Android.AndroidDiagnosticsLogger` | `com.voyageforge.android.core.diagnostics.VoyageForgeAndroidLogger` |
| APK 安装 | `VoyageForge.Depot.Runtime.Android.AndroidPackageInstaller` | `com.voyageforge.android.core.installer.AndroidPackageInstaller` |

#### 保活函数

| 函数或属性 | 说明 |
| --- | --- |
| `AndroidKeepAliveService.SdkInt` | 当前 Android API 等级。 |
| `AndroidKeepAliveService.PackageName` | 当前应用包名。 |
| `AndroidKeepAliveService.RequestNotificationPermission()` | 请求 Android 13 及以上的通知权限。 |
| `AndroidKeepAliveService.StartService()` | 启动 Android 前台保活服务。 |
| `AndroidKeepAliveService.StopService()` | 停止 Android 前台保活服务。 |
| `AndroidKeepAliveService.SetKeepAliveSwitchEnabled(bool isEnabled)` | 把用户期望的保活服务开关状态保存到 Android `SharedPreferences`。 |
| `AndroidKeepAliveService.IsKeepAliveSwitchEnabled()` | 读取用户上次保存的保活服务开关状态。 |
| `AndroidKeepAliveService.EnsureServiceFromSavedState()` | 如果保存的开关状态为开启，则在应用启动时恢复前台保活服务。 |
| `AndroidKeepAliveService.IsServiceRunning()` | 查询前台保活服务是否记录为运行中。 |
| `AndroidKeepAliveService.GetServiceStartUnixMillis()` | 获取服务启动时间戳，单位为 Unix 毫秒。 |
| `AndroidKeepAliveService.GetLastHeartbeatUnixMillis()` | 获取服务最近心跳时间戳，单位为 Unix 毫秒。 |
| `AndroidKeepAliveService.IsIgnoringBatteryOptimizations()` | 查询是否已经加入电池优化白名单。 |
| `AndroidKeepAliveService.RequestIgnoreBatteryOptimizations()` | 请求加入电池优化白名单。 |
| `AndroidKeepAliveService.RequestAutoStartPermission()` | 请求打开厂商自启动权限设置页；Android 不允许代码静默授予，需要用户手动开启。 |
| `AndroidKeepAliveService.OpenBatteryOptimizationSettings()` | 打开系统电池优化设置页，引导用户手动处理厂商限制。 |
| `AndroidKeepAliveService.OpenBackgroundRunSettings()` | 优先打开小米/红米自启动或后台省电策略页，失败时回退到应用详情/电池优化设置。 |

#### 通知函数

| 函数 | 说明 |
| --- | --- |
| `AndroidNotificationNotifier.CanPostNotifications()` | 查询当前应用是否可以发送 Android 通知栏通知。 |
| `AndroidNotificationNotifier.RequestPostNotificationsPermission(callbacks)` | 请求 Android 13 及以上的通知权限，并可在授权后通过回调继续发送通知。 |
| `AndroidNotificationNotifier.CanScheduleExactAlarms()` | 查询 Android 12 及以上是否允许当前应用安排精确闹钟。 |
| `AndroidNotificationNotifier.RequestScheduleExactAlarmPermission()` | 打开系统精确闹钟授权页，提升应用被划掉后的定时触发概率。 |
| `AndroidNotificationNotifier.ResetNotificationChannels()` | 重置插件管理的通知渠道，让有声和无声通知按当前代码策略重新创建。 |
| `AndroidNotificationNotifier.ShowNotification(int notificationId, string title, string content)` | 发送一条默认有声 Android 通知。 |
| `AndroidNotificationNotifier.ShowNotification(int notificationId, string title, string content, AndroidNotificationSoundMode soundMode)` | 按有声或无声模式发送一条 Android 通知。 |
| `AndroidNotificationNotifier.ShowAudibleNotification(int notificationId, string title, string content)` | 发送一条有声 Android 通知。 |
| `AndroidNotificationNotifier.ShowSilentNotification(int notificationId, string title, string content)` | 发送一条无声 Android 通知。 |
| `AndroidNotificationNotifier.StartScheduledNotification(int notificationId, string title, string content, TimeSpan interval, AndroidNotificationSoundMode soundMode)` | 开启周期定时通知，间隔时间由调用方自选。 |
| `AndroidNotificationNotifier.CancelScheduledNotification(int notificationId)` | 关闭指定 ID 的周期定时通知。 |
| `AndroidNotificationNotifier.IsScheduledNotificationEnabled()` | 查询当前是否开启了周期定时通知。 |
| `AndroidNotificationNotifier.IsScheduledNotificationSoundEnabled()` | 查询当前周期定时通知是否保存为有声模式。 |
| `AndroidNotificationNotifier.EnsureScheduledNotification()` | 按已保存的定时通知配置重新安装下一次 Android 闹钟。 |
| `AndroidNotificationNotifier.GetScheduledNotificationIntervalMillis()` | 获取当前周期定时通知间隔毫秒数。 |
| `AndroidNotificationNotifier.GetScheduledNotificationNextTriggerUnixMillis()` | 获取当前周期定时通知下一次触发时间戳，单位为 Unix 毫秒。 |

#### 诊断日志函数

| 函数 | 说明 |
| --- | --- |
| `AndroidDiagnosticsLogger.GetLogFilePath()` | 获取 Android 原生文件日志路径。 |
| `AndroidDiagnosticsLogger.GetLogFolderPath()` | 获取 Android 原生文件日志目录路径，便于在 UI 中展示给用户手动查找。 |
| `AndroidDiagnosticsLogger.OpenLogFolder()` | 尝试打开系统文件管理器或文件夹选择器定位日志目录。 |
| `AndroidDiagnosticsLogger.LogUnityLifecycleEvent(string eventName)` | 把 Unity 界面打开、关闭、前后台切换等生命周期事件写入 Android 原生日志。 |
| `AndroidDiagnosticsLogger.ClearLogFiles()` | 清空当前日志和上一份轮转日志，便于重新复现后台通知问题。 |

#### APK 安装函数

| 函数 | 说明 |
| --- | --- |
| `AndroidPackageInstaller.CanRequestPackageInstalls()` | 查询当前应用是否已经具备安装 APK 的能力。Android 8 及以上会检查“允许安装未知来源应用”权限。 |
| `AndroidPackageInstaller.InstallApk(string apkPath)` | 安装指定路径的 APK。若缺少未知来源安装权限，会先跳转到系统授权页，并在原生层暂存 APK 路径。 |
| `AndroidPackageInstaller.HasPendingApk()` | 查询是否存在因为授权流程而挂起的 APK 安装任务。 |
| `AndroidPackageInstaller.InstallPendingApk()` | 用户从未知来源安装授权页返回后，继续安装此前挂起的 APK。 |

#### 保活调用示例

```csharp
using UnityEngine;
using VoyageForge.Depot.Runtime.Android;

public sealed class KeepAliveExample : MonoBehaviour
{
    /// <summary>
    /// 组件启用时启动前台保活服务。
    /// </summary>
    private void OnEnable()
    {
        if (AndroidKeepAliveService.IsKeepAliveSwitchEnabled())
        {
            AndroidKeepAliveService.EnsureServiceFromSavedState();
            return;
        }

        AndroidKeepAliveService.SetKeepAliveSwitchEnabled(true);
        AndroidKeepAliveService.RequestNotificationPermission();
        AndroidKeepAliveService.StartService();
    }

    /// <summary>
    /// 每帧观察服务状态。
    /// </summary>
    private void Update()
    {
        var isRunning = AndroidKeepAliveService.IsServiceRunning();
        var isSwitchEnabled = AndroidKeepAliveService.IsKeepAliveSwitchEnabled();
        var startUnixMillis = AndroidKeepAliveService.GetServiceStartUnixMillis();
    }
}
```

#### APK 安装调用示例

```csharp
using UnityEngine;
using VoyageForge.Depot.Runtime.Android;

public sealed class ApkInstallExample : MonoBehaviour
{
    /// <summary>
    /// 安装指定 APK 文件。
    /// </summary>
    /// <param name="apkPath">APK 文件路径。</param>
    public void Install(string apkPath)
    {
        AndroidPackageInstaller.InstallApk(apkPath);
    }

    /// <summary>
    /// 应用重新获得焦点时继续处理挂起的安装任务。
    /// </summary>
    /// <param name="hasFocus">应用是否获得焦点。</param>
    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && AndroidPackageInstaller.HasPendingApk())
        {
            AndroidPackageInstaller.InstallPendingApk();
        }
    }
}
```

#### 通知调用示例

```csharp
using System;
using VoyageForge.Depot.Runtime.Android;

AndroidNotificationNotifier.RequestPostNotificationsPermission();
AndroidNotificationNotifier.ResetNotificationChannels();
AndroidNotificationNotifier.ShowAudibleNotification(
    1001,
    "Crucible 保活提示",
    "保活服务正在运行。");

AndroidNotificationNotifier.ShowSilentNotification(
    1002,
    "Crucible 静音提示",
    "这条通知不会主动播放提示音。");

AndroidNotificationNotifier.StartScheduledNotification(
    2001,
    "Crucible 定时通知",
    "这条通知会按自选时间间隔重复出现。",
    TimeSpan.FromMinutes(5),
    AndroidNotificationSoundMode.Audible);

AndroidNotificationNotifier.CancelScheduledNotification(2001);
```

#### 示例目录

| 路径 | 说明 |
| --- | --- |
| `Samples~/Android/AndroidKeepAliveSample/AndroidKeepAliveSample.cs` | Android 保活示例脚本，包含启动服务、停止服务、保存/读取开关状态、按保存状态恢复服务、请求自启动权限、请求电池优化白名单、打开系统设置、查询存活时长和打印状态。 |
| `Samples~/Android/AndroidNotificationSample/AndroidNotificationSample.cs` | Android 通知示例脚本，包含有声通知、无声通知、开启定时通知和关闭定时通知。 |

#### C# 封装注意事项

- 这些封装只在 Android 真机或 Android 构建中执行原生逻辑。
- 在 Unity Editor 中，保活查询会返回便于 UI 预览的模拟值，APK 安装会返回 `false`。
- Android 13 及以上需要通知权限，否则前台服务通知可能无法显示。
- Android 13 及以上普通通知同样需要通知权限，否则 `ShowNotification` 会返回 `false`。
- Android 8 及以上通知声音由系统通知渠道控制；插件当前使用有声渠道 `voyageforge_android_core_audible_v6` 和无声渠道 `voyageforge_android_core_silent_v2`。
- 有声渠道会优先使用插件 `res/raw/ding.wav`，资源不存在时才回退到系统默认通知音。
- 有声通知会主动调用 `Vibrator` 执行震动，避免部分设备只播放声音、不执行通知渠道震动。
- 在小米、红米和 POCO 设备上，有声通知会额外使用 `MediaPlayer` 直接播放 `res/raw/ding.wav`，用于绕开 MIUI/HyperOS 对普通通知渠道声音的额外静音策略。
- 插件会自动删除旧版通知渠道，并提供 `ResetNotificationChannels()` 让代码主动重建当前渠道，避免用户手动到系统通知列表里逐个调整旧渠道。
- 如果用户关闭了整个 App 的通知、系统勿扰模式生效、通知音量为 0，或厂商系统策略把 App 静音，普通应用代码不能绕过这些系统级限制。
- 定时通知的配置保存在 `SharedPreferences`；开启定时通知时会同步启动前台保活服务。`AlarmManager` 只负责系统唤醒，到点广播会优先拉起 `CrucibleKeepAliveService`，由 `VoyageForgeScheduledTaskService` 统一调度服务内的心跳任务和定时通知任务；只有系统拒绝后台启动服务时，广播接收器才会直接发通知兜底。
- 开启定时通知后会额外提交一次 `WorkManager` 一次性兜底任务。它会在系统允许时尝试拉起保活服务、补发到期通知并恢复下一次 `AlarmManager` 闹钟；这里没有使用 `PeriodicWorkRequest`，因为官方周期任务最小间隔是 15 分钟，测试阶段 1 分钟场景会改用一次性任务自我续约。当前依赖版本使用 `androidx.work:work-runtime:2.8.1`，用于兼容 Unity 2022 自带的 Android Gradle Plugin 7.4.2 和 JDK 11，避免新版 WorkManager 的 lint 检查 jar 触发 `UnsupportedClassVersionError`。
- 插件会排除 `kotlin-stdlib-jdk7` 和 `kotlin-stdlib-jdk8`，并统一强制 `kotlin-stdlib:1.8.22`，避免 Unity/AGP 7.4 的 `checkReleaseDuplicateClasses` 报 Kotlin 标准库重复类。
- 插件会强制 `androidx.concurrent:concurrent-futures:1.1.0`，避免 Unity 2022 自带 D8 在处理较新的 `concurrent-futures:1.2.0` 时出现 dexing 异常。
- 插件保留 `androidx.annotation:annotation-experimental:1.4.1`，这是当前环境已经解析成功的版本；Unity 2022 的 lint 可能会打印 Java 17 lint jar 警告，但在当前 `work-runtime:2.8.1` 组合下不阻塞 Release 构建。
- 本地 `core-1.12.0.aar` 已移动到 `Runtime/Plugins/Android/_Disabled` 并改为 `.off` 后缀，避免 Unity 自动导入本地 AndroidX Core 与 WorkManager 的 Maven 依赖发生重复类冲突。
- `VoyageForgeScheduledTaskService` 没有手动创建 `Thread` 或 `Timer`；它使用主线程 `Looper` 上的 `Handler.post()` 和 `Handler.postDelayed()` 实现循环。每个任务执行后返回下一次延迟毫秒数，服务存活时会自动续约，服务进程被系统杀掉后循环也会停止。
- Android 原生层会把保活、广播、Service 启动、AlarmManager 安排和通知发送结果写入 App 专属日志文件，默认路径类似 `/sdcard/Android/data/<应用包名>/files/voyageforge_logs/voyageforge_android_core.log`。如果划掉应用后没有任何新日志，通常表示厂商系统直接杀掉进程或拦截了广播/后台启动。
- 日志首行会记录日志文件创建时间和路径；Unity UI 会额外写入界面打开、关闭、进入后台和回到前台的时间，方便和手机操作时间对齐排查。
- 定时通知会在 Android 12 及以上先检查 `canScheduleExactAlarms()`；允许精确闹钟时优先使用用户可见的 `setAlarmClock`，否则降级为 `setAndAllowWhileIdle`。
- 前台服务在 Manifest 中显式声明 `android:stopWithTask="false"`，并运行在 `:voyageforge_keep_alive` 独立进程，避免标准 Android 系统在最近任务划掉 UI 进程时直接随任务停止服务。
- 如果红米/小米/POCO 在最近任务划掉应用时同时杀掉前台服务，普通应用代码无法继续执行定时通知和兜底声音，需要在系统中开启自启动、后台运行无限制、通知声音和震动权限。
- 自启动权限没有 Android 标准授权弹窗，插件的 `RequestAutoStartPermission()` 会优先打开小米/红米自启动管理页；如果厂商页面不可用，会回退到当前应用详情页，由用户手动开启相关权限。
- Android 6 及以上可请求电池优化白名单，但厂商后台限制通常还需要用户手动设置。
- 保活服务的“开关期望状态”保存在 Android `SharedPreferences` 中，应用进程被划掉不会清空；用户手动关闭开关或清除应用数据才会改掉它。
- Android 8 及以上安装 APK 需要用户允许当前应用“安装未知应用”，无法静默绕过。

### Android 权限

| 权限 | 用途 |
| --- | --- |
| `android.permission.FOREGROUND_SERVICE` | 启动前台服务。 |
| `android.permission.FOREGROUND_SERVICE_SPECIAL_USE` | Android 14 及以上 specialUse 前台服务类型。 |
| `android.permission.POST_NOTIFICATIONS` | Android 13 及以上显示通知。 |
| `android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` | 请求加入电池优化白名单。 |
| `android.permission.REQUEST_INSTALL_PACKAGES` | Android 8 及以上请求安装 APK。 |
| `android.permission.WAKE_LOCK` | 维持局部 CPU 唤醒锁。 |
| `android.permission.RECEIVE_BOOT_COMPLETED` | 开机后尝试恢复服务。 |

### 打包注意事项

- 修改 Java、Manifest 或 Gradle 后需要重新构建 Android 包。
- `compileSdkVersion` 当前为 `35`。
- `minSdkVersion` 当前为 `22`。
- `targetSdkVersion` 当前为 `35`。
- `androidx.core:core` 由 `Runtime/Plugins/Android/core-1.12.0.aar` 提供，用于 `FileProvider`。
- 旧的独立安装器 AAR 已禁用，安装能力已经源码化到 VoyageForge Android Core 插件中。
- 建议至少开启 `ARM64`；`ARMv7` 仅用于兼容老设备。

### 保活边界

插件使用 Foreground Service、常驻通知、`PARTIAL_WAKE_LOCK`、`AlarmManager.setAndAllowWhileIdle`、`BroadcastReceiver`、`BOOT_COMPLETED` 和电池优化白名单请求来提高后台存活概率。

这些能力不能保证对抗系统或用户触发的强制停止。若厂商系统执行 `force-stop` 或最近任务强清，普通第三方应用无法通过公开 API 立即自恢复。

## 已知问题

### Gradle 构建警告

#### flatDir 仓库与集中管理策略冲突

`VoyageForgeAndroidCore.androidlib/build.gradle` 中使用了 `flatDir` 仓库。AGP 7.x 起推荐通过 `dependencyResolutionManagement` 集中管理仓库，模块级 `build.gradle` 中单独声明仓库会在构建时产生警告：

```
Build was configured to prefer settings repositories over project repositories
but repository 'flatDir' was added by build file
'unityLibrary\VoyageForgeAndroidCore.androidlib\build.gradle'
```

当前 AGP 7.4.2 下仅为警告，不影响构建通过。**AGP 8.x 上可能升级为错误**，后续需要把 `flatDir` 仓库配置提升到项目级 `settings.gradle` 或移除对本地 AAR 的直接引用。

#### Java 过时 API 编译警告

`VoyageForgeAndroidCore.androidlib` 内的部分 Java 代码引用了已过时的 Android API（如 `UnityPlayerActivity.java`），`javac` 在编译阶段会输出 `deprecation` 警告。当前不影响功能，后续清理即可。

## 命名说明
- `Depot` 代表仓库、补给站。
- 在整个 VoyageForge 体系中，Depot 更适合作为共用工具与底层能力的集中存放点。
- 新增工具时，优先判断它是否属于跨模块复用的公共能力；如果是，应优先沉淀到 Depot。
