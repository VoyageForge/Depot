namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 当 ".." 想要越过根目录时怎么办。
    ///
    /// 三种语义各有用途，之所以做成枚举而不是两个 bool，
    /// 是因为"夹取"和"报错"是互斥的，用 bool 会造出无意义的组合。
    ///
    /// 与真实操作系统的对照（Windows 实测）：
    ///   <c>C:\..</c>                → <c>C:\</c>（夹取，不报错）
    ///   <c>C:\..\..\Windows</c>     → <c>C:\Windows</c>（先夹取，再往下走）
    ///   <c>C:\Windows\..\..</c>     → <c>C:\</c>
    ///   <c>Directory.GetParent("C:\")</c> → null（卷根本没有父目录）
    /// POSIX 同理：<c>/..</c> 就是 <c>/</c>。
    /// 也就是说，真实系统在"到顶"时是【夹取】而不是报错，见 <see cref="Clamp"/>。
    /// </summary>
    public enum RootEscapeMode
    {
        /// <summary>
        /// 夹取到根目录（默认）：越界的 ".." 被忽略，等价于"停在根目录原地不动"。
        ///
        /// 与 Windows 卷根行为完全同构，所以当根目录被当作"伪磁盘根"时，
        /// 这是最不容易让人意外的选择：
        ///   "/.."           → "/"
        ///   "../../x"       → "/x"      （等价于 C:\..\..\Windows → C:\Windows）
        ///   "/a/b/../../.." → "/"
        /// 结果永远落在根目录之内，<see cref="VfsPath.EscapesRoot"/> 恒为 false。
        /// </summary>
        Clamp = 0,

        /// <summary>
        /// 报 <see cref="PathError.EscapeRoot"/>。
        ///
        /// 适合"越界一定是 bug 或攻击"的场景：夹取虽然安全，但会把调用方的
        /// 路径拼装错误悄悄吞掉，报错能第一时间暴露出来。
        /// </summary>
        Error,

        /// <summary>
        /// 保留越界的 ".."，允许把根目录之外的路径表达出来。
        ///
        /// 此时 <see cref="VfsPath.EscapesRoot"/> 为 true、规范化结果是
        /// <c>"/../x"</c> 这种带越界记号的形式，可以再用
        /// <see cref="VirtualFileSystem.TryGetRealPath(VfsPath, out string, out PathError)"/>
        /// 换算出根目录之外的真实路径。
        ///
        /// 注意：这类路径在根目录之内的树里查不到节点，
        /// 也不能用来在根目录之外创建节点（树只覆盖根目录之内）。
        /// </summary>
        Escape,
    }
}
