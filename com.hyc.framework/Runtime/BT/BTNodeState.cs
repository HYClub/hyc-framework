// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTNodeState.cs
// 说明: 行为树节点执行状态
// ============================================================

namespace HYC.Framework.BT
{
    /// <summary>
    /// 节点执行状态。组合节点按子节点状态短路; 动作节点可返回 Running 表示持续执行。
    /// </summary>
    public enum BTNodeState : byte
    {
        None = 0,       // 未开始
        Running,        // 运行中(等待下一帧)
        Success,        // 成功
        Failed,         // 失败
        Paused,         // 暂停(保留现场)
    }
}
