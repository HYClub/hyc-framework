// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTInterpreter.cs
// 说明: 行为树解释器 - 遍历执行 Blob 树(核心调度逻辑)
//       只做调度, 重计算由动作节点下沉到游戏层
// ============================================================

using Unity.Entities;

namespace HYC.Framework.BT
{
    /// <summary>一次树执行的可变状态(挂在实体上, 跨帧保留)。</summary>
    public unsafe struct BTRunState
    {
        public long TreeId;
        public int RootNode;            // 根节点在 Blob 数组的索引
        public int CurrentNode;         // 当前待执行节点
        public BTNodeState Result;      // 上一轮结果

        // 运行栈(组合/装饰推进用), 长度 = 树深, 栈顶是当前执行路径
        public int StackDepth;
        public fixed int Stack[16];     // 固定容量: 树深 <= 16(可调)
    }

    /// <summary>
    /// 解释器核心。使用递归前序遍历执行节点, 返回根结果。
    /// </summary>
    public static unsafe class BTInterpreter
    {
        public const int MaxDepth = 16;

        /// <summary>
        /// 执行一次 Tick。
        /// tree: 树的 Blob 视图; state: 运行状态; ctx: 上下文。
        /// 返回树根结果(Success/Failed/Running)。
        /// </summary>
        public static BTNodeState Tick(BTRootBlob* tree, ref BTRunState state, ref BTContext ctx)
        {
            if (tree == null || tree->NodeCount == 0)
                return BTNodeState.Failed;

            state.CurrentNode = state.RootNode;
            return EvaluateNode(tree, state.RootNode, ref state, ref ctx);
        }

        /// <summary>取节点第 i 个子节点在 Nodes 数组的索引。</summary>
        private static int GetChild(BTRootBlob* tree, int nodeIndex, int i)
        {
            var node = tree->Nodes[nodeIndex];
            if (i < 0 || i >= node.ChildCount) return -1;
            return tree->ChildNodes[node.ChildStart + i];
        }

        private static BTNodeState EvaluateNode(BTRootBlob* tree, int nodeIndex, ref BTRunState state, ref BTContext ctx)
        {
            if (nodeIndex < 0 || nodeIndex >= tree->NodeCount)
                return BTNodeState.Failed;

            var node = tree->Nodes[nodeIndex];
            var view = new BTNodeView
            {
                Node = node,
                Floats = (float*)tree->Floats.GetUnsafePtr(),
                Longs = (long*)tree->Longs.GetUnsafePtr(),
                Strings = (BlobString*)tree->Strings.GetUnsafePtr(),
            };

            switch (node.Type)
            {
                // ---- 组合节点 ----
                case BTNodeType.Sequence:
                    return EvaluateSequence(tree, nodeIndex, ref state, ref ctx);
                case BTNodeType.Selector:
                    return EvaluateSelector(tree, nodeIndex, ref state, ref ctx);
                case BTNodeType.Parallel:
                    return EvaluateParallel(tree, nodeIndex, ref view, ref state, ref ctx);

                // ---- 装饰节点 ----
                case BTNodeType.Invert:
                {
                    var child = EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, ref ctx);
                    return child == BTNodeState.Success ? BTNodeState.Failed
                         : child == BTNodeState.Failed ? BTNodeState.Success
                         : child;
                }
                case BTNodeType.AlwaysSuccess:
                    EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, ref ctx);
                    return BTNodeState.Success;
                case BTNodeType.AlwaysFail:
                    EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, ref ctx);
                    return BTNodeState.Failed;
                case BTNodeType.Repeat:
                {
                    int count = (int)view.GetLong(0);
                    for (int i = 0; i < count; i++)
                    {
                        var r = EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, ref ctx);
                        if (r == BTNodeState.Failed || r == BTNodeState.Running) return r;
                    }
                    return BTNodeState.Success;
                }
                case BTNodeType.UntilSuccess:
                {
                    int guard = 64;
                    while (guard-- > 0)
                    {
                        var r = EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, ref ctx);
                        if (r == BTNodeState.Success) return BTNodeState.Success;
                        if (r == BTNodeState.Running) return BTNodeState.Running;
                    }
                    return BTNodeState.Failed;
                }
                case BTNodeType.UntilFail:
                {
                    int guard = 64;
                    while (guard-- > 0)
                    {
                        var r = EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, ref ctx);
                        if (r == BTNodeState.Failed) return BTNodeState.Success;
                        if (r == BTNodeState.Running) return BTNodeState.Running;
                    }
                    return BTNodeState.Failed;
                }
                case BTNodeType.CooldownGate:
                {
                    // Long[0] = 冷却秒; Long[1] = 黑板时间 key hash
                    float cooldownSec = view.GetFloat(0);
                    ulong timeKey = (ulong)view.GetLong(1);
                    if (!ctx.Blackboard.IsCreated || cooldownSec <= 0f)
                        return BTNodeState.Failed;
                    float elapsed = ctx.Blackboard.GetFloat(timeKey, 0f);
                    if (elapsed < cooldownSec)
                    {
                        ctx.Blackboard.SetFloat(timeKey, elapsed + ctx.DeltaTime);
                        return BTNodeState.Failed;
                    }
                    ctx.Blackboard.SetFloat(timeKey, 0f);
                    return EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, ref ctx);
                }

                // ---- 条件节点 ----
                case BTNodeType.CheckDistance:
                {
                    if (!ctx.Blackboard.IsCreated) return BTNodeState.Failed;
                    float value = ctx.Blackboard.GetFloat((ulong)view.GetLong(0));
                    float threshold = view.GetFloat(0);
                    long cmp = view.GetLong(1);
                    bool pass = cmp == 0 ? value < threshold : value > threshold;
                    return pass ? BTNodeState.Success : BTNodeState.Failed;
                }
                case BTNodeType.CheckBlackboard:
                {
                    if (!ctx.Blackboard.IsCreated) return BTNodeState.Failed;
                    bool actual = ctx.Blackboard.GetBool((ulong)view.GetLong(0));
                    bool expected = view.GetLong(1) != 0;
                    return actual == expected ? BTNodeState.Success : BTNodeState.Failed;
                }

                // ---- 动作节点 ----
                case BTNodeType.Wait:
                {
                    ulong remainKey = (ulong)view.GetLong(0);
                    if (!ctx.Blackboard.IsCreated) return BTNodeState.Failed;
                    float remain = ctx.Blackboard.GetFloat(remainKey, view.GetFloat(0));
                    remain -= ctx.DeltaTime;
                    if (remain <= 0f)
                    {
                        ctx.Blackboard.SetFloat(remainKey, 0f);
                        return BTNodeState.Success;
                    }
                    ctx.Blackboard.SetFloat(remainKey, remain);
                    return BTNodeState.Running;
                }
                case BTNodeType.NoOp:
                    return BTNodeState.Success;

                // ---- 游戏层自定义 ----
                case BTNodeType.GameCustom:
                    if (ctx.GameHandler != null)
                        return ctx.GameHandler(ref ctx, ref view);
                    return BTNodeState.Failed;

                default:
                    return BTNodeState.Failed;
            }
        }

        private static BTNodeState EvaluateSequence(BTRootBlob* tree, int nodeIndex, ref BTRunState state, ref BTContext ctx)
        {
            for (int i = 0; i < tree->Nodes[nodeIndex].ChildCount; i++)
            {
                var r = EvaluateNode(tree, GetChild(tree, nodeIndex, i), ref state, ref ctx);
                if (r == BTNodeState.Failed) return BTNodeState.Failed;
                if (r == BTNodeState.Running) return BTNodeState.Running;
            }
            return BTNodeState.Success;
        }

        private static BTNodeState EvaluateSelector(BTRootBlob* tree, int nodeIndex, ref BTRunState state, ref BTContext ctx)
        {
            for (int i = 0; i < tree->Nodes[nodeIndex].ChildCount; i++)
            {
                var r = EvaluateNode(tree, GetChild(tree, nodeIndex, i), ref state, ref ctx);
                if (r == BTNodeState.Success) return BTNodeState.Success;
                if (r == BTNodeState.Running) return BTNodeState.Running;
            }
            return BTNodeState.Failed;
        }

        private static BTNodeState EvaluateParallel(BTRootBlob* tree, int nodeIndex, ref BTNodeView view, ref BTRunState state, ref BTContext ctx)
        {
            long policy = view.GetLong(0);
            int success = 0, fail = 0;
            for (int i = 0; i < tree->Nodes[nodeIndex].ChildCount; i++)
            {
                var r = EvaluateNode(tree, GetChild(tree, nodeIndex, i), ref state, ref ctx);
                if (r == BTNodeState.Running) return BTNodeState.Running;
                if (r == BTNodeState.Success) success++;
                else fail++;
            }
            return policy == 1 ? (success > 0 ? BTNodeState.Success : BTNodeState.Failed)
                               : (fail == 0 ? BTNodeState.Success : BTNodeState.Failed);
        }
    }
}
