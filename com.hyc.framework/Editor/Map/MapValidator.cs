using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Map.Editor
{
    /// <summary>
    /// 地图体检：把「导出后才会炸」的问题提前到编辑期。
    /// <para>
    /// 检查项按「炸得最狠」排序：尺寸/网格长度不一致 &gt; 层数与瓦片集不一致 &gt; mapId 重复 &gt;
    /// 瓦片集缺失 &gt; 瓦片集自身问题（转交 <see cref="MapTileSet.Validate"/>）&gt; 值越界 &gt; 道具占地。
    /// </para>
    /// </summary>
    public static class MapValidator
    {
        /// <summary>全量体检，结果打到 Console（CI 可跑）。</summary>
        [MenuItem("HYC Framework/Map/Validate Maps", false, 78)]
        public static void ValidateAll()
        {
            var guids = AssetDatabase.FindAssets("t:" + typeof(MapAsset).FullName);
            var seenIds = new Dictionary<int, string>(guids.Length);
            var seenNames = new Dictionary<string, string>(guids.Length);
            var issues = new List<string>();
            int okCount = 0;

            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var a = AssetDatabase.LoadAssetAtPath<MapAsset>(path);
                if (a == null) continue;

                var local = CollectIssues(a);
                if (local.Count == 0) okCount++;

                if (seenIds.TryGetValue(a.mapId, out var prev))
                    local.Insert(0, $"mapId {a.mapId} 与「{prev}」重复（会让两张地图导出到同一个 blob 文件名）。");
                else
                    seenIds[a.mapId] = a.name;

                if (!string.IsNullOrEmpty(a.name))
                {
                    if (seenNames.TryGetValue(a.name, out var prevName))
                        local.Insert(0, $"资产名「{a.name}」与「{prevName}」重复（Prefab 会互相覆盖）。");
                    else
                        seenNames[a.name] = path;
                }

                foreach (var i in local)
                    issues.Add($"{path}：{i}");
            }

            if (issues.Count == 0)
            {
                Debug.Log($"[Map] 体检通过：{guids.Length} 张地图无问题。");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[Map] 体检发现 {issues.Count} 个问题（{guids.Length} 张地图，{okCount} 张干净）：");
            for (int i = 0; i < issues.Count; i++) sb.AppendLine("  - " + issues[i]);
            Debug.LogError(sb.ToString());
        }

        /// <summary>体检单张地图。<paramref name="log"/> 为真时把结果打到 Console。</summary>
        public static bool ValidateAsset(MapAsset asset, bool log)
        {
            if (asset == null) return false;
            var issues = CollectIssues(asset);
            if (log)
            {
                if (issues.Count == 0)
                    Debug.Log($"[Map] 「{asset.name}」体检通过。");
                else
                    Debug.LogError($"[Map] 「{asset.name}」有 {issues.Count} 个问题：\n  - " + string.Join("\n  - ", issues));
            }
            return issues.Count == 0;
        }

        private static List<string> CollectIssues(MapAsset asset)
        {
            var issues = new List<string>();

            if (asset.width <= 0 || asset.height <= 0)
                issues.Add($"尺寸非法：{asset.width}×{asset.height}。");

            if (asset.tileSet == null)
            {
                issues.Add("没有瓦片集：值索引无从解释，Blob 导出会被拒绝。");
                return issues;
            }

            int need = Mathf.Max(0, asset.width) * Mathf.Max(0, asset.height);
            var set = asset.tileSet;
            int gridCount = set.GridCount;

            // ---- 网格 ----
            if (asset.GridCount != gridCount)
                issues.Add($"网格层数不一致：地图持有 {asset.GridCount} 层，瓦片集要求 {gridCount} 层" +
                           "（点「改尺寸(保留)」或重开窗口会补齐）。");

            for (int g = 0; g < gridCount; g++)
            {
                var grid = asset.CellsOf(g);
                if (grid == null)
                {
                    issues.Add($"第 {g} 层「{set.GridName(g)}」没有数据。");
                    continue;
                }
                if (grid.Length != need)
                {
                    issues.Add($"第 {g} 层「{set.GridName(g)}」有 {grid.Length} 格，应为 {asset.width}×{asset.height}={need}。");
                    continue;
                }
                int max = set.ValueCount(g);
                for (int i = 0; i < grid.Length; i++)
                {
                    if (grid[i] > max)
                    {
                        issues.Add($"第 {g} 层「{set.GridName(g)}」第 {i} 格的值 {grid[i]} 超出该层值数 {max}（瓦片集被改短了？）。");
                        break;
                    }
                }
            }

            if (asset.mapId <= 0)
                issues.Add($"mapId 必须 > 0（当前 {asset.mapId}）。");

            // 瓦片集自身问题只报一次，避免每张地图都刷一遍
            var setIssues = set.Validate();
            if (setIssues.Count > 0)
                issues.Add($"瓦片集「{set.name}」有 {setIssues.Count} 个问题（用 Validate 菜单查瓦片集资产）。");

            // ---- 道具 ----
            if (asset.props != null)
            {
                for (int i = 0; i < asset.props.Count; i++)
                {
                    var p = asset.props[i];
                    if (p == null)
                    {
                        issues.Add($"道具条目 {i} 是空引用。");
                        continue;
                    }
                    if (p.propId <= 0)
                    {
                        issues.Add($"道具条目 {i} 的 propId 为 {p.propId}（0 保留给「无」）。");
                        continue;
                    }
                    asset.PropSize(p.propId, out int pw, out int ph);
                    if (p.x < 0 || p.y < 0 || p.x + pw > asset.width || p.y + ph > asset.height)
                    {
                        issues.Add($"道具条目 {i}（#{p.propId}，{pw}×{ph}）落在 ({p.x},{p.y})，超出地图范围。");
                        continue;
                    }
                    // 占地不允许重叠（SetProp 保证；手改 List 会破这条）
                    for (int j = 0; j < i; j++)
                    {
                        var q = asset.props[j];
                        if (q == null || q.propId <= 0) continue;
                        asset.PropSize(q.propId, out int qw, out int qh);
                        if (p.x < q.x + qw && q.x < p.x + pw && p.y < q.y + qh && q.y < p.y + ph)
                        {
                            issues.Add($"道具条目 {i} 与 {j} 的占地重叠（占地不可重叠）。");
                            break;
                        }
                    }
                }
            }

            return issues;
        }
    }
}
