using System;
using System.Collections.Generic;

namespace HYC.Framework.Sync
{
    /// <summary>
    /// 同进程回环传输：一对两端点，Send 入对端收件箱，Pump 时派发。
    /// 用于单机双端联调与确定性回归（无真实网络延迟行为，仅验证同步管线正确性）。
    /// 真实网络请自行实现 <see cref="ITransport"/> 注入。
    /// </summary>
    public sealed class LoopbackTransport : ITransport
    {
        private readonly Queue<(int kind, byte[] payload)> _inbox = new Queue<(int, byte[])>();
        private readonly int _peerCount;
        private LoopbackTransport _peer;

        public SyncRole Role { get; private set; }
        public int LocalPlayerId { get; private set; }
        public int PeerCount => _peerCount;
        public bool IsConnected => _peer != null;

        private LoopbackTransport(SyncRole role, int playerId, int peerCount)
        {
            Role = role;
            LocalPlayerId = playerId;
            _peerCount = peerCount;
        }

        /// <summary>创建一对互联传输（Host=玩家 0，Client=玩家 1，PeerCount=2）。</summary>
        public static void CreatePair(out LoopbackTransport host, out LoopbackTransport client)
        {
            host = new LoopbackTransport(SyncRole.Host, 0, 2);
            client = new LoopbackTransport(SyncRole.Client, 1, 2);
            host._peer = client;
            client._peer = host;
        }

        /// <summary>
        /// 创建单端传输（无对端，PeerCount=1，固定为玩家 0）：Send 静默丢弃、Pump 空转。
        /// 用于单人确定性模式（本地输入自推进，等价于录制回放基线）。
        /// 齐帧掩码按「玩家 0..PlayerCount-1 连续编号」取连续低位，solo 必须为 0 号；
        /// 多玩家录制的回放注入（缺位玩家输入从文件灌入）是 P2 范围，届时另给注入 API。
        /// </summary>
        public static LoopbackTransport CreateSolo()
        {
            return new LoopbackTransport(SyncRole.Host, 0, 1);
        }

        public event Action<ITransport, int, byte[]> MessageReceived;

        public void Send(int messageKind, ReadOnlySpan<byte> payload)
        {
            var peer = _peer;
            if (peer == null) return;
            peer._inbox.Enqueue((messageKind, payload.ToArray()));
        }

        public void Pump()
        {
            while (_inbox.Count > 0)
            {
                var (kind, payload) = _inbox.Dequeue();
                MessageReceived?.Invoke(this, kind, payload);
            }
        }

        public void Dispose()
        {
            _peer = null;
            MessageReceived = null;
        }
    }
}
