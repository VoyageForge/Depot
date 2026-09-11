using System;
using System.Collections.Generic;
using UnityEngine.Scripting;

namespace VoyageForge.Depot.Runtime.Console
{
    /// <summary>
    /// 控制台命令抽象基类。
    ///
    /// 派生类只需实现 <see cref="Name"/> 与 <see cref="Execute"/>（可选重写
    /// <see cref="Description"/>、<see cref="Usage"/>、<see cref="GetArgumentCompletions"/>），
    /// 即可被 <see cref="RuntimeConsole"/> 通过反射自动发现并注册，无需手动调用任何注册方法。
    ///
    /// 重要：派生类必须标注 [Preserve]（<see cref="PreserveAttribute"/>），
    /// 防止 IL2CPP 构建时因“没有显式代码引用”而被代码剥离，否则反射扫描无法发现该命令。
    /// </summary>
    [Preserve]
    public abstract class ConsoleCommand
    {
        /// <summary>
        /// 命令名（用户在命令输入框敲入的关键字，不区分大小写）。
        /// 必须全局唯一，否则注册时后注册者会被跳过。
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// 命令的简短描述，用于 help 命令展示。默认返回空字符串。
        /// </summary>
        public virtual string Description => string.Empty;

        /// <summary>
        /// 命令用法（如 "log &lt;message&gt;"），用于 help 命令展示。默认等于命令名。
        /// </summary>
        public virtual string Usage => Name;

        /// <summary>
        /// 执行命令。
        /// </summary>
        /// <param name="args">空格分隔的参数数组（不含命令名本身，可能为空数组）。</param>
        public abstract void Execute(string[] args);

        /// <summary>
        /// 参数补全建议。当命令名已完整输入、用户正在输入参数时调用。
        /// 默认返回空（不提供参数补全）；派生类可重写以提供自定义参数补全提示。
        ///
        /// 例如命令为 "hello &lt;name&gt;"，用户输入 "hello T" 时，本方法会收到
        /// args 为空数组、currentInput 为 "T"，返回 ["Tom", "Tony"] 即可提示可选人名。
        /// 命令名未输入完整时不会调用本方法（该阶段只做命令名补全）。
        ///
        /// 注意：命令需要根据 args.Length 自行判断参数是否已填完；
        /// 所有参数都填完后应返回空，否则会持续提示（例如 "hello Tom " 仍提示人名）。
        /// </summary>
        /// <param name="args">已完成的参数（不含正在输入的部分，可能为空数组）。</param>
        /// <param name="currentInput">正在输入的参数片段（可能为空字符串）。</param>
        /// <returns>参数补全建议列表，可为空。</returns>
        public virtual IReadOnlyList<string> GetArgumentCompletions(string[] args, string currentInput)
        {
            return Array.Empty<string>();
        }
    }
}
