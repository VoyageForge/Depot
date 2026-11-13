using System;
using VoyageForge.Depot.Runtime.Utilities;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VoyageForge.Depot.Editor.Utilities;

namespace VoyageForge.Depot.Editor
{
    [CustomPropertyDrawer(typeof(SceneReference))]
    public class SceneReferenceEditor : PropertyDrawer
    {
        private const string DefaultInspectorUxmlPath =
            "Assets/Depot/Editor/Scripts/Inspector/SceneReference/SceneReferenceInspector.uxml";

        private static VisualTreeAsset _visualTreeAsset;
        private static readonly System.Collections.Generic.Dictionary<string, bool> FoldoutStates = new();

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (_visualTreeAsset == null)
            {
                _visualTreeAsset = UxmlUtility.LoadVisualTreeAsset(DefaultInspectorUxmlPath);
            }

            if (_visualTreeAsset == null)
            {
                return new Label("SceneReference inspector UXML not found");
            }

            var rootElement = _visualTreeAsset.CloneTree();
            // PropertyDrawer lives inside the Inspector ScrollView; avoid growing into the scrollbar hit area.
            rootElement.style.flexGrow = 0;
            rootElement.style.flexShrink = 0;

            SerializedProperty guidProperty = property.FindPropertyRelative(SceneReference.GuidFieldName);
            SerializedProperty pathProperty = property.FindPropertyRelative(SceneReference.PathFieldName);
            SerializedProperty nameProperty = property.FindPropertyRelative(SceneReference.NameFieldName);

            if (guidProperty == null || pathProperty == null || nameProperty == null)
            {
                return new Label("SceneReference fields not found");
            }

            var objectField = rootElement.Q<ObjectField>("SceneAssetField");
            var noSceneHint = rootElement.Q<VisualElement>("NoSceneHint");
            var sceneDetailsFoldout = rootElement.Q<Foldout>("SceneDetailsFoldout");
            var summaryLabel = rootElement.Q<Label>("SummaryValue");
            var pathLabel = rootElement.Q<VisualElement>("ScenePath").Q<Label>("Value");
            var nameLabel = rootElement.Q<VisualElement>("SceneName").Q<Label>("Value");
            // 对象标识：Unity 6.4+ 提供 EntityId，GetInstanceID() 从 6000.5 起已是 error 级废弃
            // （CS0619: 'Use GetEntityId instead'）。EntityId 的隐式 int 转换同样已废弃，
            // 所以这里直接 ToString() 当缓存键，不做数值转换。
            // 低版本（2022.3 ~ 6000.3）继续用 InstanceID，保持包对旧 Unity 的兼容。
#if UNITY_6000_4_OR_NEWER
            string objectId = property.serializedObject.targetObject.GetEntityId().ToString();
#else
            string objectId = property.serializedObject.targetObject.GetInstanceID().ToString();
#endif
            string propertyKey = $"{objectId}:{property.propertyPath}";

            objectField.objectType = typeof(SceneAsset);
            sceneDetailsFoldout.value = GetFoldoutState(propertyKey);
            sceneDetailsFoldout.RegisterValueChangedCallback(evt => FoldoutStates[propertyKey] = evt.newValue);

            // 初始化
            UpdateDisplay(guidProperty, objectField, noSceneHint, sceneDetailsFoldout, summaryLabel, pathLabel,
                nameLabel);

            // 值改变事件
            objectField.RegisterValueChangedCallback(evt =>
            {
                property.serializedObject.Update();

                if (evt.newValue == null)
                {
                    guidProperty.stringValue = string.Empty;
                    pathProperty.stringValue = string.Empty;
                    nameProperty.stringValue = string.Empty;
                }
                else if (evt.newValue is SceneAsset sceneAsset)
                {
                    string path = AssetDatabase.GetAssetPath(sceneAsset);
                    string guid = AssetDatabase.AssetPathToGUID(path);

                    guidProperty.stringValue = guid;
                    pathProperty.stringValue = path;
                    nameProperty.stringValue = sceneAsset.name;
                }

                UpdateDisplay(guidProperty, objectField, noSceneHint, sceneDetailsFoldout, summaryLabel, pathLabel,
                    nameLabel);
                property.serializedObject.ApplyModifiedProperties();
            });

            return rootElement;
        }

        private void UpdateDisplay(
            SerializedProperty guidProperty,
            ObjectField objectField,
            VisualElement noSceneHint,
            Foldout sceneDetailsFoldout,
            Label summaryLabel,
            Label pathLabel,
            Label nameLabel)
        {
            string guid = guidProperty.stringValue;
            bool hasScene = !string.IsNullOrEmpty(guid);

            if (!hasScene)
            {
                noSceneHint.style.display = DisplayStyle.Flex;
                sceneDetailsFoldout.style.display = DisplayStyle.None;
                objectField.value = null;
                return;
            }

            noSceneHint.style.display = DisplayStyle.None;
            sceneDetailsFoldout.style.display = DisplayStyle.Flex;

            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (string.IsNullOrEmpty(path))
            {
                sceneDetailsFoldout.text = "场景详情";
                summaryLabel.text = "GUID 已失效";
                nameLabel.text = "无效的场景GUID";
                pathLabel.text = "请重新选择";
                sceneDetailsFoldout.style.backgroundColor = new StyleColor(new Color(0.95f, 0.8f, 0.8f));
                objectField.value = null;
                return;
            }

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

            if (sceneAsset == null)
            {
                sceneDetailsFoldout.text = "场景详情";
                summaryLabel.text = "资源丢失";
                nameLabel.text = "场景资源丢失";
                pathLabel.text = path;
                sceneDetailsFoldout.style.backgroundColor = new StyleColor(new Color(1f, 0.95f, 0.8f));
                objectField.value = null;
                return;
            }

            objectField.value = sceneAsset;
            sceneDetailsFoldout.text = $"场景详情 - {sceneAsset.name}";
            summaryLabel.text = sceneAsset.name;
            nameLabel.text = sceneAsset.name;
            pathLabel.text = path;

            // 重置为正常颜色
            sceneDetailsFoldout.style.backgroundColor = new StyleColor(new Color(0.91f, 0.96f, 0.91f)); // #E8F5E9
        }

        private static bool GetFoldoutState(string propertyKey)
        {
            return FoldoutStates.TryGetValue(propertyKey, out bool isExpanded) && isExpanded;
        }
    }
}