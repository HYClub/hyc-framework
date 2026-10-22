using Unity.Entities;

namespace HYC.Framework.Sync
{
    /// <summary>
    /// 同步 tick 单例。CurrentTick = 已推进到的逻辑帧序号（首个提交目标帧为 1）；
    /// AdvancedCount = 累计推进次数，逻辑系统以此判定「本帧是否踩中新 tick」。
    /// </summary>
    public struct SyncTickSingleton : IComponentData
    {
        public int CurrentTick;
        public int AdvancedCount;
    }

    /// <summary>
    /// 玩家表单例：本端玩家编号与链路对端总数（P1 成对传输下 PlayerCount = 2）。
    /// Driver 的齐帧判据 = 全部 PlayerCount 个玩家均提交当前目标帧输入。
    /// </summary>
    public struct PlayerTableSingleton : IComponentData
    {
        public int LocalPlayerId;
        public int PlayerCount;
    }
}
