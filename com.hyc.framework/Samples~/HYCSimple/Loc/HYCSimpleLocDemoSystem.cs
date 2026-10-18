// =============================================================================
//  HYCSimple · 本地化示例 · 运行时演示
//  演示 Reload → GetText → SetLanguage → HasKey → 带参格式化。
//
//  ⚠️ 刻意标了 [DisableAutoCreation]（血泪教训）：
//  示例 System 一旦被 Unity 自动拉进宿主工程的 World，就会在宿主毫不知情的情况下
//  改写框架的**进程级静态单例**（LocalizationManager / ConfigRegistry / BTManager）。
//  最典型的一次事故：本系统在第一次 OnUpdate 时把宿主的本地化表 Reload 成示例的
//  3 条数据，宿主主菜单全线变成「未找到Key : xxx」，且每帧自愈性覆盖，极难排查。
//  所以示例默认**不自动运行**，改由 HYCSimpleSceneBootstrap（用户显式挂到场景才生效）
//  手动 Update；基本的宿主安全守卫见 HYCSimpleLocSampleData.CanTakeOver。
// =============================================================================
using Unity.Entities;
using UnityEngine;
using HYC.Framework.Loc;

namespace HYC.Simple
{
    /// <summary>一次性演示：加载示例数据后查询并切换语言。默认不自动进 World。</summary>
    [DisableAutoCreation]
    public partial class HYCSimpleLocDemoSystem : SystemBase
    {
        private bool _seeded;

        protected override void OnUpdate()
        {
            if (_seeded) return;
            _seeded = true;

            bool owns = HYCSimpleLocSampleData.EnsureLoaded();

            // 宿主已有自己的本地化表 → 示例没接管，此时查询示例 key 必然查不到，也不该改全局语言。
            if (!owns)
            {
                Debug.LogWarning("[HYCSimple] Loc 演示：宿主工程已有自己的本地化表，示例不接管也不改语言。" +
                                 "要看完整演示，请把 HYCSimpleSceneBootstrap 挂到一个空场景里运行。");
                return;
            }

            // 默认语言（lang 文件写了 "cn"）→ 中文
            Debug.Log($"[HYCSimple] Loc 默认：{LocalizationManager.GetText("ui/start")}");
            Debug.Log($"[HYCSimple] Loc 欢迎：{LocalizationManager.GetText("ui/welcome", "冒险者")}");

            // 运行时切换语言（仅在示例接管了全局表时才允许改）
            LocalizationManager.SetLanguage(Locale.English);
            Debug.Log($"[HYCSimple] Loc 切英文：{LocalizationManager.GetText("ui/start")}");
            Debug.Log($"[HYCSimple] Loc 切英文欢迎：{LocalizationManager.GetText("ui/welcome", "Adventurer")}");

            Debug.Log($"[HYCSimple] HasKey(item/name)={LocalizationManager.HasKey("item/name")}");
        }
    }
}
