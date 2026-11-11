using System;
using System.Collections.Generic;
using HYC.Framework.Config;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 模板文件夹右键 → 批量创建模板（手动入口；AI 直接调 <see cref="ConfigAssetFactory.CreateTemplates"/> 即可）。
    /// 在文本框按行粘贴规格：<c>显示名,类名</c>，可选追加 <c>|字段名:类型,...</c>（类型用 ConfigFieldType 名）。
    /// 创建后可选一键生成代码（走 <see cref="ConfigTemplateCodeGen.GenerateTemplates"/>，单次收尾）。
    /// </summary>
    public class ConfigTemplateBatchCreateWindow : EditorWindow
    {
        private string targetFolder = "Assets";
        private string specText = "";
        private bool generateCode = true;
        private Vector2 scroll;

        private static string SelectedFolder()
        {
            if (Selection.assetGUIDs.Length > 0)
            {
                var p = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
                if (AssetDatabase.IsValidFolder(p))
                    return p;
            }
            return "Assets";
        }

        [MenuItem("Assets/HYC Framework/Config/Create Templates (Batch)...", false, 100)]
        private static void Open()
        {
            var w = GetWindow<ConfigTemplateBatchCreateWindow>("Batch Create Templates");
            w.targetFolder = SelectedFolder();
            w.minSize = new Vector2(440, 380);
        }

        [MenuItem("Assets/HYC Framework/Config/Create Templates (Batch)...", true, 100)]
        private static bool ValidateOpen()
            => Selection.assetGUIDs.Length > 0
               && AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]));

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Target Folder", EditorStyles.boldLabel);
            EditorGUILayout.TextField(targetFolder);
            EditorGUILayout.HelpBox("在 Project 窗口右键模板文件夹打开本窗口，目标文件夹默认为右键的文件夹。", MessageType.Info);

            EditorGUILayout.LabelField("Specs (one per line)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "格式：显示名,类名\n可选追加字段：显示名,类名|字段名:类型,字段2:类型2\n" +
                "类型用 ConfigFieldType 枚举名（如 Int / String / Reference / Enum）。\n" +
                "Reference 全名：字段:Reference:全名 ｜ Enum 类名：字段:Enum:枚举类名",
                MessageType.None);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            specText = EditorGUILayout.TextArea(specText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            generateCode = EditorGUILayout.ToggleLeft("创建后生成代码", generateCode);

            if (GUILayout.Button("创建", GUILayout.Height(28)))
                Execute();
        }

        private void Execute()
        {
            var specs = ParseSpecs(specText);
            if (specs.Count == 0)
            {
                EditorUtility.DisplayDialog("Batch Create Templates", "没有可解析的规格。每行格式：显示名,类名", "OK");
                return;
            }
            if (!AssetDatabase.IsValidFolder(targetFolder))
            {
                EditorUtility.DisplayDialog("Batch Create Templates", $"目标文件夹无效：{targetFolder}", "OK");
                return;
            }
            foreach (var s in specs)
                s.folder = targetFolder;

            ConfigTemplate[] created;
            try
            {
                created = ConfigAssetFactory.CreateTemplates(specs);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Batch Create Templates", "创建失败（已整体回滚）：\n" + e.Message, "OK");
                return;
            }

            int genOk = 0, genFail = 0;
            if (generateCode)
            {
                var r = ConfigTemplateCodeGen.GenerateTemplates(created);
                genOk = r.ok;
                genFail = r.failed;
            }

            var msg = $"创建模板：{created.Length} 个";
            if (generateCode)
                msg += $"\n生成代码：成功 {genOk}，失败 {genFail}";
            EditorUtility.DisplayDialog("Batch Create Templates", msg, "OK");
            Close();
        }

        private static List<TemplateSpec> ParseSpecs(string text)
        {
            var result = new List<TemplateSpec>();
            if (string.IsNullOrWhiteSpace(text))
                return result;
            foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                var parts = line.Split('|');
                var head = parts[0].Split(',');
                if (head.Length < 1 || string.IsNullOrWhiteSpace(head[0]))
                    continue;
                var spec = new TemplateSpec
                {
                    displayName = head[0].Trim(),
                    className = head.Length > 1 ? head[1].Trim() : "",
                };
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    spec.fields = new List<TemplateFieldSpec>();
                    foreach (var f in parts[1].Split(','))
                    {
                        var ft = f.Trim();
                        if (ft.Length == 0)
                            continue;
                        var fs = ParseField(ft, spec.className);
                        if (fs != null)
                            spec.fields.Add(fs);
                    }
                }
                result.Add(spec);
            }
            return result;
        }

        private static TemplateFieldSpec ParseField(string token, string className)
        {
            // 支持 名称:类型 或 名称:类型:参数（Reference 全名 / self 关键字 / Enum 类名）
            var segs = token.Split(':');
            if (segs.Length < 2)
                return null;
            var name = segs[0].Trim();
            var typeStr = segs[1].Trim();
            if (!Enum.TryParse<ConfigFieldType>(typeStr, true, out var type))
                return null;
            var fs = new TemplateFieldSpec { name = name, type = type };
            if (segs.Length > 2)
            {
                var arg = segs[2].Trim();
                if (type == ConfigFieldType.Reference)
                {
                    fs.refTypeFullName = ConfigTemplateCodeGen.ResolveRefArgument(arg, className);
                    // 行内没写 className 时保留 self 标记，交给工厂用最终 className 解析
                    if (ConfigTemplateCodeGen.IsSelfRefKeyword(fs.refTypeFullName))
                        fs.selfReference = true;
                }
                else if (type == ConfigFieldType.Enum)
                    fs.enumRefClassName = arg;
            }
            return fs;
        }
    }
}
