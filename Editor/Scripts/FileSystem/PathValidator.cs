using System.Collections.Generic;

namespace VoyageForge.Depot.Editor.FileSystem
{
    /// <summary>
    /// 路径段的合法性校验规则（只负责"单个段名"）。
    ///
    /// 设计说明：
    ///   校验被拆成两层——
    ///     ① 本类负责【单个段名】的规则（长度、字符、保留名……）；
    ///     ② <see cref="VfsPath"/> 负责【整条路径】的结构（拆段、处理 "." / ".."、越界判断）。
    ///   这样分层的好处是：规则集中、容易单测、容易按平台替换。
    /// </summary>
    public static class PathValidator
    {
        /// <summary>整条路径允许的最大字符数（对齐 Windows 的常见限制）。</summary>
        public const int MaxPathLength = 4096;

        /// <summary>单个目录名 / 文件名允许的最大字符数。</summary>
        public const int MaxSegmentLength = 255;

        /// <summary>
        /// 不允许出现在段名里的字符。
        /// 注意：'/' 和 '\' 不在此列，因为它们在拆段阶段就已经被当作分隔符切掉了。
        /// </summary>
        private static readonly char[] s_illegalChars =
            { '\0', '<', '>', ':', '"', '|', '?', '*' };

        /// <summary>
        /// 系统保留设备名。
        /// 在 Windows 上，"CON"、"NUL" 等名字即使带扩展名（CON.txt）也无法创建，
        /// 所以在虚拟层里直接一并禁止，避免跨平台行为不一致。
        ///
        /// <c>CONIN$</c> / <c>CONOUT$</c> 是控制台输入 / 输出的设备名，
        /// 同样属于"写出来像普通文件、实际上会落到设备上"的名字
        /// （Go 的 os.Root 文档也专门点名过 CONOUT$），所以一并禁止。
        ///
        /// ⚠ 这里刻意【不】跟虚拟路径空间一起改成区分大小写：
        ///   本集合里的名字是"Windows 设备名"，而 Windows 判断设备名是不区分大小写的
        ///   （con、CON、Con 都落到同一个设备）。若改成 Ordinal，
        ///   "con.txt" 就会被放行，可它在 Windows 上根本创建不出来——
        ///   那等于把问题从"提前报错"推迟成"运行期诡异失败"。
        /// </summary>
        private static readonly HashSet<string> s_reservedNames =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5",
                "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
                "LPT6", "LPT7", "LPT8", "LPT9",
                "CONIN$", "CONOUT$",
            };

        /// <summary>
        /// 校验【单个路径段】（即一个目录名或文件名，不含分隔符）是否合法。
        ///
        /// 调用约定：
        ///   · 传入的 segment 必须是已经用 '/' 或 '\' 拆好的单段文本；
        ///   · 返回 false 时，通过 out 参数 error 告知具体是哪种违规；
        ///   · 返回 true 时，error 一定是 <see cref="PathError.None"/>。
        ///
        /// 为什么要把"单段校验"单独拆出来？
        ///   ① 拼接路径时可以逐段校验，出错能精确定位到"是哪一段坏了"；
        ///   ② 规则集中在一处，方便按平台替换（比如 Linux 可以放宽一些规则）；
        ///   ③ 便于单元测试，不用构造整棵树就能验证规则。
        /// </summary>
        /// <param name="segment">单个目录名或文件名，例如 "readme.txt"、"images"。</param>
        /// <param name="error">失败时输出具体原因；成功时为 <see cref="PathError.None"/>。</param>
        /// <returns>合法返回 true，非法返回 false。</returns>
        public static bool IsLegalSegment(string segment, out PathError error)
        {
            // 先假定"没有错误"：后面任何一条规则命中时会在返回前改成对应错误码，
            // 这样调用方只要看返回值就知道该不该读 error。
            error = PathError.None;

            // 判断 1：空段直接放行（不算错误）。
            //   拆分路径时，"//" 或开头/结尾的 "/" 都会产生空段，这些空段是"写法冗余"，
            //   不是"非法名字"，应由上层过滤掉。例如 "/a//b" 拆出来是 ["", "a", "", "b"]，
            //   中间那两个空段不应该让整条路径失败。
            if (segment.Length == 0) return true;

            // 判断 2："." 和 ".." 直接放行（不算错误）。
            //   它们是"相对路径记号"，不是真实的目录名：语义（向上、停留）需要在
            //   【整条路径的层级栈】上处理，放在 VfsPath.TryParse 里做弹栈/忽略。
            //   如果这里直接判非法，那么 "/a/../b" 这种正常路径就会被拒绝。
            if (segment == "." || segment == "..") return true;

            // 判断 3：段名长度不能超过 MaxSegmentLength。
            //   大多数文件系统对单个目录项名字有长度上限（NTFS/ext4 都是 255），
            //   与其等到写入时被截断或报错，不如在入口处就拒绝，错误更早、更明确。
            if (segment.Length > MaxSegmentLength)
            { error = PathError.SegmentTooLong; return false; }

            // 判断 4：段名中不能包含非法字符。
            //   '\0' NUL 字符会截断底层 C 字符串；< > : " | ? * 是 Windows 保留字符。
            //   注意：'/' 和 '\' 不在这里判，因为它们在拆段阶段就已经被切掉了，
            //         不会以"段内字符"的形式出现在 segment 里。
            if (segment.IndexOfAny(s_illegalChars) >= 0)
            { error = PathError.IllegalChar; return false; }

            // 判断 5：段名不能以【空格】或【英文句点】结尾。
            //   为什么要禁止"以空格结尾"？Windows 在创建文件时会静默把结尾的空格去掉，
            //   于是 "a " 和 "a" 实际上会落到同一个文件上，导致"写的时候叫 a，读的时候找不到"。
            //   为什么要禁止"以点结尾"？同理，"a." 和 "a" 落到同一个文件；
            //   另外 ".." 之外的多点结尾在跨平台时行为不一致，一刀切禁止最省心。
            //   前面已经排除空串，所以 segment[^1] 不会越界。
            if (segment[^1] == ' ' || segment[^1] == '.')
            { error = PathError.TrailingSpaceOrDot; return false; }

            // 判断 6：段名的"主名"（第一个点之前的部分）不能是系统保留设备名。
            //   Windows 上的 CON、PRN、AUX、NUL、COM1~COM9、LPT1~LPT9 被当作"设备"而非普通文件，
            //   哪怕写成 "CON.txt" 依然会落到控制台设备上，行为完全出乎意料。
            //   先截到"主名"再比较，就能把 "CON.txt"、"NUL.log"、"CON.a.b.c" 一并拦住。
            //   大小写不敏感："con"、"CON"、"Con" 都视作同一个设备。
            //   示例：
            //     "CON"        → dotIndex = -1，stem = "CON"   → 命中
            //     "CON.txt"    → dotIndex = 3， stem = "CON"   → 命中
            //     ".gitignore" → dotIndex = 0， stem = ""      → 不命中（隐藏文件）
            //     "CONX"       → dotIndex = -1，stem = "CONX"  → 不命中
            var dotIndex = segment.IndexOf('.');
            // 没有点 → 整段就是主名；有点 → 取点之前的部分作为主名
            var stem = dotIndex < 0 ? segment : segment[..dotIndex];
            if (s_reservedNames.Contains(stem))
            { error = PathError.ReservedName; return false; }

            // 所有规则都通过：这是一个合法的路径段。
            return true;
        }

        /// <summary>把错误码翻译成给用户看的中文说明。</summary>
        /// <param name="error">要翻译的错误码。</param>
        /// <returns>面向用户的中文描述。</returns>
        public static string Describe(PathError error) => error switch
        {
            PathError.None               => "路径合法",
            PathError.Empty              => "路径为空或只包含空白字符",
            PathError.TooLong            => $"路径总长度超过 {MaxPathLength} 个字符",
            PathError.SegmentTooLong     => $"目录名或文件名超过 {MaxSegmentLength} 个字符",
            PathError.IllegalChar        => "路径中包含非法字符（< > : \" | ? * 或 NUL 字符）",
            PathError.ReservedName       => "使用了系统保留设备名（如 CON、NUL、COM1）",
            PathError.TrailingSpaceOrDot => "目录名或文件名不能以空格或英文句点结尾",
            PathError.EscapeRoot         => "\"..\" 层级过多，已越过根目录",
            PathError.OutsideRoot        => "路径落在根目录之外",
            PathError.NoRootPath         => "当前文件系统没有指定根目录路径，无法换算真实路径",
            PathError.InvalidSegment     => "拼接的片段中包含非法名字",
            _                            => "未知错误",
        };
    }
}
