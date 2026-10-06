// ============================================================
// HYC Framework - 天赋树模块(Editor)
// 文件: Editor/Talent/TalentTreeValidator.cs
// 说明: 天赋树校验器 - 返回问题列表(空 = 正常)
//       Error:  节点 id 重复、连线指向不存在的节点、出现环、
//               消耗引用了黑板上没有的属性、黑板属性没绑定配置行、treeId 重复
//       Warning: 孤立节点、重复连线、黑板属性无人使用
//       注意: 节点没有分类概念，所以不做"起点数量"之类的检查——多入口是合法的。
// ============================================================

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Talent.Editor
{
    public struct TalentIssue
    {
        public bool IsError;
        public string Message;
    }

    public static class TalentTreeValidator
    {
        /// <summary>校验一棵树。返回问题列表(空 = 正常)。</summary>
        public static List<TalentIssue> Validate(TalentTreeAsset asset)
        {
            var issues = new List<TalentIssue>();
            if (asset == null) return issues;

            // 0. 空树视为「还没开始编辑」，不报错（与 BT 编辑器一致的处理方式）
            if (asset.nodes.Count == 0)
            {
                return issues;
            }

            // 0.5 反序列化失效的节点：TalentNode 在 2.0.6 起由普通序列化类改成 ScriptableObject 子资源。
            //     在那之前保存的 .asset 里 nodes 是"内联数据"，读不进对象引用 → 每个元素都变成 null。
            //     这不是"连线错了"，而是节点根本没读出来；单独报一条清晰的错，别刷一屏悬空连线。
            int brokenNodes = 0;
            for (int i = 0; i < asset.nodes.Count; i++) if (asset.nodes[i] == null) brokenNodes++;
            if (brokenNodes > 0)
            {
                issues.Add(Err(brokenNodes == asset.nodes.Count
                    ? "节点数据已失效（该资产由 2.0.6 之前的旧格式保存）。请在画布上点「清理失效节点」后重新添加节点。"
                    : "有 " + brokenNodes + " 个节点数据失效（无法反序列化）。请在画布上点「清理失效节点」清理后重建。"));
                return issues;
            }

            var name = string.IsNullOrEmpty(asset.displayName) ? asset.name : asset.displayName;

            // 1. 节点 id
            var dupIds = asset.nodes.Where(n => n != null)
                                    .GroupBy(n => n.id)
                                    .Where(g => g.Count() > 1)
                                    .Select(g => g.Key).ToList();
            foreach (var id in dupIds)
                issues.Add(Err($"节点 id {id} 重复"));

            // 3. 连线完整性 + 重复
            var linkKeys = new HashSet<string>();
            foreach (var l in asset.links)
            {
                if (l == null) continue;
                if (asset.NodeById(l.from) == null)
                    issues.Add(Err($"连线引用了不存在的源节点 {l.from}"));
                if (asset.NodeById(l.to) == null)
                    issues.Add(Err($"连线引用了不存在的目标节点 {l.to}"));

                var key = l.from + "->" + l.to;
                if (!linkKeys.Add(key))
                    issues.Add(Warn($"存在重复连线 {key}"));
            }

            // 4. 环（天赋树必须是有向无环图）
            var cycleNode = FindNodeOnCycle(asset);
            if (cycleNode != null)
                issues.Add(Err($"「{cycleNode.title}」处在环上，天赋树必须是有向无环图"));

            // 5. 孤立节点（既无前置也无后继）与悬空入口
            //    注意：多入口是合法的（一棵树可以有好几条并行的起始分支），所以不报错；
            //    但一个节点都没有前置、自己又不在任何链路上，多半是忘了连线。
            foreach (var n in asset.nodes)
            {
                if (n == null) continue;
                var hasIn = asset.links.Any(l => l != null && l.to == n.id);
                var hasOut = asset.links.Any(l => l != null && l.from == n.id);
                if (!hasIn && !hasOut)
                    issues.Add(Warn($"「{n.title}」是孤立节点（没有任何连线）"));
            }

            // 6. 消耗的引用完整性
            foreach (var n in asset.nodes)
            {
                if (n == null) continue;
                foreach (var c in n.costs)
                {
                    if (c == null) continue;
                    if (asset.AttributeByKey(c.attrKey) == null)
                        issues.Add(Err($"「{n.title}」消耗了黑板中不存在的属性 {AttrLabel(c.attrKey)}"));
                }
            }

            // 7. 黑板属性：必须绑定配置行；没人用给警告
            foreach (var a in asset.blackboard)
            {
                if (a == null) continue;
                if (a.sourceRow == null || a.sourceRow.IsEmpty)
                    issues.Add(Err($"黑板属性 {AttrLabel(a.key)} 没有绑定配置行——属性必须来自配置表"));

                var used = asset.nodes.Any(n => n != null && n.costs.Any(c => c != null && c.attrKey == a.key));
                if (!used)
                    issues.Add(Warn($"黑板属性 {AttrLabel(a.key)} 没有被任何节点使用"));
            }

            // 8. treeId 唯一性（跨资产）
            if (asset.treeId != 0)
            {
                var others = AssetDatabase.FindAssets("t:" + nameof(TalentTreeAsset))
                    .Select(g => AssetDatabase.LoadAssetAtPath<TalentTreeAsset>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(t => t != null && t != asset && t.treeId == asset.treeId)
                    .ToList();
                if (others.Count > 0)
                    issues.Add(Err($"treeId {asset.treeId} 与「{others[0].name}」重复"));
            }
            else
            {
                issues.Add(Err("treeId 未生成，请保存一次资产"));
            }

            return issues;
        }

        private static string AttrLabel(string key)
            => string.IsNullOrEmpty(key) ? "(空)" : key;

        /// <summary>找出任一处在环上的节点，没有则返回 null。</summary>
        private static TalentNode FindNodeOnCycle(TalentTreeAsset asset)
        {
            var visited = new HashSet<int>();
            foreach (var n in asset.nodes)
            {
                if (n == null) continue;
                if (visited.Contains(n.id)) continue;
                if (Reaches(asset, n.id, n.id, new HashSet<int>())) return n;
            }
            return null;
        }

        /// <summary>从 <paramref name="cur"/> 沿后继方向能否回到 <paramref name="target"/>。</summary>
        private static bool Reaches(TalentTreeAsset asset, int cur, int target, HashSet<int> seen)
        {
            foreach (var l in asset.links)
            {
                if (l == null || l.from != cur) continue;
                if (l.to == target) return true;
                if (seen.Add(l.to))
                {
                    if (Reaches(asset, l.to, target, seen)) return true;
                }
            }
            return false;
        }

        private static TalentIssue Err(string msg) => new TalentIssue { IsError = true, Message = msg };
        private static TalentIssue Warn(string msg) => new TalentIssue { IsError = false, Message = msg };
    }
}
