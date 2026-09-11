using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using VoyageForge.Depot.Runtime.Console;

namespace VoyageForge.Depot.Tests
{
    /// <summary>
    /// 命令注册表测试：覆盖命令发现、注册、重名跳过、补全建议等纯逻辑。
    /// </summary>
    [TestFixture]
    public class ConsoleCommandRegistryTests
    {
        /// <summary>每个测试结束后清空注册表，避免测试间状态互相污染。</summary>
        [TearDown]
        public void TearDown()
        {
            ConsoleCommandRegistry.Clear();
        }

        /// <summary>扫描 Depot.Runtime 程序集，应发现内置的 clear / help / log 命令。</summary>
        [Test]
        public void DiscoverCommands_扫描运行时程序集_发现内置命令()
        {
            Assembly runtimeAssembly = typeof(LogCommand).Assembly;
            List<ConsoleCommand> discovered = ConsoleCommandRegistry.DiscoverCommands(new[] { runtimeAssembly });

            string[] names = discovered.Select(c => c.Name).ToArray();
            CollectionAssert.Contains(names, "clear");
            CollectionAssert.Contains(names, "help");
            CollectionAssert.Contains(names, "log");
            CollectionAssert.Contains(names, "exit");
        }

        /// <summary>扫描测试程序集，应发现测试用命令（验证自定义命令可被反射发现）。</summary>
        [Test]
        public void DiscoverCommands_扫描测试程序集_发现自定义命令()
        {
            Assembly testAssembly = typeof(ConsoleCommandRegistryTests).Assembly;
            List<ConsoleCommand> discovered = ConsoleCommandRegistry.DiscoverCommands(new[] { testAssembly });

            Assert.IsTrue(discovered.Any(c => c.Name == "test"));
        }

        /// <summary>注册命令后，应能按命令名查询到同一实例。</summary>
        [Test]
        public void RegisterAll_注册后_可按名查询()
        {
            TestCommand command = new TestCommand("hello");
            int registered = ConsoleCommandRegistry.RegisterAll(new ConsoleCommand[] { command });

            Assert.AreEqual(1, registered);
            Assert.IsTrue(ConsoleCommandRegistry.TryGetCommand("hello", out ConsoleCommand found));
            Assert.AreSame(command, found);
        }

        /// <summary>命令名重复时，后者应被跳过（保留先注册者）。</summary>
        [Test]
        public void RegisterAll_重名命令_后者被跳过()
        {
            TestCommand first = new TestCommand("dup");
            TestCommand second = new TestCommand("dup");

            int registered = ConsoleCommandRegistry.RegisterAll(new ConsoleCommand[] { first, second });

            Assert.AreEqual(1, registered);
            Assert.IsTrue(ConsoleCommandRegistry.TryGetCommand("dup", out ConsoleCommand found));
            Assert.AreSame(first, found);
        }

        /// <summary>命令名为空白时应被跳过，不污染命令表。</summary>
        [Test]
        public void RegisterAll_无效命令名_被跳过()
        {
            TestCommand invalid = new TestCommand("   ");
            int registered = ConsoleCommandRegistry.RegisterAll(new ConsoleCommand[] { invalid });

            Assert.AreEqual(0, registered);
            Assert.IsFalse(ConsoleCommandRegistry.TryGetCommand("   ", out ConsoleCommand _));
        }

        /// <summary>按前缀补全应返回所有匹配的命令名。</summary>
        [Test]
        public void GetCompletions_前缀匹配_返回匹配命令()
        {
            ConsoleCommandRegistry.RegisterAll(new ConsoleCommand[]
            {
                new TestCommand("clear"),
                new TestCommand("close"),
                new TestCommand("log")
            });

            IReadOnlyList<string> completions = ConsoleCommandRegistry.GetCompletions("cl");

            Assert.AreEqual(2, completions.Count);
            CollectionAssert.Contains(completions, "clear");
            CollectionAssert.Contains(completions, "close");
        }

        /// <summary>补全匹配应忽略命令名大小写。</summary>
        [Test]
        public void GetCompletions_忽略大小写()
        {
            ConsoleCommandRegistry.RegisterAll(new ConsoleCommand[] { new TestCommand("Clear") });

            IReadOnlyList<string> completions = ConsoleCommandRegistry.GetCompletions("cle");

            Assert.AreEqual(1, completions.Count);
            Assert.AreEqual("Clear", completions[0]);
        }

        /// <summary>无匹配前缀时应返回空列表。</summary>
        [Test]
        public void GetCompletions_无匹配_返回空()
        {
            ConsoleCommandRegistry.RegisterAll(new ConsoleCommand[] { new TestCommand("log") });

            IReadOnlyList<string> completions = ConsoleCommandRegistry.GetCompletions("zzz");

            Assert.IsEmpty(completions);
        }

        // ---- 参数解析（TryParseArgumentInput）----

        [Test]
        public void TryParseArgumentInput_单个参数_正确解析()
        {
            bool ok = ConsoleCommandRegistry.TryParseArgumentInput("hello Tom", out string cmd, out string[] args, out string current);

            Assert.IsTrue(ok);
            Assert.AreEqual("hello", cmd);
            Assert.IsEmpty(args);
            Assert.AreEqual("Tom", current);
        }

        [Test]
        public void TryParseArgumentInput_多参数_正确解析()
        {
            bool ok = ConsoleCommandRegistry.TryParseArgumentInput("login alice adm", out string cmd, out string[] args, out string current);

            Assert.IsTrue(ok);
            Assert.AreEqual("login", cmd);
            CollectionAssert.AreEqual(new[] { "alice" }, args);
            Assert.AreEqual("adm", current);
        }

        [Test]
        public void TryParseArgumentInput_末尾空格_当前参数为空()
        {
            bool ok = ConsoleCommandRegistry.TryParseArgumentInput("hello ", out string cmd, out string[] args, out string current);

            Assert.IsTrue(ok);
            Assert.AreEqual("hello", cmd);
            Assert.IsEmpty(args);
            Assert.AreEqual(string.Empty, current);
        }

        [Test]
        public void TryParseArgumentInput_无空格_返回false()
        {
            Assert.IsFalse(ConsoleCommandRegistry.TryParseArgumentInput("hello", out _, out _, out _));
        }

        [Test]
        public void TryParseArgumentInput_空输入_返回false()
        {
            Assert.IsFalse(ConsoleCommandRegistry.TryParseArgumentInput("", out _, out _, out _));
        }

        // ---- 命令参数补全（GetArgumentCompletions）----

        [Test]
        public void GetArgumentCompletions_第一个参数无提示_返回空()
        {
            ArgumentCompletionCommand command = new ArgumentCompletionCommand();

            Assert.IsEmpty(command.GetArgumentCompletions(new string[0], string.Empty));
        }

        [Test]
        public void GetArgumentCompletions_第二个参数_按前缀提示()
        {
            ArgumentCompletionCommand command = new ArgumentCompletionCommand();

            IReadOnlyList<string> completions = command.GetArgumentCompletions(new[] { "alice" }, "a");

            CollectionAssert.AreEqual(new[] { "admin" }, completions);
        }

        [Test]
        public void GetArgumentCompletions_参数填完_返回空()
        {
            ArgumentCompletionCommand command = new ArgumentCompletionCommand();

            Assert.IsEmpty(command.GetArgumentCompletions(new[] { "alice", "admin" }, string.Empty));
        }

        /// <summary>
        /// 测试用命令：带无参构造（供反射发现）与带参构造（供注册测试），无实际行为。
        /// </summary>
        public sealed class TestCommand : ConsoleCommand
        {
            private readonly string _name;

            /// <summary>默认无参构造，命令名为 "test"，供反射发现使用。</summary>
            public TestCommand() : this("test") { }

            /// <summary>带参构造，便于测试指定命令名。</summary>
            /// <param name="name">命令名。</param>
            public TestCommand(string name)
            {
                _name = name;
            }

            /// <inheritdoc />
            public override string Name => _name;

            /// <inheritdoc />
            public override void Execute(string[] args)
            {
                // 测试用命令，无需实际行为
            }
        }

        /// <summary>参数补全测试命令：第一个参数无提示，第二个参数提示角色，两个参数填完不再提示。</summary>
        private sealed class ArgumentCompletionCommand : ConsoleCommand
        {
            /// <inheritdoc />
            public override string Name => "argtest";

            /// <inheritdoc />
            public override void Execute(string[] args)
            {
                // 测试用命令，无需实际行为
            }

            /// <inheritdoc />
            public override IReadOnlyList<string> GetArgumentCompletions(string[] args, string currentInput)
            {
                // 第一个参数：无提示
                if (args.Length == 0)
                {
                    return Array.Empty<string>();
                }

                // 两个参数都填完：不再提示
                if (args.Length >= 2)
                {
                    return Array.Empty<string>();
                }

                // 第二个参数：按前缀提示角色
                return new[] { "admin", "user" }
                    .Where(r => r.StartsWith(currentInput, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }
    }
}
