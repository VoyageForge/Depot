using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VoyageForge.Depot.Runtime.Console
{
    /// <summary>
    /// 控制台命令注册表（静态）。
    ///
    /// 职责：
    /// 1. 存储已注册的命令（命令名 -> 命令实例）；
    /// 2. 通过反射发现程序集中的 <see cref="ConsoleCommand"/> 派生类（纯逻辑，可在后台线程执行）；
    /// 3. 提供命令查询与补全建议。
    ///
    /// 线程约定：<see cref="DiscoverCommands"/> 与 <see cref="GetScanAssemblies"/> 只读、可在线程中调用；
    /// 其余读写方法（注册、查询、补全）仅应在主线程调用。
    /// </summary>
    public static class ConsoleCommandRegistry
    {
        // 命令表：命令名 -> 命令实例，使用不区分大小写的比较器
        private static readonly Dictionary<string, ConsoleCommand> Commands =
            new Dictionary<string, ConsoleCommand>(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前已注册的所有命令实例。</summary>
        public static IReadOnlyCollection<ConsoleCommand> RegisteredCommands => Commands.Values;

        /// <summary>
        /// 扫描给定程序集，发现其中所有可实例化的 <see cref="ConsoleCommand"/> 非抽象派生类。
        /// 纯函数：不修改命令表，可在后台线程执行。
        /// </summary>
        /// <param name="assemblies">待扫描的程序集集合。</param>
        /// <returns>发现并实例化出的命令列表。</returns>
        public static List<ConsoleCommand> DiscoverCommands(IEnumerable<Assembly> assemblies)
        {
            List<ConsoleCommand> found = new List<ConsoleCommand>();

            foreach (Assembly assembly in assemblies)
            {
                Type[] types;

                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // 部分类型加载失败时，退回到成功加载的类型子集（其中可能包含 null）
                    types = ex.Types;
                    Debug.LogWarning($"[ConsoleCommandRegistry] 程序集 {assembly.GetName().Name} 部分类型加载失败：{ex.Message}");
                }

                foreach (Type type in types)
                {
                    // 跳过 null 以及非命令类型（抽象类、非 ConsoleCommand 派生类）
                    if (type == null ||
                        type.IsAbstract ||
                        !typeof(ConsoleCommand).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    try
                    {
                        // 命令类必须提供无参构造函数，否则无法自动实例化
                        found.Add((ConsoleCommand)Activator.CreateInstance(type));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ConsoleCommandRegistry] 无法实例化命令类型 {type.Name}：{ex.Message}");
                    }
                }
            }

            return found;
        }

        /// <summary>把一批命令注册到命令表，返回成功注册的数量（无效或重名会被跳过）。</summary>
        /// <param name="commands">待注册的命令。</param>
        /// <returns>成功注册的命令数量。</returns>
        public static int RegisterAll(IEnumerable<ConsoleCommand> commands)
        {
            int registered = 0;

            foreach (ConsoleCommand command in commands)
            {
                // 命令实例或命令名无效时跳过
                if (command == null || string.IsNullOrWhiteSpace(command.Name))
                {
                    continue;
                }

                // 命令名重复时跳过，保证命令表干净、可预期
                if (Commands.ContainsKey(command.Name))
                {
                    Debug.LogWarning($"[ConsoleCommandRegistry] 命令名 '{command.Name}' 已存在，重复注册被跳过。");
                    continue;
                }

                Commands[command.Name] = command;
                registered++;
            }

            return registered;
        }

        /// <summary>清空命令表（主要用于测试隔离，或运行时需要重置注册状态时）。</summary>
        public static void Clear()
        {
            Commands.Clear();
        }

        /// <summary>按命令名查询命令（不区分大小写）。</summary>
        /// <param name="name">命令名。</param>
        /// <param name="command">命中的命令实例。</param>
        /// <returns>是否找到。</returns>
        public static bool TryGetCommand(string name, out ConsoleCommand command)
        {
            return Commands.TryGetValue(name, out command);
        }

        /// <summary>返回以给定前缀开头的命令名（忽略大小写），用于命令输入补全。</summary>
        /// <param name="prefix">输入前缀。</param>
        /// <param name="maxResults">最多返回条数。</param>
        /// <returns>匹配的命令名列表（按名称排序）。</returns>
        public static IReadOnlyList<string> GetCompletions(string prefix, int maxResults = 8)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return Array.Empty<string>();
            }

            List<string> matches = new List<string>();

            foreach (string name in Commands.Keys)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(name);
                }

                // 达到上限即可停止，避免无谓遍历
                if (matches.Count >= maxResults)
                {
                    break;
                }
            }

            matches.Sort(StringComparer.OrdinalIgnoreCase);
            return matches;
        }

        /// <summary>
        /// 解析命令输入：把 "hello Tom" 拆成命令名 "hello"、已完成参数 args、正在输入的参数 currentInput。
        /// 用于参数补全。仅当输入含空格（已进入参数阶段）时返回 true。
        /// </summary>
        /// <param name="text">输入框当前内容。</param>
        /// <param name="commandName">解析出的命令名。</param>
        /// <param name="args">已完成的参数（不含正在输入的部分）。</param>
        /// <param name="currentInput">正在输入的参数片段（可能为空字符串）。</param>
        /// <returns>是否成功解析（输入含空格）。</returns>
        public static bool TryParseArgumentInput(string text, out string commandName, out string[] args, out string currentInput)
        {
            commandName = null;
            args = Array.Empty<string>();
            currentInput = string.Empty;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] parts = text.Split(' ');
            if (parts.Length < 2)
            {
                return false;
            }

            commandName = parts[0];
            currentInput = parts[parts.Length - 1];
            args = new string[parts.Length - 2];
            Array.Copy(parts, 1, args, 0, args.Length);
            return true;
        }

        /// <summary>返回当前已加载的、需要扫描的程序集（已过滤 Unity/.NET 基础程序集）。</summary>
        /// <returns>需要扫描的程序集数组。</returns>
        public static Assembly[] GetScanAssemblies()
        {
            List<Assembly> result = new List<Assembly>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IsIgnoredAssembly(assembly))
                {
                    result.Add(assembly);
                }
            }

            return result.ToArray();
        }

        /// <summary>判断一个程序集是否为 Unity/.NET 基础程序集，这类程序集不含用户命令，可跳过扫描。</summary>
        /// <param name="assembly">待判断的程序集。</param>
        /// <returns>是否应跳过扫描。</returns>
        public static bool IsIgnoredAssembly(Assembly assembly)
        {
            string name = assembly.GetName().Name ?? string.Empty;

            return name.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                   name.StartsWith("UnityEditor", StringComparison.Ordinal) ||
                   name.StartsWith("Unity.", StringComparison.Ordinal) ||
                   name.StartsWith("System", StringComparison.Ordinal) ||
                   name.StartsWith("mscorlib", StringComparison.Ordinal) ||
                   name.StartsWith("netstandard", StringComparison.Ordinal) ||
                   name.StartsWith("Mono.", StringComparison.Ordinal) ||
                   name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                   name.StartsWith("nunit", StringComparison.Ordinal);
        }
    }
}
