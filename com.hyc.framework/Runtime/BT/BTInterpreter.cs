// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTInterpreter.cs
// 说明: 行为树解释器 - 遍历执行 Blob 树(核心调度逻辑)
//       只做调度, 重计算由动作节点下沉到游戏层
//
//       状态模型(重写 NodeCanvas 版):
//       - 节点静态数据 = Blob(不可变)
//       - 节点跨帧执行态 = BTNodeRuntimeState(BTManager 按 (实体,树ID) 持久化)
//       - 组合/装饰节点读写本节点执行态续跑; 切换子节点时 ResetNode 清零整个分支
//         (对应 NodeCanvas 的 node.status + outConnections.Reset())
// ============================================================

using Unity.Collections;
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

        public int StackDepth;
        public fixed int Stack[16];

        // 执行轨迹(调试/高亮用): 本轮执行过的节点索引 + 结果
        public const int MaxTrace = 32;
        public int TraceCount;
        public fixed int TraceNodes[MaxTrace];
        public fixed byte TraceResults[MaxTrace];

        // 运行统计(自增, 调试用): 总执行次数
        public long TotalExecutions;

        // G4: Tick 降频累计器(配合 RunningBT.TickInterval, 0 表示每帧执行)
        public float TickAccum;
    }

    /// <summary>
    /// 解释器核心。递归前序遍历执行节点, 返回根结果。
    /// </summary>
    public static unsafe class BTInterpreter
    {
        public const int MaxDepth = 16;

        /// <summary>执行一次 Tick。返回树根结果。</summary>
        public static BTNodeState Tick(BTRootBlob* tree, ref BTRunState state, ref BTContext ctx)
        {
            if (tree == null || tree->NodeCount == 0)
                return BTNodeState.Failed;

            // per-node 跨帧执行态(按 实体+树 持久化, 首次调用时分配)
            var nodeStates = BTManager.GetNodeStates(ctx.Self, tree->TreeId, tree->NodeCount);

            state.TraceCount = 0; // 清空上轮轨迹
            state.CurrentNode = state.RootNode;
            // 若入口是 Root 节点, 转发其唯一子节点
            if (tree->Nodes[state.RootNode].Type == BTNodeType.Root)
            {
                var root = tree->Nodes[state.RootNode];
                if (root.ChildCount == 0)
                    return BTNodeState.Failed;
                state.CurrentNode = GetChild(tree, state.RootNode, 0);
                return EvaluateNode(tree, state.CurrentNode, ref state, nodeStates, ref ctx);
            }
            return EvaluateNode(tree, state.RootNode, ref state, nodeStates, ref ctx);
        }

        /// <summary>取节点第 i 个子节点在 Nodes 数组的索引。</summary>
        private static int GetChild(BTRootBlob* tree, int nodeIndex, int i)
        {
            var node = tree->Nodes[nodeIndex];
            if (i < 0 || i >= node.ChildCount) return -1;
            return tree->ChildNodes[node.ChildStart + i];
        }

        /// <summary>读取节点 Long 参数(不存在返回默认值)。用于解释器中不构造 view 的场合。</summary>
        private static long GetNodeLong(BTRootBlob* tree, int nodeIndex, int i, long def = 0)
        {
            var node = tree->Nodes[nodeIndex];
            if (i < 0 || i >= node.LongCount) return def;
            var longs = (long*)tree->Longs.GetUnsafePtr();
            return longs[node.LongStart + i];
        }

        /// <summary>复位某节点及其全部子孙的执行态(对应 NodeCanvas 的 outConnections.Reset())。</summary>
        private static void ResetNode(BTRootBlob* tree, ref BTContext ctx, int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= tree->NodeCount) return;
            BTManager.ResetNode(ctx.Self, tree->TreeId, tree, nodeIndex);
        }

        private static BTNodeState EvaluateNode(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            if (nodeIndex < 0 || nodeIndex >= tree->NodeCount)
                return BTNodeState.Failed;

            // 断点: 命中则暂停(返回 Running, 该分支不执行)
            if (BTManager.IsBreakpoint(state.TreeId, nodeIndex))
            {
                RecordTrace(ref state, nodeIndex, BTNodeState.Running);
                return BTNodeState.Running;
            }

            var result = EvaluateNodeInner(tree, nodeIndex, ref state, nodeStates, ref ctx);

            // 记录本节点结果到持久执行态(供父节点判断"子节点是否 Running")
            var ns = nodeStates[nodeIndex];
            ns.Status = result;
            nodeStates[nodeIndex] = ns;

            RecordTrace(ref state, nodeIndex, result);
            return result;
        }

        /// <summary>记录节点执行结果到轨迹(调试高亮用)。</summary>
        private static void RecordTrace(ref BTRunState state, int nodeIndex, BTNodeState result)
        {
            state.TotalExecutions++;
            if (state.TraceCount < BTRunState.MaxTrace)
            {
                state.TraceNodes[state.TraceCount] = nodeIndex;
                state.TraceResults[state.TraceCount] = (byte)result;
                state.TraceCount++;
            }
        }

        private static BTNodeState EvaluateNodeInner(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                     NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
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
                // ---- 入口/出口 ----
                case BTNodeType.Root:
                    if (node.ChildCount == 0) return BTNodeState.Failed;
                    return EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, nodeStates, ref ctx);
                case BTNodeType.End:
                    return BTNodeState.Success;

                // ---- 组合节点 ----
                case BTNodeType.Sequence:
                    return EvaluateSequence(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.Selector:
                    return EvaluateSelector(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.Parallel:
                    return EvaluateParallel(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.RandomSelector:
                    return EvaluateRandomSelector(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.RandomSequence:
                    return EvaluateRandomSequence(tree, nodeIndex, ref state, nodeStates, ref ctx);

                // ---- 新增组合(NodeCanvas 移植, P2) ----
                case BTNodeType.FlipSelector:
                    return EvaluateFlipSelector(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.UtilitySelector:
                    return EvaluateUtilitySelector(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.ProbabilitySelector:
                    return EvaluateProbabilitySelector(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.StepSequencer:
                    return EvaluateStepSequencer(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.Switch:
                    return EvaluateSwitch(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.BinarySelector:
                    return EvaluateBinarySelector(tree, nodeIndex, ref state, nodeStates, ref ctx);

                // ---- 装饰节点 ----
                case BTNodeType.Invert:
                {
                    var child = EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, nodeStates, ref ctx);
                    return child == BTNodeState.Success ? BTNodeState.Failed
                         : child == BTNodeState.Failed ? BTNodeState.Success
                         : child;
                }
                case BTNodeType.AlwaysSuccess:
                    EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, nodeStates, ref ctx);
                    return BTNodeState.Success;
                case BTNodeType.AlwaysFail:
                    EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, nodeStates, ref ctx);
                    return BTNodeState.Failed;

                case BTNodeType.Repeat:
                {
                    // Long[0] = 重复次数(<=0 表示无限)。跨帧持久计数, 支持 Running 子节点续跑。
                    long times = GetNodeLong(tree, nodeIndex, 0, 0);
                    int child = GetChild(tree, nodeIndex, 0);
                    if (child < 0) return BTNodeState.Failed;
                    var ns = nodeStates[nodeIndex];
                    // 子节点非 Running 时复位, 保证每次迭代从头开始
                    if (nodeStates[child].Status != BTNodeState.Running)
                        ResetNode(tree, ref ctx, child);

                    var r = EvaluateNode(tree, child, ref state, nodeStates, ref ctx);
                    if (r == BTNodeState.Running) return BTNodeState.Running;

                    if (times > 0)
                    {
                        if (ns.Iteration + 1 >= times)
                        {
                            ns.Iteration = 0;
                            nodeStates[nodeIndex] = ns;
                            return r;
                        }
                        ns.Iteration++;
                    }
                    nodeStates[nodeIndex] = ns;
                    return BTNodeState.Running;
                }
                case BTNodeType.UntilSuccess:
                {
                    // 跨帧: 子节点成功则成功, 否则持续 Running(下一帧重试)
                    int child = GetChild(tree, nodeIndex, 0);
                    if (child < 0) return BTNodeState.Failed;
                    if (nodeStates[child].Status != BTNodeState.Running)
                        ResetNode(tree, ref ctx, child);
                    var r = EvaluateNode(tree, child, ref state, nodeStates, ref ctx);
                    return r == BTNodeState.Success ? BTNodeState.Success : BTNodeState.Running;
                }
                case BTNodeType.UntilFail:
                {
                    // 跨帧: 子节点失败则成功, 否则持续 Running(下一帧重试)
                    int child = GetChild(tree, nodeIndex, 0);
                    if (child < 0) return BTNodeState.Failed;
                    if (nodeStates[child].Status != BTNodeState.Running)
                        ResetNode(tree, ref ctx, child);
                    var r = EvaluateNode(tree, child, ref state, nodeStates, ref ctx);
                    return r == BTNodeState.Failed ? BTNodeState.Success : BTNodeState.Running;
                }

                case BTNodeType.Conditional:
                {
                    // NodeCanvas ConditionalEvaluator 语义:
                    // child0 = 条件, child1 = 被包子树
                    // Long[0] = 条件不满足时返回的状态(默认 Failed, 可设 Optional/Success)
                    // Long[1] = 是否 dynamic(每帧重估, 条件变假即中断子树)
                    if (node.ChildCount < 2) return BTNodeState.Failed;
                    long failReturnRaw = GetNodeLong(tree, nodeIndex, 0, (long)BTNodeState.Failed);
                    bool isDynamic = GetNodeLong(tree, nodeIndex, 1, 0) != 0;
                    var failReturn = (BTNodeState)failReturnRaw;
                    if (failReturn == BTNodeState.None || failReturn == BTNodeState.Running)
                        failReturn = BTNodeState.Failed;

                    int condChild = GetChild(tree, nodeIndex, 0);
                    int bodyChild = GetChild(tree, nodeIndex, 1);
                    var ns = nodeStates[nodeIndex];

                    if (isDynamic)
                    {
                        var c = EvaluateNode(tree, condChild, ref state, nodeStates, ref ctx);
                        if (c == BTNodeState.Success)
                            return EvaluateNode(tree, bodyChild, ref state, nodeStates, ref ctx);
                        // 条件变假: 中断子树
                        if (nodeStates[bodyChild].Status == BTNodeState.Running)
                            ResetNode(tree, ref ctx, bodyChild);
                        return failReturn;
                    }

                    // 非 dynamic: 条件只求值一次(结果存 accessed 位, bit1 = 已求值)
                    bool accessed = (ns.Flags & 1) != 0;
                    if ((ns.Flags & 2) == 0)
                    {
                        var c = EvaluateNode(tree, condChild, ref state, nodeStates, ref ctx);
                        accessed = c == BTNodeState.Success;
                        ns.Flags = (ns.Flags & ~1) | (accessed ? 1 : 0) | 2; // bit1 = 已求值
                        nodeStates[nodeIndex] = ns;
                    }
                    if (!accessed) return failReturn;
                    return EvaluateNode(tree, bodyChild, ref state, nodeStates, ref ctx);
                }

                // ---- 新增装饰(NodeCanvas 移植, P3) ----
                case BTNodeType.Timeout:
                    return EvaluateTimeout(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.WaitUntil:
                    return EvaluateWaitUntil(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.Guard:
                    return EvaluateGuard(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.Interruptor:
                    return EvaluateInterruptor(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.Filter:
                    return EvaluateFilter(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.Iterator:
                    return EvaluateIterator(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.Monitor:
                    return EvaluateMonitor(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.Optional:
                    return EvaluateOptional(tree, nodeIndex, ref state, nodeStates, ref ctx);
                case BTNodeType.Remapper:
                    return EvaluateRemapper(tree, nodeIndex, ref state, nodeStates, ref ctx);

                case BTNodeType.TimeLimit:
                {
                    float limit = view.Node.FloatCount > 0 ? view.GetFloat(0) : 1f;
                    ulong remainKey = (ulong)(view.Node.LongCount > 0 ? view.GetLong(0) : 0);
                    if (!ctx.Blackboard.IsCreated) return BTNodeState.Failed;
                    float remain = ctx.Blackboard.GetFloat(remainKey, limit);
                    remain -= ctx.DeltaTime;
                    if (remain <= 0f)
                    {
                        ctx.Blackboard.SetFloat(remainKey, 0f);
                        return BTNodeState.Failed;
                    }
                    ctx.Blackboard.SetFloat(remainKey, remain);
                    return EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, nodeStates, ref ctx);
                }
                case BTNodeType.CooldownGate:
                {
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
                    return EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, nodeStates, ref ctx);
                }

                // ---- 事件机制(G3, 对应 NodeCanvas SendEvent / CheckEvent) ----
                case BTNodeType.SendEvent:
                {
                    ulong evKey = (ulong)GetNodeLong(tree, nodeIndex, 0, 0);
                    bool global = GetNodeLong(tree, nodeIndex, 1, 0) != 0;
                    if (evKey == 0) return BTNodeState.Failed;
                    if (global) BTEventBus.RaiseGlobal(evKey);
                    else BTEventBus.Raise(ctx.Self, evKey);
                    return BTNodeState.Success;
                }
                case BTNodeType.CheckEvent:
                {
                    ulong evKey = (ulong)GetNodeLong(tree, nodeIndex, 0, 0);
                    bool consume = GetNodeLong(tree, nodeIndex, 1, 0) != 0;
                    if (evKey == 0) return BTNodeState.Failed;
                    bool hit = consume
                        ? BTEventBus.Consume(ctx.Self, evKey)
                        : BTEventBus.IsRaised(ctx.Self, evKey);
                    return hit ? BTNodeState.Success : BTNodeState.Failed;
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

                case BTNodeType.SubTree:
                {
                    // Long[0] = 目标树 ID, 执行目标树并返回其根结果
                    long subTreeId = view.Node.LongCount > 0 ? view.GetLong(0) : 0;
                    if (subTreeId == 0) return BTNodeState.Failed;
                    var subTree = BTManager.TryGet(subTreeId);
                    if (subTree == null) return BTNodeState.Failed;
                    // 复用该实体此子树的持久化运行态, 使 Wait/CooldownGate/Until* 等跨帧状态得以延续
                    if (!BTManager.TryGetSubTreeState(ctx.Self, subTreeId, out var subState))
                        subState = new BTRunState { TreeId = subTreeId, RootNode = 0 };
                    var result = Tick(subTree, ref subState, ref ctx);
                    BTManager.SetSubTreeState(ctx.Self, subTreeId, subState);
                    return result;
                }

                // ---- 游戏层自定义 ----
                case BTNodeType.GameCustom:
                {
                    if (ctx.GameHandler != null)
                        return ctx.GameHandler(ref ctx, ref view);
                    // 默认回退: 未显式设置 GameHandler 时, 按子类型自动分发到已注册的 BTCustomNode
                    long sub = view.Node.LongCount > 0 ? view.GetLong(0) : 0;
                    return BTNodeRuntimeRegistry.Execute(sub, ref ctx, ref view, out var r) ? r : BTNodeState.Failed;
                }

                default:
                    return BTNodeState.Failed;
            }
        }

        // ================================================================
        // 组合节点
        // ================================================================

        /// <summary>
        /// Sequence(NodeCanvas Sequencer): 顺序执行, 子节点 Failed 则短路。
        /// Long[0] bit0 = dynamic: 每帧从 0 重估高优先级子节点, 切换时复位被顶掉的子节点。
        /// </summary>
        private static BTNodeState EvaluateSequence(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                    NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int count = tree->Nodes[nodeIndex].ChildCount;
            bool dynamic = (GetNodeLong(tree, nodeIndex, 0, 0) & 1) != 0;
            var ns = nodeStates[nodeIndex];
            int lastRunning = ns.Current;
            int start = dynamic ? 0 : lastRunning;

            for (int i = start; i < count; i++)
            {
                var r = EvaluateNode(tree, GetChild(tree, nodeIndex, i), ref state, nodeStates, ref ctx);

                if (r == BTNodeState.Running)
                {
                    if (dynamic && i < lastRunning)
                        for (int j = i + 1; j <= lastRunning; j++)
                            ResetNode(tree, ref ctx, GetChild(tree, nodeIndex, j));
                    ns.Current = i;
                    nodeStates[nodeIndex] = ns;
                    return BTNodeState.Running;
                }

                if (r == BTNodeState.Failed)
                {
                    if (dynamic && i < lastRunning)
                        for (int j = i + 1; j <= lastRunning; j++)
                            ResetNode(tree, ref ctx, GetChild(tree, nodeIndex, j));
                    ns.Current = 0;
                    nodeStates[nodeIndex] = ns;
                    return BTNodeState.Failed;
                }
                // Optional: 忽略成败, 继续下一个子节点(NodeCanvas 语义)
            }

            ns.Current = 0;
            nodeStates[nodeIndex] = ns;
            return BTNodeState.Success;
        }

        /// <summary>
        /// Selector(NodeCanvas Selector): 选择执行, 子节点 Success 则短路。
        /// Long[0] bit0 = dynamic: 每帧从 0 重估高优先级子节点, 切换时复位被顶掉的子节点。
        /// </summary>
        private static BTNodeState EvaluateSelector(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                    NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int count = tree->Nodes[nodeIndex].ChildCount;
            bool dynamic = (GetNodeLong(tree, nodeIndex, 0, 0) & 1) != 0;
            var ns = nodeStates[nodeIndex];
            int lastRunning = ns.Current;
            int start = dynamic ? 0 : lastRunning;

            for (int i = start; i < count; i++)
            {
                var r = EvaluateNode(tree, GetChild(tree, nodeIndex, i), ref state, nodeStates, ref ctx);

                if (r == BTNodeState.Running)
                {
                    if (dynamic && i < lastRunning)
                        for (int j = i + 1; j <= lastRunning; j++)
                            ResetNode(tree, ref ctx, GetChild(tree, nodeIndex, j));
                    ns.Current = i;
                    nodeStates[nodeIndex] = ns;
                    return BTNodeState.Running;
                }

                if (r == BTNodeState.Success)
                {
                    if (dynamic && i < lastRunning)
                        for (int j = i + 1; j <= lastRunning; j++)
                            ResetNode(tree, ref ctx, GetChild(tree, nodeIndex, j));
                    ns.Current = 0;
                    nodeStates[nodeIndex] = ns;
                    return BTNodeState.Success;
                }
                // Optional: 忽略成败, 继续下一个子节点(NodeCanvas 语义)
            }

            ns.Current = 0;
            nodeStates[nodeIndex] = ns;
            return BTNodeState.Failed;
        }

        /// <summary>
        /// Timeout(NodeCanvas): 子节点 Running 超过设定秒数则复位并返回 Failed。
        /// 计时器为本节点局部(存 Elapsed, 不进黑板); 子节点非 Running 时清零。
        /// </summary>
        private static BTNodeState EvaluateTimeout(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                   NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int child = GetChild(tree, nodeIndex, 0);
            if (child < 0) return BTNodeState.Optional;
            float limit = GetWeight(ref ctx, tree, nodeIndex, 0);
            var ns = nodeStates[nodeIndex];

            if (nodeStates[child].Status != BTNodeState.Running) ns.Elapsed = 0f;

            var r = EvaluateNode(tree, child, ref state, nodeStates, ref ctx);
            if (r == BTNodeState.Running)
            {
                ns.Elapsed += ctx.DeltaTime;
                if (limit > 0f && ns.Elapsed >= limit)
                {
                    ResetNode(tree, ref ctx, child);
                    ns.Elapsed = 0f;
                    nodeStates[nodeIndex] = ns;
                    return BTNodeState.Failed;
                }
                nodeStates[nodeIndex] = ns;
                return BTNodeState.Running;
            }
            ns.Elapsed = 0f;
            nodeStates[nodeIndex] = ns;
            return r;
        }

        /// <summary>
        /// WaitUntil(NodeCanvas): child0=条件, child1=被包子树(可选)。
        /// 条件未满足前持续 Running; 满足后执行子树(无子树时返回 Success)。
        /// </summary>
        private static BTNodeState EvaluateWaitUntil(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                     NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            if (tree->Nodes[nodeIndex].ChildCount < 1) return BTNodeState.Optional;
            var ns = nodeStates[nodeIndex];
            bool accessed = (ns.Flags & 1) != 0;

            if (!accessed)
            {
                var c = EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, nodeStates, ref ctx);
                if (c != BTNodeState.Success) return BTNodeState.Running;
                accessed = true;
                ns.Flags |= 1;
                nodeStates[nodeIndex] = ns;
            }

            if (tree->Nodes[nodeIndex].ChildCount < 2) return BTNodeState.Success;
            return EvaluateNode(tree, GetChild(tree, nodeIndex, 1), ref state, nodeStates, ref ctx);
        }

        /// <summary>
        /// Guard(NodeCanvas): 同一实体同一 token 的 Guard 互斥。
        /// 锁只在子节点 Running 期间持有; 子节点终结或被复位时释放。
        /// </summary>
        private static BTNodeState EvaluateGuard(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                 NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int child = GetChild(tree, nodeIndex, 0);
            if (child < 0) return BTNodeState.Optional;

            // token: 优先黑板字符串(Long[0]=键), 否则字面 String[0]
            ulong token = 0;
            long key = GetNodeLong(tree, nodeIndex, 0, 0);
            if (key != 0 && ctx.Blackboard.IsCreated)
            {
                var s = ctx.Blackboard.GetString((ulong)key, default);
                token = BTBlackboardRuntime.HashKey(s);
            }
            var node = tree->Nodes[nodeIndex];
            if (token == 0 && node.StringCount > 0)
            {
                var strings = (BlobString*)tree->Strings.GetUnsafePtr();
                var literal = strings[node.StringStart].ToString();
                token = BTBlackboardRuntime.HashKey(literal);
            }
            if (token == 0) token = (ulong)(nodeIndex + 1);

            var ns = nodeStates[nodeIndex];
            // 新进入: 先释放自己可能残留的锁
            if (ns.Status == BTNodeState.None) BTManager.ClearGuard(ctx.Self, token, nodeIndex);

            int holder = BTManager.GuardHolder(ctx.Self, token);
            if (holder >= 0 && holder != nodeIndex)
                return GetNodeLong(tree, nodeIndex, 1, 0) != 0 ? BTNodeState.Running : BTNodeState.Failed;

            BTManager.SetGuard(ctx.Self, token, nodeIndex);
            var r = EvaluateNode(tree, child, ref state, nodeStates, ref ctx);
            if (r != BTNodeState.Running) BTManager.ClearGuard(ctx.Self, token, nodeIndex);
            return r;
        }

        /// <summary>
        /// Interruptor(NodeCanvas): child0=条件, child1=被包。
        /// 条件为假时执行子树; 条件为真且子树 Running 时中断(复位)并返回 Failed。
        /// </summary>
        private static BTNodeState EvaluateInterruptor(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                       NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            if (tree->Nodes[nodeIndex].ChildCount < 2) return BTNodeState.Optional;
            int body = GetChild(tree, nodeIndex, 1);
            var c = EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, nodeStates, ref ctx);
            if (c != BTNodeState.Success)
                return EvaluateNode(tree, body, ref state, nodeStates, ref ctx);

            if (nodeStates[body].Status == BTNodeState.Running)
                ResetNode(tree, ref ctx, body);
            return BTNodeState.Failed;
        }

        /// <summary>
        /// Filter(NodeCanvas): 冷却(子节点终结后进入冷却)或限次(最多执行 N 次)。
        /// Long[0]=模式 0冷却/1限次; Float[0]=冷却秒或次数(Long[1]=键); Long[2]=policy; Long[3]=被过滤时返 Optional
        /// </summary>
        private static BTNodeState EvaluateFilter(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                  NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int child = GetChild(tree, nodeIndex, 0);
            if (child < 0) return BTNodeState.Optional;
            long mode = GetNodeLong(tree, nodeIndex, 0, 0);
            long policy = GetNodeLong(tree, nodeIndex, 2, 0);
            bool optionalWhenLimited = GetNodeLong(tree, nodeIndex, 3, 0) != 0;
            float amount = GetWeight(ref ctx, tree, nodeIndex, 0);
            var blocked = optionalWhenLimited ? BTNodeState.Optional : BTNodeState.Failed;
            var ns = nodeStates[nodeIndex];

            if (mode == 0)
            {
                if (ns.Elapsed > 0f)
                {
                    ns.Elapsed -= ctx.DeltaTime;
                    if (ns.Elapsed < 0f) ns.Elapsed = 0f;
                    nodeStates[nodeIndex] = ns;
                    return blocked;
                }
                var r = EvaluateNode(tree, child, ref state, nodeStates, ref ctx);
                if (r == BTNodeState.Success || r == BTNodeState.Failed)
                {
                    ns.Elapsed = amount;
                    nodeStates[nodeIndex] = ns;
                }
                return r;
            }

            int maxCount = (int)amount;
            if (ns.Iteration >= maxCount) return blocked;
            var r2 = EvaluateNode(tree, child, ref state, nodeStates, ref ctx);
            bool count =
                (r2 == BTNodeState.Success && policy == 1) ||
                (r2 == BTNodeState.Failed && policy == 2) ||
                ((r2 == BTNodeState.Success || r2 == BTNodeState.Failed) && policy == 0);
            if (count)
            {
                ns.Iteration++;
                nodeStates[nodeIndex] = ns;
            }
            return r2;
        }

        /// <summary>
        /// Iterator(NodeCanvas): 按列表长度逐个执行子节点, 当前索引写入黑板供游戏层取元素。
        /// Long[0]=长度键(0=用 Long[1] 字面); Long[2]=终止(0无 1首成功 2首失败); Long[3]=索引写入键
        /// </summary>
        private static BTNodeState EvaluateIterator(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                    NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int child = GetChild(tree, nodeIndex, 0);
            if (child < 0) return BTNodeState.Optional;

            long lenKey = GetNodeLong(tree, nodeIndex, 0, 0);
            int listCount = (int)(lenKey != 0 && ctx.Blackboard.IsCreated
                ? ctx.Blackboard.GetInt((ulong)lenKey, 0)
                : GetNodeLong(tree, nodeIndex, 1, 0));
            if (listCount <= 0) return BTNodeState.Failed;

            long term = GetNodeLong(tree, nodeIndex, 2, 0);
            long storeKey = GetNodeLong(tree, nodeIndex, 3, 0);
            var ns = nodeStates[nodeIndex];
            if (storeKey != 0 && ctx.Blackboard.IsCreated)
                ctx.Blackboard.SetInt((ulong)storeKey, ns.Iteration);

            var r = EvaluateNode(tree, child, ref state, nodeStates, ref ctx);
            if (r == BTNodeState.Success && term == 1) return BTNodeState.Success;
            if (r == BTNodeState.Failed && term == 2) return BTNodeState.Failed;
            if (r == BTNodeState.Running) return BTNodeState.Running;

            if (ns.Iteration + 1 >= listCount)
            {
                ns.Iteration = 0;
                nodeStates[nodeIndex] = ns;
                return r;
            }
            ns.Iteration++;
            ResetNode(tree, ref ctx, child);
            nodeStates[nodeIndex] = ns;
            return BTNodeState.Running;
        }

        /// <summary>
        /// Monitor(NodeCanvas): 监测被包子节点状态, 命中时执行监测动作。
        /// child0=被包, child1=动作; Long[0]=监测状态(0失败 1成功 10任意); Long[1]=返回模式(0原始 1动作)
        /// </summary>
        private static BTNodeState EvaluateMonitor(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                   NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            if (tree->Nodes[nodeIndex].ChildCount < 1) return BTNodeState.Optional;
            var ns = nodeStates[nodeIndex];
            var newStatus = EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, nodeStates, ref ctx);
            if (tree->Nodes[nodeIndex].ChildCount < 2) return newStatus;

            long monitorMode = GetNodeLong(tree, nodeIndex, 0, 0);
            bool execute =
                (newStatus == BTNodeState.Success && monitorMode == 1) ||
                (newStatus == BTNodeState.Failed && monitorMode == 0) ||
                (monitorMode == 10 && newStatus != BTNodeState.Running);

            if (execute && ns.Status != newStatus)
            {
                var a = EvaluateNode(tree, GetChild(tree, nodeIndex, 1), ref state, nodeStates, ref ctx);
                ns.MaskA = (int)a; // 记录监测动作状态
                if (a == BTNodeState.Running)
                {
                    nodeStates[nodeIndex] = ns;
                    return BTNodeState.Running;
                }
            }
            nodeStates[nodeIndex] = ns;

            if (GetNodeLong(tree, nodeIndex, 1, 0) == 1 && ns.MaskA != 0)
            {
                var actionStatus = (BTNodeState)ns.MaskA;
                if (actionStatus != BTNodeState.None) return actionStatus;
            }
            return newStatus;
        }

        /// <summary>Optional(NodeCanvas): 正常执行子节点, 但返回 Optional(父组合忽略其成败)。</summary>
        private static BTNodeState EvaluateOptional(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                    NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int child = GetChild(tree, nodeIndex, 0);
            if (child < 0) return BTNodeState.Optional;
            if (nodeStates[nodeIndex].Status == BTNodeState.None) ResetNode(tree, ref ctx, child);
            var r = EvaluateNode(tree, child, ref state, nodeStates, ref ctx);
            return r == BTNodeState.Running ? BTNodeState.Running : BTNodeState.Optional;
        }

        /// <summary>Remapper(NodeCanvas): 把子节点 Success/Failed 重映射为指定状态。</summary>
        private static BTNodeState EvaluateRemapper(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                    NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int child = GetChild(tree, nodeIndex, 0);
            if (child < 0) return BTNodeState.Optional;
            var successRemap = GetNodeLong(tree, nodeIndex, 0, 1) != 0 ? BTNodeState.Success : BTNodeState.Failed;
            var failRemap = GetNodeLong(tree, nodeIndex, 1, 0) != 0 ? BTNodeState.Success : BTNodeState.Failed;

            var r = EvaluateNode(tree, child, ref state, nodeStates, ref ctx);
            if (r == BTNodeState.Success) return successRemap;
            if (r == BTNodeState.Failed) return failRemap;
            return r;
        }

        /// <summary>
        /// FlipSelector(NodeCanvas FlipSelector): 成功的子节点"移到末尾", 使之前失败过的子节点优先再试。
        /// 实现: 用旋转起点 offset 表达队列旋转(不改动 Blob 顺序); 起点持久化在 Current。
        /// </summary>
        private static BTNodeState EvaluateFlipSelector(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                        NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int count = tree->Nodes[nodeIndex].ChildCount;
            if (count == 0) return BTNodeState.Failed;
            var ns = nodeStates[nodeIndex];
            int offset = ns.Current;

            for (int k = 0; k < count; k++)
            {
                int c = (offset + k) % count;
                var r = EvaluateNode(tree, GetChild(tree, nodeIndex, c), ref state, nodeStates, ref ctx);
                if (r == BTNodeState.Running)
                {
                    ns.Current = c;
                    nodeStates[nodeIndex] = ns;
                    return BTNodeState.Running;
                }
                if (r == BTNodeState.Success)
                {
                    ns.Current = (c + 1) % count; // 该子节点排到末尾
                    nodeStates[nodeIndex] = ns;
                    return BTNodeState.Success;
                }
            }
            nodeStates[nodeIndex] = ns;
            return BTNodeState.Failed;
        }

        /// <summary>读取子节点权重: Float[i] 为字面值, Long[i] 为黑板键(非0 时优先读黑板)。</summary>
        private static float GetWeight(ref BTContext ctx, BTRootBlob* tree, int nodeIndex, int i)
        {
            var node = tree->Nodes[nodeIndex];
            if (i < 0 || i >= node.FloatCount) return 0f;
            var floats = (float*)tree->Floats.GetUnsafePtr();
            float w = floats[node.FloatStart + i];
            long key = GetNodeLong(tree, nodeIndex, i, 0);
            return key != 0 && ctx.Blackboard.IsCreated ? ctx.Blackboard.GetFloat((ulong)key, w) : w;
        }

        /// <summary>
        /// UtilitySelector(NodeCanvas PrioritySelector 工具AI): 按权重(utility)选子节点。
        /// Long[N] = dynamic: 每帧重估最高权子节点, 切换时复位旧子节点。
        /// </summary>
        private static BTNodeState EvaluateUtilitySelector(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                           NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int count = tree->Nodes[nodeIndex].ChildCount;
            if (count == 0) return BTNodeState.Failed;
            bool dynamic = GetNodeLong(tree, nodeIndex, count, 0) != 0;
            var ns = nodeStates[nodeIndex];

            int best = 0;
            float bestU = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                float u = GetWeight(ref ctx, tree, nodeIndex, i);
                if (u > bestU) { bestU = u; best = i; }
            }

            if (dynamic)
            {
                if (ns.Current != best && nodeStates[nodeIndex].Status == BTNodeState.Running)
                    ResetNode(tree, ref ctx, GetChild(tree, nodeIndex, ns.Current));
                ns.Current = best;
                nodeStates[nodeIndex] = ns;
                return EvaluateNode(tree, GetChild(tree, nodeIndex, best), ref state, nodeStates, ref ctx);
            }

            // 非 dynamic: 按权重降序遍历(确定性, 等价 NodeCanvas 的一次排序)
            if (count > 16) count = 16;
            var order = stackalloc int[16];
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = 1; i < count; i++)
            {
                int cur = order[i];
                float cu = GetWeight(ref ctx, tree, nodeIndex, cur);
                int j = i - 1;
                while (j >= 0 && GetWeight(ref ctx, tree, nodeIndex, order[j]) < cu) { order[j + 1] = order[j]; j--; }
                order[j + 1] = cur;
            }

            for (int k = 0; k < count; k++)
            {
                var r = EvaluateNode(tree, GetChild(tree, nodeIndex, order[k]), ref state, nodeStates, ref ctx);
                if (r == BTNodeState.Success)
                {
                    ns.Current = 0;
                    nodeStates[nodeIndex] = ns;
                    return BTNodeState.Success;
                }
                if (r == BTNodeState.Running)
                {
                    ns.Current = order[k];
                    nodeStates[nodeIndex] = ns;
                    return BTNodeState.Running;
                }
            }
            ns.Current = 0;
            nodeStates[nodeIndex] = ns;
            return BTNodeState.Failed;
        }

        /// <summary>
        /// ProbabilitySelector(NodeCanvas): 按权重概率选子节点, 失败则换下一个。
        /// 首跑掷一次骰子存 Elapsed; 已失败子节点标记存 MaskA; failChance 在 Float[N](键 Long[N])。
        /// </summary>
        private static BTNodeState EvaluateProbabilitySelector(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                               NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int count = tree->Nodes[nodeIndex].ChildCount;
            if (count == 0) return BTNodeState.Failed;
            var ns = nodeStates[nodeIndex];

            if (ns.Status == BTNodeState.None)
            {
                long h = state.TotalExecutions ^ ((long)nodeIndex * 2654435761L);
                ns.Elapsed = (float)((h & 0xFFFF) % 10000) / 10000f; // 首跑掷骰(确定性伪随机)
                nodeStates[nodeIndex] = ns;
            }
            float dice = ns.Elapsed;

            float failChance = GetWeight(ref ctx, tree, nodeIndex, count);
            long failKey = GetNodeLong(tree, nodeIndex, count, 0);
            if (failKey != 0 && ctx.Blackboard.IsCreated)
                failChance = ctx.Blackboard.GetFloat((ulong)failKey, failChance);

            float total = failChance;
            for (int i = 0; i < count; i++)
                if ((ns.MaskA & (1 << i)) == 0)
                    total += GetWeight(ref ctx, tree, nodeIndex, i);
            if (total <= 0f) return BTNodeState.Failed;

            float prob = failChance / total;
            if (dice < prob) return BTNodeState.Failed;

            for (int i = 0; i < count; i++)
            {
                if ((ns.MaskA & (1 << i)) != 0) continue;
                prob += GetWeight(ref ctx, tree, nodeIndex, i) / total;
                if (dice <= prob)
                {
                    var r = EvaluateNode(tree, GetChild(tree, nodeIndex, i), ref state, nodeStates, ref ctx);
                    if (r == BTNodeState.Success || r == BTNodeState.Running) return r;
                    if (r == BTNodeState.Failed)
                    {
                        ns.MaskA |= (1 << i);
                        nodeStates[nodeIndex] = ns;
                        return BTNodeState.Running;
                    }
                }
            }
            return BTNodeState.Failed;
        }

        /// <summary>
        /// StepSequencer(NodeCanvas StepIterator): 每次执行只跑一个子节点并返回其状态。
        /// 注: NodeCanvas 在 reset 时步进; HYC 的 ResetNode 会清零状态, 故改为"子节点终结即步进"。
        /// </summary>
        private static BTNodeState EvaluateStepSequencer(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                         NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int count = tree->Nodes[nodeIndex].ChildCount;
            if (count == 0) return BTNodeState.Failed;
            var ns = nodeStates[nodeIndex];
            int idx = ns.Iteration % count;

            var r = EvaluateNode(tree, GetChild(tree, nodeIndex, idx), ref state, nodeStates, ref ctx);
            if (r != BTNodeState.Running)
            {
                ns.Iteration = (idx + 1) % count;
                nodeStates[nodeIndex] = ns;
            }
            return r;
        }

        /// <summary>
        /// Switch(NodeCanvas Switch): 按 int case 选择一个子节点执行。
        /// Long[0]=case 黑板键(0=用 Long[1] 字面), Long[2]=dynamic, Long[3]=越界模式(0=取模循环, 1=返 Failure)
        /// </summary>
        private static BTNodeState EvaluateSwitch(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                  NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int count = tree->Nodes[nodeIndex].ChildCount;
            if (count == 0) return BTNodeState.Optional;
            bool dynamic = GetNodeLong(tree, nodeIndex, 2, 0) != 0;
            bool loopIndex = GetNodeLong(tree, nodeIndex, 3, 0) == 0;
            var ns = nodeStates[nodeIndex];

            if (ns.Status == BTNodeState.None || dynamic)
            {
                long caseKey = GetNodeLong(tree, nodeIndex, 0, 0);
                long caseVal = caseKey != 0 && ctx.Blackboard.IsCreated
                    ? ctx.Blackboard.GetInt((ulong)caseKey, 0)
                    : GetNodeLong(tree, nodeIndex, 1, 0);

                int cur = (int)caseVal;
                if (loopIndex) { cur = cur % count; if (cur < 0) cur += count; }

                if (cur != ns.Current && ns.Status == BTNodeState.Running)
                    ResetNode(tree, ref ctx, GetChild(tree, nodeIndex, ns.Current));

                ns.Current = cur;
                nodeStates[nodeIndex] = ns;
                if (cur < 0 || cur >= count) return BTNodeState.Failed;
            }

            return EvaluateNode(tree, GetChild(tree, nodeIndex, ns.Current), ref state, nodeStates, ref ctx);
        }

        /// <summary>
        /// BinarySelector(NodeCanvas): child0=条件子树, child1=条件真, child2=条件假。
        /// Long[0] = dynamic: 每帧重估条件, 结果变化时复位另一侧子节点。
        /// </summary>
        private static BTNodeState EvaluateBinarySelector(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                          NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            if (tree->Nodes[nodeIndex].ChildCount < 3) return BTNodeState.Optional;
            bool dynamic = GetNodeLong(tree, nodeIndex, 0, 0) != 0;
            var ns = nodeStates[nodeIndex];
            bool evaluated = (ns.Flags & 1) != 0;

            if (dynamic || !evaluated)
            {
                var c = EvaluateNode(tree, GetChild(tree, nodeIndex, 0), ref state, nodeStates, ref ctx);
                int last = ns.Current;
                int pick = c == BTNodeState.Success ? 1 : 2;
                if (evaluated && pick != last)
                    ResetNode(tree, ref ctx, GetChild(tree, nodeIndex, last));
                ns.Current = pick;
                ns.Flags |= 1;
                nodeStates[nodeIndex] = ns;
            }

            return EvaluateNode(tree, GetChild(tree, nodeIndex, ns.Current), ref state, nodeStates, ref ctx);
        }

        /// <summary>
        /// Parallel(NodeCanvas Parallel): 每帧执行全部子节点, 按策略汇总。
        /// Long[0] = policy(0=FirstFailure, 1=FirstSuccess, 2=FirstSuccessOrFailure)
        /// Long[1] = dynamic(已完成的子节点重复执行)
        /// 状态: MaskA = finishedConnections 位掩码, Iteration = finishedConnectionsCount
        /// </summary>
        private static BTNodeState EvaluateParallel(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                    NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            int count = tree->Nodes[nodeIndex].ChildCount;
            if (count > 32) count = 32; // finished 位掩码上限
            long policy = GetNodeLong(tree, nodeIndex, 0, 0);
            bool dynamic = GetNodeLong(tree, nodeIndex, 1, 0) != 0;
            var ns = nodeStates[nodeIndex];
            BTNodeState deferred = BTNodeState.None;
            int runningMask = 0;

            for (int i = 0; i < count; i++)
            {
                bool finished = (ns.MaskA & (1 << i)) != 0;
                if (!dynamic && finished) continue;

                int child = GetChild(tree, nodeIndex, i);
                // 已完成的子节点在重复(dynamic)模式下先复位再跑
                if (finished) ResetNode(tree, ref ctx, child);

                var r = EvaluateNode(tree, child, ref state, nodeStates, ref ctx);

                if (deferred == BTNodeState.None)
                {
                    if (r == BTNodeState.Failed && (policy == 0 || policy == 2)) deferred = BTNodeState.Failed;
                    if (r == BTNodeState.Success && (policy == 1 || policy == 2)) deferred = BTNodeState.Success;
                }

                if (r == BTNodeState.Running) runningMask |= (1 << i);
                else if (!finished) { ns.MaskA |= (1 << i); ns.Iteration++; }
            }

            if (deferred != BTNodeState.None)
            {
                // ResetRunning: 复位仍处于 Running 的子节点
                for (int i = 0; i < count; i++)
                    if ((runningMask & (1 << i)) != 0)
                        ResetNode(tree, ref ctx, GetChild(tree, nodeIndex, i));
                nodeStates[nodeIndex] = ns;
                return deferred;
            }

            if (ns.Iteration >= count)
            {
                nodeStates[nodeIndex] = ns;
                // FirstFailure→Success, FirstSuccess→Failure;
                // FirstSuccessOrFailure 与 NodeCanvas 一致: 不自发终结, 落回 Running
                return policy == 0 ? BTNodeState.Success
                     : policy == 1 ? BTNodeState.Failed
                     : BTNodeState.Running;
            }

            nodeStates[nodeIndex] = ns;
            return BTNodeState.Running;
        }

        /// <summary>由种子确定性生成洗牌顺序(同种子跨帧稳定, 修复原实现每帧重洗牌的问题)。</summary>
        private static void BuildOrder(int count, int seed, int* order)
        {
            for (int i = 0; i < count; i++) order[i] = i;
            uint s = (uint)(seed == 0 ? 1 : seed);
            for (int i = count - 1; i > 0; i--)
            {
                s = s * 1664525u + 1013904223u;
                int j = (int)(s % (uint)(i + 1));
                int t = order[i]; order[i] = order[j]; order[j] = t;
            }
        }

        /// <summary>取本节点的随机种子(首次进入时生成并持久化到 Flags)。</summary>
        private static int EnsureSeed(ref BTRunState state, NativeArray<BTNodeRuntimeState> nodeStates, int nodeIndex)
        {
            var ns = nodeStates[nodeIndex];
            if (ns.Flags == 0)
            {
                long h = state.TotalExecutions ^ ((long)nodeIndex * 2654435761L);
                ns.Flags = (int)(h & 0x7FFFFFFF);
                if (ns.Flags == 0) ns.Flags = 1;
                nodeStates[nodeIndex] = ns;
            }
            return ns.Flags;
        }

        private static BTNodeState EvaluateRandomSelector(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                          NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            var count = tree->Nodes[nodeIndex].ChildCount;
            if (count == 0) return BTNodeState.Failed;
            if (count > 16) count = 16;
            var order = stackalloc int[16];
            BuildOrder(count, EnsureSeed(ref state, nodeStates, nodeIndex), order);

            for (int k = 0; k < count; k++)
            {
                var r = EvaluateNode(tree, GetChild(tree, nodeIndex, order[k]), ref state, nodeStates, ref ctx);
                if (r == BTNodeState.Success) return BTNodeState.Success;
                if (r == BTNodeState.Running) return BTNodeState.Running;
            }
            return BTNodeState.Failed;
        }

        private static BTNodeState EvaluateRandomSequence(BTRootBlob* tree, int nodeIndex, ref BTRunState state,
                                                          NativeArray<BTNodeRuntimeState> nodeStates, ref BTContext ctx)
        {
            var count = tree->Nodes[nodeIndex].ChildCount;
            if (count == 0) return BTNodeState.Success;
            if (count > 16) count = 16;
            var order = stackalloc int[16];
            BuildOrder(count, EnsureSeed(ref state, nodeStates, nodeIndex), order);

            for (int k = 0; k < count; k++)
            {
                var r = EvaluateNode(tree, GetChild(tree, nodeIndex, order[k]), ref state, nodeStates, ref ctx);
                if (r == BTNodeState.Failed) return BTNodeState.Failed;
                if (r == BTNodeState.Running) return BTNodeState.Running;
            }
            return BTNodeState.Success;
        }
    }
}
