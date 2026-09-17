using System;
using System.IO;

namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 把"用户在地址栏里随手输入的东西"解析成【工程内的目录】。
    ///
    /// 这是文件浏览器最核心的一条规则：无论用户给的是绝对路径、相对路径、
    /// 文件路径还是目录路径，最终都要落到"Assets 下的某个文件夹"，
    /// 并且用统一的 "./xxx" 形式对外表达。
    ///
    /// 解析规则（与旧版 PathInputField.Analysis 的行为保持一致）：
    ///   ① 输入为空                  → 失败；
    ///   ② 输入不是绝对路径          → 以 projectRoot 为基准补成绝对路径；
    ///   ③ 目标是文件                → 取其【所在目录】（地址栏永远停在文件夹上）；
    ///   ④ 目标是目录                → 去掉尾随分隔符（"./" 需要干净的段名）；
    ///   ⑤ 目标不存在                → 失败；
    ///   ⑥ 结果不在 projectRoot 之下 → 失败（不允许跳出工程目录）。
    ///
    /// 本类不依赖 UnityEngine：工程根目录由调用方传入，
    /// 这样既能脱离 Unity 单测，也不会把"Assets 在哪"这种环境信息焊死在逻辑里。
    /// </summary>
    public static class ProjectPathResolver
    {
        /// <summary>工程内相对路径的统一前缀，例如 "./Assets/../Sub" 会被表达成 "./Sub"。</summary>
        public const string RelativePrefix = "./";

        /// <summary>表示"工程根目录自身"的相对路径写法。</summary>
        public const string RootRelativePath = "./";

        /// <summary>
        /// 尝试把用户输入解析为工程内的一个目录。
        /// </summary>
        /// <param name="input">用户输入的原始路径，可以是绝对或相对。</param>
        /// <param name="projectRoot">工程根目录（通常是 Application.dataPath），必须是绝对路径。</param>
        /// <param name="absoluteFolder">成功时输出解析出的绝对目录路径。</param>
        /// <param name="relativePath">
        /// 成功时输出面向 UI 的相对路径：根目录为 "./"，其余为 "./子目录"。
        /// </param>
        /// <returns>解析成功返回 true。</returns>
        public static bool TryResolve(string input, string projectRoot,
                                      out string absoluteFolder, out string relativePath)
        {
            absoluteFolder = "";
            relativePath = "";

            if (string.IsNullOrWhiteSpace(input)) return false;
            if (string.IsNullOrWhiteSpace(projectRoot)) return false;

            var target = input.Trim();

            // ② 相对路径先补成绝对路径，后续处理只面对一种形态。
            if (!DiskPath.IsFullyAbsolute(target))
                target = Path.Combine(projectRoot, target);

            // 规范和类型判定：GetFullPath 会在路径非法时抛异常，
            // 这里直接兜住，把"用户乱输"统一表达为"解析失败"。
            try
            {
                target = Path.GetFullPath(target);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (PathTooLongException)
            {
                return false;
            }

            // ③④⑤ 按真实类型调整到"目录"语义。
            switch (DiskPath.GetKind(target))
            {
                case PathKind.NotFound:
                    return false;

                case PathKind.Directory:
                    // 去掉尾随分隔符，保证后面拼出来的相对路径没有多余的分隔符。
                    target = target.TrimEnd(Path.DirectorySeparatorChar,
                                            Path.AltDirectorySeparatorChar);
                    break;

                case PathKind.File:
                    // 输入的是文件时，地址栏回退到它所在的目录。
                    target = Path.GetDirectoryName(target);
                    if (string.IsNullOrEmpty(target)) return false;
                    break;

                default:
                    return false;
            }

            // ⑥ 必须落在工程根目录之下。
            string relative;
            if (!DiskPath.IsSubPathOf(target, projectRoot, out relative))
                return false;

            // 把系统分隔符统一成 '/'，让 UI 拿到跨平台一致的写法。
            var webPath = relative.Replace(Path.DirectorySeparatorChar,
                                           Path.AltDirectorySeparatorChar);

            absoluteFolder = target;
            relativePath = webPath == "."
                ? RootRelativePath
                : RelativePrefix + webPath;
            return true;
        }
    }
}
