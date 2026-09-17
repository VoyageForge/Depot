using System;
using System.Collections.Generic;

namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 文件 / 目录的公共基类：只描述"树里一个节点"的通用属性。
    ///
    /// 具体行为（能否存内容、能否有子节点）分别由
    /// <see cref="VfsFile"/> 和 <see cref="VfsDirectory"/> 承担，
    /// 这样基类不需要知道子类的存储细节。
    /// </summary>
    public abstract class VfsNode
    {
        /// <summary>节点名（单个段名，不含路径分隔符）。根目录为空串。</summary>
        public string Name { get; internal set; } = "";

        /// <summary>父目录；根目录的父目录为 null。</summary>
        public VfsDirectory Parent { get; internal set; }

        /// <summary>创建时间（UTC）。</summary>
        public DateTime CreatedAt { get; internal set; } = DateTime.UtcNow;

        /// <summary>最后修改时间（UTC）。</summary>
        public DateTime ModifiedAt { get; internal set; } = DateTime.UtcNow;

        /// <summary>节点类型。这是判断"路径是文件还是目录"的唯一权威依据。</summary>
        public abstract NodeType Type { get; }

        /// <summary>
        /// 绝对路径，如 /home/alice/a.txt。
        /// 由父链自底向上回溯拼出，因此移动节点后无需手动同步这个值。
        /// </summary>
        public string FullPath
        {
            get
            {
                if (Parent is null) return "/";

                var parts = new List<string>();
                for (VfsNode n = this; n != null && n.Parent != null; n = n.Parent)
                    parts.Add(n.Name);
                parts.Reverse();
                return "/" + string.Join("/", parts);
            }
        }

        /// <summary>
        /// 判断 node 是否是"自己或自己的后代"。
        /// 用于阻止把目录移动 / 复制到它自己的子树里（否则会形成环）。
        /// </summary>
        /// <param name="node">待判断的节点。</param>
        /// <returns>是自己或自己的后代返回 true。</returns>
        public bool IsSelfOrAncestorOf(VfsNode node)
        {
            for (var n = node; n != null; n = n.Parent)
                if (ReferenceEquals(n, this)) return true;
            return false;
        }

        /// <summary>输出形如 "File      /docs/readme.md" 的调试文本。</summary>
        public override string ToString() => $"{Type,-9} {FullPath}";
    }
}
