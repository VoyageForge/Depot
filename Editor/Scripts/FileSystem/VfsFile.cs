using System;
using System.IO;
using System.Text;

namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 文件节点，内容保存在内存里。
    ///
    /// 内容用 byte[] 而不是 string：虚拟文件系统要模拟真实文件系统，
    /// 而"文本"只是字节的一种解释方式，编码由调用方决定。
    /// </summary>
    public sealed class VfsFile : VfsNode
    {
        /// <summary>文件内容。用空数组而不是 null，避免到处判空。</summary>
        private byte[] _data = Array.Empty<byte>();

        /// <summary>固定为 <see cref="NodeType.File"/>。</summary>
        public override NodeType Type => NodeType.File;

        /// <summary>文件字节数。</summary>
        public long Length => _data.Length;

        /// <summary>
        /// 读取全部内容。返回副本而不是内部数组，
        /// 防止调用方直接改写文件内容而绕过 ModifiedAt 的更新。
        /// </summary>
        /// <returns>文件内容的副本。</returns>
        public byte[] ReadAllBytes() => (byte[])_data.Clone();

        /// <summary>覆盖写入全部内容，并刷新修改时间。</summary>
        /// <param name="data">新的文件内容。</param>
        public void WriteAllBytes(ReadOnlySpan<byte> data)
        {
            _data = data.ToArray();
            ModifiedAt = DateTime.UtcNow;
        }

        /// <summary>按指定编码（默认 UTF-8）读取文本内容。</summary>
        /// <param name="encoding">文本编码；为 null 时使用 UTF-8。</param>
        /// <returns>解码后的文本。</returns>
        public string ReadAllText(Encoding encoding = null)
            => (encoding ?? Encoding.UTF8).GetString(_data);

        /// <summary>按指定编码（默认 UTF-8）写入文本内容。</summary>
        /// <param name="text">要写入的文本。</param>
        /// <param name="encoding">文本编码；为 null 时使用 UTF-8。</param>
        public void WriteAllText(string text, Encoding encoding = null)
            => WriteAllBytes((encoding ?? Encoding.UTF8).GetBytes(text));

        /// <summary>打开一个只读内存流，便于用 Stream 接口消费文件内容。</summary>
        /// <returns>只读的 <see cref="MemoryStream"/>。</returns>
        public Stream OpenRead() => new MemoryStream(_data, writable: false);
    }
}
