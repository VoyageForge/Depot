namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 一棵虚拟文件树的统计结果。
    /// 从 <see cref="VirtualFileSystem"/> 抽出来做独立类型，
    /// 这样"怎么数"和"数完怎么用"分开，调用方也不必再拆元组。
    /// </summary>
    public readonly struct VfsStatistics
    {
        /// <summary>文件个数。</summary>
        public int FileCount { get; }

        /// <summary>目录个数（不包含根目录本身）。</summary>
        public int DirectoryCount { get; }

        /// <summary>所有文件的字节数总和。</summary>
        public long TotalBytes { get; }

        /// <summary>构造统计结果。</summary>
        /// <param name="fileCount">文件个数。</param>
        /// <param name="directoryCount">目录个数。</param>
        /// <param name="totalBytes">字节总数。</param>
        public VfsStatistics(int fileCount, int directoryCount, long totalBytes)
        {
            FileCount = fileCount;
            DirectoryCount = directoryCount;
            TotalBytes = totalBytes;
        }

        /// <summary>
        /// 遍历整棵树统计文件数、目录数与字节总数。
        /// 根目录自身不计入 <see cref="DirectoryCount"/>。
        /// </summary>
        /// <param name="fileSystem">要统计的文件系统；为 null 时返回全零结果。</param>
        /// <returns>统计结果。</returns>
        public static VfsStatistics From(VirtualFileSystem fileSystem)
        {
            if (fileSystem is null) return new VfsStatistics(0, 0, 0);

            var files = 0;
            var dirs = 0;
            long bytes = 0;

            foreach (var node in fileSystem.Walk("/"))
            {
                var file = node as VfsFile;
                if (file != null)
                {
                    files++;
                    bytes += file.Length;
                }
                else
                {
                    dirs++;
                }
            }

            return new VfsStatistics(files, dirs, bytes);
        }

        /// <summary>便于日志输出，例如 "3 文件 / 2 目录 / 128 字节"。</summary>
        public override string ToString()
            => $"{FileCount} 文件 / {DirectoryCount} 目录 / {TotalBytes} 字节";
    }
}
