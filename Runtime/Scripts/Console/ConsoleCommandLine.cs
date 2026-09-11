using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoyageForge.Depot.Runtime.Console
{
    /// <summary>
    /// 控制台命令输入行：负责命令输入框的按键处理、命令补全、参数补全、命令历史与命令执行。
    /// 从 <see cref="RuntimeConsole"/> 拆分出来，保持单一职责。
    /// </summary>
    public sealed class ConsoleCommandLine
    {
        private readonly TextField _input;
        private readonly VisualElement _suggestionsContainer;

        // 补全建议状态
        private List<Label> _suggestionItems;
        private int _selectedSuggestionIndex = -1;
        private float _lastTabPressTime = float.NegativeInfinity;
        private string _suggestionFillPrefix = string.Empty;
        private bool _suggestionIsArgument;

        // 命令历史状态
        private readonly List<string> _commandHistory = new List<string>();
        private int _historyIndex = -1;
        private string _pendingInput = string.Empty;

        private const float TabDoubleTapWindow = 0.3f; // 双击 Tab 的判定时间窗口（秒）

        /// <summary>
        /// 创建命令输入行。
        /// </summary>
        /// <param name="input">命令输入框（来自 UXML）。</param>
        /// <param name="suggestionsContainer">命令补全建议容器。</param>
        public ConsoleCommandLine(TextField input, VisualElement suggestionsContainer)
        {
            _input = input;
            _suggestionsContainer = suggestionsContainer;
        }

        /// <summary>命令执行期间回调（用于设置日志列表强制滚动到底部）。</summary>
        public Action<bool> ForceScrollToBottom { get; set; }

        /// <summary>判断命令输入框当前是否持有焦点。</summary>
        public bool IsInputFocused()
        {
            return _input != null &&
                   _input.focusController != null &&
                   _input.focusController.focusedElement == _input;
        }

        /// <summary>延迟一帧让输入框获得焦点，并把光标移到文本末尾（供显示面板与补全后调用）。</summary>
        public void FocusInput()
        {
            if (_input == null)
            {
                return;
            }

            _input.schedule.Execute(() =>
            {
                if (_input == null || !_input.enabledSelf)
                {
                    return;
                }

                // 先失焦再聚焦，强制走一遍完整的焦点切换，重新初始化文本输入状态
                _input.Blur();
                _input.Focus();

                SetCursorToEnd();
            });
        }

        /// <summary>设置输入框可用状态（命令扫描期间禁用）。</summary>
        public void SetInputEnabled(bool enabled)
        {
            if (_input != null)
            {
                _input.SetEnabled(enabled);
            }
        }

        /// <summary>命令输入框按键处理：回车执行、Tab 补全/全选、上下方向键选择建议或历史。</summary>
        public void OnKeyDown(KeyDownEvent evt)
        {
            if (_input == null)
            {
                return;
            }

            // 上下方向键：有补全建议时导航建议，否则浏览命令历史
            if (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow)
            {
                if (IsSuggestionsVisible())
                {
                    NavigateSuggestion(evt.keyCode == KeyCode.DownArrow ? 1 : -1);
                }
                else
                {
                    NavigateHistory(evt.keyCode == KeyCode.UpArrow ? -1 : 1);
                }

                evt.StopPropagation();
                evt.PreventDefault();
                return;
            }

            // Tab：有建议时填入选中项，无建议时双击全选
            if (evt.keyCode == KeyCode.Tab)
            {
                HandleTabKey(evt);
                return;
            }

            // 回车：执行命令，并阻止 TextField 默认回车行为
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                ExecuteCommand();
                evt.StopPropagation();
                evt.PreventDefault();
            }
        }

        /// <summary>输入内容变化时刷新命令补全建议。</summary>
        public void OnValueChanged(string text)
        {
            UpdateSuggestions(text);
        }

        // ---------------------------------------------------------------
        // 命令执行
        // ---------------------------------------------------------------

        /// <summary>取出输入框内容并解析执行命令。</summary>
        private void ExecuteCommand()
        {
            string raw = _input.value;
            _input.value = string.Empty;

            // 回车后延迟一帧重新聚焦，抵消 uGUI EventSystem 对回车键的焦点干扰
            FocusInput();

            // 记录命令历史（空输入不记录），并重置历史浏览位置
            if (!string.IsNullOrWhiteSpace(raw))
            {
                _commandHistory.Add(raw);
                _historyIndex = -1;
                _pendingInput = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            // 按空格拆分：第一段为命令名，其余为参数
            string[] parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            string command = parts[0];
            string[] args = new string[parts.Length - 1];
            Array.Copy(parts, 1, args, 0, args.Length);

            if (ConsoleCommandRegistry.TryGetCommand(command, out ConsoleCommand commandInstance))
            {
                try
                {
                    // 命令执行期间产生的日志总是滚动到底部
                    ForceScrollToBottom?.Invoke(true);
                    commandInstance.Execute(args);
                    ForceScrollToBottom?.Invoke(false);
                }
                catch (Exception ex)
                {
                    ForceScrollToBottom?.Invoke(false);
                    Debug.LogError($"[Console] 命令 '{command}' 执行失败：{ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[Console] 未知命令：{command}");
            }
        }

        // ---------------------------------------------------------------
        // 命令历史
        // ---------------------------------------------------------------

        /// <summary>浏览命令历史（delta 为 -1 上翻更早命令、+1 下翻更新命令）。</summary>
        private void NavigateHistory(int delta)
        {
            if (_commandHistory.Count == 0)
            {
                return;
            }

            // 首次进入历史浏览时，保存当前输入行，便于下翻到底后恢复
            if (_historyIndex < 0)
            {
                _pendingInput = _input.value;
                _historyIndex = _commandHistory.Count;
            }

            int newIndex = _historyIndex + delta;

            // 下翻超过最新：恢复到进入浏览前保存的输入行
            if (newIndex >= _commandHistory.Count)
            {
                _historyIndex = _commandHistory.Count;
                _input.SetValueWithoutNotify(_pendingInput);
                SetCursorToEnd();
                return;
            }

            // 上翻超过最早：停在最早一条
            if (newIndex < 0)
            {
                newIndex = 0;
            }

            _historyIndex = newIndex;
            _input.SetValueWithoutNotify(_commandHistory[_historyIndex]);
            SetCursorToEnd();
        }

        // ---------------------------------------------------------------
        // 命令补全
        // ---------------------------------------------------------------

        /// <summary>根据输入内容刷新命令补全建议列表。</summary>
        private void UpdateSuggestions(string text)
        {
            if (_suggestionsContainer == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                HideSuggestions();
                return;
            }

            // 判断当前是命令名阶段还是参数阶段，并获取对应的补全建议
            _suggestionIsArgument = text.Contains(' ');
            _suggestionFillPrefix = _suggestionIsArgument ? GetArgumentPrefix(text) : string.Empty;

            IReadOnlyList<string> completions = _suggestionIsArgument
                ? GetArgumentCompletions(text)
                : ConsoleCommandRegistry.GetCompletions(text);

            if (completions.Count == 0)
            {
                HideSuggestions();
                return;
            }

            // 命令名阶段：唯一匹配且已完整输入时隐藏；参数阶段需保留提示，不适用此规则
            if (!_suggestionIsArgument)
            {
                bool uniqueAndComplete = completions.Count == 1 &&
                                         completions[0].Equals(text, StringComparison.OrdinalIgnoreCase);
                if (uniqueAndComplete)
                {
                    HideSuggestions();
                    return;
                }
            }

            _suggestionsContainer.Clear();
            _suggestionItems = new List<Label>();
            _selectedSuggestionIndex = 0;

            foreach (string completion in completions)
            {
                Label item = new Label(completion);
                item.AddToClassList("console-suggestion-item");

                item.RegisterCallback<ClickEvent>(_ => AcceptSuggestionValue(completion));

                _suggestionsContainer.Add(item);
                _suggestionItems.Add(item);
            }

            UpdateSuggestionHighlight();
            _suggestionsContainer.style.display = DisplayStyle.Flex;
        }

        /// <summary>补全建议当前是否可见。</summary>
        private bool IsSuggestionsVisible()
        {
            return _suggestionsContainer != null &&
                   _suggestionsContainer.style.display == DisplayStyle.Flex;
        }

        /// <summary>按方向移动补全建议的选中项（delta 为 +1 下移、-1 上移）。</summary>
        private void NavigateSuggestion(int delta)
        {
            if (_suggestionItems == null || _suggestionItems.Count == 0)
            {
                return;
            }

            _selectedSuggestionIndex =
                (_selectedSuggestionIndex + delta + _suggestionItems.Count) % _suggestionItems.Count;
            UpdateSuggestionHighlight();
        }

        /// <summary>刷新补全建议项的选中高亮。</summary>
        private void UpdateSuggestionHighlight()
        {
            for (int i = 0; i < _suggestionItems.Count; i++)
            {
                _suggestionItems[i].EnableInClassList("console-suggestion-item-selected", i == _selectedSuggestionIndex);
            }
        }

        /// <summary>处理 Tab 键：有补全建议时填入选中项，否则检测双击全选。</summary>
        private void HandleTabKey(KeyDownEvent evt)
        {
            if (IsSuggestionsVisible())
            {
                AcceptSuggestion();
                evt.StopPropagation();
                evt.PreventDefault();
                return;
            }

            // 无建议：检测“双击 Tab”全选
            float now = Time.unscaledTime;
            if (now - _lastTabPressTime <= TabDoubleTapWindow)
            {
                _input.SelectAll();
                _lastTabPressTime = float.NegativeInfinity;
            }
            else
            {
                _lastTabPressTime = now;
            }

            evt.StopPropagation();
            evt.PreventDefault();
        }

        /// <summary>把当前选中的建议项填入输入框（供 Tab 键使用）。</summary>
        private void AcceptSuggestion()
        {
            if (_suggestionItems == null || _suggestionItems.Count == 0)
            {
                return;
            }

            if (_selectedSuggestionIndex < 0 || _selectedSuggestionIndex >= _suggestionItems.Count)
            {
                _selectedSuggestionIndex = 0;
            }

            AcceptSuggestionValue(_suggestionItems[_selectedSuggestionIndex].text);
        }

        /// <summary>把补全建议值填入输入框（命令名补全填“命令名+空格”，参数补全填“前缀+参数+空格”）。</summary>
        private void AcceptSuggestionValue(string completion)
        {
            string fill = _suggestionIsArgument
                ? _suggestionFillPrefix + completion + " "
                : completion + " ";

            _input.SetValueWithoutNotify(fill);
            SetCursorToEnd();

            if (!IsInputFocused())
            {
                FocusInput();
            }

            // 填入后立即刷新建议：命令名补全后紧接着触发参数补全提示；
            // 参数补全后会根据“是否还有下一参数”自动继续提示或隐藏。
            UpdateSuggestions(fill);
        }

        /// <summary>解析参数补全建议：命令名已完整、正在输入参数时，调用命令的参数补全。</summary>
        private IReadOnlyList<string> GetArgumentCompletions(string text)
        {
            if (!ConsoleCommandRegistry.TryParseArgumentInput(text, out string commandName, out string[] args, out string currentInput))
            {
                return Array.Empty<string>();
            }

            if (!ConsoleCommandRegistry.TryGetCommand(commandName, out ConsoleCommand command))
            {
                return Array.Empty<string>();
            }

            return command.GetArgumentCompletions(args, currentInput);
        }

        /// <summary>计算参数补全时，填入建议项前需要保留的前缀（命令名 + 已完成参数 + 空格）。</summary>
        private string GetArgumentPrefix(string text)
        {
            if (text.EndsWith(" "))
            {
                return text;
            }

            int lastSpace = text.LastIndexOf(' ');
            if (lastSpace < 0)
            {
                return string.Empty;
            }

            return text.Substring(0, lastSpace + 1);
        }

        /// <summary>隐藏补全建议并清理选中状态。</summary>
        private void HideSuggestions()
        {
            if (_suggestionsContainer != null)
            {
                _suggestionsContainer.Clear();
                _suggestionsContainer.style.display = DisplayStyle.None;
            }

            _suggestionItems = null;
            _selectedSuggestionIndex = -1;
        }

        /// <summary>把命令输入框光标与选择起点移到文本末尾。</summary>
        private void SetCursorToEnd()
        {
            int length = _input.value.Length;
            _input.cursorIndex = length;
            _input.selectIndex = length;
        }
    }
}
