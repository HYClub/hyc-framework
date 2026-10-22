using Unity.Entities;

namespace HYC.Framework.Sync
{
    /// <summary>
    /// 帧同步逻辑系统基类。游戏逻辑系统继承本类，只在 <see cref="Step"/> 内写确定性逻辑。
    /// OnUpdate 被封闭为闸门：仅当 Driver 本帧推进了新 tick 才触发 Step（未齐帧的帧直接跳过），
    /// 逻辑执行速率与渲染帧率解耦，两端严格同 tick 同序执行。
    ///
    /// ⚠️ 游戏侧具体类必须自带 [UpdateInGroup(typeof(UpdateGroup_A3))]（或自有组）——
    /// Entities ILPP 用 Cecil 直读类型特性、不继承泛型基类特性；SyncBootstrap.Install 会校验并报错提示。
    /// </summary>
    public abstract partial class LockstepLogicSystem : SystemBase
    {
        private int _lastAdvanced = -1;

        /// <summary>本次 Step 的逻辑帧序号（0 = 尚未推进）。</summary>
        protected int CurrentTick { get; private set; }

        protected sealed override void OnCreate()
        {
            RequireForUpdate<SyncTickSingleton>();
        }

        // Entities SourceGen 会把 OnUpdate 改名为 __OnUpdate_xxx，带 sealed 会触发 CS0238
        // （重命名后已不是 override），故只能 override 不能 sealed。子类禁止 override OnUpdate
        // （会绕过 tick 闸门），只实现 Step。
        protected override void OnUpdate()
        {
            var tick = SystemAPI.GetSingleton<SyncTickSingleton>();
            if (tick.AdvancedCount <= 0 || tick.AdvancedCount == _lastAdvanced) return;
            _lastAdvanced = tick.AdvancedCount;
            CurrentTick = tick.CurrentTick;
            Step(CurrentTick);
        }

        /// <summary>推进一个逻辑帧。两端同 tick 调用，禁读非确定性数据（Time / UnityEngine.Random / 帧计数）。</summary>
        protected abstract void Step(int tick);

        /// <summary>取本 tick 指定玩家的输入（经对应 LockstepInputSystem{TInput} 转发）。</summary>
        protected bool TryGetInput<TInput>(int playerId, out TInput input)
            where TInput : unmanaged, IComponentData
        {
            var provider = World.GetExistingSystemManaged<LockstepInputSystem<TInput>>();
            if (provider == null)
            {
                input = default;
                return false;
            }
            return provider.TryGetInput(CurrentTick, playerId, out input);
        }
    }
}
