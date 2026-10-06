// =============================================================================
//  HYCSimple · 属性示例 · 编辑器生成「真实」属性资产
//  把 HYC 属性系统的两个 ScriptableObject 资产落到工程里，供 Attr 示例系统走「真实链路」：
//    AttributeCatalog.asset（单一事实源：列出 HP/MP/MoveSpeed/IsInvincible）
//    AttributeSetAsset.asset（预设：引用目录 + 勾选 4 个 key，Rebuild 烘焙出 values）
//  这样示例就不再是「代码里手搓 buffer」，而是和真实项目一样：先有资产，再由
//  AttributeInitSystem / AttributeInitializer.InitFromSet 灌进实体。
// =============================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using HYC.Framework.Attr;

namespace HYC.Simple.Editor
{
    public static class HYCSimpleAttrSampleAssets
    {
        private const string AssetDir = "Assets/HYCSimpleSampleAssets";
        private const string CatalogPath = AssetDir + "/GameAttrCatalog.asset";
        private const string SetPath = AssetDir + "/GameHeroSet.asset";

        [MenuItem("HYC Framework/Samples/Attr/创建示例属性资产", false, 30)]
        public static void CreateAssets() => CreateAssetsInternal();

        /// <summary>生成/刷新示例属性资产（幂等）。统一菜单也调用它。</summary>
        public static void CreateAssetsInternal()
        {
            if (!AssetDatabase.IsValidFolder(AssetDir))
                AssetDatabase.CreateFolder("Assets", "HYCSimpleSampleAssets");

            // 1) 目录：列出 4 个属性（key + 类型 + 默认/上下限）
            var catalog = AssetDatabase.LoadAssetAtPath<AttributeCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AttributeCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.entries.Clear();
            catalog.entries.Add(new AttributeCatalogEntry { key = "HP",         type = AttributeType.Int,  defaultRaw = 100, minRaw = 0,    maxRaw = 9999 });
            catalog.entries.Add(new AttributeCatalogEntry { key = "MP",         type = AttributeType.Int,  defaultRaw = 50,  minRaw = 0,    maxRaw = 9999 });
            catalog.entries.Add(new AttributeCatalogEntry { key = "MoveSpeed",  type = AttributeType.Float, defaultRaw = System.BitConverter.DoubleToInt64Bits(3.5),
                                                           minRaw = System.BitConverter.DoubleToInt64Bits(0), maxRaw = System.BitConverter.DoubleToInt64Bits(20) });
            catalog.entries.Add(new AttributeCatalogEntry { key = "IsInvincible", type = AttributeType.Bool, defaultRaw = 0, minRaw = 0, maxRaw = 1 });
            EditorUtility.SetDirty(catalog);

            // 2) 预设：引用目录，勾选 4 个 key，Rebuild 把 values 烘焙好
            var set = AssetDatabase.LoadAssetAtPath<AttributeSetAsset>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<AttributeSetAsset>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.catalog = catalog;
            set.keys = new List<string> { "HP", "MP", "MoveSpeed", "IsInvincible" };
            set.Rebuild();   // 从 catalog + keys 烘焙出 values
            EditorUtility.SetDirty(set);

            AssetDatabase.SaveAssets();
            Debug.Log($"[HYCSimple] 已创建示例属性资产：\n  {CatalogPath}\n  {SetPath}\n" +
                      "（Attr 示例系统现在会加载 GameHeroSet.asset 并走 InitFromSet 真实链路）");
        }
    }
}
#endif
