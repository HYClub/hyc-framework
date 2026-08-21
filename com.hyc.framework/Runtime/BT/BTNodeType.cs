// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTNodeType.cs
// 说明: 行为树节点类型枚举(通用底层, 任何游戏复用)
// ============================================================

namespace HYC.Framework.BT
{
    /// <summary>
    /// 行为树节点类型。节点是纯数据(Blob), 由解释器按类型分发执行。
    /// 组合/装饰为结构节点, 条件/动作为叶子节点。
    /// </summary>
    public enum BTNodeType : byte
    {
        // ---- 入口/出口 ----
        Root,           // 树的唯一入口, 转发其唯一子节点结果
        End,            // 显式终止, 返回 Success(技能流程提前结束用)

        // ---- 组合节点(有子节点) ----
        Sequence,       // 顺序执行, 子节点 Failed 则短路
        Selector,       // 选择执行, 子节点 Success 则短路
        Parallel,       // 并行执行, 按策略汇总
        RandomSelector, // 随机打乱后选择执行(任一成功即成功)
        RandomSequence, // 随机打乱后顺序执行(任一失败即失败)

        // ---- 装饰节点(包一个子节点) ----
        Invert,         // 结果取反
        Repeat,         // 重复 N 次
        UntilSuccess,   // 循环直到成功
        UntilFail,      // 循环直到失败
        AlwaysSuccess,  // 强制成功
        AlwaysFail,     // 强制失败
        CooldownGate,   // 冷却门(冷却期内返回 Failed)
        Conditional,    // 条件装饰: 子节点0=条件, 子节点1=被包子树(条件不过则不执行)
        TimeLimit,      // 超时限制: 子树运行超过 N 秒强制 Failed

        // ---- 条件节点(叶子, 判断) ----
        CheckDistance,  // 距离判断(黑板值 vs 阈值)
        CheckBlackboard,// 黑板值判断(bool/比较)

        // ---- 动作节点(叶子, 干活) ----
        Wait,           // 等待指定秒数(Running)
        NoOp,           // 空操作

        // ---- 特殊 ----
        SubTree,        // 子树引用: 执行另一棵树(参数 Long[0]=目标树ID)

        // ---- 扩展节点(由游戏层注册, 0x80 起) ----
        GameCustom = 128,
    }

    /// <summary>树资产类型: 技能树 / 角色AI树 / 其他(用于节点语义注册分区)。</summary>
    public enum BTTreeKind
    {
        Skill = 0,
        AI = 1,
        Other = 2,
    }
}
