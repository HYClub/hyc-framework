using System.IO;
using UnityEditor;
using UnityEngine;
using HYC.Simple.Input;
using HYC.Framework.Editor;

namespace HYC.Simple.Input.Editor
{
    /// <summary>
    /// 一键生成示例输入组件：用 SampleInputAssetBuilder 建资产 + 建 InputComponentMapping（指向本样例的 UI/Sense 子目录）+ 跑生成器。
    /// 菜单：HYC Simple/Input/Generate Sample Input Components。
    /// 生成产物（UIInputComponent.cs / SenseInputComponent.cs 及各自 .asmdef）也可直接手写，这里只是省去手动步骤。
    /// </summary>
    public static class SampleInputSetup
    {
        [MenuItem("HYC Simple/Input/Generate Sample Input Components")]
        public static void Generate()
        {
            var asset = SampleInputAssetBuilder.Build();
            var mapping = ScriptableObject.CreateInstance<InputComponentMapping>();
            mapping.asset = asset;
            var root = InputRoot();
            mapping.maps.Add(new InputMapTarget { mapName = "UI", outputFolder = root + "/UI", ns = "HYC.Simple.Input.UI" });
            mapping.maps.Add(new InputMapTarget { mapName = "Sense", outputFolder = root + "/Sense", ns = "HYC.Simple.Input.Sense" });
            var err = InputComponentGenerator.Generate(mapping);
            if (err != null) Debug.LogError($"[HYC.Simple.Input] 生成失败：\n{err}");
            else Debug.Log("[HYC.Simple.Input] 已生成 UI/Sense 输入组件到 " + root);
        }

        /// <summary>根据本脚本在样例中的位置推算 Input 目录（兼容包内 Samples~ 与导入后的 Assets/Samples/...）。</summary>
        static string InputRoot()
        {
            var guids = AssetDatabase.FindAssets("SampleInputSetup t:MonoScript");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("HYCSimple/Input/Editor/SampleInputSetup.cs"))
                    return Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
            }
            return "Assets";
        }
    }
}
