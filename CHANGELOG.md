# Changelog

本文件由开发者手动维护，发布流程不会自动生成或修改此文件。

## v0.0.22

### Fixed
- 修复控制台日志列表滚动条样式：滚动条变细（10px）、只保留垂直滚动条（隐藏水平滚动条与两端箭头按钮）、优化鼠标滚轮滚动速度。

## v0.0.21

### Added
- 控制台命令参数补全：`ConsoleCommand` 新增可选 `GetArgumentCompletions(args, currentInput)`，命令名完整输入后可为参数提供补全提示。
- 控制台命令历史：上下方向键浏览执行过的命令（下翻到底恢复当前输入）。
- 新增命令补全示例（`hello` 简单示例、`login` 复杂示例：第一个参数无提示、第二个参数有提示）。

### Changed
- 日志列表滚动优化：新日志仅在“本来就在底部”或“命令执行期间”才自动滚动到底部，向上翻看旧日志时保持位置不动。
- 重构 `RuntimeConsole`：拆分出 `ConsoleLogList`（日志列表 / 过滤）与 `ConsoleCommandLine`（命令输入 / 补全 / 历史）两个独立类，降低单一脚本复杂度。

### Tests
- 新增参数解析与参数补全测试用例，命令注册表测试覆盖至 16 个。

## v0.0.20

### Changed
- `UxmlUtility` 支持跨程序集定位：按调用方所在的包 / Assets 搜索 UXML 文件，嵌入 Assets 或作为 UPM 包安装（`com.voyageforge.depot` / `com.voyageforge.bridge`）都能正确找到。

## v0.0.19

### Changed
- `UxmlUtility.FindUxmlPath` 新增兼容模式：允许传入相对路径或带扩展名的文件名，自动提取资源名进行匹配。

### Fixed
- 修复控制台隐藏后仍拦截后方 UI 输入的问题：隐藏时同时隐藏占满屏幕的面板容器。

## v0.0.18

### Fixed
- 修复控制台 `PanelSettings` 使用 Unity 默认主题样式表导致的样式错误：新增 `RuntimeConsole.tss` 自定义主题样式表，并将 `themeUss` 指向它。

## v0.0.17

### Added
- 新增基于 UI Toolkit（UXML + USS）的运行时控制台 `RuntimeConsole`：捕获 `Debug.Log/Warning/Error/Exception`，支持级别过滤与计数、堆栈展开、标题栏拖拽，连按 3 次 `Tab` 键唤醒/隐藏。
- 控制台命令系统：基于 `ConsoleCommand` 基类 + `[Preserve]` 特性 + 反射自动发现，内置 `help` / `clear` / `log` / `exit` 命令。
- 命令补全：输入命令名时自动弹出前缀匹配建议，支持上下方向键选择、`Tab` 填入、双击 `Tab` 全选。
- 命令后台扫描：命令在后台线程反射发现，不阻塞主线程，扫描期间显示 loading 并禁用输入。
- 新增命令系统 EditMode 测试用例。

### Changed
- `FileBrowser` 重构为 `LabelGroup`，并新增 `PathInputField`。
- `FileBrowser` 原型改为外部 CSS/JS 动态渲染。
- UXML 加载工具重构，并新增 `FileBrowser` 窗口。
- 发布流程改为手动维护 CHANGELOG。

## v0.0.16

- 函数静态化。

## v0.0.15

- 优化 Singleton。
- 优化 MonoSingleton。

## v0.0.14

- MonoSingleton 允许自定义名称。
- 新增 README 文档。

## v0.0.13

- 新增资源路径脚本生成器。

## v0.0.12

- 移动 alias 路径，实现 ForgeMetaDatabase。

## v0.0.11

- 修复 Unity GUI 报错问题。

## v0.0.10

- 等第一帧渲染完全结束后再自动安装 Harmony。

## v0.0.9

- 新增泛型事件中心。

## v0.0.8

- 新增项目浏览器资源别名系统（ProjectBrowserAlias）。
- 新增 GitHub Actions 自动化发布流程。
- 通过分支清理优化发布流程。

## v0.0.6

- 添加 CHANGELOG.md 的 meta 文件。

## v0.0.5

- 新增版本回滚保护（version-guard）CI。

## v0.0.4

- Singleton / MonoSingleton 新增 `IsInitialized`、`HasInstance`、`IsDestroying` 生命周期标志。
- 移除已禁用的 AndroidX Core AAR 文件。

## v0.0.3

- 新增 Android 诊断日志、保活、通知支持。
- 新增 Singleton 泛型类。
- 新增编辑器文件选择工具窗口（UXML/USS）。
- 新增 Newtonsoft.Json 的 Vector3Converter。
- 新增 GIF 转序列帧工具与 Built-in/URP UI Shader 模板。
- 重构 DepotSettingsProvider。
- 优化版本解析逻辑。
- 示例目录结构标准化。

## v0.0.2

- 完成 Depot 包命名、版本号、作者与发布流程的初始对齐。
