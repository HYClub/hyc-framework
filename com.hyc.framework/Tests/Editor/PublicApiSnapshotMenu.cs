using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// 公开 API 快照维护菜单。放在 <c>Tests/</c> 下而不是 <c>HYC Framework/</c> 下，
    /// 是刻意区分：前 23 个框架菜单是给使用方的功能入口，这一个是框架开发者的自检工具。
    /// </summary>
    public static class PublicApiSnapshotMenu
    {
        [MenuItem("Tools/Tests/Update Public API Snapshot")]
        private static void UpdateSnapshot()
        {
            var changed = PublicApiSnapshot.Write();
            Debug.Log(changed
                ? $"[API 快照] 已更新 {PublicApiSnapshot.SnapshotPath} —— 请 git add 该文件。"
                : $"[API 快照] 内容无变化，无需更新（{PublicApiSnapshot.SnapshotPath}）。");
        }

        [MenuItem("Tools/Tests/Show Public API Diff")]
        private static void ShowDiff()
        {
            var abs = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), PublicApiSnapshot.SnapshotPath);
            if (!System.IO.File.Exists(abs))
            {
                EditorUtility.DisplayDialog("Public API 快照", $"快照不存在：\n{PublicApiSnapshot.SnapshotPath}\n\n执行「Tools/Tests/Update Public API Snapshot」以生成。", "好");
                return;
            }

            var expected = System.IO.File.ReadAllText(abs);
            var actual = PublicApiSnapshot.Build();
            if (expected == actual)
            {
                EditorUtility.DisplayDialog("Public API 快照", "当前公开 API 与快照一致，无差异。", "好");
                return;
            }

            // 只报差异行（逐行 set 差集）。刻意不引入外部 diff 工具 / RevealInFinder：
            // 那两条路径在本框架里零使用，未经验证，不如把差异直接打进 Console 可靠。
            var expLines = new System.Collections.Generic.HashSet<string>(
                expected.Split('\n'), System.StringComparer.Ordinal);
            var actLines = new System.Collections.Generic.HashSet<string>(
                actual.Split('\n'), System.StringComparer.Ordinal);

            var removed = new System.Collections.Generic.List<string>();
            var added = new System.Collections.Generic.List<string>();
            foreach (var l in expLines) if (l.Trim().Length > 0 && !actLines.Contains(l)) removed.Add(l);
            foreach (var l in actLines) if (l.Trim().Length > 0 && !expLines.Contains(l)) added.Add(l);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Public API 快照] 检测到差异。");
            sb.AppendLine($"  消失 {removed.Count} 条 / 新增 {added.Count} 条" +
                          $"（各最多列 40 条，完整内容见 {PublicApiSnapshot.SnapshotPath} 与 Build() 输出）");
            if (removed.Count > 0) { sb.AppendLine("--- 消失 ---"); removed.RemoveAll(x => x.Length == 0); for (int i = 0; i < System.Math.Min(40, removed.Count); i++) sb.AppendLine("  - " + removed[i].Trim()); }
            if (added.Count > 0) { sb.AppendLine("--- 新增 ---"); added.RemoveAll(x => x.Length == 0); for (int i = 0; i < System.Math.Min(40, added.Count); i++) sb.AppendLine("  + " + added[i].Trim()); }
            Debug.Log(sb.ToString());

            EditorUtility.DisplayDialog("Public API 快照",
                $"有差异：消失 {removed.Count} 条，新增 {added.Count} 条。\n\n明细已输出到 Console。",
                "好");
        }
    }
}