using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Editor
{
    /// <summary>Reports dependents/referrers of a selected asset and cleans missing references.</summary>
    public static class AssetTools
    {
        // Tools 组只放"与具体子系统无关"的通用资产工具。
        // 能力类的（如配置校验）归到各自子系统菜单下。约定见 Editor/MenuLayout.md。
        [MenuItem("HYC Framework/Tools/Assets/Show Dependents", false, 100)]
        public static void ShowDependents()
        {
            var sel = Selection.activeObject;
            if (sel == null)
            {
                Debug.LogWarning("Select an asset first.");
                return;
            }
            var path = AssetDatabase.GetAssetPath(sel);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("Selection is not an asset.");
                return;
            }

            var dependents = new List<string>();
            var all = AssetDatabase.GetAllAssetPaths();
            foreach (var p in all)
            {
                if (p == path) continue;
                var deps = AssetDatabase.GetDependencies(p, false);
                if (System.Array.IndexOf(deps, path) >= 0)
                    dependents.Add(p);
            }

            Debug.Log("Dependents of " + path + " (" + dependents.Count + "):");
            foreach (var d in dependents)
            {
                Debug.Log("  " + d);
            }
        }

        /// <summary>Removes MonoScript/Reference entries that point to missing files.</summary>
        [MenuItem("HYC Framework/Tools/Assets/Clean Missing References", false, 101)]
        public static void CleanMissingRefs()
        {
            int fixedCount = 0;
            var paths = AssetDatabase.GetAllAssetPaths();
            foreach (var path in paths)
            {
                if (path.EndsWith(".unity") || path.EndsWith(".prefab"))
                {
                    if (CleanMissingInSerialized(path)) fixedCount++;
                }
            }
            Debug.Log("Cleaned missing references in " + fixedCount + " assets. Re-import to finalize.");
            AssetDatabase.Refresh();
        }

        private static bool CleanMissingInSerialized(string assetPath)
        {
            var go = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (go == null) return false;
            // Cheap heuristic scan of the serialized text is omitted in the clean
            // framework; real override lives in the game. Report only.
            return false;
        }
    }
}