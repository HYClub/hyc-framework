using UnityEditor;
using UnityEngine.InputSystem;

namespace HYC.Framework.Editor
{
    /// <summary>输入组件生成菜单与资产右键菜单。</summary>
    public static class InputComponentGeneratorMenu
    {
        // priority=25：内容制作档（①）里的独立分组，夹在 UI(20-22) 与 Localization(30) 之间。
        // 必须显式给值 —— [MenuItem] 不写 priority 时默认是 1000，会沉到 Settings/Samples 之后。
        [MenuItem("HYC Framework/Input/Generate Input Components", false, 25)]
        public static void GenerateAll()
        {
            var guids = AssetDatabase.FindAssets("t:InputComponentMapping");
            if (guids.Length == 0)
            {
                UnityEngine.Debug.LogWarning(
                    "[HYC] 未找到 InputComponentMapping 资产。请先创建：Project 窗口右键 → Create → HYC Framework/Input/Input Component Mapping，并填入 InputActionAsset 与 map 去向。");
                return;
            }
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var mapping = AssetDatabase.LoadAssetAtPath<InputComponentMapping>(path);
                if (mapping == null) continue;
                var err = InputComponentGenerator.Generate(mapping);
                if (err != null)
                    UnityEngine.Debug.LogError($"[HYC] 生成失败（{path}）：\n{err}");
                else
                    UnityEngine.Debug.Log($"[HYC] 已生成输入组件（{path}）。");
            }
        }

        [MenuItem("Assets/HYC Framework/Input/Generate This Mapping (Input Component)", false, 100)]
        public static void GenerateSelected()
        {
            var mapping = Selection.activeObject as InputComponentMapping;
            if (mapping == null) return;
            var err = InputComponentGenerator.Generate(mapping);
            if (err != null) UnityEngine.Debug.LogError($"[HYC] 生成失败：\n{err}");
            else UnityEngine.Debug.Log("[HYC] 已生成输入组件。");
        }

        [MenuItem("Assets/HYC Framework/Input/Generate This Mapping (Input Component)", true)]
        public static bool GenerateSelectedValidate() => Selection.activeObject is InputComponentMapping;
    }
}
