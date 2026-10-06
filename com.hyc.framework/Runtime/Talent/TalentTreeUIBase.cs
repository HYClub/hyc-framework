using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Talent
{
    /// <summary>单个 slot 的三种可视状态。</summary>
    public enum SlotState
    {
        /// <summary>前置未满足 / 资源不足 —— 锁定, 不可点。</summary>
        Locked,
        /// <summary>可解锁 —— 高亮, 可点。</summary>
        Available,
        /// <summary>已解锁(点亮)。</summary>
        Unlocked,
    }

    /// <summary>
    /// UI 上的「口子」。把天赋树里的某个<b>节点(子资源)</b>拖进 <see cref="node"/> 即可把它绑到这个槽位。
    /// 列表可增删/重排 —— 这就是你「挂在 prefab 上、把节点拖进来」的落点。
    /// </summary>
    [System.Serializable]
    public class TalentNodeSlot
    {
        /// <summary>拖进来的节点(从 Project 窗口展开树资产后拖单个节点)。</summary>
        public TalentNode node;
    }

    /// <summary>
    /// 天赋树 UI 的<b>运行期基类</b>(UGUI / MonoBehaviour)。
    ///
    /// 职责只有两件:
    /// 1. 持有 <see cref="slots"/>(口子) + 一颗 <see cref="TalentRuntime"/>, 算好每个槽位的
    ///    <see cref="SlotState"/>(锁定/可点/点亮);
    /// 2. 把点击转成 <see cref="TalentRuntime.TryUnlock"/>, 解锁成功后回调 <see cref="Refresh"/> 让 UI 重绘。
    ///
    /// <b>具体怎么画</b>(改颜色/文本/特效)交给子类在 <see cref="ApplySlot"/> 里做 —— 基类只算状态,
    /// 不碰任何视觉细节, 因此天生跨项目通用。生成器产出的 <c>&lt;Prefix&gt;TalentUI</c> 就是它的子类。
    /// </summary>
    public class TalentTreeUIBase : MonoBehaviour
    {
        /// <summary>要展示的天赋树资产(拖进来, 或生成时自动指好)。</summary>
        public TalentTreeAsset treeAsset;

        /// <summary>口子列表: 每个槽位绑定一个节点。可拖拽增删/重排。</summary>
        public List<TalentNodeSlot> slots = new List<TalentNodeSlot>();

        /// <summary>运行期状态机(由 <see cref="Initialize"/> 建立)。</summary>
        protected TalentRuntime runtime { get; private set; }

        /// <summary>
        /// 初始化: 建立运行期状态机 + 注入资源提供器 + 首次刷新。
        /// 资源提供器由游戏侧实现(见 <see cref="ITalentResourceProvider"/>), 框架不内置点数体系。
        /// </summary>
        public void Initialize(ITalentResourceProvider provider)
        {
            if (treeAsset == null) return;
            var rt = new TalentRuntime();
            rt.Bind(TalentTree.Build(treeAsset));
            rt.Resources = provider;
            BindRuntime(rt);
        }

        /// <summary>
        /// 初始化(重载): 直接接收一个已建好的 <see cref="TalentRuntime"/>。
        /// 生成器产出的 <c>&lt;Prefix&gt;TalentController</c> 既是运行期状态机、又实现了
        /// <see cref="ITalentResourceProvider"/>, 直接传进来即可(它会在构造里把 Resources 指向自己)。
        /// </summary>
        public void Initialize(TalentRuntime rt)
        {
            if (treeAsset == null || rt == null) return;
            if (rt.Tree == null) rt.Bind(TalentTree.Build(treeAsset));
            BindRuntime(rt);
        }

        private void BindRuntime(TalentRuntime rt)
        {
            runtime = rt;
            runtime.OnChanged += _ => Refresh();
            Refresh();
        }

        /// <summary>重算所有槽位状态并调用 <see cref="ApplySlot"/> 让子类绘制。</summary>
        public virtual void Refresh()
        {
            if (runtime == null) return;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || s.node == null) continue;
                var state = runtime.IsUnlocked(s.node.id) ? SlotState.Unlocked
                          : runtime.CanUnlock(s.node.id) ? SlotState.Available
                          : SlotState.Locked;
                ApplySlot(s, s.node, state);
            }
        }

        /// <summary>
        /// 子类在此把状态画到具体 UI 上(设颜色/文本/可交互/图标)。
        /// 基类只算状态, 不假设任何视觉结构。默认空实现(纯逻辑调试用)。
        /// </summary>
        protected virtual void ApplySlot(TalentNodeSlot slot, TalentNode node, SlotState state) { }

        /// <summary>某个槽位被点击时调用(子类在 Button 上挂这个, 或基类若找到 Button 自动挂)。</summary>
        public void RequestUnlock(int nodeId)
        {
            if (runtime != null && runtime.TryUnlock(nodeId)) Refresh();
        }
    }
}
