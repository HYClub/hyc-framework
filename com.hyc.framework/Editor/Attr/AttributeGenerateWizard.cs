// ============================================================
// HYC Framework - 属性模块(Editor)
// 文件: Editor/Attr/AttributeGenerateWizard.cs
// 说明: 「生成属性访问器」对话框（优化方向 A）。
//       ⚠️ 必须独立一个文件：Unity 只给「与 .cs 同名的类」生成 MonoScript，
//       ScriptableWizard 依赖它序列化自身字段(catalog 等)。
// ============================================================

using HYC.Framework.Attr;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Attr.Editor
{
    /// <summary>「生成属性访问器」对话框。先校验目录，再收集输出参数，最后交给 <see cref="AttributeCodeGenerator"/>。</summary>
    public class AttributeGenerateWizard : ScriptableWizard
    {
        [HideInInspector] public AttributeCatalog catalog;
        public string outputFolder = "Assets/HYC/Generated/Attributes";
        public string classNamePrefix = "Game";
        public string namespaceName = "HYC.Framework.Attr.Generated";

        private string _error;
        private static AttributeCatalog _pending;

        public static void Show(AttributeCatalog cat)
        {
            if (cat == null) return;
            _pending = cat;
            var w = DisplayWizard<AttributeGenerateWizard>("生成属性访问器", "生成");
            w.catalog = cat;
            w.classNamePrefix = AttributeCodeGenerator.SanitizePrefix(cat.name);
            w.classNamePrefix = string.IsNullOrEmpty(w.classNamePrefix) ? "Game" : w.classNamePrefix;
        }

        private void OnWizardUpdate()
        {
            if (catalog == null) catalog = _pending;   // 兜底：域重载后字段可能丢失
            _error = null;
            if (catalog == null)
                _error = "没有可生成的属性目录资产。";
            else
            {
                var issues = AttributeValidator.Validate(catalog).FindAll(i => i.IsError);
                if (issues.Count > 0)
                    _error = "属性目录有错误，先修正：\n" + string.Join("\n", issues.ConvertAll(i => i.Message));
            }
            var p = AttributeCodeGenerator.SanitizePrefix(classNamePrefix);
            if (string.IsNullOrEmpty(p)) _error = "类名前缀清洗后为空，请换一个。";
            if (string.IsNullOrEmpty(outputFolder)) _error = "输出目录不能为空。";
            helpString = _error ?? ("将生成: " + p + "AttrKey.cs / " + p + "AttrKeyMap.cs / " + p + "AttrBag.cs");
            errorString = _error;
        }

        private void OnWizardCreate()
        {
            if (!string.IsNullOrEmpty(_error)) return;
            var prefix = AttributeCodeGenerator.SanitizePrefix(classNamePrefix);
            var err = AttributeCodeGenerator.Generate(catalog, outputFolder, prefix, namespaceName);
            if (!string.IsNullOrEmpty(err)) EditorUtility.DisplayDialog("生成失败", err, "OK");
            else EditorUtility.DisplayDialog("生成完成", "已生成到:\n" + outputFolder, "OK");
        }
    }
}
