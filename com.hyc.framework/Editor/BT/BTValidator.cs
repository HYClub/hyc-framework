// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTValidator.cs
// 说明: 行为树校验器 - 检查树结构问题, 返回警告/错误列表
//       1. 孤立节点(没连进树)
//       2. Root 缺失/多个
//       3. 重复 TreeId
//       4. 子树引用缺失(SubTree 引用的树不存在)
// ============================================================

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    public struct BTValidationIssue
    {
        public bool IsError;
        public string Message;
    }

    public static class BTValidator
    {
        /// <summary>校验一棵树。返回问题列表(空 = 正常)。</summary>
        public static List<BTValidationIssue> Validate(BTTreeAsset tree)
        {
            var issues = new List<BTValidationIssue>();
            if (tree == null) return issues;

            // 1. Root 检查
            var roots = tree.Nodes.Where(n => n.Type == BTNodeType.Root).ToList();
            if (roots.Count == 0)
                issues.Add(new BTValidationIssue { IsError = true, Message = "缺少 Root 入口节点" });
            else if (roots.Count > 1)
                issues.Add(new BTValidationIssue { IsError = true, Message = $"存在 {roots.Count} 个 Root, 只允许 1 个" });

            if (tree.Nodes.Count == 0)
            {
                issues.Add(new BTValidationIssue { IsError = true, Message = "树为空(没有节点)" });
                return issues;
            }

            // 2. 可达性: 从 Root 出发 BFS, 找出不可达节点
            var childrenMap = new Dictionary<long, List<long>>();
            foreach (var n in tree.Nodes) childrenMap[n.NodeId] = new List<long>();
            foreach (var c in tree.Connections)
            {
                if (childrenMap.ContainsKey(c.SourceNodeId) && !childrenMap[c.SourceNodeId].Contains(c.TargetNodeId))
                    childrenMap[c.SourceNodeId].Add(c.TargetNodeId);
            }

            var reachable = new HashSet<long>();
            var queue = new Queue<long>();
            // 起点: Root 节点(若有)或所有节点
            if (roots.Count > 0)
            {
                foreach (var r in roots) { reachable.Add(r.NodeId); queue.Enqueue(r.NodeId); }
            }
            else
            {
                foreach (var n in tree.Nodes) { reachable.Add(n.NodeId); queue.Enqueue(n.NodeId); }
            }
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!childrenMap.TryGetValue(cur, out var kids)) continue;
                foreach (var k in kids)
                {
                    if (reachable.Add(k)) queue.Enqueue(k);
                }
            }
            var orphan = tree.Nodes.Where(n => !reachable.Contains(n.NodeId) && n.Type != BTNodeType.Root).ToList();
            if (orphan.Count > 0)
                issues.Add(new BTValidationIssue { IsError = false, Message = $"{orphan.Count} 个节点未连接(孤立), 不会被执行" });

            // 3. Root 有连线吗
            if (roots.Count == 1)
            {
                var r = roots[0];
                bool rootConnected = tree.Connections.Any(c => c.SourceNodeId == r.NodeId);
                if (!rootConnected)
                    issues.Add(new BTValidationIssue { IsError = true, Message = "Root 未连线(连到实际起始逻辑)" });
            }

            // 4. SubTree 引用检查
            foreach (var n in tree.Nodes.Where(n => n.Type == BTNodeType.SubTree && n.LongParams.Count > 0))
            {
                long targetId = n.LongParams[0];
                if (targetId == 0)
                {
                    issues.Add(new BTValidationIssue { IsError = true, Message = "子树节点未选择目标树" });
                    continue;
                }
                if (!TreeExists(targetId))
                    issues.Add(new BTValidationIssue { IsError = true, Message = $"子树引用不存在: TreeId={targetId}" });
            }

            return issues;
        }

        /// <summary>项目内是否存在指定 TreeId 的树。</summary>
        public static bool TreeExists(long treeId)
        {
            var guids = AssetDatabase.FindAssets("t:BTTreeAsset");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var tree = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(path);
                if (tree != null && tree.TreeId == treeId)
                    return true;
            }
            return false;
        }
    }
}
