// =============================================================================
//  HYCSimple · 统一「创建全部示例资产」入口
//  一次把示例需要的「真实数据资产」都生成出来：
//    · BT   → HYCSimpleDemoTree.asset（并导出 StreamingAssets/BTBlob/*.blob）
//    · Attr → GameAttrCatalog.asset + GameHeroSet.asset
//  之后再进 Play，BT / Attr 示例系统就能读到这些资产（而不只是代码里手搓数据）。
//  说明：Loc 示例是运行时在 persistentDataPath 生成 blob，无需预制资产。
// =============================================================================
#if UNITY_EDITOR
using UnityEditor;

namespace HYC.Simple.Editor
{
    public static class HYCSimpleSampleDataCreator
    {
        [MenuItem("HYC Framework/Samples/创建全部示例资产", false, 20)]
        public static void CreateAll()
        {
            HYCSimpleBTTreeBuilder.CreateSampleTree();
            HYCSimpleAttrSampleAssets.CreateAssetsInternal();
            UnityEngine.Debug.Log("[HYCSimple] 已创建全部示例资产（BT 树 + 属性目录/预设）。进 Play 即可被示例系统读取。");
        }
    }
}
#endif
