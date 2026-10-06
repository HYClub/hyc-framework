using HYC.Framework.Attr;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Attr.Editor
{
    [CustomEditor(typeof(AttributeSetAsset))]
    public class AttributeSetAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var set = (AttributeSetAsset)target;
            if (set.catalog == null)
            {
                EditorGUILayout.HelpBox("先指定 catalog。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("勾选要包含的属性", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            foreach (var e in set.catalog.entries)
            {
                bool on = set.keys.Contains(e.key);
                bool next = EditorGUILayout.ToggleLeft(e.key + "  (" + e.type + ")", on);
                if (next && !on) set.keys.Add(e.key);
                else if (!next && on) set.keys.Remove(e.key);
            }
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(set);
                set.Rebuild();
            }

            EditorGUILayout.HelpBox("values 已烘焙 " + set.values.Count + " 个属性（OnValidate 自动维护）。", MessageType.None);
        }
    }
}
