using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using HYC.Framework.Config.Editor;   // ConfigIdService, ConfigDataSettings
using HYC.Framework.Talent;          // TalentTreeAsset

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 复制资产自动重发号：配置（ConfigBase 派生，走 ConfigIdService）与天赋树（TalentTreeAsset，走 treeId） alike。
    ///
    /// <para><b>判定"复制"而非"新建"，是复制就直接重派发：</b>新建资产（Project 窗口 Duplicate /
    /// Ctrl+C-V / 跨文件夹拖复制）都会触发 <see cref="AssetModificationProcessor.OnWillCreateAsset"/>，
    /// 入参就是这个「新资产」路径。复制出的新资产会逐字节带上原资产的 ID / treeId，因此它的 ID 必然已经存在于
    /// 工程中（属于原件）——只要新资产的 ID 命中"既有 ID 集"就判定为复制，<b>直接重派发，无需逐资产全量查重</b>。
    /// 走工厂新建的资产由 <c>ConfigAssetFactory</c> 派发唯一 ID，不会命中既有集，故被跳过（不会误重发）。</para>
    ///
    /// <para>手动兜底（全量查重，修历史脏数据）：<c>HYC Framework/Config/Reissue Duplicate IDs</c> 与
    /// <c>HYC Framework/Talent/Reissue Duplicate Tree IDs</c>。</para>
    /// </summary>
    public class DuplicateIdGuard : AssetModificationProcessor
    {
        private static readonly List<string> sPending = new List<string>();
        private static bool sScheduled;

        /// <summary>新建资产（含 Duplicate / 粘贴 / 拖复制）必然触发。入参是 .meta 路径。</summary>
        static string OnWillCreateAsset(string metaPath)
        {
            // 只关心 .asset（配置与天赋树都是 ScriptableObject .asset）；其它（.cs/.prefab…）直接跳过
            if (!string.IsNullOrEmpty(metaPath) &&
                metaPath.EndsWith(".asset.meta", StringComparison.OrdinalIgnoreCase))
            {
                sPending.Add(metaPath.Substring(0, metaPath.Length - ".meta".Length));
                if (!sScheduled)
                {
                    sScheduled = true;
                    EditorApplication.delayCall += Flush;
                }
            }
            return metaPath; // 返回原路径 = 不阻止创建
        }

        static void Flush()
        {
            sScheduled = false;
            if (sPending.Count == 0)
                return;

            // 取下当前批次，避免重派发触发的再导入又把新路径塞进来形成递归
            var paths = sPending.ToArray();
            sPending.Clear();
            var pendingPaths = new HashSet<string>(paths);

            // 构建一次"既有 ID 集"（排除本次新建的自己）：复制资产的 ID 必然已在集中 → 后续 O(1) 判定复制
            var configIds = new HashSet<long>();
            var configGuids = new HashSet<string>();
            foreach (var c in ConfigIdService.CollectConfigAssets(ConfigDataSettings.RootFolder))
            {
                if (pendingPaths.Contains(AssetDatabase.GetAssetPath(c)))
                    continue; // 排除本次刚落地的自己，否则每个新建都会"撞自己"被误判为复制
                if (ConfigIdService.TryReadId(c, out var id, out var g))
                {
                    if (id != 0) configIds.Add(id);
                    if (!string.IsNullOrEmpty(g)) configGuids.Add(g);
                }
            }

            var talentIds = new HashSet<long>();
            foreach (var g in AssetDatabase.FindAssets("t:TalentTreeAsset"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (pendingPaths.Contains(p))
                    continue;
                var t = AssetDatabase.LoadAssetAtPath<TalentTreeAsset>(p);
                if (t != null && t.treeId != 0)
                    talentIds.Add(t.treeId);
            }

            foreach (var path in paths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset == null)
                    continue;

                // 图标传播：复制 / 粘贴落地的新资产不带 session 图标，按模板补齐
                if (asset is ConfigTemplate tplAsset)
                    ConfigIconApplier.ApplyForTemplate(tplAsset);
                else
                    ConfigIconApplier.ApplyForAsset(asset);

                if (asset is TalentTreeAsset talent)
                {
                    // 复制判定：treeId 命中既有集（属于原件）
                    if (talent.treeId != 0 && talentIds.Contains(talent.treeId))
                    {
                        talent.treeId = 0;
                        talent.EnsureTreeId();   // 重新派发（规则同 ConfigId.Generate）
                        EditorUtility.SetDirty(talent);
                        Debug.Log($"[DuplicateIdGuard] 天赋树「{talent.name}」为复制资产，已自动重派发 treeId。");
                    }
                }
                else if (ConfigIdService.TryReadId(asset, out var id, out var guid))
                {
                    // 复制判定：ID 或 GUID 命中既有集（属于原件）→ 直接重派发，不走全量查重
                    bool isCopy = (id != 0 && configIds.Contains(id)) ||
                                  (!string.IsNullOrEmpty(guid) && configGuids.Contains(guid));
                    if (isCopy)
                    {
                        ConfigIdService.TryWriteId(asset, ConfigIdService.RequestIdSync());
                        Debug.Log($"[DuplicateIdGuard] 配置「{asset.name}」为复制资产，已自动重派发 ID/GUID。");
                    }
                }
                // 非配置 / 非天赋树的 .asset（如模板、其它 ScriptableObject）→ 不处理
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ---------- 手动兜底（全量查重，修历史脏数据）----------

        [MenuItem("HYC Framework/Config/Reissue Duplicate IDs", false, 3)]
        static void MenuReissueConfig()
        {
            var all = ConfigIdService.CollectConfigAssets(ConfigDataSettings.RootFolder);
            var issues = ConfigIdService.FindIssues(all);
            if (issues.Count == 0)
            {
                EditorUtility.DisplayDialog("ID 查重", "未发现重复 / 未分配的 ID/GUID。", "确定");
                return;
            }
            int n = ConfigIdService.Reissue(issues);
            EditorUtility.DisplayDialog("ID 重派发",
                $"已为 {n} 个配置资产重新派发 ID/GUID。\n（引用了旧 ID 的地方需同步修改）", "确定");
        }

        [MenuItem("HYC Framework/Talent/Reissue Duplicate Tree IDs", false, 51)]
        static void MenuReissueTalent()
        {
            var all = LoadAllTalentTrees();
            var seen = new Dictionary<long, TalentTreeAsset>();
            var dup = new List<TalentTreeAsset>();
            foreach (var t in all)
            {
                if (t == null || t.treeId == 0)
                    continue;
                if (seen.ContainsKey(t.treeId))
                    dup.Add(t);
                else
                    seen[t.treeId] = t;
            }
            if (dup.Count == 0)
            {
                EditorUtility.DisplayDialog("TreeId 查重", "未发现重复 / 未分配的 treeId。", "确定");
                return;
            }
            foreach (var t in dup)
            {
                t.treeId = 0;
                t.EnsureTreeId();
                EditorUtility.SetDirty(t);
            }
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("TreeId 重派发", $"已为 {dup.Count} 棵天赋树重新派发 treeId。", "确定");
        }

        static List<TalentTreeAsset> LoadAllTalentTrees()
        {
            var list = new List<TalentTreeAsset>();
            foreach (var g in AssetDatabase.FindAssets("t:TalentTreeAsset"))
            {
                var t = AssetDatabase.LoadAssetAtPath<TalentTreeAsset>(AssetDatabase.GUIDToAssetPath(g));
                if (t != null)
                    list.Add(t);
            }
            return list;
        }
    }
}
