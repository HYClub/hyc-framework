using HYC.Framework.Dots;
using Unity.Entities;
using UnityEngine.InputSystem;

namespace HYC.Framework.Input
{
    /// <summary>
    /// 输入组件驱动系统。挂在 <see cref="UpdateGroup_A0"/>（框架的「输入 tick 采样」组），随固定步长每帧执行。
    /// 遍历 <see cref="InputPlayerRegistry"/> 里所有已注册玩家，对每个玩家遍历所有已注册写入器，
    /// 读出该玩家 InputActionAsset 实例里对应 map 的动作当前值，写进玩家实体上的组件。
    /// <para>未标 [DisableAutoCreation] → 由框架自动装配创建并挂组，宿主无需手动接线。</para>
    /// <para>输入是对象化的（InputSystem），本系统不跑 Burst，属托管 system，符合输入采集的天然形态。</para>
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_A0))]
    public partial class InputComponentDriverSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var em = EntityManager;
            foreach (var kvp in InputPlayerRegistry.Slots)
            {
                var player = kvp.Key;
                var slot = kvp.Value;
                var asset = slot.asset;
                if (asset == null) continue;

                foreach (var writer in InputMapWriterRegistry.All)
                {
                    var map = asset.FindActionMap(writer.MapName);
                    if (map == null) continue;
                    if (!map.enabled) map.Enable();
                    writer.Write(em, player, map, slot.prev);
                }

                // 写完本玩家全部输入组件后，立即回调所有注册方（如 UI 层 lambda），
                // 回调内读到的是本 tick 刚写好的最新组件值。
                InputPlayerRegistry.InvokeHandlers(em, player);
            }
        }
    }
}
