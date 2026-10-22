using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace HYC.Framework.Sync
{
    /// <summary>
    /// 帧同步输入系统基类。游戏定义 blittable 输入结构 TInput（unmanaged IComponentData），
    /// 继承本类并实现 <see cref="SampleLocal"/>（采集本端输入）。
    ///
    /// 数据流：本帧 OnUpdate 采样 → 本地入库（立即可读）→ 二进制经 Driver 发远端；
    /// 远端输入由 Driver 转 <see cref="ILockstepInputSink.OnRemoteInput"/> 入库。
    /// Driver 齐帧后逻辑系统经 <see cref="TryGetInput"/> 按 tick 取两端输入。
    ///
    /// ⚠️ 游戏侧具体类必须自带两个特性——Entities ILPP 用 Cecil 直读类型特性、不继承泛型基类特性：
    /// [UpdateInGroup(typeof(UpdateGroup_A0))]
    /// [UpdateBefore(typeof(LockstepDriver))]
    /// 后者保证本地输入在 Driver 齐帧检查前完成提交，缺失会导致推进降速（详见 61 号文档）。
    /// </summary>
    public abstract partial class LockstepInputSystem<TInput> : SystemBase, ILockstepInputSink
        where TInput : unmanaged, IComponentData
    {
        private NativeHashMap<int2, TInput> _inputs;
        private NativeHashMap<int, uint> _readyMask;
        private LockstepDriver _driver;

        protected override void OnCreate()
        {
            _inputs = new NativeHashMap<int2, TInput>(64, Allocator.Persistent);
            _readyMask = new NativeHashMap<int, uint>(64, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (_driver != null)
                _driver.UnregisterSink(this);
            if (_inputs.IsCreated)
                _inputs.Dispose();
            if (_readyMask.IsCreated)
                _readyMask.Dispose();
        }

        // Entities SourceGen 会把 OnUpdate 改名为 __OnUpdate_xxx，带 sealed 会触发 CS0238
        // （重命名后已不是 override），故只能 override 不能 sealed。子类禁止 override OnUpdate
        // （会绕过采样本帧的闸门），只实现 SampleLocal。
        protected override void OnUpdate()
        {
            if (_driver == null)
            {
                _driver = World.GetExistingSystemManaged<LockstepDriver>();
                if (_driver == null) return; // 同步层未装配：本系统静默不工作
                _driver.RegisterSink(this);
            }

            var players = SystemAPI.GetSingleton<PlayerTableSingleton>();
            var input = default(TInput);
            SampleLocal(players.LocalPlayerId, ref input);
            Submit(_driver.CurrentTick + 1, players.LocalPlayerId, input);
        }

        /// <summary>采集本端输入（默认不采，输入保持 default，供录制 / 基线测试）。</summary>
        protected virtual void SampleLocal(int localPlayerId, ref TInput input)
        {
        }

        private void Submit(int tick, int playerId, TInput input)
        {
            Store(tick, playerId, input);

            Span<byte> buffer = stackalloc byte[BlitWriter.SizeOf<TInput>() + 2 * sizeof(int)];
            var writer = BlitWriter.Create(buffer);
            writer.Write(tick);
            writer.Write(playerId);
            writer.Write(input);
            _driver.SendInput(buffer);
        }

        private void Store(int tick, int playerId, in TInput input)
        {
            _inputs[new int2(tick, playerId)] = input;
            MarkReady(tick, playerId);
        }

        private void MarkReady(int tick, int playerId)
        {
            uint bit = 1u << playerId;
            if (_readyMask.TryGetValue(tick, out uint mask))
                _readyMask[tick] = mask | bit;
            else
                _readyMask.Add(tick, bit);
        }

        void ILockstepInputSink.OnRemoteInput(int tick, int playerId, ReadOnlySpan<byte> body)
        {
            var reader = BlitReader.Create(body);
            Store(tick, playerId, reader.Read<TInput>());
        }

        bool ILockstepInputSink.IsTickReady(int tick, uint requiredMask)
        {
            return _readyMask.IsCreated
                && _readyMask.TryGetValue(tick, out uint mask)
                && (mask & requiredMask) == requiredMask;
        }

        void ILockstepInputSink.PruneBefore(int keepFromTick)
        {
            using (var staleTicks = new NativeList<int>(Allocator.Temp))
            {
                foreach (var entry in _readyMask)
                    if (entry.Key < keepFromTick) staleTicks.Add(entry.Key);
                for (int i = 0; i < staleTicks.Length; i++)
                    _readyMask.Remove(staleTicks[i]);
            }

            using (var staleInputs = new NativeList<int2>(Allocator.Temp))
            {
                foreach (var entry in _inputs)
                    if (entry.Key.x < keepFromTick) staleInputs.Add(entry.Key);
                for (int i = 0; i < staleInputs.Length; i++)
                    _inputs.Remove(staleInputs[i]);
            }
        }

        /// <summary>取指定 tick 指定玩家的输入（逻辑系统在 Step 内调用）。</summary>
        public bool TryGetInput(int tick, int playerId, out TInput input)
        {
            if (_inputs.IsCreated && _inputs.TryGetValue(new int2(tick, playerId), out input))
                return true;
            input = default;
            return false;
        }
    }
}
