using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoyageForge.Depot.Runtime.Console
{
    /// <summary>
    /// 控制台日志列表视图：负责日志缓冲、级别过滤、计数与列表渲染。
    /// 从 <see cref="RuntimeConsole"/> 拆分出来，保持单一职责。
    /// </summary>
    public sealed class ConsoleLogList
    {
        private readonly ScrollView _list;
        private readonly int _maxEntries;

        // 日志缓冲与过滤状态
        private readonly List<ConsoleLogEntry> _entries = new List<ConsoleLogEntry>();
        private readonly Dictionary<ConsoleFilter, VisualElement> _filterButtons = new Dictionary<ConsoleFilter, VisualElement>();
        private readonly Dictionary<ConsoleFilter, Label> _filterCounts = new Dictionary<ConsoleFilter, Label>();
        private ConsoleFilter _activeFilter = ConsoleFilter.All;

        // 各级别日志计数，用于过滤标签上的数量角标
        private int _countLog;
        private int _countWarning;
        private int _countError;

        // 命令执行期间产生的日志强制滚动到底部
        private bool _forceScrollToBottom;

        /// <summary>
        /// 创建日志列表视图。
        /// </summary>
        /// <param name="list">日志列表 ScrollView（来自 UXML）。</param>
        /// <param name="maxEntries">日志缓冲上限。</param>
        public ConsoleLogList(ScrollView list, int maxEntries)
        {
            _list = list;
            _maxEntries = maxEntries;

            // 提高滚轮滚动速度（默认每次滚动太少，滚几圈才动一点）
            _list.mouseWheelScrollSize = 120f;

            // 让垂直滚动条变细：递归限制 scroller 内部所有元素宽度为 10px
            ConstrainScrollbarWidth(_list.verticalScroller, 10f);
        }

        /// <summary>递归限制滚动条内部所有元素的宽度（USS 无法覆盖 slider 内部默认 24px，改用代码设置）。</summary>
        private static void ConstrainScrollbarWidth(VisualElement element, float width)
        {
            if (element == null)
            {
                return;
            }

            element.style.width = width;
            element.style.maxWidth = width;

            foreach (VisualElement child in element.Children())
            {
                ConstrainScrollbarWidth(child, width);
            }
        }

        /// <summary>命令执行期间产生的日志是否强制滚动到底部。</summary>
        public bool ForceScrollToBottom
        {
            get => _forceScrollToBottom;
            set => _forceScrollToBottom = value;
        }

        /// <summary>写入一条日志到缓冲并更新计数（不重建列表）。</summary>
        /// <param name="entry">日志条目。</param>
        public void AddLog(ConsoleLogEntry entry)
        {
            _entries.Add(entry);

            // 超出缓冲上限时丢弃最旧的一条，并同步扣减对应计数
            if (_entries.Count > _maxEntries)
            {
                DecrementCount(_entries[0].Type);
                _entries.RemoveAt(0);
            }

            IncrementCount(entry.Type);
            UpdateFilterCounts();
        }

        /// <summary>按当前过滤条件重建日志列表。</summary>
        /// <param name="scrollToBottom">重建后是否滚动到底部（默认滚动）。</param>
        public void RebuildList(bool scrollToBottom = true)
        {
            if (_list == null)
            {
                return;
            }

            _list.Clear();

            // 按缓冲顺序（旧 -> 新）渲染，符合控制台习惯
            foreach (ConsoleLogEntry entry in _entries)
            {
                if (!MatchesFilter(entry.Type))
                {
                    continue;
                }

                _list.Add(BuildEntryRow(entry));
            }

            if (scrollToBottom)
            {
                ScrollToBottom();
            }
        }

        /// <summary>清空日志缓冲与计数。</summary>
        public void Clear()
        {
            _entries.Clear();
            _countLog = 0;
            _countWarning = 0;
            _countError = 0;
            UpdateFilterCounts();
        }

        /// <summary>判断日志列表当前是否滚动到底部（允许少量误差）。</summary>
        /// <returns>是否在底部。</returns>
        public bool IsAtBottom()
        {
            if (_list == null)
            {
                return false;
            }

            Scroller scroller = _list.verticalScroller;
            if (scroller == null)
            {
                return true;
            }

            return scroller.value >= scroller.highValue - 2f;
        }

        /// <summary>绑定一个过滤标签：缓存元素与数量角标，并注册点击事件。</summary>
        /// <param name="tree">搜索根节点。</param>
        /// <param name="name">过滤标签在 UXML 中的名称。</param>
        /// <param name="filter">对应的过滤类型。</param>
        public void BindFilterButton(VisualElement tree, string name, ConsoleFilter filter)
        {
            VisualElement button = tree.Q<VisualElement>(name);
            if (button == null)
            {
                return;
            }

            _filterButtons[filter] = button;

            // 找到标签内的数量角标 Label（按 class 查找）
            Label count = button.Q<Label>(className: "console-filter-count");
            if (count != null)
            {
                _filterCounts[filter] = count;
            }

            // 点击后切换激活过滤类型并刷新列表
            button.RegisterCallback<ClickEvent>(_ =>
            {
                _activeFilter = filter;
                ApplyFilterVisual();
                RebuildList();
            });
        }

        /// <summary>刷新过滤标签的“激活”高亮样式。</summary>
        public void ApplyFilterVisual()
        {
            foreach (KeyValuePair<ConsoleFilter, VisualElement> pair in _filterButtons)
            {
                pair.Value.EnableInClassList("console-filter-button-active", pair.Key == _activeFilter);
            }
        }

        /// <summary>把各级别计数写回过滤标签上的数量角标。</summary>
        public void UpdateFilterCounts()
        {
            if (_filterCounts.TryGetValue(ConsoleFilter.All, out Label all))
            {
                all.text = (_countLog + _countWarning + _countError).ToString();
            }

            if (_filterCounts.TryGetValue(ConsoleFilter.Log, out Label log))
            {
                log.text = _countLog.ToString();
            }

            if (_filterCounts.TryGetValue(ConsoleFilter.Warning, out Label warning))
            {
                warning.text = _countWarning.ToString();
            }

            if (_filterCounts.TryGetValue(ConsoleFilter.Error, out Label error))
            {
                error.text = _countError.ToString();
            }
        }

        // ---------------------------------------------------------------
        // 私有辅助
        // ---------------------------------------------------------------

        /// <summary>根据日志条目构建一行 UI：圆点 + 箭头 + 时间戳 + 消息，有堆栈时附可展开的堆栈块。</summary>
        private VisualElement BuildEntryRow(ConsoleLogEntry entry)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("console-entry");
            row.AddToClassList(GetEntryClass(entry.Type));

            bool hasStack = !string.IsNullOrEmpty(entry.StackTrace);

            VisualElement line = new VisualElement();
            line.AddToClassList("console-entry-line");

            VisualElement dot = new VisualElement();
            dot.AddToClassList("console-entry-dot");
            line.Add(dot);

            Label caret = new Label(hasStack ? "▸" : string.Empty);
            caret.AddToClassList("console-entry-caret");
            line.Add(caret);

            Label timestamp = new Label(entry.Timestamp);
            timestamp.AddToClassList("console-entry-ts");
            line.Add(timestamp);

            Label message = new Label(entry.Message);
            message.AddToClassList("console-entry-msg");
            line.Add(message);

            row.Add(line);

            if (hasStack)
            {
                Label trace = new Label(entry.StackTrace);
                trace.AddToClassList("console-entry-stack");
                trace.style.display = DisplayStyle.None;
                row.Add(trace);

                line.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();

                    bool wasAtBottom = IsAtBottom();
                    bool expanded = trace.style.display != DisplayStyle.None;
                    trace.style.display = expanded ? DisplayStyle.None : DisplayStyle.Flex;
                    caret.text = expanded ? "▸" : "▾";

                    if (wasAtBottom)
                    {
                        ScrollToBottom();
                    }
                });
            }

            return row;
        }

        /// <summary>判断某条日志是否匹配当前激活的过滤类型。</summary>
        private bool MatchesFilter(LogType type)
        {
            switch (_activeFilter)
            {
                case ConsoleFilter.Log:
                    return type == LogType.Log;
                case ConsoleFilter.Warning:
                    return type == LogType.Warning;
                case ConsoleFilter.Error:
                    return type == LogType.Error || type == LogType.Assert || type == LogType.Exception;
                default:
                    return true;
            }
        }

        /// <summary>根据日志类型返回对应的 USS 样式类名。</summary>
        private static string GetEntryClass(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return "console-entry-warning";
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return "console-entry-error";
                default:
                    return "console-entry-log";
            }
        }

        /// <summary>把日志列表滚动到底部（延迟到布局完成后执行）。</summary>
        private void ScrollToBottom()
        {
            if (_list == null)
            {
                return;
            }

            _list.schedule.Execute(() =>
            {
                ScrollToBottomNow();
                _list.schedule.Execute(ScrollToBottomNow);
            });
        }

        /// <summary>立即把日志列表滚动到底部（供延迟调度调用）。</summary>
        private void ScrollToBottomNow()
        {
            Scroller scroller = _list.verticalScroller;
            if (scroller != null)
            {
                scroller.value = scroller.highValue;
            }
        }

        /// <summary>根据日志类型累加对应级别的计数。</summary>
        private void IncrementCount(LogType type)
        {
            if (type == LogType.Log)
            {
                _countLog++;
            }
            else if (type == LogType.Warning)
            {
                _countWarning++;
            }
            else if (IsErrorType(type))
            {
                _countError++;
            }
        }

        /// <summary>根据日志类型扣减对应级别的计数（用于缓冲溢出丢弃旧日志时）。</summary>
        private void DecrementCount(LogType type)
        {
            if (type == LogType.Log)
            {
                _countLog--;
            }
            else if (type == LogType.Warning)
            {
                _countWarning--;
            }
            else if (IsErrorType(type))
            {
                _countError--;
            }
        }

        /// <summary>判断是否为“错误类”日志（Error / Assert / Exception 都归入 Error）。</summary>
        private static bool IsErrorType(LogType type)
        {
            return type == LogType.Error || type == LogType.Assert || type == LogType.Exception;
        }
    }
}
