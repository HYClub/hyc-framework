// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTContext.cs
// 说明: 行为树执行上下文 - 一次 Tick 期间共享的数据
// ============================================================

using System;
using Unity.Entities;

namespace HYC.Framework.BT
{
    /// <summary>
    /// 树执行上下文。解释器每帧对一个实体执行树时构造。
    /// 游戏层在自定义动作节点里读写。
    /// </summary>
    public struct BTContext
    {
        public Entity Self;                 // 树所属实体
        public float DeltaTime;             // 本帧 deltaTime
        public BTBlackboardRuntime Blackboard; // 黑板实例(可 IsCreated=false)

        /// <summary>游戏层自定义节点回调(按子类型 subType 分发), 由游戏层注册。</summary>
        public BTGameActionHandler GameHandler;
    }

    /// <summary>
    /// 游戏层自定义动作处理委托。
    /// ctx: 上下文; view: 当前节点参数视图; subType: 自定义节点子类型(存于 Long[0]).
    /// 返回 Success/Failed/Running。
    /// </summary>
    public delegate BTNodeState BTGameActionHandler(ref BTContext ctx, ref BTNodeView view);
}
