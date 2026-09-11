using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;
using VoyageForge.Depot.Runtime.Utilities;

namespace VoyageForge.Depot.Runtime.Console
{
    /// <summary>
    /// 基于 UI Toolkit（UXML + USS）实现的运行时控制台。
    ///
    /// 职责：
    /// 1. 捕获 <see cref="Application.logMessageReceived"/> 产生的日志；
    /// 2. 管理面板显隐、唤醒与拖拽；
    /// 3. 协调日志列表（<see cref="ConsoleLogList"/>）与命令输入（<see cref="ConsoleCommandLine"/>）两个子模块。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public class RuntimeConsole : MonoSingleton<RuntimeConsole>
    {
        // 资源路径：UXML / USS / PanelSettings 都放在 Resources/Depot/Console/ 目录下
        private const string UxmlResourcePath = "Depot/Console/RuntimeConsole";
        private const string UssResourcePath = "Depot/Console/RuntimeConsole";
        private const string PanelSettingsResourcePath = "Depot/Console/RuntimeConsole";

        // ---------------------------------------------------------------
        // 可配置字段（Inspector 中可见）
        // ---------------------------------------------------------------

        [Header("Log")]
        [SerializeField, Min(1)] private int _maxEntries = 300;   // 日志缓冲上限

        [Header("Panel")]
        [SerializeField] private bool _visibleOnInitialize;        // 初始化后是否立即显示
        [SerializeField] private bool _draggable = true;           // 是否允许拖动标题栏

        [Header("Wake")]
        [SerializeField] private KeyCode _wakeKey = KeyCode.Tab;   // 唤醒按键
        [SerializeField, Min(2)] private int _wakeTapCount = 3;    // 需要连按的次数
        [SerializeField, Min(0.1f)] private float _wakeTapWindow = 0.5f; // 两次按键最大间隔（秒）

        [Header("Input")]
        [SerializeField] private bool _enableCommandInput = true;  // 是否启用命令输入框

        // ---------------------------------------------------------------
        // UI 元素引用
        // ---------------------------------------------------------------

        private UIDocument _document;
        private VisualElement _panel;
        private VisualElement _consoleRoot;
        private VisualElement _header;
        private ScrollView _list;
        private TextField _commandInput;
        private VisualElement _commandBar;
        private VisualElement _loadingOverlay;
        private VisualElement _loadingSpinner;
        private float _loadingAngle;
        private VisualElement _suggestionsContainer;

        // ---------------------------------------------------------------
        // 子模块
        // ---------------------------------------------------------------

        private ConsoleLogList _logList;         // 日志列表视图
        private ConsoleCommandLine _commandLine; // 命令输入行

        // ---------------------------------------------------------------
        // 自定义字体（文本设置）
        // ---------------------------------------------------------------

        private static PanelTextSettings _customTextSettings;  // 用户自定义的文本设置（如中文字体）

        // ---------------------------------------------------------------
        // 拖拽状态
        // ---------------------------------------------------------------

        private bool _dragging;
        private Vector2 _dragStartPointer;
        private Vector2 _dragStartTranslate;
        private Vector2 _dragTranslate;

        // ---------------------------------------------------------------
        // 唤醒状态
        // ---------------------------------------------------------------

        private int _wakeTaps;
        private float _lastWakeTapTime = float.NegativeInfinity;

        // ---------------------------------------------------------------
        // 命令扫描状态（后台线程扫描 + 主线程合并）
        // ---------------------------------------------------------------

        private readonly object _commandScanLock = new object();
        private bool _commandScanStarted;
        private bool _commandScanCompleted;
        private List<ConsoleCommand> _commandScanResult;
        private string _commandScanError;

        /// <summary>控制台当前是否可见（静态，无实例时返回 false）。</summary>
        public static bool IsVisible => HasInstance && Instance.IsShown;

        /// <summary>当前实例是否可见。</summary>
        public bool IsShown => _consoleRoot != null && _consoleRoot.style.display == DisplayStyle.Flex;

        /// <summary>新日志写入缓冲后触发，供外部监听日志流。</summary>
        public static event Action<ConsoleLogEntry> EntryLogged;

        // ---------------------------------------------------------------
        // 初始化
        // ---------------------------------------------------------------

        /// <summary>创建/获取控制台单例，首次调用会完成面板构建（默认隐藏）。</summary>
        public static RuntimeConsole Initialize()
        {
            return Instance;
        }

        /// <summary>
        /// 设置控制台使用的自定义文本设置（PanelTextSettings），用于加载自定义字体（如中文字体）。
        /// 需在 <see cref="Initialize"/> 之前调用，面板构建时会应用。
        /// 传入 null 则恢复使用默认文本设置。
        /// </summary>
        /// <param name="textSettings">PanelTextSettings 资源；null 恢复默认。</param>
        public static void SetTextSettings(PanelTextSettings textSettings)
        {
            _customTextSettings = textSettings;
        }

        // ---------------------------------------------------------------
        // 生命周期（继承自 MonoSingleton）
        // ---------------------------------------------------------------

        /// <summary>MonoSingleton 初始化回调：构建面板、启动后台扫描命令。</summary>
        protected override void OnInitialize()
        {
            BuildPanel();
            StartCommandScan();
            OnConsoleInitialized();
        }

        /// <summary>面板构建完成、命令后台扫描已启动后调用。派生类可重写。</summary>
        protected virtual void OnConsoleInitialized() { }

        /// <summary>控制台显隐状态发生变化时调用。派生类可重写。</summary>
        protected virtual void OnVisibilityChanged(bool visible) { }

        /// <summary>每收到一条日志时调用（在写入缓冲之后）。派生类可重写。</summary>
        protected virtual void OnLogReceived(ConsoleLogEntry entry) { }

        private void OnEnable()
        {
            Application.logMessageReceived += HandleLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void Update()
        {
            HandleWakeInput();
            PollCommandScan();
            UpdateLoadingSpinner();
        }

        // ---------------------------------------------------------------
        // 唤醒
        // ---------------------------------------------------------------

        /// <summary>检测“连按 N 次唤醒键”的输入，累计达标后切换面板显隐。</summary>
        private void HandleWakeInput()
        {
            // 命令输入框聚焦时，Tab 优先用于输入框交互（补全/全选），不作为唤醒键
            if (_commandLine != null && _commandLine.IsInputFocused())
            {
                return;
            }

            if (!Input.GetKeyDown(_wakeKey))
            {
                return;
            }

            float now = Time.unscaledTime;

            if (now - _lastWakeTapTime > _wakeTapWindow)
            {
                _wakeTaps = 0;
            }

            _lastWakeTapTime = now;
            _wakeTaps++;

            if (_wakeTaps >= _wakeTapCount)
            {
                _wakeTaps = 0;
                _lastWakeTapTime = float.NegativeInfinity;
                Toggle();
            }
        }

        // ---------------------------------------------------------------
        // 面板构建
        // ---------------------------------------------------------------

        /// <summary>构建整个面板：加载资源、查询元素、创建子模块并绑定事件。</summary>
        private void BuildPanel()
        {
            _document = GetComponent<UIDocument>();

            if (_document.panelSettings == null)
            {
                _document.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResourcePath);
                if (_document.panelSettings == null)
                {
                    Debug.LogWarning($"[RuntimeConsole] 未找到 PanelSettings 资源：{PanelSettingsResourcePath}，回退到运行时创建的默认设置。");
                    _document.panelSettings = CreatePanelSettings();
                }
            }

            _document.sortingOrder = 32767;

            // 应用用户自定义的文本设置（中文字体等）
            if (_customTextSettings != null)
            {
                _document.panelSettings.textSettings = _customTextSettings;
            }

            VisualTreeAsset uxml = Resources.Load<VisualTreeAsset>(UxmlResourcePath);
            if (uxml == null)
            {
                Debug.LogError($"[RuntimeConsole] 未找到 UXML 资源：{UxmlResourcePath}。请确认文件位于 Resources 目录下。");
                return;
            }

            StyleSheet uss = Resources.Load<StyleSheet>(UssResourcePath);
            if (uss == null)
            {
                Debug.LogWarning($"[RuntimeConsole] 未找到 USS 资源：{UssResourcePath}。");
            }

            _panel = uxml.CloneTree();

            _panel.style.flexGrow = 1;
            _panel.style.width = new Length(100, LengthUnit.Percent);
            _panel.style.height = new Length(100, LengthUnit.Percent);
            _panel.style.alignItems = Align.Center;
            _panel.style.justifyContent = Justify.Center;

            _document.rootVisualElement.Add(_panel);

            if (uss != null)
            {
                _panel.styleSheets.Add(uss);
            }

            // 查询 UXML 中的关键元素
            _consoleRoot = _panel.Q<VisualElement>("console-root");
            _header = _panel.Q<VisualElement>("console-header");
            _list = _panel.Q<ScrollView>("console-list");
            _commandInput = _panel.Q<TextField>("console-command-input");
            _commandBar = _panel.Q<VisualElement>("console-command-bar");

            // 创建 loading 覆盖层与补全建议容器
            BuildLoadingOverlay();
            BuildSuggestions();

            // 创建子模块：日志列表 + 命令输入行
            _logList = new ConsoleLogList(_list, _maxEntries);
            _commandLine = new ConsoleCommandLine(_commandInput, _suggestionsContainer);
            _commandLine.ForceScrollToBottom = force => _logList.ForceScrollToBottom = force;

            // 绑定过滤标签
            _logList.BindFilterButton(_panel, "filter-all", ConsoleFilter.All);
            _logList.BindFilterButton(_panel, "filter-log", ConsoleFilter.Log);
            _logList.BindFilterButton(_panel, "filter-warning", ConsoleFilter.Warning);
            _logList.BindFilterButton(_panel, "filter-error", ConsoleFilter.Error);

            // 绑定关闭按钮（走 exit 命令）
            _panel.Q<Button>("console-close-button")?.RegisterCallback<ClickEvent>(_ => ExecuteExitCommand());

            // 绑定命令输入框
            if (_commandInput != null)
            {
                if (_enableCommandInput)
                {
                    _commandInput.selectAllOnFocus = false;
                    _commandInput.selectAllOnMouseUp = false;
                    _commandInput.RegisterCallback<KeyDownEvent>(evt => _commandLine.OnKeyDown(evt), TrickleDown.TrickleDown);
                    _commandInput.RegisterValueChangedCallback(evt => _commandLine.OnValueChanged(evt.newValue));
                }
                else
                {
                    _commandInput.style.display = DisplayStyle.None;
                }
            }

            // 绑定拖拽事件
            if (_draggable && _header != null)
            {
                _header.RegisterCallback<PointerDownEvent>(OnHeaderPointerDown);
                _header.RegisterCallback<PointerMoveEvent>(OnHeaderPointerMove);
                _header.RegisterCallback<PointerUpEvent>(OnHeaderPointerUp);
                _header.RegisterCallback<PointerCaptureOutEvent>(_ => _dragging = false);
            }

            // 初始化界面状态
            _logList.ApplyFilterVisual();
            _logList.UpdateFilterCounts();
            SetVisible(_visibleOnInitialize);
            _logList.RebuildList();
        }

        /// <summary>运行时创建默认 PanelSettings（回退方案）。</summary>
        private static PanelSettings CreatePanelSettings()
        {
            PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Depot RuntimeConsole PanelSettings";
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.match = 0.5f;
            return settings;
        }

        // ---------------------------------------------------------------
        // 日志处理
        // ---------------------------------------------------------------

        /// <summary>Unity 日志回调：封装条目写入缓冲，并通知外部与派生类。</summary>
        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            ConsoleLogEntry entry = new ConsoleLogEntry(
                condition,
                stackTrace,
                type,
                DateTime.Now.ToString("HH:mm:ss.fff"));

            _logList.AddLog(entry);

            EntryLogged?.Invoke(entry);
            OnLogReceived(entry);

            // 面板可见时重建列表；仅“命令执行期间”或“本来就在底部”才滚动到底部
            if (IsShown)
            {
                _logList.RebuildList(_logList.ForceScrollToBottom || _logList.IsAtBottom());
            }
        }

        // ---------------------------------------------------------------
        // 可见性与折叠
        // ---------------------------------------------------------------

        /// <summary>显示控制台。</summary>
        public void Show()
        {
            SetVisible(true);
        }

        /// <summary>隐藏控制台。</summary>
        public void Hide()
        {
            SetVisible(false);
        }

        /// <summary>切换控制台显隐。</summary>
        public void Toggle()
        {
            SetVisible(!IsShown);
        }

        /// <summary>设置控制台显隐状态。</summary>
        public void SetVisible(bool visible)
        {
            if (_consoleRoot == null)
            {
                return;
            }

            bool wasShown = IsShown;

            // 同时隐藏占满屏幕的面板容器，避免透明容器拦截后方 UI 输入
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _panel.style.display = display;
            _consoleRoot.style.display = display;

            if (visible)
            {
                _logList.RebuildList();
                _commandLine.FocusInput();
            }

            if (wasShown != visible)
            {
                OnVisibilityChanged(visible);
            }
        }

        /// <summary>执行“退出”命令关闭控制台；命令未注册时直接隐藏。</summary>
        private void ExecuteExitCommand()
        {
            if (ConsoleCommandRegistry.TryGetCommand("exit", out ConsoleCommand command))
            {
                command.Execute(Array.Empty<string>());
            }
            else
            {
                Hide();
            }
        }

        /// <summary>静态：显示当前单例。</summary>
        public static void ShowInstance()
        {
            Instance?.Show();
        }

        /// <summary>静态：隐藏当前单例。</summary>
        public static void HideInstance()
        {
            Instance?.Hide();
        }

        /// <summary>静态：切换当前单例显隐。</summary>
        public static void ToggleInstance()
        {
            Instance?.Toggle();
        }

        /// <summary>清空日志缓冲与计数，并刷新界面。</summary>
        public void Clear()
        {
            _logList.Clear();
            _logList.RebuildList();
        }

        // ---------------------------------------------------------------
        // 拖拽
        // ---------------------------------------------------------------

        private void OnHeaderPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _consoleRoot == null)
            {
                return;
            }

            if (IsInsideButton(evt.target as VisualElement))
            {
                return;
            }

            _dragging = true;
            _dragStartPointer = evt.position;
            _dragStartTranslate = _dragTranslate;
            _header.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private static bool IsInsideButton(VisualElement element)
        {
            while (element != null)
            {
                if (element is Button)
                {
                    return true;
                }

                element = element.parent;
            }

            return false;
        }

        private void OnHeaderPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging || _consoleRoot == null || !_header.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            Vector2 delta = (Vector2)evt.position - _dragStartPointer;
            _dragTranslate = _dragStartTranslate + delta;
            _consoleRoot.style.translate = new Translate(_dragTranslate.x, _dragTranslate.y);
        }

        private void OnHeaderPointerUp(PointerUpEvent evt)
        {
            if (_dragging && _header != null && _header.HasPointerCapture(evt.pointerId))
            {
                _header.ReleasePointer(evt.pointerId);
            }

            _dragging = false;
        }

        // ---------------------------------------------------------------
        // 命令扫描（后台线程）
        // ---------------------------------------------------------------

        /// <summary>启动后台线程扫描命令，避免阻塞主线程。</summary>
        private void StartCommandScan()
        {
            if (_commandScanStarted)
            {
                return;
            }

            _commandScanStarted = true;
            SetCommandScanning(true);

            Thread worker = new Thread(ScanCommandsWorker)
            {
                IsBackground = true,
                Name = "DepotConsoleCommandScan"
            };
            worker.Start();
        }

        /// <summary>后台线程入口：反射扫描所有程序集，把结果写回共享字段。</summary>
        private void ScanCommandsWorker()
        {
            List<ConsoleCommand> found = null;
            string error = null;

            try
            {
                found = ConsoleCommandRegistry.DiscoverCommands(ConsoleCommandRegistry.GetScanAssemblies());
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            lock (_commandScanLock)
            {
                _commandScanResult = found;
                _commandScanError = error;
                _commandScanCompleted = true;
            }
        }

        /// <summary>每帧轮询：后台扫描完成后，在主线程合并结果并恢复 UI。</summary>
        private void PollCommandScan()
        {
            List<ConsoleCommand> found;
            string error;

            lock (_commandScanLock)
            {
                if (!_commandScanCompleted)
                {
                    return;
                }

                found = _commandScanResult;
                error = _commandScanError;
                _commandScanCompleted = false;
            }

            if (error != null)
            {
                Debug.LogError($"[RuntimeConsole] 命令扫描失败：{error}");
            }
            else if (found != null)
            {
                int registered = ConsoleCommandRegistry.RegisterAll(found);
                Debug.Log($"[RuntimeConsole] 命令扫描完成，共注册 {registered} 个命令。");
            }

            SetCommandScanning(false);
        }

        /// <summary>设置“命令扫描中”状态：切换输入可用性与 loading 覆盖层。</summary>
        private void SetCommandScanning(bool scanning)
        {
            _commandLine?.SetInputEnabled(!scanning);

            if (_loadingOverlay != null)
            {
                _loadingOverlay.style.display = scanning ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>每帧驱动 loading 转圈动画（仅扫描期间可见）。</summary>
        private void UpdateLoadingSpinner()
        {
            if (_loadingSpinner == null ||
                _loadingOverlay == null ||
                _loadingOverlay.style.display == DisplayStyle.None)
            {
                return;
            }

            _loadingAngle = (_loadingAngle + 180f * Time.unscaledDeltaTime) % 360f;
            _loadingSpinner.style.rotate = new Rotate(new Angle(_loadingAngle, AngleUnit.Degree));
        }

        /// <summary>创建命令扫描期间的 loading 覆盖层（初始隐藏）。</summary>
        private void BuildLoadingOverlay()
        {
            if (_consoleRoot == null)
            {
                return;
            }

            _loadingOverlay = new VisualElement();
            _loadingOverlay.AddToClassList("console-loading-overlay");

            _loadingSpinner = new VisualElement();
            _loadingSpinner.AddToClassList("console-loading-spinner");
            _loadingOverlay.Add(_loadingSpinner);

            Label text = new Label("正在扫描命令…");
            text.AddToClassList("console-loading-text");
            _loadingOverlay.Add(text);

            _loadingOverlay.style.display = DisplayStyle.None;
            _consoleRoot.Add(_loadingOverlay);
        }

        /// <summary>创建命令补全建议容器（初始隐藏，插在命令输入栏上方）。</summary>
        private void BuildSuggestions()
        {
            if (_commandBar == null || _commandBar.parent == null)
            {
                return;
            }

            _suggestionsContainer = new VisualElement();
            _suggestionsContainer.AddToClassList("console-suggestions");
            _suggestionsContainer.style.display = DisplayStyle.None;

            _commandBar.parent.Insert(_commandBar.parent.IndexOf(_commandBar), _suggestionsContainer);
        }
    }
}
