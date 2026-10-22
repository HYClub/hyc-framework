namespace HYC.Framework.Sync
{
    /// <summary>
    /// 传输层消息类别。P1 帧同步只有输入一种；快照 / 生成销毁（P2/P3）在此追加。
    /// </summary>
    public static class NetMessage
    {
        /// <summary>帧同步输入。载荷 = int tick + int playerId + TInput 二进制（blittable memcpy）。</summary>
        public const int Input = 1;
    }
}
