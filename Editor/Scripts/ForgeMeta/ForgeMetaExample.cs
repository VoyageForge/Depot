using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace VoyageForge.Depot.Editor
{
    /// <summary>
    /// ForgeMeta 示例菜单，展示嵌套字段的各种重载用法。
    /// 菜单路径：VoyageForge > Depot > ForgeMeta
    /// </summary>
    public static class ForgeMetaExample
    {
        [MenuItem("VoyageForge/Depot/ForgeMeta/设置元数据（合并路径）")]
        static void SetMerged()
        {
            string guid = GetSelectedAssetGuid();
            if (string.IsNullOrEmpty(guid))
                return;
            ForgeMetaDatabase.SetNestedField(guid, "com.voyageforge.depot.layer", "TestLayer");
            ForgeMetaDatabase.SetNestedField(guid, "com.voyageforge.bridge.category", "TestCategory");
            Debug.Log($"[ForgeMeta] 写入（合并路径） GUID: {guid}");
        }

        [MenuItem("VoyageForge/Depot/ForgeMeta/设置元数据（分离路径+键）")]
        static void SetSeparate()
        {
            string guid = GetSelectedAssetGuid();
            if (string.IsNullOrEmpty(guid))
                return;
            ForgeMetaDatabase.SetNestedField(guid, "com.voyageforge.depot", "layer", "TestLayer");
            ForgeMetaDatabase.SetNestedField(guid, "com.voyageforge.bridge", "category", "TestCategory");
            Debug.Log($"[ForgeMeta] 写入（分离路径） GUID: {guid}");
        }

        [MenuItem("VoyageForge/Depot/ForgeMeta/获取并显示元数据")]
        static void GetAndPrint()
        {
            string guid = GetSelectedAssetGuid();
            if (string.IsNullOrEmpty(guid))
                return;

            var meta = ForgeMetaDatabase.Get(guid);
            if (meta == null)
            {
                Debug.Log($"[ForgeMeta] 无元数据文件 (GUID: {guid})");
                return;
            }

            Debug.Log($"[ForgeMeta] === 完整元数据 (GUID: {guid}) ===");
            Debug.Log($"版本: {meta.version}");
            Debug.Log($"存储的 GUID: {meta.guid}");
            PrintDictionary(meta.fields, 0);

            string layer = ForgeMetaDatabase.GetNestedField(guid, "com.voyageforge.depot.layer");
            string category = ForgeMetaDatabase.GetNestedField(guid, "com.voyageforge.bridge", "category");
            Debug.Log($"获取: layer={layer}, category={category}");

            if (ForgeMetaDatabase.TryGetNestedField(guid, "com.voyageforge.depot.layer", out string tryLayer))
                Debug.Log($"TryGet（合并路径）成功: {tryLayer}");
            else
                Debug.Log("TryGet（合并路径）失败");

            if (ForgeMetaDatabase.TryGetNestedField(guid, "com.voyageforge.bridge", "category", out string tryCategory))
                Debug.Log($"TryGet（分离路径）成功: {tryCategory}");
            else
                Debug.Log("TryGet（分离路径）失败");
        }

        [MenuItem("VoyageForge/Depot/ForgeMeta/删除元数据")]
        static void Delete()
        {
            string guid = GetSelectedAssetGuid();
            if (string.IsNullOrEmpty(guid))
                return;
            ForgeMetaDatabase.Delete(guid);
            Debug.Log($"[ForgeMeta] 已删除 (GUID: {guid})");
        }

        [MenuItem("VoyageForge/Depot/ForgeMeta/批量设置元数据")]
        static void BatchSet()
        {
            var guids = GetSelectedAssetGuids();
            if (guids == null || guids.Length == 0)
                return;

            int count = 0;
            // 先收集成批量写入记录，再一次提交：整批只会触发一次 AssetDatabase.Refresh()，
            // 而不是每个资源刷一次。值取同一个时间戳，整批写入内容保持一致。
            string timestamp = System.DateTime.Now.ToString();
            var writes = new List<ForgeMetaWrite>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                    continue;
                writes.Add(new ForgeMetaWrite(guid, "com.voyageforge.batch.time", timestamp));
                count++;
            }

            int applied = ForgeMetaDatabase.SetNestedFieldBatch(writes);
            Debug.Log($"[ForgeMeta] 已为 {count} 个资源批量写入，实际改动 {applied} 个");
        }

        [MenuItem("VoyageForge/Depot/ForgeMeta/显示 GUID")]
        static void ShowGuid()
        {
            string guid = GetSelectedAssetGuid();
            if (!string.IsNullOrEmpty(guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Debug.Log($"路径: {path}\nGUID: {guid}");
                EditorGUIUtility.systemCopyBuffer = guid;
                Debug.Log("GUID 已复制到剪贴板。");
            }
        }

        // ---------- 菜单验证 ----------
        [MenuItem("VoyageForge/Depot/ForgeMeta/设置元数据（合并路径）", true)]
        [MenuItem("VoyageForge/Depot/ForgeMeta/设置元数据（分离路径+键）", true)]
        [MenuItem("VoyageForge/Depot/ForgeMeta/获取并显示元数据", true)]
        [MenuItem("VoyageForge/Depot/ForgeMeta/删除元数据", true)]
        [MenuItem("VoyageForge/Depot/ForgeMeta/批量设置元数据", true)]
        [MenuItem("VoyageForge/Depot/ForgeMeta/显示 GUID", true)]
        static bool Validate() => Selection.activeObject != null;

        // ---------- 辅助 ----------
        private static string GetSelectedAssetGuid()
        {
            if (Selection.activeObject == null)
            {
                Debug.LogWarning("请先在 Project 窗口中选中一个资源。");
                return null;
            }
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
            {
                Debug.LogWarning("请选中文件，而不是文件夹或无效对象。");
                return null;
            }
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning("无法获取 GUID。");
                return null;
            }
            return guid;
        }

        private static string[] GetSelectedAssetGuids()
        {
            var objects = Selection.objects;
            if (objects == null || objects.Length == 0)
            {
                Debug.LogWarning("请先选中资源。");
                return null;
            }
            var list = new List<string>();
            foreach (var obj in objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path))
                    continue;
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                    list.Add(guid);
            }
            return list.ToArray();
        }

        private static void PrintDictionary(Dictionary<string, object> dict, int indent)
        {
            string ind = new string(' ', indent * 2);
            foreach (var kv in dict)
            {
                if (kv.Value is Dictionary<string, object> nested)
                {
                    Debug.Log($"{ind}{kv.Key}:");
                    PrintDictionary(nested, indent + 1);
                }
                else
                {
                    Debug.Log($"{ind}{kv.Key}: {kv.Value}");
                }
            }
        }
    }
}