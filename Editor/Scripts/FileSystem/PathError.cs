namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 路径校验失败的原因。
    ///
    /// 用枚举而不是 bool，是为了让调用方能针对不同错误给出不同提示，
    /// 也方便在日志里区分"用户输入错误"和"程序逻辑错误"。
    /// </summary>
    public enum PathError
    {
        /// <summary>没有错误，路径合法。</summary>
        None = 0,

        /// <summary>路径为 null，或只包含空白字符。</summary>
        Empty,

        /// <summary>路径总长度超限。</summary>
        TooLong,

        /// <summary>路径中某一段（单个目录名或文件名）长度超限。</summary>
        SegmentTooLong,

        /// <summary>包含操作系统不允许的字符。</summary>
        IllegalChar,

        /// <summary>使用了操作系统保留的设备名（如 CON、NUL、COM1）。</summary>
        ReservedName,

        /// <summary>段名以空格或英文句点结尾（Windows 会静默去掉，易造成歧义）。</summary>
        TrailingSpaceOrDot,

        /// <summary>使用了过多的 ".."，试图越过根目录，例如 "/../a"。</summary>
        EscapeRoot,

        /// <summary>
        /// 解析后的路径落在【根目录之外】。
        /// 与 <see cref="EscapeRoot"/> 的区别：EscapeRoot 说的是"用 .. 往上爬越界"，
        /// 本错误说的是"直接给了一条根目录之外的绝对路径"，例如根是 D:\Proj\Assets
        /// 却传入了 D:\Other\x。
        /// </summary>
        OutsideRoot,

        /// <summary>
        /// 纯虚拟模式（没有指定根目录路径）下试图换算真实磁盘路径。
        /// 见 <see cref="VirtualFileSystem.IsVirtual"/>。
        /// </summary>
        NoRootPath,

        /// <summary>拼接时某个片段非法（用于区分是"某段"而非"整条"出错）。</summary>
        InvalidSegment,
    }
}
