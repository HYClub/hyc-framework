// =============================================================================
//  HYCSimple · 场景/引导示例 · 把上述几条线在引导期串起来
//  这是一个普通 MonoBehaviour（非 ECS），挂到任意场景的 GameObject 上即可在 Start 时
//  执行「数据阶段」加载：本地化 → 行为树 → 配置，最后跑一次属性演示。
//  用来演示一个游戏的 Bootstrap 应当按什么顺序把各系统喂饱，再让玩法/UI 上场。
//
//  ⚠️ 为什么必须由它来驱动各 Demo System（这条很重要，勿删）：
//  下游的 HYCSimple*DemoSystem 全部标了 [DisableAutoCreation] —— 它们**不会**被 Unity
//  自动拉进 World。原因是示例 System 一旦偷偷自启，就会在宿主毫不知情的情形下改写
//  框架的进程级静态单例（LocalizationManager / ConfigRegistry / BTManager），
//  曾造成「宿主主菜单全线『未找到Key : xxx』且怎么重导本地化都会被下一帧洗掉」的事故。
//  正确姿势就是本类的做法：**用户显式把本脚本挂到一个空场景的 GameObject 上**，
//  由它按正确的顺序手动 Update 一遍，示例才运行。宿主工程什么都不挂，就完全零副作用。
//
//  ⚠️⚠️ 跨端铁律（真实项目务必照做，勿照抄本类的简化写法）：
//  本类是「示例」，为了自包含用了 Directory.Exists 判断本地化目录。**真实项目不要这么写**——
//  WebGL 没有文件系统、Android 的 StreamingAssets 在 APK 压缩包内，System.IO 在两者上都读不到。
//  正确做法（全平台同一套代码、零 #if 分支）：
//    1) 引导用框架异步入口：await new FrameworkBootstrap().InitializeAsync("World");
//       它内部先 await StreamingAssets.PreloadAsync() 再建 World，PC/iOS 上是 no-op。
//    2) 判存在用 StreamingAssets.Exists(相对路径)，列目录用 StreamingAssets.GetKeysUnder(相对目录)。
//    3) 平台判据是 StreamingAssets.NeedsPreload（= IsWebGL || IsAndroid），不是 IsWebGL。
//  详见 Documentation~/99-Blob-与数据约定.md「五·补 · 跨端数据加载契约」。
// =============================================================================
using System;
using System.IO;
using Unity.Entities;
using UnityEngine;
using HYC.Framework.BT;
using HYC.Framework.IO;
using HYC.Framework.Loc;

namespace HYC.Simple
{
    /// <summary>在场景 Start 时执行数据阶段加载，串联 Loc / BT / Config / Attr。</summary>
    public sealed class HYCSimpleSceneBootstrap : MonoBehaviour
    {
        [Tooltip("本地化数据目录（真实项目填 StreamingAssets/Localization；留空则走示例自生成数据）。\n" +
                 "注意：填了路径就会直接 Reload 到该目录，会覆盖当前已加载的本地化表。")]
        public string localizationFolder;

        [Header("要运行哪些演示")]
        public bool runLocDemo = true;
        public bool runBTDemo = true;
        public bool runConfigDemo = true;
        public bool runAttrDemo = true;

        private void Start()
        {
            // —— 数据阶段（对应更新阶梯 A1）——
            // 1) 本地化：真实项目从 StreamingAssets 重载；本例用自包含示例数据。
            //    ⚠️ 简化写法：示例为了自包含用 Directory.Exists 判目录。真实项目请改成
            //       StreamingAssets.Exists("Localization")，且引导要走 FrameworkBootstrap.InitializeAsync
            //       （先 await PreloadAsync）——见文件头「跨端铁律」。
            var canReadFolder = !StreamingAssets.NeedsPreload && Directory.Exists(localizationFolder);
            if (!string.IsNullOrEmpty(localizationFolder) && canReadFolder)
                LocalizationManager.Reload(localizationFolder);
            else
                HYCSimpleLocSampleData.EnsureLoaded();

            // 2) 行为树 / 3) 配置 / 4) 属性：手动驱动各自的 Demo System（它们都禁用了自动创建）
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogWarning("[HYCSimple] 拿不到 DefaultGameObjectInjectionWorld，跳过 ECS 演示。");
                return;
            }

            // 注意顺序：Loc → BT → Config 是「数据阶段」，Attr 属于「玩法阶段」，放最后。
            if (runLocDemo)    RunDemo<HYCSimpleLocDemoSystem>(world);
            if (runBTDemo)     RunDemo<HYCSimpleBTDemoSystem>(world);
            if (runConfigDemo) RunDemo<HYCSimpleConfigDemoSystem>(world);
            if (runAttrDemo)   RunDemo<HYCSimpleAttrDemoSystem>(world);

            Debug.Log($"[HYCSimple] 场景引导完成：当前语言={LocalizationManager.SelectedLanguage}，" +
                      $"本地化条目={LocalizationManager.IDs?.Length ?? 0} 条。");
        }

        /// <summary>手动跑一次被 [DisableAutoCreation] 禁掉的示例 System。单个抛错不影响其余。</summary>
        private static void RunDemo<T>(World world) where T : SystemBase
        {
            try
            {
                world.GetOrCreateSystemManaged<T>().Update();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HYCSimple] {typeof(T).Name} 演示失败（已忽略，不影响其余示例）：{ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
