// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTNodeRuntimeState.cs
// 说明: per-node 跨帧执行态 - 重写 NodeCanvas 行为树所需的持久状态
//       对应 NodeCanvas 每个节点实例的 Status + 装饰器字段 + outConnections.Reset()
//       按节点在 Blob 数组的索引寻址, 由 BTManager 按 (实体, 树ID) 持久化
// ============================================================

namespace HYC.Framework.BT
{
    /// <summary>
    /// 单个节点的跨帧执行态。解释器每帧查表续跑, 组合/装饰节点据此续跑或切换子节点时调用 ResetNode 清零。
    /// 字段按节点类型复用: Current/Iteration/MaskA/Elapsed/Flags 覆盖所有 NodeCanvas 节点的持久需求,
    /// 避免为每种节点定义独立结构(保持 Burst 友好、定长)。
    /// </summary>
    public struct BTNodeRuntimeState
    {
        public BTNodeState Status;     // 本节点上一帧结果(None/Running/Success/Failed/Optional)
        public int  Current;           // 游标: lastRunningNodeIndex / runningIndex(高16) / succeedIndex / FlipSelector旋转起点 / Switch.current
        public int  Iteration;          // Repeater计数 / StepIterator步进 / Iterator idx / Parallel.finishedConnectionsCount
        public int  MaskA;              // 位掩码: Parallel.finishedConnections / ProbabilitySelector.indexFailed (≤32 子)
        public float Elapsed;          // Timeout / Filter.cooldown 计时; ProbabilitySelector 存首跑 dice
        public int  Flags;              // 位: accessed(WaitUntil/ConditionalEvaluator) / isGuarding(Guard) / 等
    }
}
