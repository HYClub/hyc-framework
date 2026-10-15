// =============================================================================
//  HYCSimple · 本地化示例 · 自包含数据生成（运行时）
//  真实项目里 id/lang/*.lang 由 Loc 数据编辑器从 Excel 导出到 StreamingAssets。
//  为让 Sample 导入即跑、不依赖手写二进制 blob，这里在运行时用框架同款 API
//  （BlobBuilder + BlobAssetReference<CfgLocalization>.Write）造一份最小多语言数据，
//  写到 persistentDataPath 后在**安全的前提下**交给 LocalizationManager.Reload。
//
//  ⚠️ 中文处理（为什么不用「手写 unsafe 版」）：
//  内置 BlobBuilder.AllocateString 在 Unity 2022.3 / Entities 1.0 已按 UTF-8 真实字节数分配，
//  中文不再被截断，且它是**安全 API（不需要 /unsafe）**，因此本例直接用内置版，
//  不依赖工程的 Player Settings 开启「Allow unsafe code」，也不依赖示例 asmdef 的 allowUnsafeCode。
//  仅在你的 Unity 版本极老、内置 AllocateString 仍按「长度×2+1」截断时，才需要改用
//  HYC.Framework 编辑器里的自定义 unsafe 写入（见 LocalizedExcelReader.AllocateString）。
//
//  ⚠️⚠️ 宿主安全守卫（请勿删除这段逻辑）：
//  LocalizationManager 是**进程级静态单例**，而 Reload() 的第一步是把旧状态全部清空：
//      mIDs = null; mLangs = null; mTexts = null; mLang2Index = null; mKey2Index = null;
//  再按 folder 重填。宿主工程通常在 [RuntimeInitializeOnLoadMethod]
//  阶段已从 StreamingAssets 加载了自己的本地化表；示例若在这里无脑 Reload 到自己的
//  数据，会把宿主整张表洗掉（表现为「未找到Key」，且因每帧兜底导致宿主难以重导恢复）。
//  因此本类**只在确认全局表里没有宿主数据时才接管**（见 CanTakeOver），
//  EnsureLoaded() 返回是否接管，调用方须据此决定能不能改全局语言。
// =============================================================================
using System;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using HYC.Framework.Loc;

namespace HYC.Simple
{
    /// <summary>生成并加载一份最小多语言数据（幂等，且不会覆盖宿主工程已加载的本地化表）。</summary>
    public static class HYCSimpleLocSampleData
    {
        /// <summary>示例数据用到的 key，同时充当「这张表是不是示例自己的」判别依据。</summary>
        public static readonly string[] SampleKeys = { "ui/start", "ui/welcome", "item/name" };

        private static bool _loaded;
        private static bool _ownsGlobalTable;

        /// <summary>
        /// 生成并加载一份最小多语言数据（幂等）。
        /// </summary>
        /// <returns>
        /// true  = 示例已接管全局本地化表（可以随意演示 / 切换语言）；<br/>
        /// false = 宿主工程已有自己的本地化数据，示例**未**接管，调用方请只做只读查询。
        /// </returns>
        public static bool EnsureLoaded()
        {
            if (_loaded) return _ownsGlobalTable;
            _loaded = true;

            string folder = Path.Combine(Application.persistentDataPath, "HYCSimpleLoc");
            Directory.CreateDirectory(folder);

            string[] cnTexts = { "开始游戏", "欢迎，{0}！", "魔法剑" };
            string[] enTexts = { "Start Game", "Welcome, {0}!", "Magic Sword" };

            WriteLangFile(Path.Combine(folder, "id"), SampleKeys);  // 键列表（CfgLocalization blob）
            WriteLangFile(Path.Combine(folder, "cn.lang"), cnTexts);
            WriteLangFile(Path.Combine(folder, "en.lang"), enTexts);
            File.WriteAllText(Path.Combine(folder, "lang"), "cn");  // 默认语言码

            // —— 守卫：宿主已有自己的本地化表时，绝不 Reload ——
            if (!CanTakeOver())
            {
                Debug.LogWarning(
                    "[HYCSimple] 检测到宿主工程已加载自己的本地化数据" +
                    $"（IDs={LocalizationManager.IDs?.Length ?? 0} 条），为避免覆盖工程的表，本次**跳过 Reload**。" +
                    "示例数据已生成到 " + folder + "；想看完整演示请把示例挂到空场景中运行。");
                return _ownsGlobalTable = false;
            }

            LocalizationManager.Reload(folder);
            return _ownsGlobalTable = true;
        }

        /// <summary>
        /// 判断能否接管全局表。只有两种情况允许接管：
        /// ① 全局表是空的（空工程跑示例，正是本例的设计场景）；
        /// ② 全局表里已经（只）是本示例的数据（重复调用幂等）。
        /// 其余一律视为宿主数据，不接管。
        /// </summary>
        private static bool CanTakeOver()
        {
            var ids = LocalizationManager.IDs;
            if (ids == null || ids.Length == 0) return true;   // ① 空表
            foreach (var k in SampleKeys)                      // ② 已是示例数据
                if (Array.IndexOf(ids, k) >= 0) return true;
            return false;                                      // 宿主数据 → 不接管
        }

        private static void WriteLangFile(string path, string[] texts)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CfgLocalization>();
            var arr = builder.Allocate(ref root.values, texts.Length);
            for (int i = 0; i < texts.Length; i++)
                builder.AllocateString(ref arr[i], texts[i]);   // 安全 API：内置按 UTF-8 字节数分配，无需 unsafe
            BlobAssetReference<CfgLocalization>.Write(builder, path, 3);
            builder.Dispose();
        }
    }
}
