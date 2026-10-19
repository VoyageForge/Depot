namespace VoyageForge.Depot.Editor
{
    /// <summary>
    /// 批量写入 ForgeMeta 时的一条记录：把 Value 写到 Guid 对应 .forge~ 文件里的 Path 位置上。
    ///
    /// Path 是点分合并路径，例如 "com.voyageforge.forge-filesystem.displayName"；
    /// 与 ForgeMetaDatabase.SetNestedField 的 path 参数完全是同一套语义。
    ///
    /// 设计成不可变结构体而不是可变类：一次批量调用里同一条记录不应该被中途改写，
    /// 否则批内行为会依赖遍历顺序之外的东西，排查起来非常困难。
    /// 结构体也让批量调用在条目很多时不必先在堆上分配一堆对象。
    /// </summary>
    public readonly struct ForgeMetaWrite
    {
        /// <summary>目标资产的 Unity GUID，也是 .forge~ 文件的定位键。</summary>
        public readonly string Guid;

        /// <summary>点分嵌套路径，如 "com.voyageforge.depot.layer"。</summary>
        public readonly string Path;

        /// <summary>要写入的字符串值；传空字符串表示删除该键（与 SetNestedField 语义一致）。</summary>
        public readonly string Value;

        /// <param name="guid">目标资产的 Unity GUID</param>
        /// <param name="path">点分嵌套路径</param>
        /// <param name="value">要写入的值</param>
        public ForgeMetaWrite(string guid, string path, string value)
        {
            Guid = guid;
            Path = path;
            Value = value;
        }
    }
}
