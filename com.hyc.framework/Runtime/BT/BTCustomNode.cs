// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTCustomNode.cs
// 说明: 游戏层自定义节点基类 - 通过继承实现自定义行为树节点
//       编辑器自动扫描子类, 子类型下拉自动出现
//       无需手动注册
// ============================================================

using Unity.Entities;

namespace HYC.Framework.BT
{
    /// <summary>自定义节点分类。</summary>
    public enum BTCustomNodeKind
    {
        Condition = 1,   // 条件: 返回 Success/Failed, 可阻断(样式红)
        Action = 2,      // 动作: 返回 Success/Failed/Running, 叶子(样式绿)
        Composite = 3,   // 组合: 有子节点, 动态端口(样式蓝)
    }

    /// <summary>
    /// 游戏层自定义节点基类。
    /// 继承它并实现成员, 编辑器自动识别(子类型下拉/参数面板/分类样式),
    /// 运行时自动分发执行。
    /// </summary>
    /// 叶子范式(对应 NodeCanvas 的 ActionNode/ConditionNode):
    /// - Kind=Action(动作, 对应 ActionTask): 每次 Execute 推进一小步, 未完成返回 Running,
    ///   完成返回 Success/Failed。跨帧进度存黑板或本节点参数; 不需要协程(驱动系统每帧 Tick)。
    /// - Kind=Condition(条件, 对应 ConditionTask): 立即判定并返回 Success/Failed, 不返回 Running;
    ///   需要取反时套 Invert 节点(对应 ConditionTask.invert)。
    /// - 参数读取建议用 view.GetFloatVar / GetIntVar: 支持"字面值 or 黑板变量"两种来源
    ///   (对应 NodeCanvas 的 BBParameter&lt;T&gt;)。
    /// </summary>
    public abstract class BTCustomNode
    {
        /// <summary>子类型 ID(同树类型内唯一, 由编辑器分配)。</summary>
        public abstract long SubType { get; }

        /// <summary>节点显示名(如 "找最近敌人")。</summary>
        public abstract string NodeName { get; }

        /// <summary>节点分类(决定样式 + 是否有子节点)。</summary>
        public abstract BTCustomNodeKind Kind { get; }

        /// <summary>所属树类型(技能树/AI树)。同树类型内 SubType 唯一。</summary>
        public virtual BTTreeKind TreeKind => BTTreeKind.Other;

        /// <summary>节点说明(显示在节点上)。</summary>
        public virtual string Description => "";

        /// <summary>参数定义(编辑器自动渲染输入/下拉)。</summary>
        public virtual BTGameNodeParamDesc[] Params => null;

        /// <summary>运行时执行逻辑。通过 ctx 读写世界数据/黑板。</summary>
        public abstract BTNodeState Execute(ref BTContext ctx, ref BTNodeView view);

        /// <summary>
        /// 停止回调(对应 NodeCanvas ActionTask.OnStop)。
        /// 当本节点从 Running 被复位/中断(父节点切换子节点、树完成等)时由解释器调用,
        /// interrupted=true 表示是被打断而非正常结束。用于释放占用资源、取消动画/定时器等。
        /// </summary>
        public virtual void OnStop(bool interrupted) { }
    }

    /// <summary>
    /// 游戏层注入的世界数据(自定义节点读取用)。
    /// 游戏层定义自己的数据结构(如 BattleWorldData: 单位/属性/阵营数组), 驱动系统每帧填入。
    /// 自定义节点在 Execute 里 cast 使用。
    /// </summary>
    public class BTCustomNodeContext
    {
        public object Data;   // 游戏层自定义世界数据(如 NativeArray 容器)
        public System.Func<ulong, float> GetBlackboardFloat; // 可选黑板快捷读
        public System.Action<ulong, float> SetBlackboardFloat;
    }
}
