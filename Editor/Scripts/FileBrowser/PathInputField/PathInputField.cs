using UnityEngine;
using UnityEngine.UIElements;
using VoyageForge.Depot.Editor.FileSystem;
using VoyageForge.Depot.Editor.Utilities;

namespace VoyageForge.Depot.Editor.ProjectBrowser
{
    /// <summary>
    /// 文件浏览器的路径输入框。
    ///
    /// 职责：只做"输入框 UI"这一件事——把用户敲进来的文本交给
    /// <see cref="ProjectPathResolver"/> 判定，成功就更新 <see cref="RelativePath"/>，
    /// 失败就回滚显示。
    ///
    /// 路径的解析规则（绝对/相对、文件/目录、是否越出工程）全部在
    /// <see cref="ProjectPathResolver"/> / <see cref="DiskPath"/> 里，
    /// 本类不再重复实现，避免两套规则漂移。
    /// </summary>
    public sealed class PathInputField : VFVisualElement
    {
        /// <summary>默认相对路径：表示"工程根目录"。</summary>
        private const string DefaultRelativePath = "./";

        /// <summary>承载输入框的容器，来自同名 uxml。</summary>
        private VisualElement _container;

        /// <summary>用户最近一次提交的原始输入（可能是绝对路径，也可能是相对路径）。</summary>
        private string _value = DefaultRelativePath;

        /// <summary>当前生效的、面向浏览器的相对路径（形如 "./Sub/Dir"）。</summary>
        private string _relativePath = DefaultRelativePath;

        /// <summary>实际的文本输入控件。</summary>
        private TextField _textField;

        // ---------- UxmlFactory 和 UxmlTraits（支持 UI Builder 和 UXML 序列化） ----------
        public new class UxmlFactory : UxmlFactory<PathInputField, UxmlTraits>
        {
        }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
        }

        public PathInputField()
        {
            name = "path-input-field";
            AddToClassList("path-input-field");
            _container = TreeAsset.InstantiateWithFillAndAddTo(this);

            _textField = new TextField
            {
                style =
                {
                    flexGrow = 1
                },
                value = Value
            };

            _textField.RegisterCallback<NavigationSubmitEvent>(OnSubmit);
            _textField.RegisterCallback<FocusOutEvent>(OnFocusOut);

            _container.Add(_textField);
        }

        /// <summary>
        /// 用户输入的原始路径。
        /// 赋值时会尝试解析：成功则记录输入并同步 <see cref="RelativePath"/>；
        /// 失败则丢弃本次输入，把输入框刷回当前生效的相对路径。
        /// </summary>
        public string Value
        {
            get => _value;
            set
            {
                string relativePath;
                if (ProjectPathResolver.TryResolve(value, Application.dataPath,
                                                   out _, out relativePath))
                {
                    _value = value;
                    RelativePath = relativePath;
                }
                else
                {
                    // 输入非法：保持当前选中目录不变，仅把输入框内容回滚。
                    RelativePath = RelativePath;
                }
            }
        }

        /// <summary>当前生效的相对路径，形如 "./" 或 "./Sub/Dir"。</summary>
        public string RelativePath
        {
            get => _relativePath;
            set
            {
                _relativePath = value;
                _textField.value = value;
            }
        }

        /// <summary>
        /// 输入框失焦时校验路径。
        /// </summary>
        /// <param name="evt">失焦事件。</param>
        private void OnFocusOut(FocusOutEvent evt)
        {
            if (_textField.value == Value)
            {
                _textField.value = RelativePath;
                return;
            }

            Value = _textField.value;
        }

        /// <summary>
        /// 回车提交时校验路径。
        /// </summary>
        /// <param name="evt">提交事件。</param>
        private void OnSubmit(NavigationSubmitEvent evt)
        {
            if (_textField.value == Value)
            {
                _textField.value = RelativePath;
                return;
            }

            Value = _textField.value;
        }
    }
}
