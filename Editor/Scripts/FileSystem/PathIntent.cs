namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 当路径还不存在时，从写法上推断用户意图。
    ///
    /// ⚠ 重要认知：路径字符串本身【不携带】类型信息。
    ///   类型是存储在节点上的元数据，必须查到节点才能确定。
    ///   下面这些只是"书写习惯"带来的弱线索，不能当作事实。
    /// </summary>
    public enum PathIntent
    {
        /// <summary>无法判断。</summary>
        Unknown,

        /// <summary>语法上明确表示目录（以 "/" 结尾）。</summary>
        WantDirectory,
    }
}
