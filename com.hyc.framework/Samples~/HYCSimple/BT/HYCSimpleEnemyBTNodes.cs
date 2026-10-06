// =============================================================================
//  HYCSimple · 行为树示例 · 真实自定义节点（运行时）
//  这些类是「示例的真实行为数据」：
//    · 树资产（HYCSimpleDemoTree.asset）里用 GameCustom 节点引用 subType 1 / 2 / 3；
//    · 进 Play 后，BTNodeRuntimeRegistry 扫描本程序集（反射）自动发现并注册它们，
//      无需任何手动注册调用；
//    · BTInterpreterSystem 每帧 Tick 时，会真正调用这里的 Execute 并打印日志。
//  这正是框架「游戏层自定义节点」的标准接法：继承 BTCustomNode，填 SubType/Kind/Execute。
// =============================================================================
using HYC.Framework.BT;
using UnityEngine;

namespace HYC.Simple
{
    /// <summary>日志节流：约每秒一条，避免每个 Tick 刷屏。</summary>
    internal static class HYCSimpleBTThrottle
    {
        private static float _timer;
        public static bool Tick(ref BTContext ctx)
        {
            _timer -= ctx.DeltaTime;
            if (_timer <= 0f)
            {
                _timer = 1.0f;
                return true;
            }
            return false;
        }
    }

    /// <summary>条件节点：目标是否在攻击范围内？（示例用随机数模拟，真实项目读黑板/世界数据）</summary>
    public sealed class HYCSimpleEnemyIsTargetInRange : BTCustomNode
    {
        public override long SubType => 1;
        public override string NodeName => "目标在范围内?";
        public override BTCustomNodeKind Kind => BTCustomNodeKind.Condition;
        public override BTTreeKind TreeKind => BTTreeKind.Other;

        static HYCSimpleEnemyIsTargetInRange()
        {
            BTGameNodeRegistry.Register(BTTreeKind.Other, 1,
                new BTGameNodeInfo { Name = "目标在范围内?", Category = 1 });
        }

        public override BTNodeState Execute(ref BTContext ctx, ref BTNodeView view)
        {
            bool inRange = Random.value < 0.35f; // 模拟「有时在范围内」
            if (HYCSimpleBTThrottle.Tick(ref ctx))
                Debug.Log($"[HYCSimple] BT条件[目标在范围内?] = {(inRange ? "是" : "否")}");
            return inRange ? BTNodeState.Success : BTNodeState.Failed;
        }
    }

    /// <summary>动作节点：攻击目标。（示例只打印日志，真实项目在此写伤害/动画/特效）</summary>
    public sealed class HYCSimpleEnemyAttack : BTCustomNode
    {
        public override long SubType => 2;
        public override string NodeName => "攻击";
        public override BTCustomNodeKind Kind => BTCustomNodeKind.Action;
        public override BTTreeKind TreeKind => BTTreeKind.Other;

        static HYCSimpleEnemyAttack()
        {
            BTGameNodeRegistry.Register(BTTreeKind.Other, 2,
                new BTGameNodeInfo { Name = "攻击", Category = 2 });
        }

        public override BTNodeState Execute(ref BTContext ctx, ref BTNodeView view)
        {
            if (HYCSimpleBTThrottle.Tick(ref ctx))
                Debug.Log("[HYCSimple] BT动作[攻击] 对目标造成伤害!");
            return BTNodeState.Success; // 单次攻击完成
        }
    }

    /// <summary>动作节点：巡逻。（持续行为，返回 Running 让树保持活跃）</summary>
    public sealed class HYCSimpleEnemyPatrol : BTCustomNode
    {
        public override long SubType => 3;
        public override string NodeName => "巡逻";
        public override BTCustomNodeKind Kind => BTCustomNodeKind.Action;
        public override BTTreeKind TreeKind => BTTreeKind.Other;

        static HYCSimpleEnemyPatrol()
        {
            BTGameNodeRegistry.Register(BTTreeKind.Other, 3,
                new BTGameNodeInfo { Name = "巡逻", Category = 2 });
        }

        public override BTNodeState Execute(ref BTContext ctx, ref BTNodeView view)
        {
            if (HYCSimpleBTThrottle.Tick(ref ctx))
                Debug.Log("[HYCSimple] BT动作[巡逻] 沿路线移动中…");
            return BTNodeState.Running; // 巡逻是持续行为
        }
    }
}
