// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTDataWindowHelpers.cs
// 说明: 行为树编辑器辅助工具 - 列出所有树资产(供节点下拉选择)
// ============================================================

using System.Linq;
using UnityEditor;

namespace HYC.Framework.BT.Editor
{
    public struct TreeIdList
    {
        public string[] names;
        public long[] ids;
    }

    public static class BTDataWindowHelpers
    {
        /// <summary>列出项目内所有行为树资产(名称 + TreeId)。</summary>
        public static TreeIdList LoadAllTreeIds()
        {
            var guids = AssetDatabase.FindAssets("t:BTTreeAsset");
            var names = new System.Collections.Generic.List<string> { "(无)" };
            var ids = new System.Collections.Generic.List<long> { 0 };
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var tree = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(path);
                if (tree == null) continue;
                names.Add($"{tree.TreeId}: {tree.name}");
                ids.Add(tree.TreeId);
            }
            return new TreeIdList { names = names.ToArray(), ids = ids.ToArray() };
        }
    }
}
