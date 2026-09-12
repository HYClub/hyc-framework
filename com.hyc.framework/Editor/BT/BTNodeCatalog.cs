// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTNodeCatalog.cs
// 说明: 节点类型目录 - 显示名 / 分类 / NodeCanvas 配色
//       供 E1 节点库侧边栏 与 E2 节点外观配色 共用(单一数据源)
//       配色取自 NodeCanvas 运行时类特性([Color]/[Category]), 作为数据沿用
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    /// <summary>节点分类(对应 NodeCanvas 的 Category, 用于节点库分组与配色分区)。</summary>
    public enum BTNodeCategory
    {
        Root,       // 入口/出口
        Composite,  // 组合
        Decorator,  // 装饰
        Condition,  // 条件(叶子)
        Action,     // 动作(叶子)
        SubGraph,   // 子图
    }

    /// <summary>
    /// 节点类型目录。新增节点类型(P2/P3)时在此登记一条即可,
    /// 节点库侧边栏与画布配色自动生效。
    /// </summary>
    public static class BTNodeCatalog
    {
        public struct Entry
        {
            public string Name;
            public BTNodeCategory Category;
            public string ColorHex;
        }

        // NodeCanvas 配色(源自其节点类 [Color] 特性)
        private const string C_Sequencer = "bf7fff";   // Sequencer 紫
        private const string C_Selector = "b3ff7f";    // Selector 绿
        private const string C_Parallel = "ff64cb";    // Parallel 粉
        private const string C_Decorator = "cfd8dc";   // 装饰(灰蓝)
        private const string C_Condition = "ffb74d";   // 条件(琥珀)
        private const string C_Action = "5DCAA5";      // 动作(青绿)
        private const string C_SubGraph = "ffe4e1";    // 子图(浅粉)
        private const string C_Root = "9e9e9e";        // 入口/出口(灰)

        private static readonly Dictionary<BTNodeType, Entry> _map = new Dictionary<BTNodeType, Entry>
        {
            // 入口/出口
            { BTNodeType.Root,      new Entry { Name = "开始",           Category = BTNodeCategory.Root,      ColorHex = C_Root } },
            { BTNodeType.End,       new Entry { Name = "结束(成功)",     Category = BTNodeCategory.Action,    ColorHex = C_Root } },

            // 组合
            { BTNodeType.Sequence,        new Entry { Name = "顺序 Sequencer",       Category = BTNodeCategory.Composite, ColorHex = C_Sequencer } },
            { BTNodeType.Selector,        new Entry { Name = "选择 Selector",        Category = BTNodeCategory.Composite, ColorHex = C_Selector } },
            { BTNodeType.Parallel,        new Entry { Name = "并行 Parallel",        Category = BTNodeCategory.Composite, ColorHex = C_Parallel } },
            { BTNodeType.RandomSelector,  new Entry { Name = "随机选择",             Category = BTNodeCategory.Composite, ColorHex = C_Selector } },
            { BTNodeType.RandomSequence,  new Entry { Name = "随机顺序",             Category = BTNodeCategory.Composite, ColorHex = C_Sequencer } },
            // 新增组合(P2, NodeCanvas 移植)
            { BTNodeType.FlipSelector,        new Entry { Name = "翻转选择 FlipSelector",   Category = BTNodeCategory.Composite, ColorHex = C_Selector } },
            { BTNodeType.UtilitySelector,     new Entry { Name = "工具选择 Priority",       Category = BTNodeCategory.Composite, ColorHex = C_Selector } },
            { BTNodeType.ProbabilitySelector, new Entry { Name = "概率选择 Probability",    Category = BTNodeCategory.Composite, ColorHex = C_Selector } },
            { BTNodeType.StepSequencer,       new Entry { Name = "逐步 Step",               Category = BTNodeCategory.Composite, ColorHex = C_Sequencer } },
            { BTNodeType.Switch,              new Entry { Name = "分支 Switch",             Category = BTNodeCategory.Composite, ColorHex = C_Selector } },
            { BTNodeType.BinarySelector,      new Entry { Name = "二选一 Binary",           Category = BTNodeCategory.Composite, ColorHex = C_Selector } },

            // 装饰
            { BTNodeType.Invert,        new Entry { Name = "取反 Inverter",   Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.Repeat,        new Entry { Name = "重复 Repeat",     Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.UntilSuccess,  new Entry { Name = "直到成功",        Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.UntilFail,     new Entry { Name = "直到失败",        Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.AlwaysSuccess, new Entry { Name = "总是成功",        Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.AlwaysFail,    new Entry { Name = "总是失败",        Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.CooldownGate,  new Entry { Name = "冷却门 Cooldown", Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.Conditional,   new Entry { Name = "条件 Conditional", Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.TimeLimit,     new Entry { Name = "超时 TimeLimit",  Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            // 新增装饰(P3, NodeCanvas 移植)
            { BTNodeType.Timeout,    new Entry { Name = "超时中断 Timeout",   Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.WaitUntil,  new Entry { Name = "等待直到 WaitUntil", Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.Guard,      new Entry { Name = "令牌守卫 Guard",     Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.Interruptor,new Entry { Name = "中断 Interrupt",     Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.Filter,     new Entry { Name = "过滤 Filter",        Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.Iterator,   new Entry { Name = "遍历 Iterate",       Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.Monitor,    new Entry { Name = "监视 Monitor",       Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.Optional,   new Entry { Name = "可选 Optional",      Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },
            { BTNodeType.Remapper,   new Entry { Name = "重映射 Remap",       Category = BTNodeCategory.Decorator, ColorHex = C_Decorator } },

            // 条件
            { BTNodeType.CheckDistance,   new Entry { Name = "距离判断",   Category = BTNodeCategory.Condition, ColorHex = C_Condition } },
            { BTNodeType.CheckBlackboard, new Entry { Name = "黑板判断",   Category = BTNodeCategory.Condition, ColorHex = C_Condition } },

            // 动作
            { BTNodeType.Wait,       new Entry { Name = "等待 Wait",     Category = BTNodeCategory.Action, ColorHex = C_Action } },
            { BTNodeType.NoOp,       new Entry { Name = "空操作",         Category = BTNodeCategory.Action, ColorHex = C_Action } },
            { BTNodeType.GameCustom, new Entry { Name = "自定义节点",     Category = BTNodeCategory.Action, ColorHex = C_Action } },

            // 事件机制(G3)
            { BTNodeType.CheckEvent, new Entry { Name = "事件判断 CheckEvent", Category = BTNodeCategory.Condition, ColorHex = C_Condition } },
            { BTNodeType.SendEvent,  new Entry { Name = "抛事件 SendEvent",   Category = BTNodeCategory.Action,    ColorHex = C_Action } },

            // 子图
            { BTNodeType.SubTree, new Entry { Name = "子树 SubTree", Category = BTNodeCategory.SubGraph, ColorHex = C_SubGraph } },
        };

        private static readonly Dictionary<BTNodeCategory, string> _labels = new Dictionary<BTNodeCategory, string>
        {
            { BTNodeCategory.Root,      "入口 Root" },
            { BTNodeCategory.Composite, "组合 Composites" },
            { BTNodeCategory.Decorator, "装饰 Decorators" },
            { BTNodeCategory.Condition, "条件 Conditions" },
            { BTNodeCategory.Action,    "动作 Actions" },
            { BTNodeCategory.SubGraph,  "子图 SubGraphs" },
        };

        public static bool TryGet(BTNodeType type, out Entry entry) => _map.TryGetValue(type, out entry);

        public static string DisplayName(BTNodeType type)
            => _map.TryGetValue(type, out var e) ? e.Name : type.ToString();

        public static BTNodeCategory CategoryOf(BTNodeType type)
            => _map.TryGetValue(type, out var e) ? e.Category : BTNodeCategory.Action;

        public static string ColorHexOf(BTNodeType type)
            => _map.TryGetValue(type, out var e) ? e.ColorHex : C_Action;

        /// <summary>节点主色(用于标题栏/分类着色)。解析失败返回中性灰。</summary>
        public static Color ColorOf(BTNodeType type)
            => ColorUtility.TryParseHtmlString("#" + ColorHexOf(type), out var c) ? c : Color.gray;

        public static string CategoryLabel(BTNodeCategory cat)
            => _labels.TryGetValue(cat, out var s) ? s : cat.ToString();

        /// <summary>按分类枚举节点类型(供节点库分组)。</summary>
        public static IEnumerable<BTNodeType> ByCategory(BTNodeCategory cat)
        {
            foreach (var kv in _map)
                if (kv.Value.Category == cat)
                    yield return kv.Key;
        }

        /// <summary>全部已登记分类(用于节点库根分组顺序)。</summary>
        public static BTNodeCategory[] Categories { get; } =
        {
            BTNodeCategory.Composite,
            BTNodeCategory.Decorator,
            BTNodeCategory.Condition,
            BTNodeCategory.Action,
            BTNodeCategory.SubGraph,
        };
    }

    /// <summary>
    /// 拖拽载荷: 从节点库拖到画布时携带的节点描述(NodeCanvas 式"拖节点进画布")。
    /// 单一数据源, BTGraphIMGUI 消费。
    /// </summary>
    public sealed class BTDragPayload
    {
        public BTNodeType Type;     // 内置节点类型 / 或 GameCustom(自定义)
        public long SubType;        // 自定义节点子类型(GameCustom 时有效)
        public bool IsCustom;       // 是否自定义节点
        public string Name;         // 显示名(拖拽时作为标题)
    }
}
