using System;
using System.Collections.Generic;
using HYC.Framework.Dots;
using Unity.Entities;
using UnityEngine;

namespace HYC.Framework.Sync
{
    /// <summary>
    /// Driver 与输入系统之间的输入汇契约（非泛型：Driver 不关心具体输入类型，
    /// 由 <see cref="LockstepInputSystem{TInput}"/> 实现并按自身类型解码）。
    /// </summary>
    internal interface ILockstepInputSink
    {
        void OnRemoteInput(int tick, int playerId, ReadOnlySpan<byte> body);

        bool IsTickReady(int tick, uint requiredMask);

        void PruneBefore(int keepFromTick);
    }

    /// <summary>
    /// 帧同步驱动器（A0 引导组）：泵传输 → 收远端输入 → 齐帧检查 → 推进 tick。
    /// 固定步长由框架阶梯树的 RateManager 保证，本系统每次 OnUpdate 对应一次逻辑帧机会。
    /// 由 SyncBootstrap.Install 创建入组并绑定传输，不参与自动装配。
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_A0))]
    [DisableAutoCreation]
    public partial class LockstepDriver : SystemBase
    {
        /// <summary>输入历史保留窗口（tick 数），供逻辑系统读取最近若干帧输入后回收。</summary>
        private const int KeepInputTicks = 64;

        private ITransport _transport;
        private readonly List<ILockstepInputSink> _sinks = new List<ILockstepInputSink>();
        private int _stallFrames;
        private bool _stallLogged;

        /// <summary>输入未齐允许等待的最大逻辑帧数；0 = 永久等待（默认）。超时强制推进并按 default 处理缺失输入。</summary>
        public int MaxStallFrames;

        /// <summary>当前已推进到的逻辑帧。</summary>
        public int CurrentTick => SystemAPI.GetSingleton<SyncTickSingleton>().CurrentTick;

        /// <summary>传输是否已绑定且链路可用（未绑定或对端断开均视同断链）；供 SyncSessionSystem 轮询。</summary>
        public bool IsConnected => _transport != null && _transport.IsConnected;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerTableSingleton>();
        }

        protected override void OnUpdate()
        {
            if (_transport == null) return;
            if (_transport.IsConnected) _transport.Pump();

            var tick = SystemAPI.GetSingleton<SyncTickSingleton>();
            var players = SystemAPI.GetSingleton<PlayerTableSingleton>();
            if (players.PlayerCount <= 0) return;

            int nextTick = tick.CurrentTick + 1;
            uint requiredMask = PlayerMask(players.PlayerCount);

            bool ready = _sinks.Count > 0;
            foreach (var sink in _sinks)
            {
                if (!sink.IsTickReady(nextTick, requiredMask))
                {
                    ready = false;
                    break;
                }
            }

            if (!ready)
            {
                if (MaxStallFrames <= 0) return;
                _stallFrames++;
                if (_stallFrames < MaxStallFrames) return;
                if (!_stallLogged)
                {
                    _stallLogged = true;
                    Debug.LogError($"[HYC.Sync] 输入等待超过 {MaxStallFrames} 帧未齐，强制推进 tick {nextTick}（缺失玩家按 default 输入处理）。");
                }
            }
            else
            {
                _stallFrames = 0;
                _stallLogged = false;
            }

            tick.CurrentTick = nextTick;
            tick.AdvancedCount++;
            SystemAPI.SetSingleton(tick);

            int keepFromTick = nextTick - KeepInputTicks;
            foreach (var sink in _sinks)
                sink.PruneBefore(keepFromTick);
        }

        protected override void OnDestroy()
        {
            if (_transport != null)
                _transport.MessageReceived -= OnMessage;
        }

        /// <summary>绑定传输（幂等；重复绑定直接返回）。</summary>
        public void Bind(ITransport transport)
        {
            if (_transport != null) return;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.MessageReceived += OnMessage;
        }

        /// <summary>输入系统把本地输入二进制交给 Driver 经传输发出（已连接才发）。</summary>
        public void SendInput(ReadOnlySpan<byte> payload)
        {
            if (_transport == null || !_transport.IsConnected) return;
            _transport.Send(NetMessage.Input, payload);
        }

        internal void RegisterSink(ILockstepInputSink sink)
        {
            if (!_sinks.Contains(sink))
                _sinks.Add(sink);
        }

        internal void UnregisterSink(ILockstepInputSink sink)
        {
            _sinks.Remove(sink);
        }

        private void OnMessage(ITransport from, int messageKind, byte[] payload)
        {
            if (messageKind != NetMessage.Input) return;

            var reader = BlitReader.Create(payload);
            int tick = reader.Read<int>();
            int playerId = reader.Read<int>();
            var body = payload.AsSpan(2 * sizeof(int));

            foreach (var sink in _sinks)
                sink.OnRemoteInput(tick, playerId, body);
        }

        private static uint PlayerMask(int playerCount)
        {
            if (playerCount <= 0) return 0;
            if (playerCount >= 32) return 0xFFFFFFFFu;
            return (1u << playerCount) - 1;
        }
    }
}
