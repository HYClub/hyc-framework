// ============================================================
// HYC Framework - Editor
// 文件: Editor/GeneratedFolderMigration.cs
// 说明: 存量工程「生成代码旧目录 → 统一总根」一键搬迁。
//       生成代码曾散落 5 个目录（Assets/GeneratedConfigs、Assets/GeneratedUI、
//       Assets/HYC/Generated/Attributes、Assets/Talent/Generated、Assets/BTTrees/Nodes），
//       现已统一收口到 Assets/HYC/Generated/ 下按模块分文件夹（见 GeneratedPaths）。
//       本工具把这些旧目录里的生成文件搬到新结构，并同步更新 HYCSettings 里
//       仍指向旧默认的 OutputDir / EditorDir 设置值。
//       ⚠️ 只搬「框架旧默认目录」，用户自定义的输出目录不在迁移范围。
//       ⚠️ 逐文件搬迁且不覆盖：目标已存在同名文件时，内容相同去重、内容不同记冲突
//       （BT 自定义节点是半手写文件，绝不能静默覆盖）。
// ============================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HYC.Framework.Config.Editor;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Editor
{
    /// <summary>
    /// 生成代码旧目录迁移工具。菜单入口 <c>HYC Framework/Config/Migrate Generated Code</c>（仅在有旧目录残留时可用）。
    /// </summary>
    public static class GeneratedFolderMigration
    {
        /// <summary>旧目录 → 新目录映射表（顺序即搬迁顺序）。</summary>
        private static readonly (string legacy, string target)[] Pairs =
        {
            (ConfigTemplateCodeGen.LegacyOutputDir, GeneratedPaths.Config),
            ("Assets/GeneratedUI", GeneratedPaths.UI),
            ("Assets/HYC/Generated/Attributes", GeneratedPaths.Attr),
            ("Assets/Talent/Generated", GeneratedPaths.Talent),
            ("Assets/BTTrees/Nodes", GeneratedPaths.BTNodes),
        };

        /// <summary>是否存在可迁移的旧目录内容（菜单启用判据 / 设置窗口提示判据）。</summary>
        public static bool HasLegacyContent()
        {
            var root = Directory.GetCurrentDirectory();
            return Pairs.Any(p => Directory.Exists(Path.Combine(root, p.legacy))
                                  && Directory.GetFileSystemEntries(Path.Combine(root, p.legacy)).Length > 0);
        }

        /// <summary>
        /// 执行迁移：逐文件搬移 + 更新仍指向旧默认的 HYCSettings 设置值。
        /// </summary>
        /// <returns>人类可读的迁移报告（含冲突清单，冲突需用户手动处理）。</returns>
        public static string Migrate()
        {
            var root = Directory.GetCurrentDirectory();
            var report = new StringBuilder();
            var movedTotal = 0;
            var dedupTotal = 0;
            var conflicts = new List<string>();

            foreach (var (legacy, target) in Pairs)
            {
                var legacyAbs = Path.Combine(root, legacy);
                if (!Directory.Exists(legacyAbs))
                    continue;

                var moved = 0;
                var dedup = 0;
                // 含 .meta 一起搬（GUID 不变，资产引用不悬空）
                foreach (var src in Directory.GetFiles(legacyAbs, "*", SearchOption.AllDirectories))
                {
                    var rel = src.Substring(legacyAbs.Length + 1);
                    var dst = Path.Combine(root, target, rel);
                    var dstDir = Path.GetDirectoryName(dst);
                    if (!Directory.Exists(dstDir))
                        Directory.CreateDirectory(dstDir);

                    if (!File.Exists(dst))
                    {
                        File.Move(src, dst);
                        moved++;
                    }
                    else if (SameContent(src, dst))
                    {
                        File.Delete(src);   // 新旧内容一致 → 去重
                        dedup++;
                    }
                    else
                    {
                        // 内容不同 = 可能是用户改过的文件（如 BT 节点手写逻辑），记冲突不覆盖
                        conflicts.Add($"{legacy}/{rel}  →  已存在同名且内容不同，跳过");
                    }
                }

                if (moved + dedup > 0 && !Directory.GetFileSystemEntries(legacyAbs).Any())
                    Directory.Delete(legacyAbs, true);   // 搬空后删旧目录树

                if (moved + dedup > 0)
                    report.AppendLine($"· {legacy} → {target}：搬移 {moved} 个文件，去重 {dedup} 个");
                movedTotal += moved;
                dedupTotal += dedup;
            }

            // 设置值仍指向旧默认 → 同步更新（用户自定义目录不动）
            var settings = HYCSettings.Instance;
            var settingsChanged = false;
            if (settings.OutputDir == ConfigTemplateCodeGen.LegacyOutputDir)
            {
                settings.OutputDir = GeneratedPaths.Config;
                settingsChanged = true;
            }
            if (settings.EditorDir == ConfigTemplateCodeGen.LegacyOutputDir + "/Editor")
            {
                settings.EditorDir = GeneratedPaths.ConfigEditor;
                settingsChanged = true;
            }
            if (settingsChanged)
                report.AppendLine("· HYCSettings：OutputDir / EditorDir 已指向新目录");

            AssetDatabase.Refresh();

            if (movedTotal + dedupTotal == 0)
                report.AppendLine("没有可迁移的内容。");
            report.AppendLine();
            report.AppendLine("提示：迁移后建议跑一次 Regenerate All Config Code 验证新目录产物可编译；" +
                              "BT 自定义节点为逐字搬移（含手写修改），无需重新生成。");
            if (conflicts.Count > 0)
            {
                report.AppendLine();
                report.AppendLine($"⚠ {conflicts.Count} 个文件因目标已存在且内容不同被跳过（未覆盖，请手动处理）：");
                foreach (var c in conflicts.Take(20))
                    report.AppendLine("  - " + c);
                if (conflicts.Count > 20)
                    report.AppendLine($"  ...等共 {conflicts.Count} 个");
            }
            return report.ToString();
        }

        private static bool SameContent(string a, string b)
        {
            var fa = File.ReadAllBytes(a);
            var fb = File.ReadAllBytes(b);
            return fa.SequenceEqual(fb);
        }

        // ------------------------------------------------------------ 菜单入口

        [MenuItem("HYC Framework/Config/Migrate Generated Code", false, 8)]
        private static void MenuMigrate()
        {
            var dirs = string.Join("\n", Pairs
                .Where(p => Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), p.legacy)))
                .Select(p => "  " + p.legacy + "  →  " + p.target));
            if (!EditorUtility.DisplayDialog("迁移生成代码目录",
                    "将把以下旧目录里的生成文件搬到统一总根：\n" + dirs +
                    "\n\n已存在且内容不同的文件不会覆盖（会记入冲突报告）。继续？", "迁移", "取消"))
                return;

            var report = Migrate();
            EditorUtility.DisplayDialog("迁移完成", report, "确定");
            Debug.Log("[HYC] 生成代码目录迁移完成：\n" + report);
        }

        [MenuItem("HYC Framework/Config/Migrate Generated Code", true)]
        private static bool MenuMigrateValidate() => HasLegacyContent();
    }
}
