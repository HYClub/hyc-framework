// ============================================================
// HYC Framework - 天赋树模块(Editor)
// 文件: Editor/Talent/TalentGenerateWizard.cs
// 说明: 「生成代码 / UI」对话框。
//       · 先 TalentTreeValidator.Validate 卡住错误；
//       · 再收集输出参数(目录 / 类名前缀 / 命名空间 / 仅选中节点)；
//       · 最后交给 TalentCodeGenerator 生成三件套。
//       ⚠️ 必须独立一个文件：Unity 只给「与 .cs 同名的类」生成 MonoScript，
//       ScriptableWizard / EditorWindow 依赖它序列化自身字段(asset 等)。
//       类名与文件名不一致时，Script 槽会显示 None (Mono Script)、字段丢失
//       （本类原先挤在 TalentCodeGenerator.cs 里，症状就是 asset 变 null、
//        对话框报「没有可生成的天赋树资产」）。
// ============================================================

using System.Collections.Generic;
using HYC.Framework.Talent;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Talent.Editor
{
    /// <summary>
    /// 「生成代码 / UI」对话框。先校验树, 再收集输出参数, 最后交给 <see cref="TalentCodeGenerator"/>。
    /// </summary>
    public class TalentGenerateWizard : ScriptableWizard
    {
        [HideInInspector] public TalentTreeAsset asset;
        [HideInInspector] public List<int> selectedIds = new List<int>();

        public string outputFolder = "Assets/Talent/Generated";
        public string classNamePrefix = "MyTree";
        public string namespaceName = "HYC.Framework.Talent.Generated";
        public bool onlySelectedNodes = false;

        private string _error;

        // 静态兜底：万一窗口字段没反序列化回来(历史原因 / 域重载)，也能从静态引用恢复。
        private static TalentTreeAsset _pendingAsset;
        private static List<int> _pendingSelected;

        public static void Show(TalentTreeAsset asset, List<int> selected)
        {
            if (asset == null) return;

            _pendingAsset = asset;
            _pendingSelected = selected ?? new List<int>();

            var w = DisplayWizard<TalentGenerateWizard>("生成天赋树代码 / UI", "生成");
            w.asset = asset;
            w.selectedIds = _pendingSelected;
            w.classNamePrefix = TalentCodeGenerator.SanitizePrefix(
                string.IsNullOrEmpty(asset.displayName) ? asset.name : asset.displayName);
            w.classNamePrefix = string.IsNullOrEmpty(w.classNamePrefix) ? "Talent" : w.classNamePrefix;
        }

        private void OnWizardUpdate()
        {
            if (asset == null) asset = _pendingAsset;          // 兜底
            if (selectedIds == null) selectedIds = _pendingSelected;

            _error = null;
            if (asset == null)
                _error = "没有可生成的天赋树资产。";
            else
            {
                var issues = TalentTreeValidator.Validate(asset).FindAll(i => i.IsError);
                if (issues.Count > 0)
                    _error = "天赋树有错误, 先修正:\n" + string.Join("\n", issues.ConvertAll(i => i.Message));
            }

            var p = TalentCodeGenerator.SanitizePrefix(classNamePrefix);
            if (string.IsNullOrEmpty(p)) _error = "类名前缀清洗后为空, 请换一个。";
            if (string.IsNullOrEmpty(outputFolder)) _error = "输出目录不能为空。";

            helpString = _error ?? ("将生成: " + p + "TalentController.cs / " + p + "TalentUI.cs / " + p + "TalentTree.prefab");
            errorString = _error;
        }

        private void OnWizardCreate()
        {
            if (!string.IsNullOrEmpty(_error)) return;
            var prefix = TalentCodeGenerator.SanitizePrefix(classNamePrefix);
            var only = onlySelectedNodes ? selectedIds : null;
            var err = TalentCodeGenerator.Generate(asset, only, outputFolder, prefix, namespaceName);
            if (!string.IsNullOrEmpty(err))
                EditorUtility.DisplayDialog("生成失败", err, "OK");
            else
                EditorUtility.DisplayDialog("生成完成", "已生成到:\n" + outputFolder, "OK");
        }
    }
}
