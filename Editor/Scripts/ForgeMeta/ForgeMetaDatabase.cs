using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VoyageForge.Depot.Editor
{
    /// <summary>
    /// ForgeMeta 核心数据库 API，所有操作均基于资产 GUID。
    /// 支持嵌套字段（合并路径或分离路径），并提供缓存以减少 I/O。
    /// </summary>
    public static class ForgeMetaDatabase
    {
        // ---------- 缓存 ----------
        private class CacheEntry
        {
            public ForgeMetadata Metadata;
            public DateTime LastWriteTime;
        }

        private static readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>();

        /// <summary>
        /// 批量写入的嵌套深度。大于 0 表示当前正处于批量写入中，
        /// 此时每次字段改动都不必单独刷新 AssetDatabase，由批量入口在整批结束后统一刷新一次。
        ///
        /// 用深度计数而不是 bool，是为了让批量操作内部再嵌套批量调用时，
        /// 内层结束后不会提前把刷新行为恢复成"每条都刷"。
        /// </summary>
        private static int _batchDepth;


        /// <summary>
        /// 根据资产 GUID 获取对应的伴随文件路径（格式：原文件名.forge~）。
        /// </summary>
        private static string GetForgeFilePath(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;

            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            string directory = Path.GetDirectoryName(assetPath);
            string fileName = Path.GetFileName(assetPath);
            string forgeFileName = fileName + ".forge~";
            return Path.Combine(directory, forgeFileName).Replace("\\", "/");
        }

        /// <summary>检测指定 GUID 的资产是否存在元数据文件</summary>
        public static bool Exists(string guid)
        {
            string forgePath = GetForgeFilePath(guid);
            return forgePath != null && File.Exists(forgePath);
        }

        /// <summary>
        /// 读取完整元数据对象，带缓存。
        /// 若文件被外部修改，会自动重新读取并更新缓存。
        /// </summary>
        public static ForgeMetadata Get(string guid)
        {
            string forgePath = GetForgeFilePath(guid);
            if (string.IsNullOrEmpty(forgePath))
                return null;

            if (_cache.TryGetValue(guid, out CacheEntry entry))
            {
                if (File.Exists(forgePath))
                {
                    DateTime fileTime = File.GetLastWriteTimeUtc(forgePath);
                    if (entry.LastWriteTime == fileTime)
                        return entry.Metadata;
                    else
                        _cache.Remove(guid); // 文件已修改，缓存失效
                }
                else
                {
                    _cache.Remove(guid);
                    return null;
                }
            }

            if (!File.Exists(forgePath))
                return null;

            var meta = ForgeMetaSerializer.Deserialize(forgePath);
            if (meta != null)
            {
                _cache[guid] = new CacheEntry
                {
                    Metadata = meta,
                    LastWriteTime = File.GetLastWriteTimeUtc(forgePath)
                };
            }
            return meta;
        }

        /// <summary>获取元数据中存储的 GUID（用于校验）</summary>
        public static string GetStoredGuid(string guid)
        {
            var meta = Get(guid);
            return meta?.guid;
        }

        // ==================== 设置方法（两种重载） ====================

        /// <summary>
        /// 设置嵌套字段值（合并路径），如 "com.voyageforge.depot.layer"。
        /// 中间节点自动创建，value 为空则删除该键。
        /// 若值已存在相同内容，则不执行任何写入操作。
        /// </summary>
        public static void SetNestedField(string guid, string path, string value)
        {
            ApplyNestedField(guid, path, value);
        }

        /// <summary>
        /// 批量写入多个字段，返回实际发生改动的条数。
        ///
        /// 与循环调用 SetNestedField 的唯一区别是刷新时机：单条写入每写一条就调用一次
        /// AssetDatabase.Refresh()，写 N 条就刷 N 次；批量写入在整批结束后只刷一次。
        /// 文件本身仍然是一条一个 .forge~，合并规则、缓存更新、"值相同则跳过"的判断
        /// 与单条写入完全一致——所以批量入口不会写出和单条不同的结果。
        ///
        /// 单条失败（GUID 无法解析、序列化异常等）只记录错误日志并继续处理其余条目，
        /// 不会中断整批，因此返回值可能小于传入条数。
        /// </summary>
        public static int SetNestedFieldBatch(IEnumerable<ForgeMetaWrite> writes)
        {
            if (writes == null)
                return 0;

            int applied = 0;
            _batchDepth++;
            try
            {
                foreach (ForgeMetaWrite write in writes)
                {
                    try
                    {
                        if (ApplyNestedField(write.Guid, write.Path, write.Value))
                            applied++;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"[ForgeMeta] 批量写入失败 guid={write.Guid} path={write.Path} → {exception.Message}");
                    }
                }
            }
            finally
            {
                // 必须放在 finally：某条写入抛异常也要恢复正常的刷新行为，
                // 否则这一批之后所有单条写入都会静默地不再刷新 AssetDatabase。
                _batchDepth--;
            }

            // 整批只刷新一次；没有任何实际改动时连这一次也省掉。
            if (applied > 0)
                AssetDatabase.Refresh();

            return applied;
        }

        /// <summary>
        /// 真正执行一次字段写入，返回是否发生了改动。
        ///
        /// 单独抽出来是因为批量写入需要知道每条是否真的改了（未变化的条目不计入返回值），
        /// 而原来对外暴露的 void 版本无法表达这一点。
        /// </summary>
        private static bool ApplyNestedField(string guid, string path, string value)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string forgePath = GetForgeFilePath(guid);
            if (forgePath == null)
                return false;

            var meta = Get(guid) ?? new ForgeMetadata(guid);
            if (!SetNestedValue(meta.fields, path, value))
                return false;

            SaveMetadata(meta, forgePath, guid);
            return true;
        }

        /// <summary>
        /// 设置嵌套字段值（分离路径+键），如 path="com.voyageforge.depot", key="layer"。
        /// </summary>
        public static void SetNestedField(string guid, string path, string key, string value)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(key))
                return;
            SetNestedField(guid, path + "." + key, value);
        }

        // ==================== 获取方法（两种重载） ====================

        /// <summary>获取嵌套字段值（合并路径）</summary>
        public static string GetNestedField(string guid, string path)
        {
            var meta = Get(guid);
            if (meta == null)
                return null;

            object current = meta.fields;
            foreach (var part in path.Split('.'))
            {
                if (current is Dictionary<string, object> dict && dict.TryGetValue(part, out object next))
                    current = next;
                else
                    return null;
            }
            return current as string;
        }

        /// <summary>获取嵌套字段值（分离路径+键）</summary>
        public static string GetNestedField(string guid, string path, string key)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(key))
                return null;
            return GetNestedField(guid, path + "." + key);
        }

        // ==================== TryGet 方法（两种重载） ====================

        /// <summary>尝试获取嵌套字段值（合并路径）</summary>
        public static bool TryGetNestedField(string guid, string path, out string value)
        {
            value = GetNestedField(guid, path);
            return value != null;
        }

        /// <summary>尝试获取嵌套字段值（分离路径+键）</summary>
        public static bool TryGetNestedField(string guid, string path, string key, out string value)
        {
            value = GetNestedField(guid, path, key);
            return value != null;
        }

        // ==================== 内部辅助 ====================

        /// <summary>
        /// 在根字典中根据点分隔路径设置值，自动创建中间字典。
        /// 返回值：true 表示值实际发生了变化，false 表示值与现有值相同（无需写入）。
        /// </summary>
        private static bool SetNestedValue(Dictionary<string, object> root, string path, string value)
        {
            var parts = path.Split('.');
            object current = root;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (i == parts.Length - 1)
                {
                    if (current is Dictionary<string, object> dict)
                    {
                        if (dict.TryGetValue(part, out object existing))
                        {
                            if (existing is string str && str == value)
                                return false; // 值相同，无需更改
                        }
                        else if (string.IsNullOrEmpty(value))
                            return false; // 不存在且要删除，无需操作

                        if (string.IsNullOrEmpty(value))
                            dict.Remove(part);
                        else
                            dict[part] = value;
                        return true;
                    }
                    else
                    {
                        Debug.LogError($"[ForgeMeta] 路径 '{path}' 中间节点不是字典，无法设置值。");
                        return false;
                    }
                }
                else
                {
                    if (current is Dictionary<string, object> dict)
                    {
                        if (!dict.TryGetValue(part, out object next))
                        {
                            var newDict = new Dictionary<string, object>();
                            dict[part] = newDict;
                            current = newDict;
                        }
                        else if (next is Dictionary<string, object> nextDict)
                        {
                            current = nextDict;
                        }
                        else
                        {
                            var newDict = new Dictionary<string, object>();
                            dict[part] = newDict;
                            current = newDict;
                        }
                    }
                    else
                    {
                        Debug.LogError($"[ForgeMeta] 路径 '{path}' 中间节点不是字典，无法继续。");
                        return false;
                    }
                }
            }
            return false;
        }

        /// <summary>保存元数据，若字段为空则删除文件，并更新缓存</summary>
        private static void SaveMetadata(ForgeMetadata meta, string forgePath, string guid)
        {
            if (meta.fields.Count == 0)
            {
                Delete(guid);
                return;
            }

            if (meta.guid != guid)
                meta.guid = guid;

            ForgeMetaSerializer.Serialize(forgePath, meta);

            // 批量写入期间不逐条刷新，由 SetNestedFieldBatch 在整批结束后统一刷一次。
            if (_batchDepth == 0)
                AssetDatabase.Refresh();

            _cache[guid] = new CacheEntry
            {
                Metadata = meta,
                LastWriteTime = File.GetLastWriteTimeUtc(forgePath)
            };
        }

        /// <summary>删除指定 GUID 资产的元数据文件，并清除缓存</summary>
        public static void Delete(string guid)
        {
            string forgePath = GetForgeFilePath(guid);
            if (forgePath == null)
                return;

            if (File.Exists(forgePath))
            {
                File.Delete(forgePath);

                // 批量写入期间同样延后刷新，避免删除也逐条触发一次 AssetDatabase 扫描。
                if (_batchDepth == 0)
                    AssetDatabase.Refresh();
            }
            _cache.Remove(guid);
        }

        /// <summary>清空元数据（同 Delete）</summary>
        public static void Clear(string guid) => Delete(guid);
    }
}