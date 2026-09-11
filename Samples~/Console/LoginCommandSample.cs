using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Scripting;
using VoyageForge.Depot.Runtime.Console;

namespace VoyageForge.Depot.Samples.Console
{
    /// <summary>
    /// 参数补全示例（复杂）：login &lt;name&gt; &lt;role&gt;。
    ///
    /// 第一个参数 name 不提供补全提示（自由输入）；第二个参数 role 提供固定角色提示。
    /// 展示“某个参数无提示、后续参数有提示”的场景。
    /// </summary>
    [Preserve]
    public sealed class LoginCommandSample : ConsoleCommand
    {
        // 可选的角色列表
        private static readonly string[] Roles = { "admin", "user", "guest" };

        /// <inheritdoc />
        public override string Name => "login";

        /// <inheritdoc />
        public override string Usage => "login <name> <role>";

        /// <inheritdoc />
        public override string Description => "登录（参数补全复杂示例：第一个参数无提示，第二个参数有提示）";

        /// <inheritdoc />
        public override void Execute(string[] args)
        {
            string name = args.Length > 0 ? args[0] : "anonymous";
            string role = args.Length > 1 ? args[1] : "user";
            Debug.Log($"[Login] {name} 以 {role} 身份登录");
        }

        /// <summary>
        /// 复杂参数补全：第一个参数不提示，第二个参数提供固定角色提示。
        /// </summary>
        /// <param name="args">已完成的参数。空数组表示正在输入第一个参数（name）。</param>
        /// <param name="currentInput">正在输入的参数片段。</param>
        /// <returns>匹配的补全建议列表。</returns>
        public override IReadOnlyList<string> GetArgumentCompletions(string[] args, string currentInput)
        {
            // 第一个参数（name）：不提供提示，返回空（自由输入）
            if (args.Length == 0)
            {
                return Array.Empty<string>();
            }

            // 两个参数都填完后（args.Length >= 2）不再提示
            if (args.Length >= 2)
            {
                return Array.Empty<string>();
            }

            // 第二个参数（role）：提供固定角色提示
            return Roles
                .Where(r => r.StartsWith(currentInput, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
