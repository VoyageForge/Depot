using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace VoyageForge.Depot.Editor.Utilities
{
    /// <summary>
    /// Depot 项目设置提供器。
    /// 负责在 Project Settings 中展示打包辅助配置。
    /// </summary>
    public sealed class DepotSettingsProvider : SettingsProvider
    {
        private const string SettingsPath = "Project/VoyageForge/Depot";
        private const string SettingsUxmlPath = "Assets/Depot/Editor/Scripts/Utilities/DepotProjectSettings.uxml";
        private static VisualTreeAsset _settingsVisualTreeAsset;

        /// <summary>
        /// 创建 Depot 项目设置提供器实例。
        /// </summary>
        public DepotSettingsProvider() : base(SettingsPath, SettingsScope.Project)
        {
            label = "Depot";
            activateHandler = (_, rootElement) => BuildUi(rootElement);
            keywords = new HashSet<string>(new[] { "Depot", "Auto", "Version", "Build", "Skip", "Splash" });
        }

        /// <summary>
        /// 注册 Depot 项目设置页。
        /// </summary>
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new DepotSettingsProvider();
        }

        /// <summary>
        /// 构建 Depot 项目设置界面。
        /// </summary>
        private static void BuildUi(VisualElement rootElement)
        {
            var settings = DepotProjectSettings.instance;
            settings.EnsureSaved();

            rootElement.Clear();
            var visualTreeAsset = LoadSettingsVisualTreeAsset();
            if (visualTreeAsset == null)
            {
                rootElement.Add(new Label("Depot Project Settings UXML not found."));
                return;
            }

            visualTreeAsset.CloneTree(rootElement);

            var autoVersionToggle = rootElement.Q<Toggle>("AutoVersionToggle");
            var incrementField = rootElement.Q<IntegerField>("AutoVersionIncrementField");
            var skipSplashToggle = rootElement.Q<Toggle>("SkipSplashToggle");

            autoVersionToggle.value = settings.AutoVersionEnabled;
            autoVersionToggle.RegisterValueChangedCallback(evt =>
            {
                settings.AutoVersionEnabled = evt.newValue;
            });

            incrementField.value = settings.AutoVersionIncrementStep;
            incrementField.RegisterValueChangedCallback(evt =>
            {
                int sanitizedValue = evt.newValue < 1 ? 1 : evt.newValue;
                if (incrementField.value != sanitizedValue)
                {
                    incrementField.SetValueWithoutNotify(sanitizedValue);
                }

                settings.AutoVersionIncrementStep = sanitizedValue;
            });

            skipSplashToggle.value = settings.SkipSplashEnabled;
            skipSplashToggle.RegisterValueChangedCallback(evt =>
            {
                settings.SkipSplashEnabled = evt.newValue;
                SkipSplashEditor.ApplySetting(evt.newValue);
            });
        }

        private static VisualTreeAsset LoadSettingsVisualTreeAsset()
        {
            if (_settingsVisualTreeAsset != null)
            {
                return _settingsVisualTreeAsset;
            }

            // 直接按 Asset 路径加载（原 UxmlUtility 已移除：它的按名搜索能力在本包内没有使用场景）。
            _settingsVisualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SettingsUxmlPath);
            return _settingsVisualTreeAsset;
        }
    }
}
