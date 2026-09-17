namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 查询一个路径"实际是什么"。
    ///
    /// 与 <see cref="NodeType"/> 的区别：
    ///   · <see cref="NodeType"/> 是【节点自己】的类型；
    ///   · PathKind 是【一次查询】的结果，多了一个"不存在"的可能。
    /// </summary>
    public enum PathKind
    {
        /// <summary>路径不存在。</summary>
        NotFound,

        /// <summary>路径指向一个已存在的文件。</summary>
        File,

        /// <summary>路径指向一个已存在的目录。</summary>
        Directory,
    }
}
