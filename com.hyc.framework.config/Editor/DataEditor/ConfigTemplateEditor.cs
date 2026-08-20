using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Right-pane editor for <see cref="ConfigTemplate"/> assets inside the
    /// data editor: edit display name / class name / order and the field list
    /// (name, description, type, list flag), then save or generate the C#
    /// config class. Replaces the standalone template window.
    /// </summary>
    [CfgEditor(typeof(ConfigTemplate))]
    public class ConfigTemplateEditor : ConfigDataEditor
    {
        private SerializedProperty mFieldsProp;
        private Vector2 mScroll;

        protected override void Init()
        {
            mFieldsProp = mTarget.FindProperty("fields");
        }

        public override void OnGUI(float viewW, float viewH)
        {
            if (mTarget == null)
                return;

            mTarget.Update();

            // 顶部标题栏：模板显示名-类名
            var tpl = mTarget.targetObject as ConfigTemplate;
            var title = tpl != null ? $"{tpl.displayName}-{tpl.className}" : "配置模板-ConfigTemplate";
            GUILayout.BeginArea(new Rect(6, 4, viewW - 12, 20));
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUILayout.EndArea();
            GUIDrawer.FillRect(new Rect(1, 26, viewW - 2, 1));

            const float top = 32;
            GUILayout.BeginArea(new Rect(6, top, viewW - 12, viewH - top - 6));

            var tplNow = mTarget.targetObject as ConfigTemplate;
            var classValid = true;

            // 页签切换
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            mActiveTab = GUILayout.Toolbar(mActiveTab, new[] { "基础", "字段", "字段检查配置" }, EditorStyles.toolbarButton);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            mScroll = EditorGUILayout.BeginScrollView(mScroll);

            if (mActiveTab == 0)
            {
                DrawBasicTab();
            }
            else if (mActiveTab == 1)
            {
                DrawFieldsTab(ref classValid);
            }
            else
            {
                DrawChecksTab();
            }

            EditorGUILayout.EndScrollView();

            // 错误汇总（公共区）
            var tplErrors = new List<string>();

            if (!classValid)
            {
                var cp = mTarget.FindProperty("className");
                tplErrors.Add($"类名 \u201c{cp.stringValue}\u201d 不是合法的 C# 标识符");
            }

            var fieldError = FindFirstFieldError();
            if (fieldError != null)
            {
                tplErrors.Add(fieldError.Kind == 0
                    ? $"字段名 \u201c{fieldError.Name}\u201d 不是合法的 C# 标识符"
                    : (fieldError.Kind == 1
                        ? $"字段名 \u201c{fieldError.Name}\u201d 重复"
                        : $"字段名 \u201c{fieldError.Name}\u201d 与基类字段重复"));
            }

            if (tplNow != null)
            {
                foreach (var other in ConfigTemplateCodeGen.LoadAllTemplates())
                {
                    if (other == tplNow)
                        continue;
                    if (other.className == tplNow.className)
                    {
                        tplErrors.Add($"类名 {tplNow.className} 已被其他模板使用");
                        break;
                    }
                }

                if (tplNow.baseTemplate != null)
                {
                    var chainNow = new List<ConfigTemplate>();
                    ConfigTemplateCodeGen.TryBuildInheritanceChain(tplNow, chainNow, out _);
                    foreach (var c in chainNow)
                    {
                        if (!ConfigTemplateCodeGen.IsGenerated(c))
                            tplErrors.Add($"基类 {c.className} 未生成代码，请先生成基类");
                    }
                }
            }

            if (mFieldsProp != null)
            {
                for (var i = 0; i < mFieldsProp.arraySize; i++)
                {
                    var fp = mFieldsProp.GetArrayElementAtIndex(i);
                    var ftype = (ConfigFieldType)fp.FindPropertyRelative("type").intValue;
                    var fref = fp.FindPropertyRelative("refTypeFullName").stringValue;
                    var fname = fp.FindPropertyRelative("name").stringValue;
                    if (ftype == ConfigFieldType.Reference && !string.IsNullOrEmpty(fref) && Type.GetType(fref) == null)
                    {
                        tplErrors.Add($"字段 {fname} 的引用类型不存在（未生成或已删除）");
                    }
                    if (ftype == ConfigFieldType.Enum)
                    {
                        var fenum = fp.FindPropertyRelative("enumRefClassName").stringValue;
                        if (string.IsNullOrEmpty(fenum))
                            tplErrors.Add($"字段 {fname} 已选枚举，但未选择枚举定义");
                        else if (ConfigEnumCodeGen.FindEnum(fenum) == null)
                            tplErrors.Add($"字段 {fname} 的枚举 {fenum} 不存在（未创建或已删除）");
                        else if (!ConfigEnumCodeGen.IsGenerated(ConfigEnumCodeGen.FindEnum(fenum)))
                            tplErrors.Add($"字段 {fname} 的枚举 {fenum} 未生成代码，请先生成枚举代码");
                    }
                }
            }

            foreach (var e in tplErrors)
                EditorGUILayout.HelpBox(e, MessageType.Error);

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "字段类型选\u201c配置引用…\u201d可从所有已注册配置类型中选择，生成资产引用字段。\n" +
                "字段\u201c导出\u201d决定客户端/服务器类型包含的字段；\u201c检查\u201d配置在字段检查配置页。",
                MessageType.Info);

            // 右下角：生成代码 + 保存模板（最底部）
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("生成代码", GUILayout.Width(90)))
                Generate();
            if (GUILayout.Button("保存模板", GUILayout.Width(90)))
                Save();
            EditorGUILayout.EndHorizontal();

            GUILayout.EndArea();

            mTarget.ApplyModifiedProperties();
        }

        private int mActiveTab;

        /// <summary>基础页：显示名/类名（改名）/基类/图标。</summary>
        private void DrawBasicTab()
        {
            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            EditorGUILayout.LabelField("配置模板", EditorStyles.boldLabel);

            // displayName
            var displayProp = mTarget.FindProperty("displayName");
            var displayValue = displayProp.stringValue;
            EditorGUILayout.PropertyField(displayProp);
            var sanitized = ConfigTemplateCodeGen.SanitizeDisplayName(displayValue);
            if (sanitized != displayValue)
            {
                displayProp.stringValue = sanitized;
                mTarget.ApplyModifiedProperties();
            }

            // className（生成过则只读 + 改名按钮）
            var classProp = mTarget.FindProperty("className");
            var classValid = ConfigTemplateCodeGen.IsValidIdentifier(classProp.stringValue);
            var generated = !string.IsNullOrEmpty(tpl.lastGeneratedClassName);

            var oldBg = GUI.backgroundColor;
            var oldContent = GUI.contentColor;
            var oldEnabled = GUI.enabled;
            if (!classValid)
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                GUI.contentColor = new Color(1f, 0.55f, 0.55f);
            }

            EditorGUILayout.BeginHorizontal();
            if (generated)
                GUI.enabled = false;
            EditorGUILayout.PropertyField(classProp);
            GUI.enabled = oldEnabled;

            if (generated && GUILayout.Button("改名…", GUILayout.Width(64)))
            {
                PromptRename(tpl);
            }
            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = oldBg;
            GUI.contentColor = oldContent;

            EditorGUILayout.Space(4);

            // 基类
            EditorGUILayout.LabelField("基类", EditorStyles.boldLabel);
            var baseProp = mTarget.FindProperty("baseTemplate");
            var baseObj = baseProp.objectReferenceValue as ConfigTemplate;
            var baseUngenerated = baseObj != null && !ConfigTemplateCodeGen.IsGenerated(baseObj);
            var baseOldBg = GUI.backgroundColor;
            var baseOldContent = GUI.contentColor;
            if (baseUngenerated)
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                GUI.contentColor = new Color(1f, 0.55f, 0.55f);
            }
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(baseProp, new GUIContent("基类模板"));
            if (EditorGUI.EndChangeCheck())
            {
                mTarget.ApplyModifiedProperties();
            }
            // 保护：基类不能为空——清空后回归 ConfigBase
            if (baseProp.objectReferenceValue == null)
            {
                var cfgBase = ConfigTemplateCodeGen.GetConfigBaseTemplate();
                if (cfgBase != null)
                {
                    baseProp.objectReferenceValue = cfgBase;
                    mTarget.ApplyModifiedProperties();
                }
            }
            GUI.backgroundColor = baseOldBg;
            GUI.contentColor = baseOldContent;

            // 图标
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("图标", EditorStyles.boldLabel);
            DrawIconField();
        }

        /// <summary>字段页：字段列表（含导出目标 + 范围）。</summary>
        private void DrawFieldsTab(ref bool classValid)
        {
            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            classValid = ConfigTemplateCodeGen.IsValidIdentifier(mTarget.FindProperty("className").stringValue);

            EditorGUILayout.LabelField("字段列表（基类字段只读）", EditorStyles.boldLabel);
            DrawMergedFieldTable();
        }

        /// <summary>字段检查配置页：每字段 非空/非0/范围 × 级别。</summary>
        private void DrawChecksTab()
        {
            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            EditorGUILayout.LabelField("字段检查配置", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            var allFields = ConfigTemplateCodeGen.GetAllFields(tpl, out _);

            // 表头
            var headerRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            var w = headerRect.width;
            EditorGUI.LabelField(new Rect(headerRect.x, headerRect.y, w * 0.30f, headerRect.height), "字段", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(headerRect.x + w * 0.30f, headerRect.y, w * 0.16f, headerRect.height), "非空", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(headerRect.x + w * 0.46f, headerRect.y, w * 0.16f, headerRect.height), "非0", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(headerRect.x + w * 0.62f, headerRect.y, w * 0.16f, headerRect.height), "范围", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(headerRect.x + w * 0.78f, headerRect.y, w * 0.22f, headerRect.height), "范围值(Min~Max)", EditorStyles.boldLabel);

            EditorGUILayout.Space(2);

            var lineH = EditorGUIUtility.singleLineHeight + 2;
            var levelNames = new[] { "不检查", "Info", "Warning", "Error" };

            // 遍历自身字段（检查配置只对自身可编辑字段生效）
            for (var i = 0; i < mFieldsProp.arraySize; i++)
            {
                var prop = mFieldsProp.GetArrayElementAtIndex(i);
                var nameProp = prop.FindPropertyRelative("name");
                var notEmptyProp = prop.FindPropertyRelative("notEmptyCheck");
                var notZeroProp = prop.FindPropertyRelative("notZeroCheck");
                var rangeProp = prop.FindPropertyRelative("rangeCheck");
                var hasRangeProp = prop.FindPropertyRelative("hasRange");
                var minProp = prop.FindPropertyRelative("minValue");
                var maxProp = prop.FindPropertyRelative("maxValue");

                var row = EditorGUILayout.GetControlRect(true, lineH);
                EditorGUI.LabelField(new Rect(row.x, row.y, row.width * 0.30f, row.height), nameProp.stringValue);

                DrawLevelPopup(new Rect(row.x + row.width * 0.30f, row.y, row.width * 0.16f - 2, row.height), notEmptyProp, levelNames);
                DrawLevelPopup(new Rect(row.x + row.width * 0.46f, row.y, row.width * 0.16f - 2, row.height), notZeroProp, levelNames);
                DrawLevelPopup(new Rect(row.x + row.width * 0.62f, row.y, row.width * 0.16f - 2, row.height), rangeProp, levelNames);

                // 范围值
                var rangeRect = new Rect(row.x + row.width * 0.78f, row.y, row.width * 0.22f, row.height);
                if (hasRangeProp.boolValue)
                {
                    EditorGUI.LabelField(new Rect(rangeRect.x, rangeRect.y, 28, rangeRect.height), "Min", EditorStyles.miniLabel);
                    EditorGUI.FloatField(new Rect(rangeRect.x + 26, rangeRect.y, rangeRect.width * 0.35f, rangeRect.height), minProp.floatValue);
                    EditorGUI.LabelField(new Rect(rangeRect.x + rangeRect.width * 0.35f + 30, rangeRect.y, 30, rangeRect.height), "Max", EditorStyles.miniLabel);
                    EditorGUI.FloatField(new Rect(rangeRect.x + rangeRect.width * 0.35f + 58, rangeRect.y, rangeRect.width * 0.3f, rangeRect.height), maxProp.floatValue);
                }
                else
                {
                    EditorGUI.LabelField(rangeRect, "未启用（勾选范围级别后可用）", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "每项检查可独立选择级别：不检查 / Info / Warning / Error。\n" +
                "选择\u201c范围\u201d级别后需在字段页设置 Min/Max 值。",
                MessageType.Info);
        }

        /// <summary>级别下拉（不检查/Info/Warning/Error）。</summary>
        private void DrawLevelPopup(Rect rect, SerializedProperty levelProp, string[] levelNames)
        {
            var idx = Mathf.Clamp(levelProp.enumValueIndex, 0, levelNames.Length - 1);
            EditorGUI.BeginChangeCheck();
            idx = EditorGUI.Popup(rect, idx, levelNames);
            if (EditorGUI.EndChangeCheck())
                levelProp.enumValueIndex = idx;
        }

        /// <summary>图标字段绘制：当前图标预览 + 选择内置 / 浏览自定义。</summary>
        private void DrawIconField()
        {
            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            var builtInProp = mTarget.FindProperty("iconBuiltInName");
            var customProp = mTarget.FindProperty("iconCustom");

            EditorGUILayout.BeginHorizontal();

            // 当前图标预览
            var tex = ConfigTemplateIcon.Resolve(tpl);
            var previewRect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32), GUILayout.Height(32));
            if (tex != null)
                GUI.DrawTexture(previewRect, tex);
            else
                GUI.Box(previewRect, GUIContent.none);

            EditorGUILayout.BeginVertical();

            // 内置图标选择
            var builtInLabel = string.IsNullOrEmpty(builtInProp.stringValue)
                ? "选择内置图标…"
                : $"内置: {builtInProp.stringValue}";
            if (GUILayout.Button(builtInLabel, GUILayout.Height(22)))
            {
                ConfigIconPicker.Open(name =>
                {
                    builtInProp.stringValue = name;
                    customProp.objectReferenceValue = null;
                    mTarget.ApplyModifiedProperties();
                }, tex2d =>
                {
                    customProp.objectReferenceValue = tex2d;
                    mTarget.ApplyModifiedProperties();
                });
            }

            // 自定义图标 + 清除
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("浏览项目图标…", GUILayout.Height(20)))
            {
                ConfigIconPicker.Open(name =>
                {
                    builtInProp.stringValue = name;
                    customProp.objectReferenceValue = null;
                    mTarget.ApplyModifiedProperties();
                }, tex2d =>
                {
                    customProp.objectReferenceValue = tex2d;
                    mTarget.ApplyModifiedProperties();
                });
            }
            if (GUILayout.Button("清除", GUILayout.Width(50), GUILayout.Height(20)))
            {
                builtInProp.stringValue = "";
                customProp.objectReferenceValue = null;
                mTarget.ApplyModifiedProperties();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            // 当前选中显示
            if (customProp.objectReferenceValue != null)
                EditorGUILayout.LabelField("当前: 自定义图标", EditorStyles.miniLabel);
            else if (!string.IsNullOrEmpty(builtInProp.stringValue))
                EditorGUILayout.LabelField($"当前: 内置图标 ({builtInProp.stringValue})", EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField("当前: 默认图标", EditorStyles.miniLabel);
        }

        /// <summary>并表绘制：表头 + 基类只读字段 + 自身可编辑字段。</summary>
        private void DrawMergedFieldTable()
        {
            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            // 基类只读字段（顶层 → 直接基类）
            var chain = new List<ConfigTemplate>();
            ConfigTemplateCodeGen.TryBuildInheritanceChain(tpl, chain, out var chainError);
            if (chainError != null)
            {
                EditorGUILayout.HelpBox(chainError, MessageType.Error);
                return;
            }

            var lineH = EditorGUIUtility.singleLineHeight + 2;
            var rowH = lineH * 2; // 双行：字段编辑 + 导出/检查

            // 基类是否有未生成的（需要红色提示）
            var chainHasUngenerated = false;
            foreach (var c in chain)
            {
                if (!ConfigTemplateCodeGen.IsGenerated(c))
                    chainHasUngenerated = true;
            }
            if (chainHasUngenerated)
            {
                var warnRect = EditorGUILayout.GetControlRect(true, lineH);
                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                GUI.Box(warnRect, "基类代码未生成，请先为基类模板生成代码");
                GUI.backgroundColor = oldBg;
                EditorGUILayout.Space(2);
            }

            // 表头
            var headerRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            var (hName, hDesc, hType, hList, hExport, hRange) = ColumnWidths(headerRect.width, headerRect.y, headerRect.height);
            EditorGUI.LabelField(hName, "字段名", EditorStyles.boldLabel);
            EditorGUI.LabelField(hDesc, "描述", EditorStyles.boldLabel);
            EditorGUI.LabelField(hType, "类型", EditorStyles.boldLabel);
            EditorGUI.LabelField(hList, "列表", EditorStyles.boldLabel);
            EditorGUI.LabelField(hExport, "导出", EditorStyles.boldLabel);
            EditorGUI.LabelField(hRange, "范围", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(headerRect.xMax - 60, headerRect.y, 60, headerRect.height), "操作", EditorStyles.boldLabel);

            // 基类字段行（只读）
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var f in chain[i].fields)
                {
                    var row = EditorGUILayout.GetControlRect(true, lineH);
                    DrawReadOnlyFieldRow(row, chain[i].className, f);
                }
            }

            if (chain.Count > 0)
            {
                EditorGUILayout.Space(2);
                GUIDrawer.FillRect(EditorGUILayout.GetControlRect(true, 1));
                EditorGUILayout.Space(2);
            }

            // 自身字段行（主行单行 + 可选展开明细）
            for (var i = 0; i < mFieldsProp.arraySize; i++)
            {
                var index = i;
                var row = EditorGUILayout.GetControlRect(true, lineH);
                DrawEditableFieldRow(row, index);

                // 展开明细区
                if (mExpandedFields.Contains(index))
                {
                    var detailProp = mFieldsProp.GetArrayElementAtIndex(index);
                    DrawExpandedFieldDetail(detailProp);
                    EditorGUILayout.Space(2);
                }
            }

            // 添加按钮
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 添加字段", GUILayout.Width(90)))
            {
                mFieldsProp.arraySize++;
                mTarget.ApplyModifiedProperties();
            }
            EditorGUILayout.EndHorizontal();
        }

        private (Rect, Rect, Rect, Rect, Rect, Rect) ColumnWidths(float total, float y, float height)
        {
            const float opsW = 60f; // 上移/下移/删除 按钮区
            var avail = total - opsW;
            var x = 0f;
            var name = new Rect(x, y, avail * 0.10f, height); x += name.width;   // 字段名
            var desc = new Rect(x, y, avail * 0.12f, height); x += desc.width;   // 描述
            var type = new Rect(x, y, avail * 0.085f, height); x += type.width;  // 类型
            var list = new Rect(x, y, avail * 0.03f, height); x += list.width;   // 列表
            var export = new Rect(x, y, avail * 0.16f, height); x += export.width; // 导出
            var range = new Rect(x, y, avail - x + avail * 0f, height); // 剩余给范围
            range = new Rect(x, y, avail - x, height);
            var ops = new Rect(total - opsW, y, opsW, height);
            return (name, desc, type, list, export, range);
        }

        private void DrawReadOnlyFieldRow(Rect row, string sourceClass, ConfigTemplateField f)
        {
            var (nameR, descR, typeR, listR, _, _) = ColumnWidths(row.width, row.y, row.height);

            // 基类字段：只读（label 样式），与可编辑的输入框区分
            EditorGUI.LabelField(nameR, f.name);
            EditorGUI.LabelField(descR, f.description);

            // 类型 + 来源标注（放在类型列内，不超出窗口）
            var typeLabel = FieldTypeLabel(f);
            var srcLabel = $"({sourceClass})";
            var srcWidth = GUI.skin.label.CalcSize(new GUIContent(srcLabel)).x + 4;
            var typeWidth = typeR.width - srcWidth;
            if (typeWidth < 30)
                typeWidth = 30;
            EditorGUI.LabelField(new Rect(typeR.x, typeR.y, typeWidth, typeR.height), typeLabel);
            EditorGUI.LabelField(new Rect(typeR.x + typeWidth, typeR.y, srcWidth, typeR.height), srcLabel, EditorStyles.miniLabel);

            EditorGUI.LabelField(listR, f.isList ? "List" : "");
        }

        /// <summary>是否为数值类型（可配 Min/Max 范围）。</summary>
        private static bool IsNumericType(ConfigFieldType type)
        {
            switch (type)
            {
                case ConfigFieldType.Int:
                case ConfigFieldType.Long:
                case ConfigFieldType.Float:
                case ConfigFieldType.Double:
                case ConfigFieldType.Short:
                case ConfigFieldType.Byte:
                case ConfigFieldType.UInt:
                case ConfigFieldType.Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private void DrawEditableFieldRow(Rect row, int index)
        {
            var prop = mFieldsProp.GetArrayElementAtIndex(index);
            var nameProp = prop.FindPropertyRelative("name");
            var descProp = prop.FindPropertyRelative("description");
            var typeProp = prop.FindPropertyRelative("type");
            var listProp = prop.FindPropertyRelative("isList");
            var refProp = prop.FindPropertyRelative("refTypeFullName");
            var enumProp = prop.FindPropertyRelative("enumRefClassName");
            var exportProp = prop.FindPropertyRelative("exportTarget");

            var lineH = EditorGUIUtility.singleLineHeight;
            var ctrlY = row.y + (lineH - 16) * 0.5f; // 16px 控件垂直居中

            // 展开箭头
            var expanded = mExpandedFields.Contains(index);
            if (GUI.Button(new Rect(row.x + 2, ctrlY, 16, 16), expanded ? "▼" : "▶", EditorStyles.label))
            {
                if (expanded) mExpandedFields.Remove(index);
                else mExpandedFields.Add(index);
            }

            var (nameR, descR, typeR, listR, exportR, _) = ColumnWidths(row.width, row.y, lineH);
            // 行首让出箭头宽度
            var shiftX = 20f;
            var nameRect = new Rect(nameR.x + shiftX, ctrlY, nameR.width - shiftX - 2, 16);
            var descRect = new Rect(descR.x + shiftX, ctrlY, descR.width - 2, 16);
            var typeRect = new Rect(typeR.x + shiftX, ctrlY, typeR.width - 2, 16);
            var listRect = new Rect(listR.x + shiftX, ctrlY, 16, 16);
            var exportRect = new Rect(exportR.x + shiftX, ctrlY, exportR.width - 2, 16);

            // 字段名：非法标识符或重复时标红
            var nameValid = ConfigTemplateCodeGen.IsValidIdentifier(nameProp.stringValue) && !IsDuplicateFieldName(index);
            var refMissing = (ConfigFieldType)typeProp.intValue == ConfigFieldType.Reference
                            && !string.IsNullOrEmpty(refProp.stringValue)
                            && Type.GetType(refProp.stringValue) == null;
            var enumMissing = (ConfigFieldType)typeProp.intValue == ConfigFieldType.Enum
                              && (string.IsNullOrEmpty(enumProp.stringValue)
                                  || ConfigEnumCodeGen.FindEnum(enumProp.stringValue) == null);

            var oldBg = GUI.backgroundColor;
            var oldContent = GUI.contentColor;
            if (nameValid && (refMissing || enumMissing))
            {
                var bg = new Rect(row.x, row.y, row.width, lineH);
                GUI.color = new Color(1f, 0.55f, 0.55f, 0.25f);
                GUI.DrawTexture(bg, EditorGUIUtility.whiteTexture);
                GUI.color = Color.white;
            }
            if (!nameValid)
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                GUI.contentColor = new Color(1f, 0.55f, 0.55f);
            }

            nameProp.stringValue = EditorGUI.TextField(nameRect, nameProp.stringValue);
            GUI.backgroundColor = oldBg;
            GUI.contentColor = oldContent;

            descProp.stringValue = EditorGUI.TextField(descRect, descProp.stringValue);
            DrawTypeField(typeRect, typeProp, refProp, enumProp, exportProp);
            listProp.boolValue = EditorGUI.Toggle(listRect, listProp.boolValue);

            // 导出下拉（主行）
            var exportNames = new[] { "客户端", "服务器", "两者" };
            var exportIdx = Mathf.Clamp(exportProp.enumValueIndex, 0, 2);
            EditorGUI.BeginChangeCheck();
            exportIdx = EditorGUI.Popup(exportRect, exportIdx, exportNames);
            if (EditorGUI.EndChangeCheck())
                exportProp.enumValueIndex = exportIdx;

            // 操作按钮（右侧，16px 高垂直居中）
            var btnW = 20f;
            if (GUI.Button(new Rect(row.width - 60, ctrlY, btnW, 16), "↑"))
                MoveOwnField(index, -1);
            if (GUI.Button(new Rect(row.width - 40, ctrlY, btnW, 16), "↓"))
                MoveOwnField(index, 1);
            if (GUI.Button(new Rect(row.width - 20, ctrlY, btnW, 16), "×"))
                DeleteOwnField(index);
        }

        private readonly List<int> mExpandedFields = new List<int>();

        /// <summary>展开明细区：字段描述 + 范围(Min/Max) + 引用信息。</summary>
        private void DrawExpandedFieldDetail(SerializedProperty prop)
        {
            var typeProp = prop.FindPropertyRelative("type");
            var descProp = prop.FindPropertyRelative("description");
            var hasRangeProp = prop.FindPropertyRelative("hasRange");
            var minProp = prop.FindPropertyRelative("minValue");
            var maxProp = prop.FindPropertyRelative("maxValue");
            var isNumeric = IsNumericType((ConfigFieldType)typeProp.intValue);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("描述", GUILayout.Width(50));
            descProp.stringValue = EditorGUILayout.TextField(descProp.stringValue);
            EditorGUILayout.EndHorizontal();

            if (isNumeric)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("范围", GUILayout.Width(50));
                hasRangeProp.boolValue = EditorGUILayout.Toggle(hasRangeProp.boolValue, GUILayout.Width(20));
                if (hasRangeProp.boolValue)
                {
                    minProp.floatValue = EditorGUILayout.FloatField(minProp.floatValue, GUILayout.Width(60));
                    EditorGUILayout.LabelField("~", GUILayout.Width(14));
                    maxProp.floatValue = EditorGUILayout.FloatField(maxProp.floatValue, GUILayout.Width(60));
                }
                else
                {
                    EditorGUILayout.LabelField("未启用", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void MoveOwnField(int index, int delta)
        {
            var target = index + delta;
            if (target < 0 || target >= mFieldsProp.arraySize)
                return;            mFieldsProp.MoveArrayElement(index, target);
            mTarget.ApplyModifiedProperties();
        }

        private void DeleteOwnField(int index)
        {
            if (index < 0 || index >= mFieldsProp.arraySize)
                return;
            mFieldsProp.DeleteArrayElementAtIndex(index);
            mTarget.ApplyModifiedProperties();
        }

        /// <summary>True when this own field's name duplicates another own field or any inherited field.</summary>
        private bool IsDuplicateFieldName(int index)
        {
            if (mFieldsProp == null)
                return false;

            var name = mFieldsProp.GetArrayElementAtIndex(index).FindPropertyRelative("name").stringValue;
            if (string.IsNullOrEmpty(name))
                return false;

            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return false;

            // 继承字段名
            var chain = new List<ConfigTemplate>();
            ConfigTemplateCodeGen.TryBuildInheritanceChain(tpl, chain, out _);
            foreach (var baseTpl in chain)
            {
                foreach (var baseField in baseTpl.fields)
                {
                    if (baseField.name == name)
                        return true;
                }
            }

            // 自身其他字段
            for (var i = 0; i < mFieldsProp.arraySize; i++)
            {
                if (i == index)
                    continue;
                if (mFieldsProp.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name)
                    return true;
            }
            return false;
        }

        /// <summary>First own field error: Kind 0 = invalid identifier, 1 = duplicate, 2 = collides with inherited. Null when clean.</summary>
        private FieldError FindFirstFieldError()
        {
            if (mFieldsProp == null)
                return null;

            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return null;

            // 继承字段名集合
            var inherited = new HashSet<string>();
            var chain = new List<ConfigTemplate>();
            ConfigTemplateCodeGen.TryBuildInheritanceChain(tpl, chain, out _);
            foreach (var baseTpl in chain)
                foreach (var baseField in baseTpl.fields)
                    inherited.Add(baseField.name);

            var own = new HashSet<string>();
            for (var i = 0; i < mFieldsProp.arraySize; i++)
            {
                var name = mFieldsProp.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue;
                if (!ConfigTemplateCodeGen.IsValidIdentifier(name))
                    return new FieldError { Name = name, Kind = 0 };
                if (!own.Add(name))
                    return new FieldError { Name = name, Kind = 1 };
                if (inherited.Contains(name))
                    return new FieldError { Name = name, Kind = 2 };
            }
            return null;
        }

        private class FieldError
        {
            public string Name;
            public int Kind;
        }

        /// <summary>字段类型下拉的分类路径映射（值类型/Unity 数学/Unity 对象），用于多级菜单。</summary>
        private static readonly Dictionary<ConfigFieldType, string> TypeMenuPaths = new Dictionary<ConfigFieldType, string>
        {
            // 值类型
            { ConfigFieldType.String, "值类型/字符串" },
            { ConfigFieldType.Int, "值类型/整数" },
            { ConfigFieldType.Long, "值类型/整数" },
            { ConfigFieldType.Short, "值类型/整数" },
            { ConfigFieldType.Byte, "值类型/整数" },
            { ConfigFieldType.UInt, "值类型/整数" },
            { ConfigFieldType.Float, "值类型/浮点" },
            { ConfigFieldType.Double, "值类型/浮点" },
            { ConfigFieldType.Decimal, "值类型/浮点" },
            { ConfigFieldType.Bool, "值类型/布尔" },
            { ConfigFieldType.Char, "值类型/字符" },
            // Unity 数学类型
            { ConfigFieldType.Vector2, "Unity 数学/向量" },
            { ConfigFieldType.Vector3, "Unity 数学/向量" },
            { ConfigFieldType.Vector4, "Unity 数学/向量" },
            { ConfigFieldType.Vector2Int, "Unity 数学/向量" },
            { ConfigFieldType.Vector3Int, "Unity 数学/向量" },
            { ConfigFieldType.Quaternion, "Unity 数学/旋转" },
            { ConfigFieldType.Color, "Unity 数学/颜色" },
            { ConfigFieldType.Color32, "Unity 数学/颜色" },
            { ConfigFieldType.Rect, "Unity 数学/矩形" },
            { ConfigFieldType.RectInt, "Unity 数学/矩形" },
            { ConfigFieldType.RectOffset, "Unity 数学/矩形" },
            { ConfigFieldType.Bounds, "Unity 数学/边界" },
            { ConfigFieldType.BoundsInt, "Unity 数学/边界" },
            { ConfigFieldType.Gradient, "Unity 数学/曲线" },
            { ConfigFieldType.AnimationCurve, "Unity 数学/曲线" },
            { ConfigFieldType.LayerMask, "Unity 数学/其他" },
            // Unity 对象类型
            { ConfigFieldType.Texture2D, "Unity 对象/贴图" },
            { ConfigFieldType.Sprite, "Unity 对象/贴图" },
            { ConfigFieldType.Material, "Unity 对象/材质" },
            { ConfigFieldType.Shader, "Unity 对象/材质" },
            { ConfigFieldType.Mesh, "Unity 对象/网格" },
            { ConfigFieldType.PhysicMaterial, "Unity 对象/物理" },
            { ConfigFieldType.GameObject, "Unity 对象/游戏对象" },
            { ConfigFieldType.AudioClip, "Unity 对象/音频" },
            { ConfigFieldType.Font, "Unity 对象/文本" },
            { ConfigFieldType.TextAsset, "Unity 对象/文本" },
            { ConfigFieldType.Object, "Unity 对象/通用" },
            // 多语言（仅 loc 包已安装时可用）
            { ConfigFieldType.LocalizedKey, "多语言/多语言Key" },
        };

        private void DrawTypeField(Rect rect, SerializedProperty typeProp, SerializedProperty refProp, SerializedProperty enumProp, SerializedProperty exportProp)
        {
            var type = (ConfigFieldType)typeProp.intValue;

            string label;
            if (type == ConfigFieldType.Reference)
            {
                var t = ResolveRefType(refProp.stringValue);
                label = t != null ? ConfigTypeDisplay.GetFullLabel(t) : "引用:未选择";
            }
            else if (type == ConfigFieldType.Enum)
            {
                var e = ConfigEnumCodeGen.FindEnum(enumProp.stringValue);
                label = e != null ? $"枚举:{e.className}" : "枚举:未选择";
            }
            else if (type == ConfigFieldType.LocalizedKey)
            {
                label = "多语言Key";
            }
            else
            {
                label = type.ToString();
            }

            if (EditorGUI.DropdownButton(rect, new GUIContent(label), FocusType.Keyboard))
            {
                var menu = new GenericMenu();

                // 基础类型：多级分类（多语言分类仅 loc 包已安装时显示）
                foreach (var kv in TypeMenuPaths)
                {
                    if (kv.Key == ConfigFieldType.LocalizedKey && !LocAccess.IsLocInstalled)
                        continue;
                    var item = kv.Key;
                    var path = $"{kv.Value}/{item}";
                    menu.AddItem(new GUIContent(path), type == item, () =>
                    {
                        typeProp.intValue = (int)item;
                        refProp.stringValue = "";
                        enumProp.stringValue = "";
                        // 多语言 key 默认导出到客户端，用户可改
                        if (item == ConfigFieldType.LocalizedKey)
                            exportProp.enumValueIndex = (int)ConfigExportTarget.Client;
                        mTarget.ApplyModifiedProperties();
                    });
                }

                // 配置引用：直接列出所有配置类型（按 [CfgAsset] 显示名路径）
                menu.AddSeparator("");
                var cfgTypes = TypeCache.GetTypesWithAttribute<CfgAssetAttribute>()
                    .Where(t => typeof(ScriptableObject).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericType
                                && t != typeof(ConfigTemplate) && t != typeof(ConfigEnumDefinition))
                    .OrderBy(t => t.Name)
                    .ToArray();
                foreach (var t in cfgTypes)
                {
                    var cfgType = t;
                    var display = ConfigTypeDisplay.GetShortName(cfgType);
                    menu.AddItem(new GUIContent($"引用/配置引用/{display} ({cfgType.Name})"),
                        type == ConfigFieldType.Reference && refProp.stringValue == cfgType.AssemblyQualifiedName, () =>
                        {
                            typeProp.intValue = (int)ConfigFieldType.Reference;
                            refProp.stringValue = cfgType.AssemblyQualifiedName;
                            enumProp.stringValue = "";
                            mTarget.ApplyModifiedProperties();
                        });
                }
                if (cfgTypes.Length == 0)
                    menu.AddDisabledItem(new GUIContent("引用/配置引用/（暂无配置类型）"));

                // 枚举引用：直接列出所有枚举定义
                var enums = ConfigEnumCodeGen.LoadAllEnums();
                foreach (var def in enums)
                {
                    var e = def;
                    var display = string.IsNullOrEmpty(e.displayName) ? e.className : e.displayName;
                    menu.AddItem(new GUIContent($"引用/枚举引用/{display} ({e.className})"),
                        type == ConfigFieldType.Enum && enumProp.stringValue == e.className, () =>
                        {
                            typeProp.intValue = (int)ConfigFieldType.Enum;
                            enumProp.stringValue = e.className;
                            refProp.stringValue = "";
                            mTarget.ApplyModifiedProperties();
                        });
                }
                if (enums.Count == 0)
                    menu.AddDisabledItem(new GUIContent("引用/枚举引用/（暂无枚举）"));

                menu.DropDown(rect);
            }
        }

        private static Type ResolveRefType(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName))
                return null;
            return Type.GetType(assemblyQualifiedName);
        }

        private static string FieldTypeLabel(ConfigTemplateField f)
        {
            if (f.type == ConfigFieldType.Reference)
            {
                var t = ResolveRefType(f.refTypeFullName);
                return t != null ? ConfigTypeDisplay.GetFullLabel(t) : "引用:未选择";
            }
            if (f.type == ConfigFieldType.Enum)
            {
                var e = ConfigEnumCodeGen.FindEnum(f.enumRefClassName);
                return e != null ? $"枚举:{e.className}" : "枚举:未选择";
            }
            if (f.type == ConfigFieldType.LocalizedKey)
                return "多语言Key";
            return f.type.ToString();
        }

        private void Generate()
        {
            mTarget.ApplyModifiedProperties();

            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            // 兜底净化（输入框已即时净化，这里防止外部改动直接调用）
            var sanitized = ConfigTemplateCodeGen.SanitizeDisplayName(tpl.displayName);
            if (sanitized != tpl.displayName)
            {
                tpl.displayName = sanitized;
                mTarget.ApplyModifiedProperties();
            }

            if (!ConfigTemplateCodeGen.Validate(tpl, out var error))
            {
                EditorUtility.DisplayDialog("无法生成", error, "确定");
                return;
            }

            if (!ConfigTemplateCodeGen.WriteFile(tpl, out error))
            {
                EditorUtility.DisplayDialog("生成失败", error, "确定");
                return;
            }

            mWindow?.ShowNotification(new GUIContent($"已生成 {tpl.className}.cs"));
        }

        /// <summary>
        /// 改名流程：弹出输入框 → 选择保持/断开链接 → 二次确认 → 执行。
        /// className 生成后只读，改名只能走此按钮。
        /// </summary>
        private void PromptRename(ConfigTemplate tpl)
        {
            var oldName = tpl.lastGeneratedClassName;
            var newName = tpl.className;

            // 输入框
            var input = newName;
            if (!EditorInputDialog.Show(
                "改名类名",
                $"当前类名: {newName}\n上次生成类名: {oldName}\n\n输入新类名:",
                ref input,
                name => ConfigTemplateCodeGen.IsValidIdentifier(name),
                "新类名不是合法的 C# 标识符"))
            {
                return; // 取消
            }

            newName = input.Trim();

            if (newName == oldName)
            {
                EditorUtility.DisplayDialog("无变化", "类名未变化", "确定");
                return;
            }

            // 写回 className
            var classProp = mTarget.FindProperty("className");
            classProp.stringValue = newName;
            mTarget.ApplyModifiedProperties();

            // 检查是否有旧生成文件需要处理
            var oldFile = ConfigTemplateCodeGen.GetOldGeneratedFile(tpl);
            if (oldFile != null)
            {
                // 选择处理方式
                var keepLink = EditorUtility.DisplayDialog(
                    "处理旧生成文件",
                    $"检测到旧生成文件 {oldName}.cs。\n\n" +
                    $"【保持链接】重命名文件为 {newName}.cs，已有资产引用不断（推荐）。\n\n" +
                    $"【断开链接】删除旧文件，已有 {oldName} 资产的脚本引用将丢失（不可逆）。",
                    "保持链接", "断开链接");

                if (!keepLink)
                {
                    // 断开链接：红字二次警告
                    var confirmBreak = EditorUtility.DisplayDialog(
                        "断开链接确认",
                        $"即将删除 {oldName}.cs！\n\n" +
                        $"⚠ 所有使用 {oldName} 类型的配置资产的脚本引用将变为 Missing！\n\n" +
                        "此操作不可恢复，确认继续？",
                        "确认删除", "取消");
                    if (!confirmBreak)
                        return;
                }

                // 执行确认
                var finalConfirm = EditorUtility.DisplayDialog(
                    "执行确认",
                    keepLink
                        ? $"将重命名 {oldName}.cs → {newName}.cs 并生成新代码。"
                        : $"将删除 {oldName}.cs 并生成新的 {newName}.cs。",
                    "执行", "取消");
                if (!finalConfirm)
                    return;

                if (!ConfigTemplateCodeGen.HandleClassNameRename(tpl, keepLink, out var error))
                {
                    EditorUtility.DisplayDialog("处理失败", error, "确定");
                    return;
                }
            }

            // 生成新代码
            string error2;
            if (!ConfigTemplateCodeGen.WriteFile(tpl, out error2))
            {
                EditorUtility.DisplayDialog("生成失败", error2, "确定");
                return;
            }

            mWindow?.ShowNotification(new GUIContent($"已改名并重新生成 {newName}.cs"));
        }

        private void Save()
        {
            mTarget.ApplyModifiedProperties();

            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            // 兜底净化
            var sanitized = ConfigTemplateCodeGen.SanitizeDisplayName(tpl.displayName);
            if (sanitized != tpl.displayName)
            {
                tpl.displayName = sanitized;
                mTarget.ApplyModifiedProperties();
            }

            // 保存时同步资产名 = displayName（保留 GUID，引用不断）
            var pathBefore = AssetDatabase.GetAssetPath(tpl);
            var newPath = ConfigTemplateCodeGen.SyncTemplateAssetName(tpl);
            if (newPath != null && newPath != pathBefore)
                mWindow?.ShowNotification(new GUIContent($"资产已重命名为 {Path.GetFileName(newPath)}"));

            // 只保存当前模板，避免 SaveAssets 把其他 dirty 资产一并写盘
            EditorUtility.SetDirty(tpl);
            AssetDatabase.SaveAssetIfDirty(tpl);
            mWindow?.ShowNotification(new GUIContent("模板已保存"));
        }
    }
}
