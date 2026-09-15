using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoyageForge.Depot.Runtime.Console
{
    /// <summary>
    /// 控制台日志列表视图：负责日志缓冲、级别过滤、计数与列表渲染。
    ///
    /// 性能说明：
    /// 使用虚拟化 <see cref="ListView"/>（makeItem / bindItem）替代全量 <see cref="ScrollView"/> 重建。
    /// ListView 只为“可见区域”创建行元素，滚动时回收复用；大量日志时不再 O(n) 全量
    /// 清空重建（旧实现每条日志都 Clear + 重加最多 300 行），行元素数量从缓冲上限降到一屏可见数量。
    /// </summary>
    public sealed class ConsoleLogList
    {
        private readonly ListView _list;          // 虚拟化日志列表
        private readonly ScrollView _scrollView;  // ListView 内部的 ScrollView（用于滚轮速度 / 滚动条定制）
        private readonly int _maxEntries;

        // 日志缓冲与过滤状态
        private readonly List<ConsoleLogEntry> _entries = new List<ConsoleLogEntry>();          // 完整缓冲（旧 -> 新）
        private readonly List<LogRowModel> _viewItems = new List<LogRowModel>();                // ListView 数据源（已过滤 + 展开状态）
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
        /// <param name="list">日志列表 ListView（来自 UXML）。</param>
        /// <param name="maxEntries">日志缓冲上限。</param>
        public ConsoleLogList(ListView list, int maxEntries)
        {
            _list = list;
            _maxEntries = maxEntries;

            // 2022.3 起 ListView 不再继承 ScrollView，内部组合了一个 ScrollView，需用 Q 查询拿到。
            _scrollView = list.Q<ScrollView>();

            // 虚拟化方式：动态行高。日志消息可自动换行、堆栈可展开，行高不固定，不能使用 FixedHeight。
            _list.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;

            // 日志行不需要 ListView 自带的选中态
            _list.selectionType = SelectionType.None;

            // 行模板与绑定：元素由 ListView 按需创建并复用
            _list.makeItem = MakeItem;
            _list.bindItem = BindItem;
            _list.itemsSource = _viewItems;

            if (_scrollView != null)
            {
                // 提高滚轮滚动速度（默认每次滚动太少，滚几圈才动一点）
                _scrollView.mouseWheelScrollSize = 120f;

                // 让垂直滚动条变细：递归限制 scroller 内部所有元素宽度为 10px
                ConstrainScrollbarWidth(_scrollView.verticalScroller, 10f);
            }
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

        /// <summary>
        /// 写入一条日志到缓冲与视图数据（不触发渲染，也不实例化任何 UI 元素）。
        /// 视图数据始终与“缓冲 + 当前过滤”保持同步，供后续 <see cref="RefreshView"/> 渲染。
        /// </summary>
        /// <param name="entry">日志条目。</param>
        public void AddLog(ConsoleLogEntry entry)
        {
            _entries.Add(entry);

            // 超出缓冲上限时丢弃最旧的一条，并同步扣减对应计数
            ConsoleLogEntry removed = default;
            bool removedAny = false;
            if (_entries.Count > _maxEntries)
            {
                removed = _entries[0];
                _entries.RemoveAt(0);
                removedAny = true;
                DecrementCount(removed.Type);
            }

            IncrementCount(entry.Type);

            // 同步视图数据（仅数据，不实例化元素）：
            // 被丢弃的最旧条目若在当前过滤中，则它是视图中的第一条（缓冲顺序），移除之；
            // 新条目若匹配当前过滤则追加到视图末尾。
            if (removedAny && MatchesFilter(removed.Type) && _viewItems.Count > 0)
            {
                _viewItems.RemoveAt(0);
            }

            if (MatchesFilter(entry.Type))
            {
                _viewItems.Add(new LogRowModel { Entry = entry });
            }

            UpdateFilterCounts();
        }

        /// <summary>
        /// 仅重新渲染可见行（视图数据已是最新、无需重新过滤时调用，例如新增一条日志）。
        /// ListView.Rebuild 只重建“可见”行元素，复杂度为 O(可见行数) 而非 O(缓冲总数)。
        /// </summary>
        /// <param name="scrollToBottom">渲染后是否滚动到底部。</param>
        public void RefreshView(bool scrollToBottom)
        {
            if (_list == null)
            {
                return;
            }

            _list.Rebuild();

            if (scrollToBottom)
            {
                ScrollToBottom();
            }
        }

        /// <summary>按当前过滤条件重新构建视图数据并渲染（过滤切换、显示面板、清空时调用）。</summary>
        /// <param name="scrollToBottom">渲染后是否滚动到底部（默认滚动）。</param>
        public void RebuildList(bool scrollToBottom = true)
        {
            if (_list == null)
            {
                return;
            }

            RebuildViewItems();
            RefreshView(scrollToBottom);
        }

        /// <summary>清空日志缓冲、视图数据与计数。</summary>
        public void Clear()
        {
            _entries.Clear();
            _viewItems.Clear();
            _countLog = 0;
            _countWarning = 0;
            _countError = 0;
            UpdateFilterCounts();
        }

        /// <summary>判断日志列表当前是否滚动到底部（允许少量误差）。</summary>
        /// <returns>是否在底部。</returns>
        public bool IsAtBottom()
        {
            if (_scrollView == null)
            {
                return false;
            }

            Scroller scroller = _scrollView.verticalScroller;
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

            // 点击后切换激活过滤类型并重建视图数据 + 渲染
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

        /// <summary>按当前过滤条件，从完整缓冲重建视图数据（元素展开状态会重置）。</summary>
        private void RebuildViewItems()
        {
            _viewItems.Clear();

            foreach (ConsoleLogEntry entry in _entries)
            {
                if (MatchesFilter(entry.Type))
                {
                    _viewItems.Add(new LogRowModel { Entry = entry });
                }
            }
        }

        /// <summary>创建一行日志的 UI 模板（圆点 + 箭头 + 时间戳 + 消息 + 可展开堆栈）。由 ListView 按需调用。</summary>
        private VisualElement MakeItem()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("console-entry");

            VisualElement line = new VisualElement();
            line.AddToClassList("console-entry-line");

            VisualElement dot = new VisualElement();
            dot.AddToClassList("console-entry-dot");
            line.Add(dot);

            Label caret = new Label();
            caret.AddToClassList("console-entry-caret");
            line.Add(caret);

            Label timestamp = new Label();
            timestamp.AddToClassList("console-entry-ts");
            line.Add(timestamp);

            Label message = new Label();
            message.AddToClassList("console-entry-msg");
            line.Add(message);

            Label stack = new Label();
            stack.AddToClassList("console-entry-stack");
            stack.style.display = DisplayStyle.None;

            row.Add(line);
            row.Add(stack);

            // 缓存子元素引用，避免 bindItem 里反复 Q 查询
            EntryRowRefs refs = new EntryRowRefs
            {
                Line = line,
                Caret = caret,
                Timestamp = timestamp,
                Message = message,
                Stack = stack
            };
            row.userData = refs;

            // 点击整行切换堆栈展开/折叠（展开状态存在行模型上，元素复用后仍能还原）
            line.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();

                if (refs.Model == null)
                {
                    return;
                }

                bool wasAtBottom = IsAtBottom();
                refs.Model.IsExpanded = !refs.Model.IsExpanded;

                // 行高变化（堆栈展开/收起），Rebuild 重建可见行并重测高度
                _list.Rebuild();

                if (wasAtBottom)
                {
                    ScrollToBottom();
                }
            });

            return row;
        }

        /// <summary>把数据（行模型）绑定到复用出来的行元素上。由 ListView 在渲染可见行时调用。</summary>
        /// <param name="element">makeItem 返回的行元素。</param>
        /// <param name="index">在视图数据中的索引。</param>
        private void BindItem(VisualElement element, int index)
        {
            EntryRowRefs refs = (EntryRowRefs)element.userData;
            refs.Model = _viewItems[index];
            ConsoleLogEntry entry = refs.Model.Entry;

            // 更新级别样式类（控制圆点与消息文字颜色）
            element.RemoveFromClassList("console-entry-log");
            element.RemoveFromClassList("console-entry-warning");
            element.RemoveFromClassList("console-entry-error");
            element.AddToClassList(GetEntryClass(entry.Type));

            bool hasStack = !string.IsNullOrEmpty(entry.StackTrace);
            refs.Caret.text = hasStack ? (refs.Model.IsExpanded ? "▾" : "▸") : string.Empty;
            refs.Timestamp.text = entry.Timestamp;
            refs.Message.text = entry.Message;
            refs.Stack.text = entry.StackTrace;
            refs.Stack.style.display = hasStack && refs.Model.IsExpanded ? DisplayStyle.Flex : DisplayStyle.None;
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
            if (_scrollView == null)
            {
                return;
            }

            _scrollView.schedule.Execute(() =>
            {
                ScrollToBottomNow();
                _scrollView.schedule.Execute(ScrollToBottomNow);
            });
        }

        /// <summary>立即把日志列表滚动到底部（供延迟调度调用）。</summary>
        private void ScrollToBottomNow()
        {
            Scroller scroller = _scrollView.verticalScroller;
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

        /// <summary>
        /// 列表行模型：在 <see cref="ConsoleLogEntry"/> 之外额外承载“堆栈是否展开”状态。
        /// 行元素会被 ListView 回收复用，展开状态必须存在数据侧而非元素侧，否则复用后状态错乱。
        /// </summary>
        private sealed class LogRowModel
        {
            /// <summary>日志条目数据。</summary>
            public ConsoleLogEntry Entry;

            /// <summary>该行堆栈当前是否展开。</summary>
            public bool IsExpanded;
        }

        /// <summary>一行日志元素的子元素引用缓存，避免 bindItem 反复 Q 查询。</summary>
        private sealed class EntryRowRefs
        {
            public VisualElement Line;
            public Label Caret;
            public Label Timestamp;
            public Label Message;
            public Label Stack;

            /// <summary>当前绑定的行模型（bindItem 中赋值，点击展开时读取）。</summary>
            public LogRowModel Model;
        }
    }
}
