using System.Collections.Generic;

namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 目录节点，内部用字典保存子节点。
    ///
    /// 查找与增删都标记为 internal：外部只能通过
    /// <see cref="VirtualFileSystem"/> 的接口操作目录结构，
    /// 从而保证"改动结构时同步维护父指针和修改时间"这条不变量不被绕过。
    /// </summary>
    public sealed class VfsDirectory : VfsNode
    {
        /// <summary>
        /// 子节点表，按【区分大小写】比较（<see cref="System.StringComparer.Ordinal"/>）。
        ///
        /// 为什么刻意区分大小写？虚拟路径空间是我们自己定规则的地方，
        /// 区分大小写能让行为在 Windows / Linux / macOS 上完全一致，
        /// 不会出现"同一份代码在 Windows 上能查到、到 Linux 上查不到"的差异。
        /// （Zio 的 SubFileSystem 同样默认按 Ordinal 比较。）
        ///
        /// 代价要知道：Windows 的磁盘本身不区分大小写，所以真实世界里的
        /// "同一个文件"在本树里可能对应两个不同节点（"A.txt" 与 "a.txt"）。
        /// 因为本类只做内存中的路径与节点管理、不与磁盘做一致性校验，
        /// 这个代价是良性的。
        /// </summary>
        private readonly Dictionary<string, VfsNode> _children =
            new Dictionary<string, VfsNode>(System.StringComparer.Ordinal);

        /// <summary>固定为 <see cref="NodeType.Directory"/>。</summary>
        public override NodeType Type => NodeType.Directory;

        /// <summary>直接子节点数量。</summary>
        public int Count => _children.Count;

        /// <summary>直接子节点集合（只读视图）。</summary>
        public IReadOnlyCollection<VfsNode> Children => _children.Values;

        /// <summary>按名字查找直接子节点；找不到返回 null。</summary>
        /// <param name="name">子节点名。</param>
        /// <returns>找到的节点，或 null。</returns>
        internal VfsNode Find(string name)
        {
            VfsNode node;
            return _children.TryGetValue(name, out node) ? node : null;
        }

        /// <summary>挂上一个子节点（同名会覆盖，调用方需自行保证不重复）。</summary>
        /// <param name="node">要挂上的节点，其 Name 已设置。</param>
        internal void Attach(VfsNode node) => _children[node.Name] = node;

        /// <summary>按名字摘掉一个子节点。</summary>
        /// <param name="name">要移除的子节点名。</param>
        /// <returns>确实移除了返回 true。</returns>
        internal bool Detach(string name) => _children.Remove(name);
    }
}
