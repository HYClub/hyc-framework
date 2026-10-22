using HYC.Framework.Dots;
using HYC.Framework.Input;
using HYC.Simple.Input.Sense;
using HYC.Simple.Input.UI;
using Unity.Entities;
using UnityEngine;

namespace HYC.Simple.Input
{
    /// <summary>
    /// 示例玩家生成器（单玩家演示）：建一个玩家实体、挂上 UI/Sense 输入组件、
    /// 克隆一份资产实例并注册进 InputPlayerRegistry。
    /// 多玩家扩展：每次有玩家加入就再建一个实体 + 挂组件 + 用该玩家的资产实例 Register，
    /// 设备分配交给游戏层（见 AI-NOTES 与 LocalPlayerJoinDetector）。
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_A0))]
    public partial class SampleInputPlayerSpawner : SystemBase
    {
        private bool _done;
        protected override void OnUpdate()
        {
            if (_done) return;
            _done = true;
            var em = EntityManager;
            var e = em.CreateEntity();
            UIInputComponent.AddTo(em, e);
            SenseInputComponent.AddTo(em, e);
            // 每个玩家一份资产实例（克隆，可绑各自设备）。这里单玩家演示用默认设备。
            var asset = Object.Instantiate(SampleInputAssetBuilder.Build());
            InputPlayerRegistry.Register(e, asset);
            Debug.Log("[HYC.Simple.Input] 已创建示例玩家实体并完成输入组件注册（单玩家演示；多玩家见 AI-NOTES）。");
        }
    }
}
