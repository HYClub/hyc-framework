// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTInterpreterSystem.cs
// 说明: 行为树 ECS 驱动系统 - 对挂有 RunningBT 的实体执行树
//       挂载: 由游戏层决定放入哪个 UpdateGroup(默认根组)
// ============================================================



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
        /// G4: Tick 间隔(秒)。0 = 每帧执行; &gt;0 = 按该频率节流(如 0.1 = 10Hz)。
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
    /// 需要 [UpdateInGroup] 由游戏层指定(默认挂初始化组, 游戏层可覆盖)。
    /// </summary>
    public partial struct BTInterpreterSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RunningBT>();
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        public unsafe void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            foreach (var (bt, entity) in SystemAPI.Query<RefRW<RunningBT>>().WithEntityAccess())
            {
                var tree = BTManager.TryGet(bt.ValueRO.TreeId);
                if (tree == null)
                {
                    bt.ValueRW.RunState.Result = BTNodeState.Failed;
                    continue;
                }

                // G4: Tick 降频(0 = 每帧; >0 = 按间隔节流, 用累计时间作为本次 deltaTime)
                float interval = bt.ValueRO.TickInterval;
                float stepDt = dt;
                if (interval > 0f)
                {
                    bt.ValueRW.RunState.TickAccum += dt;
                    if (bt.ValueRW.RunState.TickAccum < interval) continue;
                    stepDt = bt.ValueRW.RunState.TickAccum;
                    bt.ValueRW.RunState.TickAccum = 0f;
                }

                // G2: 黑板解析 — 实体私有黑板从注册表取(游戏层可自行 SetForEntity 登记),
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
        }
    }
}
