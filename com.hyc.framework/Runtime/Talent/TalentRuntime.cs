using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Talent
{
    /// <summary>
    /// 天赋解锁的<b>资源提供器</b>。框架只管"能不能解锁 / 解锁后改状态"这套规则,
    /// <b>不管点数从哪来</b>——点数(力量/金币/技能点……)由游戏侧实现本接口注入,
    /// 这样 TalentRuntime 对任何项目通用, 符合框架"只做数据与 UI 底座"的定位。
    /// </summary>
    public interface ITalentResourceProvider
    {
        /// <summary>查询某属性当前可用点数(attrKey 来自树的黑板)。</summary>
        int GetAvailable(string attrKey);

        /// <summary>实际扣除(解锁成功时调用一次)。返回是否成功扣除。</summary>
        bool Consume(string attrKey, int amount);
    }

    /// <summary>
    /// 一棵天赋树的运行期状态机: 谁解锁了、点了几级、<b>能不能点 / 能不能解锁</b>。
    ///
    /// <b>框架只提供规则, 不替游戏做决定</b>——具体能从玩家身上扣多少点, 由
    /// <see cref="ITalentResourceProvider"/> 决定。节点数据来自 <see cref="TalentTree"/>(只读视图),
    /// 与 <see cref="TalentTreeAsset"/> 的静态定义解耦。
    /// </summary>
    public sealed class TalentRuntime
    {
        public TalentTree Tree { get; private set; }
        public ITalentResourceProvider Resources { get; set; }

        private readonly HashSet<int> _unlocked = new HashSet<int>();
        private readonly Dictionary<int, int> _level = new Dictionary<int, int>();

        /// <summary>某节点解锁(或状态变化)后触发。</summary>
        public event Action<int> OnUnlocked;
        /// <summary>任何状态变化后触发(子类用来重绘 UI)。</summary>
        public event Action<int> OnChanged;

        public void Bind(TalentTree tree) { Tree = tree; }

        public bool IsUnlocked(int id) => _unlocked.Contains(id);

        public int LevelOf(int id)
        {
            int v;
            return _level.TryGetValue(id, out v) ? v : 0;
        }

        public bool IsMaxed(int id)
        {
            var n = Tree != null ? Tree.GetNode(id) : null;
            return n != null && LevelOf(id) >= n.maxLevel;
        }

        /// <summary>所有前置已解锁 且 自身未满级 → 可点(高亮/可交互)。</summary>
        public bool CanUnlock(int id)
        {
            if (Tree == null) return false;
            var n = Tree.GetNode(id);
            if (n == null || IsUnlocked(id) || IsMaxed(id)) return false;
            var preds = Tree.PredecessorsOf(id);
            for (int i = 0; i < preds.Count; i++)
                if (!IsUnlocked(preds[i].id)) return false;
            return true;
        }

        /// <summary>
        /// 试解锁: 校验 <see cref="CanUnlock"/> + 资源够 → 扣点 + 置位 + 触发事件。返回是否成功。
        /// 多属性消耗会一次性校验与扣除(任一项不足直接整体失败, 不会扣一半)。
        /// </summary>
        public bool TryUnlock(int id)
        {
            if (!CanUnlock(id)) return false;
            var n = Tree.GetNode(id);
            if (n == null || Resources == null) return false;

            for (int i = 0; i < n.costs.Count; i++)
            {
                var c = n.costs[i];
                if (c == null) continue;
                if (Resources.GetAvailable(c.attrKey) < c.amount) return false;
            }
            for (int i = 0; i < n.costs.Count; i++)
            {
                var c = n.costs[i];
                if (c == null) continue;
                if (!Resources.Consume(c.attrKey, c.amount)) return false;
            }

            _unlocked.Add(id);
            _level[id] = 1;
            OnUnlocked?.Invoke(id);
            OnChanged?.Invoke(id);
            return true;
        }

        /// <summary>由存档/外部直接设置某节点解锁状态(例如读档回灌)。</summary>
        public void SetUnlocked(int id, bool on, int level = 1)
        {
            if (on) { _unlocked.Add(id); _level[id] = Mathf.Max(1, level); }
            else { _unlocked.Remove(id); _level.Remove(id); }
            OnChanged?.Invoke(id);
        }

        /// <summary>UI 展示用: 该节点本次解锁所需的(属性, 数量, 当前可用)清单。</summary>
        public List<TalentCostView> UnlockCost(int id)
        {
            var outList = new List<TalentCostView>();
            if (Tree == null) return outList;
            var n = Tree.GetNode(id);
            if (n == null) return outList;
            bool have = Resources != null;
            for (int i = 0; i < n.costs.Count; i++)
            {
                var c = n.costs[i];
                if (c == null) continue;
                int avail = have ? Resources.GetAvailable(c.attrKey) : 0;
                outList.Add(new TalentCostView(c.attrKey, c.amount, avail));
            }
            return outList;
        }
    }

    /// <summary>UI 展示用的消耗快照(属性 key + 需要量 + 当前可用量)。</summary>
    public readonly struct TalentCostView
    {
        public readonly string attrKey;
        public readonly int amount;
        public readonly int available;
        public TalentCostView(string attrKey, int amount, int available)
        {
            this.attrKey = attrKey;
            this.amount = amount;
            this.available = available;
        }
    }
}
