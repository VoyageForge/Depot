using System;

namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 虚拟节点树的深拷贝。
    ///
    /// 从 <see cref="VirtualFileSystem"/> 抽出来的原因：复制一棵子树
    /// 是纯粹的"数据结构克隆"问题，和文件系统的路径解析、权限、
    /// 父指针维护没有关系，独立出来后既能单测也能复用到别处。
    /// </summary>
    internal static class VfsNodeCloner
    {
        /// <summary>
        /// 深拷贝一个节点（含全部后代）；拷贝结果的 Parent 尚未设置，
        /// 由调用方负责挂到目标目录上。
        /// </summary>
        /// <param name="source">源节点。</param>
        /// <param name="name">拷贝后使用的新名字（移动/重命名式复制需要）。</param>
        /// <returns>拷贝出来的新节点。</returns>
        public static VfsNode Clone(VfsNode source, string name)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));

            // 文件：内容必须复制，否则两个节点会共享同一块逻辑内容。
            var file = source as VfsFile;
            if (file != null)
            {
                var newFile = new VfsFile { Name = name, CreatedAt = file.CreatedAt };
                newFile.WriteAllBytes(file.ReadAllBytes());
                return newFile;
            }

            // 目录：递归复制所有子节点，并修正子节点的 Parent 指向新目录。
            var dir = (VfsDirectory)source;
            var newDir = new VfsDirectory { Name = name, CreatedAt = dir.CreatedAt };
            foreach (var child in dir.Children)
            {
                var clonedChild = Clone(child, child.Name);
                clonedChild.Parent = newDir;
                newDir.Attach(clonedChild);
            }
            return newDir;
        }
    }
}
