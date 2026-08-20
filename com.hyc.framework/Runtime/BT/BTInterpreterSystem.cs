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

                var ctx = new BTContext
                {
                    Self = entity,
                    DeltaTime = dt,
                    Blackboard = default, // 游戏层可自行附加黑板
                    GameHandler = null,
                };

                bt.ValueRW.RunState.Result = BTInterpreter.Tick(tree, ref bt.ValueRW.RunState, ref ctx);
            }
        }
    }
}
