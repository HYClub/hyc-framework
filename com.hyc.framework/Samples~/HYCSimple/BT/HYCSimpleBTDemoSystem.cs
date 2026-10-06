// =============================================================================
//  HYCSimple · 行为树示例 · 运行时演示
//  演示「加载 → 挂组件 → 自动 Tick」：进 Play 后 LoadAll() 把导出的 Blob 注册进
//  BTManager，再给实体挂 RunningBT，BTInterpreterSystem（框架自带 ISystem）即逐帧驱动。
//
//  本例的树是一棵**有真实行为数据**的敌人 AI 树（见 HYCSimpleBTTreeBuilder.CreateSampleTree）：
//      Root → Selector[ Sequence( IsTargetInRange(条件) → Attack(动作) ), Patrol(动作) ]
//  三个叶子是 GameCustom 节点（subType 1/2/3），对应 HYCSimpleEnemyBTNodes.cs 里
//  的运行时 BTCustomNode 子类——它们会被 BTNodeRuntimeRegistry 反射自动注册，
//  所以进 Play 后控制台会真的打印「目标在范围? / 攻击 / 巡逻」的逐帧日志。
//
//  ⚠️ 前置：进 Play 前请先在编辑器运行一次菜单
//     HYC Framework/Samples/BT/创建示例树并导出（把树资产导出成 StreamingAssets/BTBlob/*.blob）。
//     编辑器里不导出也能跑（BTAutoRegisterOnPlay 自动注册），但那样验证不到打包链路。
//
//  ⚠️ 刻意标了 [DisableAutoCreation]：示例 System 若自动进宿主工程的 World，会悄悄改写
//     框架静态单例（BTManager / LocalizationManager / ConfigManager），干扰宿主。
//     改由 HYCSimpleSceneBootstrap（用户显式挂到场景才生效）手动 Update。
// =============================================================================
using Unity.Entities;
using UnityEngine;
using HYC.Framework.BT;

namespace HYC.Simple
{
    /// <summary>一次性演示：加载树 → 挂 RunningBT。默认不自动进 World。</summary>
    [DisableAutoCreation]
    public partial class HYCSimpleBTDemoSystem : SystemBase
    {
        private bool _seeded;
        private const long SampleTreeId = 900001;

        protected override void OnUpdate()
        {
            if (_seeded) return;
            _seeded = true;

            // 1) 加载真机打包的树（编辑器里若已自动注册，同 TreeId 会被覆盖，无害）
            int ok = BTBlobLoader.LoadAll();
            Debug.Log($"[HYCSimple] 行为树加载：成功 {ok} 棵");

            // 2) 确认树已注册
            if (!BTManager.Contains(SampleTreeId))
            {
                Debug.LogWarning(
                    $"[HYCSimple] 行为树 {SampleTreeId} 未注册！请先运行菜单 " +
                    "HYC Framework/Samples/BT/创建示例树并导出，再进 Play。");
                return;
            }

            // 3) 造实体并挂 RunningBT，BTInterpreterSystem 会自动逐帧 Tick
            Entity e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, new RunningBT
            {
                TreeId = SampleTreeId,
                RunState = default,   // BTRunState
                TickInterval = 0f,    // 0 = 每帧；>0 按秒节流（如 0.1 = 10Hz）
            });
            Debug.Log($"[HYCSimple] 已为实体挂载 RunningBT(TreeId={SampleTreeId})，BTInterpreterSystem 将逐帧 Tick。");
        }
    }
}
