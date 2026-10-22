using System;

namespace HYC.Framework.Sync
{
    /// <summary>
    /// 同步传输抽象：一条链路两个端点（P1 帧同步直连模型，N 人中继为后续路线）。
    /// 框架不绑定任何第三方网络库，真实传输（ENet / WebSocket / Relay）由宿主实现本接口注入。
    /// </summary>
    /// <remarks>
    /// <see cref="Send"/> 用 <see cref="ReadOnlySpan{T}"/> 零拷贝传参（同步调用合法）；
    /// 收包事件参数不能是 ref struct（不能作泛型实参），故为 byte[]。
    /// </remarks>
    public interface ITransport
    {
        SyncRole Role { get; }

        /// <summary>本端玩家编号（传输成对分配，Host = 0，Client = 1）。</summary>
        int LocalPlayerId { get; }

        /// <summary>链路对端玩家数（P1 成对传输 = 2）。</summary>
        int PeerCount { get; }

        bool IsConnected { get; }

        void Send(int messageKind, ReadOnlySpan<byte> payload);

        event Action<ITransport, int, byte[]> MessageReceived;

        /// <summary>泵消息：把已到达的分批派发为 MessageReceived 事件。</summary>
        void Pump();

        void Dispose();
    }
}
