using System;
using System.IO;
using System.Text;

namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 虚拟文件树的单文件二进制镜像读写（持久化）。
    ///
    /// 从 <see cref="VirtualFileSystem"/> 抽出来的原因：序列化格式（魔数、版本号、
    /// 字段顺序）是独立的演进维度，将来加字段 / 升版本时只改这里，
    /// 不会碰到文件系统的导航与增删逻辑。
    ///
    /// 镜像布局（小端）：
    ///   Magic(4) · Version(4) · 根目录子节点数(4) · 节点…（深度优先递归）
    /// 每个节点：
    ///   Type(1) · Name(字符串) · CreatedAt(8) · ModifiedAt(8)
    ///   文件 → 内容长度(4) · 内容字节
    ///   目录 → 子节点数(4) · 子节点…
    /// </summary>
    public static class VfsImageSerializer
    {
        /// <summary>镜像魔数，ASCII "MVFS" 的小端表示，用于快速识别文件格式。</summary>
        private const uint Magic = 0x5346564D;

        /// <summary>当前镜像格式版本；读到不一致的版本直接拒绝，避免误解析。</summary>
        private const int Version = 1;

        /// <summary>
        /// 把整棵树写入流。调用方负责流的生命周期（本方法不关闭流）。
        /// </summary>
        /// <param name="fileSystem">要保存的文件系统。</param>
        /// <param name="stream">目标流，必须可写。</param>
        public static void Save(VirtualFileSystem fileSystem, Stream stream)
        {
            if (fileSystem is null) throw new ArgumentNullException(nameof(fileSystem));
            if (stream is null) throw new ArgumentNullException(nameof(stream));

            // leaveOpen: true —— 流的关闭权交给调用方，便于一个流里串多段数据。
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(fileSystem.Root.Count);
                foreach (var child in fileSystem.Root.Children)
                    WriteNode(writer, child);
            }
        }

        /// <summary>
        /// 从流中读回一棵完整的文件树。
        /// </summary>
        /// <param name="stream">源流，必须可读，且定位在镜像开头。</param>
        /// <returns>还原出来的文件系统。</returns>
        /// <exception cref="InvalidDataException">魔数或版本号不匹配时抛出。</exception>
        public static VirtualFileSystem Load(Stream stream)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));

            using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            {
                if (reader.ReadUInt32() != Magic)
                    throw new InvalidDataException("不是有效的 MVFS 镜像");

                var version = reader.ReadInt32();
                if (version != Version)
                    throw new InvalidDataException($"不支持的镜像版本: {version}");

                var fileSystem = new VirtualFileSystem();
                var count = reader.ReadInt32();
                for (var i = 0; i < count; i++)
                    fileSystem.Root.Attach(ReadNode(reader, fileSystem.Root));

                return fileSystem;
            }
        }

        /// <summary>递归写入一个节点及其全部后代。</summary>
        /// <param name="writer">目标写入器。</param>
        /// <param name="node">要写入的节点。</param>
        private static void WriteNode(BinaryWriter writer, VfsNode node)
        {
            writer.Write((byte)node.Type);
            writer.Write(node.Name);
            writer.Write(node.CreatedAt.Ticks);
            writer.Write(node.ModifiedAt.Ticks);

            var file = node as VfsFile;
            if (file != null)
            {
                var data = file.ReadAllBytes();
                writer.Write(data.Length);
                writer.Write(data);
                return;
            }

            var dir = (VfsDirectory)node;
            writer.Write(dir.Count);
            foreach (var child in dir.Children) WriteNode(writer, child);
        }

        /// <summary>递归读回一个节点及其全部后代。</summary>
        /// <param name="reader">源读取器。</param>
        /// <param name="parent">该节点的父目录。</param>
        /// <returns>读出的节点。</returns>
        private static VfsNode ReadNode(BinaryReader reader, VfsDirectory parent)
        {
            var type = (NodeType)reader.ReadByte();
            var name = reader.ReadString();
            var created = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var modified = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);

            if (type == NodeType.File)
            {
                var length = reader.ReadInt32();
                var data = reader.ReadBytes(length);
                var file = new VfsFile
                {
                    Name = name,
                    Parent = parent,
                    CreatedAt = created,
                    ModifiedAt = modified
                };
                file.WriteAllBytes(data);
                // WriteAllBytes 会把 ModifiedAt 刷成"现在"，这里再恢复成镜像里的时间。
                file.ModifiedAt = modified;
                return file;
            }

            var dir = new VfsDirectory
            {
                Name = name,
                Parent = parent,
                CreatedAt = created,
                ModifiedAt = modified
            };
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
                dir.Attach(ReadNode(reader, dir));
            return dir;
        }
    }
}
