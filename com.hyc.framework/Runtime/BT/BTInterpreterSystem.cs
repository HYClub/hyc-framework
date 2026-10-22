// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTInterpreterSystem.cs
// 说明: 行为树 ECS 驱动系统 - 对挂有 RunningBT 的实体执行树
//       挂载: 默认落 UpdateGroup_A3(Gameplay)，使用方可自行 CreateSystem 换组
// ============================================================



using HYC.Framework.Dots;
using Unity.Collections;
using Unity.Entities;

namespace HYC.Framework.BT
{
    /// <summary>树实例组件: 挂到需要跑行为树的实体上。</summary>
    public struct RunningBT : IComponentData
    {
        public long TreeId;
        public BTRunState RunState;
        /// <summary>
        /// Tick 间隔(秒)。0 = 每帧执行; &gt;0 = 按该频率节流(如 0.1 = 10Hz)。
        /// 节流时把累计时间作为本次 Tick 的 deltaTime 传入, 保证 Wait/Timeout/冷却等计时正确。
        /// </summary>
        public float TickInterval;
    }

    /// <summary>树执行结果组件(本帧树根结果), 供游戏层读取决策。</summary>
    public struct BTLastResult : IComponentData
    {
        public BTNodeState State;
    }

    /// <summary>
    /// 每帧对挂 RunningBT 的实体执行一次树 Tick。
    ///
    /// 默认落在 <see cref="UpdateGroup_A3"/>（Gameplay：simulation / skills / AI / combat），
    /// 所以行为树与技能、战斗处在同一梯队，执行顺序是确定的。
    /// 旧实现<b>没有</b> [UpdateInGroup]，系统被自动创建后落进 Root 组根级，
    /// 相对 A0~B9 整条阶梯的位置取决于 Unity 的自动排序 —— 同一份配置在改动
    /// 其他系统的 Update 属性后可能就变了顺序，且没有任何编译期提示。
    ///
    /// 使用方若要换组，Entities 不允许在一个 system 上叠两个 [UpdateInGroup]，
    /// 正确做法是自己 CreateSystem&lt;BTInterpreterSystem&gt;() 放进目标组，并给本类型加
    /// [DisableAutoCreation]（此时需自行保证每帧调用 Update）。
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_A3))]
    public partial struct BTInterpreterSystem : ISystem
    {
        /// <summary>
        /// 上一帧成功 Tick 的实体。用来发现"实体已从 <see cref="RunningBT"/> 查询里消失"。
        ///
        /// 为什么要自己盯：<c>BTManager</c> 的 per-node 执行态是 <c>Allocator.Persistent</c> 的
        /// <c>NativeArray</c>，原本唯一的释放路径是「树跑完（结果 != Running）时调
        /// <c>ClearNodeStates</c>」。但实体可能在树还 Running 时就被销毁——典型是
        /// 「旧实体被销毁并由新实体取代」（例如敌人被替换为新形态实体）。组件随实体一起消失，
        /// Query 不再返回它，那条释放路径<b>永远不会执行</b>，
        /// Persistent 分配就泄漏了（每死一个敌人漏一份，直到进 Play 退出才被
        /// <c>BTManager.DisposeAll</c> 兜住）。同理还有该实体的子树态与黑板。
        ///
        /// 所以这里显式记住上一帧活跃集合：本帧 Query 里没有、但上一帧有 → 实体已被销毁
        /// → 主动清掉它的全部 BT 运行态。
        ///
        /// 用两份 <see cref="NativeHashSet{T}"/> 交替（ISystem 内不能用托管字段）：
        /// 一份存上一帧、一份存本帧，帧末交换。容量 64 起步，按需自动增长。
        /// </summary>
        private NativeHashSet<Entity> _activePrev;
        private NativeHashSet<Entity> _activeCurr;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RunningBT>();
            _activePrev = new NativeHashSet<Entity>(64, Allocator.Persistent);
            _activeCurr = new NativeHashSet<Entity>(64, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            // World 销毁：把残留实体的运行态全部放掉，否则 BTManager 里的 NativeArray
            // 谁也释放不了（DisposeAll 只在重载时兜底）。
            BTManager.ClearAllRuntimeStates();
            if (_activePrev.IsCreated) _activePrev.Dispose();
            if (_activeCurr.IsCreated) _activeCurr.Dispose();
        }

        public unsafe void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            _activeCurr.Clear();

            foreach (var (bt, entity) in SystemAPI.Query<RefRW<RunningBT>>().WithEntityAccess())
            {
                _activeCurr.Add(entity);
                var tree = BTManager.TryGet(bt.ValueRO.TreeId);
                if (tree == null)
                {
                    bt.ValueRW.RunState.Result = BTNodeState.Failed;
                    continue;
                }

                // Tick 降频(0 = 每帧; >0 = 按间隔节流, 用累计时间作为本次 deltaTime)
                float interval = bt.ValueRO.TickInterval;
                float stepDt = dt;
                if (interval > 0f)
                {
                    bt.ValueRW.RunState.TickAccum += dt;
                    if (bt.ValueRW.RunState.TickAccum < interval) continue;
                    stepDt = bt.ValueRW.RunState.TickAccum;
                    bt.ValueRW.RunState.TickAccum = 0f;
                }

                // 黑板解析 — 实体私有黑板从注册表取(游戏层可自行 SetForEntity 登记),
                // 全局共享黑板供跨实体数据使用。未登记时均为 default(节点内部会做 IsCreated 判断)。
                BTBlackboardRuntime entityBB = default;
                BTBlackboardRegistry.TryGetForEntity(entity, out entityBB);
                BTBlackboardRuntime sharedBB = default;
                BTBlackboardRegistry.TryGetGlobal(out sharedBB);

                var ctx = new BTContext
                {
                    Self = entity,
                    DeltaTime = stepDt,
                    Blackboard = entityBB,
                    SharedBlackboard = sharedBB,
                    GameHandler = null,
                };

                var result = BTInterpreter.Tick(tree, ref bt.ValueRW.RunState, ref ctx);
                bt.ValueRW.RunState.Result = result;
                // 树本帧未处于 Running(已完成/失败/孤立), 清除该实体的子树运行态与节点级执行态, 避免状态泄漏或重复复用
                if (result != BTNodeState.Running)
                {
                    BTManager.ClearSubTreeStates(entity);
                    BTManager.ClearNodeStates(entity);
                }
            }

            // 实体已从查询里消失（被销毁，或被移除了 RunningBT）→ 它在 BTManager 里的
            // Persistent NativeArray / 黑板永远等不到「树跑完」那条释放路径，在这里兜底。
            ReleaseVanishedEntities();

            // 两份集合交替：本帧变下一帧的「上一帧」
            var t = _activePrev;
            _activePrev = _activeCurr;
            _activeCurr = t;
        }

        /// <summary>
        /// 释放「上一帧活跃、本帧已不在 <see cref="RunningBT"/> 查询里」的实体的全部 BT 运行态。
        ///
        /// 这里不做 <c>EntityManager.Exists</c> 复核：实体可能只是被摘掉了 RunningBT 而仍存在，
        /// 那种情况同样不该保留 per-node 执行态（下次挂回组件应当从头开始）。
        /// 遍历 <c>_activePrev</c> 时只读 <c>_activeCurr</c>，无别名冲突。
        /// </summary>
        private void ReleaseVanishedEntities()
        {
            // NativeHashSet 的计数属性叫 Count（不是 Length），且它没有 ToNativeArray。
            if (!_activePrev.IsCreated || _activePrev.Count == 0)
                return;

            // 本帧一个活跃实体都没有 → 上一帧的必然全部消失，整批清掉（不必逐个 Contains）。
            if (!_activeCurr.IsCreated || _activeCurr.Count == 0)
            {
                var it0 = _activePrev.GetEnumerator();
                while (it0.MoveNext())
                    BTManager.ReleaseEntityRuntimeState(it0.Current);
                return;
            }

            using var it = _activePrev.GetEnumerator();
            while (it.MoveNext())
            {
                if (_activeCurr.Contains(it.Current)) continue;
                BTManager.ReleaseEntityRuntimeState(it.Current);
            }
        }
    }
}
