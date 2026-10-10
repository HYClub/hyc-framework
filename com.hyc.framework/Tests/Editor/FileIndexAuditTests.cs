using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// 索引文档自检：<c>com.hyc.framework/Documentation~/100-文件与类索引.md</c> 声称的文件数与类型数，
    /// 必须等于实际扫描结果。索引是这个仓库的"事实登记表"，一旦与磁盘脱节，
    /// 查类就会按它给的路径去，而那条路径可能已经不存在了——这是最隐蔽的一类文档腐化。
    ///
    /// <para><b>为什么这个测试不依赖 Unity 编译环境</b>：它只做文本解析与目录扫描，
    /// 不碰任何 UnityEditor API。因此即使 CI 环境受限（例如拿不到 Unity 许可），
    /// 它仍能在纯 .NET 环境下作为兜底闸门运行。</para>
    ///
    /// <para><b>范围</b>：只统计参与编译的三个目录（Runtime / Editor / Tests），
    /// <b>不含</b> <c>Samples~/</c>（带 <c>~</c> 不被 Unity 编译）。
    /// 注意：Unity 只为"类名 = .cs 文件名"的类型生成 MonoScript，
    /// 因此文件索引按 .cs 计，类型索引按声明计，两者本就不该相等。</para>
    /// </summary>
    public static class FileIndexAudit
    {
        /// <summary>索引文档路径（相对仓库根）。</summary>
        public const string IndexPath = "com.hyc.framework/Documentation~/100-文件与类索引.md";

        /// <summary>包目录名（仓库根下的包）。</summary>
        public const string PackageDir = "com.hyc.framework";

        /// <summary>参与编译的目录。不含 Samples~。</summary>
        public static readonly string[] CompiledDirs = { "Runtime", "Editor", "Tests" };

        /// <summary>一条不一致的发现。</summary>
        public readonly struct Issue
        {
            public readonly string Kind;
            public readonly string Message;

            public Issue(string kind, string message)
            {
                Kind = kind;
                Message = message;
            }

            public override string ToString() => $"[{Kind}] {Message}";
        }

        /// <summary>定位仓库根：从当前工作目录向上找含 com.hyc.framework 的目录。</summary>
        public static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, PackageDir)))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>统计参与编译的 .cs 文件数（Samples~ 不计）。</summary>
        public static int CountScriptFiles(string repoRoot)
        {
            var n = 0;
            foreach (var rel in CompiledDirs)
            {
                var dir = Path.Combine(repoRoot, PackageDir, rel);
                if (!Directory.Exists(dir)) continue;
                n += Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Length;
            }
            return n;
        }

        /// <summary>
        /// 统计类型声明数（class / struct / interface / enum / delegate）。
        /// 剥离注释与字符串字面量后再匹配，避免注释里的 <c>class Foo</c> 被计入。
        /// </summary>
        public static int CountTypeDeclarations(string repoRoot)
        {
            var n = 0;
            var re = new Regex(@"\b(class|struct|interface|enum|delegate)\s+[A-Za-z_][A-Za-z0-9_]*");
            foreach (var rel in CompiledDirs)
            {
                var dir = Path.Combine(repoRoot, PackageDir, rel);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                    n += CountInFile(File.ReadAllText(file), re);
            }
            return n;
        }

        /// <summary>剥离注释与字符串字面量后统计类型声明数。</summary>
        public static int CountInFile(string text, Regex re)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            var i = 0;
            var n = text.Length;
            while (i < n)
            {
                var c = text[i];
                var next = i + 1 < n ? text[i + 1] : '\0';

                if (c == '/' && next == '/')
                {
                    while (i < n && text[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/')) i++;
                    i += 2;
                    continue;
                }
                // 逐字字符串 @"..."
                if (c == '@' && next == '"')
                {
                    i += 2;
                    while (i < n)
                    {
                        if (text[i] == '"' && i + 1 < n && text[i + 1] == '"') { i += 2; continue; }
                        if (text[i] == '"') { i++; break; }
                        i++;
                    }
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    var quote = c;
                    i++;
                    while (i < n)
                    {
                        if (text[i] == '\\') { i += 2; continue; }
                        if (text[i] == quote) { i++; break; }
                        if (text[i] == '\n') break;
                        i++;
                    }
                    continue;
                }

                sb.Append(c);
                i++;
            }
            return re.Matches(sb.ToString()).Count;
        }

        /// <summary>解析索引文档声明的统计数字。</summary>
        public static bool TryReadDeclaredCounts(string indexText, out int files, out int types)
        {
            files = types = 0;
            var m = Regex.Match(indexText,
                @"\*\*(\d+)\s*个\s*`\.cs`\s*文件\*\*.*?\*\*(\d+)\s*个类型声明\*\*",
                RegexOptions.Singleline);
            if (!m.Success) return false;
            files = int.Parse(m.Groups[1].Value);
            types = int.Parse(m.Groups[2].Value);
            return true;
        }

        /// <summary>执行全部检查，返回发现的全部问题（空列表 = 全部一致）。</summary>
        public static List<Issue> Audit()
        {
            var issues = new List<Issue>();

            var repoRoot = FindRepoRoot();
            if (repoRoot == null)
            {
                issues.Add(new Issue("环境", $"未能从当前目录向上找到含 {PackageDir} 的仓库根（cwd={Directory.GetCurrentDirectory()}）"));
                return issues;
            }

            var indexPath = Path.Combine(repoRoot, IndexPath);
            if (!File.Exists(indexPath))
            {
                issues.Add(new Issue("缺失", $"找不到索引文档：{IndexPath}"));
                return issues;
            }

            var indexText = File.ReadAllText(indexPath);

            if (!TryReadDeclaredCounts(indexText, out var declaredFiles, out var declaredTypes))
            {
                issues.Add(new Issue("格式", $"{IndexPath} 的统计声明行格式已变，无法解析。请更新本测试的解析正则。"));
                return issues;
            }

            var actualFiles = CountScriptFiles(repoRoot);
            var actualTypes = CountTypeDeclarations(repoRoot);

            if (actualFiles != declaredFiles)
                issues.Add(new Issue("文件数", $"声明 {declaredFiles}，实际 {actualFiles}（差 {actualFiles - declaredFiles:+#;-#}）。新增/删除 .cs 后请同步 {IndexPath}"));
            if (actualTypes != declaredTypes)
                issues.Add(new Issue("类型数", $"声明 {declaredTypes}，实际 {actualTypes}（差 {actualTypes - declaredTypes:+#;-#}）。新增/删除类型后请同步 {IndexPath}"));

            // 分组标题之和也必须等于总声明数，否则索引内部就自相矛盾了
            var groups = Regex.Matches(indexText, @"###\s+`[^`]+`（(\d+)\s*个文件）");
            var sum = 0;
            foreach (Match g in groups) sum += int.Parse(g.Groups[1].Value);
            if (groups.Count > 0 && sum != declaredFiles)
                issues.Add(new Issue("分组", $"{groups.Count} 个分组标题的文件数之和为 {sum}，与总声明 {declaredFiles} 不符"));

            return issues;
        }

        /// <summary>人类可读的审计报告（供编辑器菜单调用）。</summary>
        public static string Report()
        {
            var issues = Audit();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[索引自检] com.hyc.framework/Documentation~/100-文件与类索引.md");
            if (issues.Count == 0)
            {
                sb.AppendLine("  结果：全部一致 ✓");
                return sb.ToString();
            }

            sb.AppendLine($"  发现 {issues.Count} 个问题：");
            foreach (var it in issues) sb.AppendLine("  - " + it);
            return sb.ToString();
        }
    }

    /// <summary>索引文档与磁盘的一致性测试。</summary>
    public class FileIndexAuditTests
    {
        [Test]
        public void 索引声明的文件数与类型数_与磁盘一致()
        {
            var issues = FileIndexAudit.Audit();
            Assert.IsEmpty(issues, "索引文档与实际不一致：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 分组标题之和_等于总声明文件数()
        {
            // Audit 已覆盖，这里单独断言一次，便于失败时定位到具体环节
            var issues = FileIndexAudit.Audit();
            Assert.IsFalse(
                issues.Exists(i => i.Kind == "分组"),
                "分组标题的文件数之和与总声明不符");
        }

        [Test]
        public void Samples目录_不参与编译故不计入()
        {
            var repoRoot = FileIndexAudit.FindRepoRoot();
            Assert.IsNotNull(repoRoot, "未找到仓库根");

            var samples = Path.Combine(repoRoot, FileIndexAudit.PackageDir, "Samples~");
            if (!Directory.Exists(samples)) return; // 未检出该目录时跳过

            var sampleCs = Directory.GetFiles(samples, "*.cs", SearchOption.AllDirectories).Length;
            Assert.Greater(sampleCs, 0, "Samples~ 应含示例代码（带 ~ 不被 Unity 编译）");

            // 断言它们确实没被计入
            var inSamples = FileIndexAudit.CompiledDirs.Contains("Samples~");
            Assert.IsFalse(inSamples, "Samples~ 带 ~ 尾缀，不应出现在 CompiledDirs 里");
        }
    }
}