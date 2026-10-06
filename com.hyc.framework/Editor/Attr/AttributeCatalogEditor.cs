using HYC.Framework.Attr;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Attr.Editor
{
    [CustomEditor(typeof(AttributeCatalog))]
    public class AttributeCatalogEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            var cat = (AttributeCatalog)target;

            if (GUILayout.Button("生成属性访问器（enum + 映射 + 类型安全 AttrBag）"))
                AttributeGenerateWizard.Show(cat);

            if (GUILayout.Button("校验目录"))
            {
                var issues = AttributeValidator.Validate(cat);
                if (issues.Count == 0) EditorUtility.DisplayDialog("校验通过", "目录没有错误。", "OK");
                else
                {
                    var msg = string.Join("\n", issues.ConvertAll(i => (i.IsError ? "[错误] " : "[警告] ") + i.Message));
                    EditorUtility.DisplayDialog("校验结果", msg, "OK");
                }
            }
        }

        // priority=41：Attributes 档内。
        [MenuItem("HYC Framework/Attributes/Generate Accessor", false, 41)]
        static void MenuGenerate()
        {
            var cat = Selection.activeObject as AttributeCatalog;
            if (cat == null)
            {
                var guids = AssetDatabase.FindAssets("t:AttributeCatalog");
                if (guids.Length > 0)
                    cat = AssetDatabase.LoadAssetAtPath<AttributeCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (cat == null)
            {
                EditorUtility.DisplayDialog("生成属性访问器", "项目中找不到 AttributeCatalog 资产（先创建并填好属性）。", "OK");
                return;
            }
            AttributeGenerateWizard.Show(cat);
        }
    }
}
