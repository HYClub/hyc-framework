using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.RedDot
{
    /// <summary>红点节点的默认展示形态（仅影响 UI 默认显示方式，聚合同时计算「是否亮」与「计数」）。</summary>
    public enum RedDotMode
    {
        /// <summary>圆点：只关心「亮不亮」。</summary>
        Bool = 0,

        /// <summary>数字：除圆点外还显示聚合计数（如「3 封未读」）。</summary>
        Count = 1,
    }

    /// <summary>单个红点节点的**自有**状态快照（供宿主存档；不含聚合结果）。</summary>
    [Serializable]
    public struct RedDotState
    {
        public int Id;
        public int Count;
        public bool Flag;
    }

    /// <summary>
    /// 红点树：树结构 + 向上聚合 + 查询 + 清除 + 变更通知。
    ///
    /// <para><b>定位（与 Talent 同哲学）</b>：只提供「树 + 聚合 + 通知」这套通用机制，
    /// <b>不内置</b>业务判定（什么条件算「有红点」）、资源来源、存档格式。
    /// 业务侧只负责 <see cref="Mark"/> / <see cref="SetCount"/>，表现侧只负责读与 <see cref="Consume"/>。</para>
    ///
    /// <para><b>聚合语义</b>：节点「亮」= 自身亮 或 任一子节点亮；节点「计数」= 自身计数 + 所有子节点计数之和。
    /// 因此只给叶子打标，父节点会自动亮；给叶子设计数，父节点自动累加。</para>
    ///
    /// <para><b>为什么是静态内存树而不是 ECS 组件</b>：红点态要<b>跨帧持久</b>
    /// （单帧 <c>EventMessage</c> 承载不了），且被 UI（B9）与玩法（A3）双向访问；
    /// 静态容器免去「谁是权威实体」的争议，也避免每帧 query。代价是不参与 Job/Burst——
    /// 红点是低频状态，不构成瓶颈。</para>
    ///
    /// <para><b>注册约定</b>：<b>先注册父、再注册子</b>（树自顶向下声明）。
    /// 若子先于父注册，父注册时会回收这些「父 id 指向自己」的孤儿节点，不会丢子节点。</para>
    ///
    /// <para><b>线程</b>：只在主线程使用（注册/打标/查询/订阅均无锁）。</para>
    /// </summary>
    public static class RedDotManager
    {
        private sealed class Node
        {
            public RedDotKey Key;
            public string Name;
            public int ParentId;          // 0 = 根
            public RedDotMode Mode;
            public readonly List<int> Children = new List<int>();
            public bool OwnFlag;          // 业务直接置的「亮」
            public int OwnCount;          // 业务直接置的「数」
            public bool Active;           // 聚合结果
            public int Count;             // 聚合结果
        }

        private static readonly Dictionary<int, Node> s_nodes = new Dictionary<int, Node>();

        /// <summary>注册顺序（编辑器窗口与存档导出保持稳定次序）。</summary>
        private static readonly List<int> s_order = new List<int>();

        private static int s_version;

        /// <summary>
        /// 某节点的**聚合值**发生变化时触发（自身或祖先，逐个回调）。
        /// 订阅者应在视图关闭时退订（<c>AbsUISystem</c> 已在 <c>OnViewClose</c> 自动处理）。
        /// </summary>
        public static event Action<RedDotKey> OnChanged;

        /// <summary>聚合状态的版本号，每次有节点变化自增（供 UI 判断是否需要全量刷新）。</summary>
        public static int Version => s_version;

        /// <summary>已注册节点数。</summary>
        public static int Count => s_nodes.Count;

        /// <summary>按注册顺序返回全部键。</summary>
        public static IReadOnlyList<RedDotKey> Keys
        {
            get
            {
                var list = new List<RedDotKey>(s_order.Count);
                for (var i = 0; i < s_order.Count; i++)
                    list.Add(new RedDotKey(s_order[i]));
                return list;
            }
        }

        // ------------------------------------------------------------------ 注册

        /// <summary>
        /// 注册一个红点节点。重复注册同一 id 会更新父/名称/模式（便于热更或重复初始化）。
        /// </summary>
        /// <param name="key">节点键（<see cref="RedDotKey.IsValid"/> 必须为 true）。</param>
        /// <param name="parent">父节点；默认值（id 0）表示根节点。</param>
        /// <param name="name">显示名（编辑器窗口 / <see cref="RedDotKey.ToString"/> 用）。</param>
        /// <param name="mode">默认展示形态（bool 圆点 / count 数字）。</param>
        public static void Register(RedDotKey key, RedDotKey parent = default,
            string name = null, RedDotMode mode = RedDotMode.Bool)
        {
            if (!key.IsValid)
            {
                Debug.LogWarning("[RedDot] 注册被忽略：RedDotKey 无效（id = 0）。");
                return;
            }

            if (!s_nodes.TryGetValue(key.Id, out var node))
            {
                node = new Node { Key = key };
                s_nodes[key.Id] = node;
                s_order.Add(key.Id);
            }

            node.Name = name ?? node.Name;
            node.Mode = mode;

            // 父变更：先从旧父摘除，再挂到新父
            var newParentId = parent.IsValid ? parent.Id : 0;
            if (node.ParentId != newParentId)
            {
                DetachFromParent(node);
                node.ParentId = newParentId;
                if (newParentId != 0 && s_nodes.TryGetValue(newParentId, out var p))
                    p.Children.Add(key.Id);
            }

            // 回收「先于父注册」的孤儿：把父 id 指向本节点、但还没挂进 Children 的节点接上
            for (var i = 0; i < s_order.Count; i++)
            {
                var otherId = s_order[i];
                if (otherId == key.Id)
                    continue;
                if (!s_nodes.TryGetValue(otherId, out var other))
                    continue;
                if (other.ParentId != key.Id)
                    continue;
                if (!node.Children.Contains(otherId))
                    node.Children.Add(otherId);
            }

            PropagateUp(node);
        }

        /// <summary>清空全部节点与订阅后的状态（切场景 / 测试用；订阅者仍需各自退订）。</summary>
        public static void Clear()
        {
            s_nodes.Clear();
            s_order.Clear();
            s_version = 0;
        }

        private static void DetachFromParent(Node node)
        {
            if (node.ParentId == 0)
                return;
            if (s_nodes.TryGetValue(node.ParentId, out var p))
                p.Children.Remove(node.Key.Id);
        }

        // ------------------------------------------------------------------ 业务写入（只改「自有值」，聚合自动向上）

        /// <summary>设置节点自身是否亮（不影响子节点）。父链会自动重算。</summary>
        public static void Mark(RedDotKey key, bool on = true)
        {
            if (!s_nodes.TryGetValue(key.Id, out var node))
                return;
            if (node.OwnFlag == on)
                return;
            node.OwnFlag = on;
            PropagateUp(node);
        }

        /// <summary>设置节点自身的计数（&lt;=0 视为清零）。父链会自动累加。</summary>
        public static void SetCount(RedDotKey key, int count)
        {
            if (!s_nodes.TryGetValue(key.Id, out var node))
                return;
            if (count < 0) count = 0;
            if (node.OwnCount == count)
                return;
            node.OwnCount = count;
            PropagateUp(node);
        }

        /// <summary>
        /// 清除本节点自身的亮与计数（业务「已读」）。**不**影响子节点——
        /// 需要连同子树一起清时用 <see cref="ConsumeWithChildren"/>。
        /// </summary>
        public static void Consume(RedDotKey key)
        {
            if (!s_nodes.TryGetValue(key.Id, out var node))
                return;
            if (!node.OwnFlag && node.OwnCount == 0)
                return;
            node.OwnFlag = false;
            node.OwnCount = 0;
            PropagateUp(node);
        }

        /// <summary>清除本节点及整棵子树的亮与计数（如「全部已读」）。</summary>
        public static void ConsumeWithChildren(RedDotKey key)
        {
            if (!s_nodes.TryGetValue(key.Id, out var node))
                return;

            ClearOwnRecursive(node, 0);

            // 先自下而上重算子树（父节点的聚合值依赖子节点的**新**聚合值），再向祖先传播。
            RecomputeSubtree(node);
            if (node.ParentId != 0 && s_nodes.TryGetValue(node.ParentId, out var parent))
                PropagateUp(parent);
        }

        /// <summary>递归清空子树的自有值（带环保护）。聚合值由调用方随后重算。</summary>
        private static void ClearOwnRecursive(Node node, int depth)
        {
            if (depth > 64)
                return;

            node.OwnFlag = false;
            node.OwnCount = 0;

            for (var i = 0; i < node.Children.Count; i++)
            {
                if (s_nodes.TryGetValue(node.Children[i], out var child))
                    ClearOwnRecursive(child, depth + 1);
            }
        }

        // ------------------------------------------------------------------ 查询（读聚合结果）

        /// <summary>节点是否亮（自身或任一子节点亮）。未注册返回 false。</summary>
        public static bool IsActive(RedDotKey key)
            => s_nodes.TryGetValue(key.Id, out var node) && node.Active;

        /// <summary>节点聚合计数（自身 + 子树之和）。未注册返回 0。</summary>
        public static int GetCount(RedDotKey key)
            => s_nodes.TryGetValue(key.Id, out var node) ? node.Count : 0;

        /// <summary>一次性取聚合结果。未注册返回 false 且输出 0。</summary>
        public static bool TryGet(RedDotKey key, out bool active, out int count)
        {
            if (s_nodes.TryGetValue(key.Id, out var node))
            {
                active = node.Active;
                count = node.Count;
                return true;
            }
            active = false;
            count = 0;
            return false;
        }

        // ------------------------------------------------------------------ 只读面（编辑器窗口 / 测试）

        /// <summary>显示名（未注册返回 null）。</summary>
        public static string GetDisplayName(int id)
            => s_nodes.TryGetValue(id, out var node) ? node.Name : null;

        public static RedDotKey GetParent(RedDotKey key)
            => s_nodes.TryGetValue(key.Id, out var node) && node.ParentId != 0
                ? new RedDotKey(node.ParentId) : default;

        public static RedDotMode GetMode(RedDotKey key)
            => s_nodes.TryGetValue(key.Id, out var node) ? node.Mode : RedDotMode.Bool;

        public static bool GetOwnFlag(RedDotKey key)
            => s_nodes.TryGetValue(key.Id, out var node) && node.OwnFlag;

        public static int GetOwnCount(RedDotKey key)
            => s_nodes.TryGetValue(key.Id, out var node) ? node.OwnCount : 0;

        public static int GetChildCount(RedDotKey key)
            => s_nodes.TryGetValue(key.Id, out var node) ? node.Children.Count : 0;

        // ------------------------------------------------------------------ 持久化边界（不落盘，交给宿主存档）

        /// <summary>导出全部节点的**自有**状态（聚合值可重算，不必存）。</summary>
        public static void ExportState(List<RedDotState> output)
        {
            if (output == null) return;
            output.Clear();
            for (var i = 0; i < s_order.Count; i++)
            {
                if (!s_nodes.TryGetValue(s_order[i], out var node))
                    continue;
                if (!node.OwnFlag && node.OwnCount == 0)
                    continue;
                output.Add(new RedDotState { Id = node.Key.Id, Count = node.OwnCount, Flag = node.OwnFlag });
            }
        }

        /// <summary>从宿主持久化的状态恢复自有值，并重算整棵树。</summary>
        public static void ImportState(IReadOnlyList<RedDotState> states)
        {
            if (states == null) return;

            for (var i = 0; i < s_order.Count; i++)
            {
                if (!s_nodes.TryGetValue(s_order[i], out var node))
                    continue;
                node.OwnFlag = false;
                node.OwnCount = 0;
            }

            for (var i = 0; i < states.Count; i++)
            {
                if (!s_nodes.TryGetValue(states[i].Id, out var node))
                    continue;   // 已删的节点：忽略残留存档
                node.OwnFlag = states[i].Flag;
                node.OwnCount = states[i].Count < 0 ? 0 : states[i].Count;
            }

            // 聚合是**自下而上**的：必须后序遍历（先子后父）重算，否则父节点会读到子节点的旧聚合值。
            for (var i = 0; i < s_order.Count; i++)
            {
                if (!s_nodes.TryGetValue(s_order[i], out var node))
                    continue;
                // 父不存在（残留存档 / 只注册了子）时，该节点按根处理
                if (node.ParentId != 0 && s_nodes.ContainsKey(node.ParentId))
                    continue;
                RecomputeSubtree(node);
            }
        }

        /// <summary>后序重算整棵子树（先子后父），逐个通知聚合值变化的节点。</summary>
        private static void RecomputeSubtree(Node node, int depth = 0)
        {
            // 递归同样要防环（误把 A 的父设成 B、B 的父设成 A 会无限下钻）
            if (depth > 64)
            {
                Debug.LogWarning("[RedDot] 检测到父子链异常（可能存在环），已中止子树重算。");
                return;
            }

            for (var i = 0; i < node.Children.Count; i++)
            {
                if (s_nodes.TryGetValue(node.Children[i], out var child))
                    RecomputeSubtree(child, depth + 1);
            }

            if (!Recompute(node))
                return;

            s_version++;
            OnChanged?.Invoke(node.Key);
        }

        // ------------------------------------------------------------------ 聚合

        /// <summary>
        /// 从 <paramref name="node"/> 向上重算并逐级通知；某级聚合值未变即可停止
        /// （父级只依赖子级聚合值，子级没变父级必然没变）。
        /// </summary>
        private static void PropagateUp(Node node)
        {
            var current = node;
            var guard = 0;
            while (current != null)
            {
                if (++guard > 64)
                {
                    Debug.LogWarning("[RedDot] 检测到父子链异常（可能存在环），已中止向上聚合。");
                    break;
                }

                var changed = Recompute(current);
                if (!changed)
                    break;

                s_version++;
                OnChanged?.Invoke(current.Key);

                current = current.ParentId != 0 && s_nodes.TryGetValue(current.ParentId, out var parent)
                    ? parent
                    : null;
            }
        }

        /// <summary>重算单个节点的聚合值，返回是否发生变化。</summary>
        private static bool Recompute(Node node)
        {
            var active = node.OwnFlag || node.OwnCount > 0;
            var count = node.OwnCount;

            for (var i = 0; i < node.Children.Count; i++)
            {
                if (!s_nodes.TryGetValue(node.Children[i], out var child))
                    continue;
                if (child.Active)
                    active = true;
                count += child.Count;
            }

            if (node.Active == active && node.Count == count)
                return false;

            node.Active = active;
            node.Count = count;
            return true;
        }
    }
}
