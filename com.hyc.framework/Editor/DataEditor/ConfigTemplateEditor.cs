using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

            // 页签切换（「字段」页签 tooltip 承载字段类型 / 导出 / 检查说明）
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            mActiveTab = GUILayout.Toolbar(mActiveTab, new[]
            {
                new GUIContent("基础", "显示名 / 类名（改名）/ 基类 / 图标。"),
                new GUIContent("字段",
                    "字段类型选“配置引用…”可从所有已注册配置类型中选择，生成资产引用字段。\n" +
                    "字段“导出”决定客户端/服务器类型包含的字段；“检查”配置在字段检查配置页。"),
                new GUIContent("字段检查配置"),
                new GUIContent("导出")
            }, EditorStyles.toolbarButton);
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
            else if (mActiveTab == 2)
            {
                DrawChecksTab();
            }
            else
            {
                DrawExportTab();
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
                var nameConflict = ConfigTemplateCodeGen.FindClassNameConflict(tplNow);
                if (nameConflict != null)
                    tplErrors.Add($"类名 {tplNow.className} 已被其他模板使用");

                if (tplNow.baseTemplate != null)
                {
                    var chainNow = new List<ConfigTemplate>();
                    ConfigTemplateCodeGen.TryBuildInheritanceChain(tplNow, chainNow, out _);
                    foreach (var c in chainNow)
                    {
                        if (!ConfigTemplateCodeGen.IsBaseReady(c))
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

            // 导出可行性错误（硬错误，与生成校验一致；不阻断保存，只在此处红字提示）
            if (tplNow != null)
                foreach (var fe in ConfigTemplateCodeGen.CollectExportFeasibilityErrors(tplNow))
                    tplErrors.Add(fe);

            foreach (var e in tplErrors)
                EditorGUILayout.HelpBox(e, MessageType.Error);

            // 关联参数条件警告（不阻断生成：无效条件不发射、求值时视为满足）
            foreach (var w in ConfigTemplateCodeGen.CollectVisibilityWarnings(tplNow))
                EditorGUILayout.HelpBox(w, MessageType.Warning);

            EditorGUILayout.Space(4);

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

        /// <summary>基础页：左侧 88×88 可点击图标 + 右侧显示名/类名（改名）/基类。</summary>
        private void DrawBasicTab()
        {
            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            EditorGUILayout.BeginHorizontal();

            // 左：图标列。GUIStyle.none 占位取精确 88×88 rect 后手动绘制——GetRect 系列元素与
            // 带 stretch 的样式在滚动视图内会被拉离固定尺寸，手动 rect 不受布局系统影响
            GUILayout.Box(GUIContent.none, GUIStyle.none, GUILayout.Width(88), GUILayout.Height(88));
            var iconRect = GUILayoutUtility.GetLastRect();
            DrawIconSwatch(iconRect, tpl);

            // 右：字段（标签列收窄，给左列让宽度）
            EditorGUILayout.BeginVertical();

            var oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 112;

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

            // 基类（tooltip 说明保护逻辑）
            var baseProp = mTarget.FindProperty("baseTemplate");
            var baseObj = baseProp.objectReferenceValue as ConfigTemplate;
            var baseUngenerated = baseObj != null && !ConfigTemplateCodeGen.IsBaseReady(baseObj);
            var baseOldBg = GUI.backgroundColor;
            var baseOldContent = GUI.contentColor;
            if (baseUngenerated)
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                GUI.contentColor = new Color(1f, 0.55f, 0.55f);
            }
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(baseProp, new GUIContent("基类模板",
                "清空时自动回归 ConfigBase；基类未生成代码时红底提示。"));
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

            EditorGUIUtility.labelWidth = oldLabelWidth;

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
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

        // ---------- “导出”页：每字段 客户端/服务端 导出设置 ----------

        private class ExportOption
        {
            public string Label;
            public ConfigFieldExportMode Mode;
            public string Member;
            public ConfigFixedStringSize Size;

            public ExportOption(string label, ConfigFieldExportMode mode, string member = "",
                ConfigFixedStringSize size = ConfigFixedStringSize.Size128)
            {
                Label = label;
                Mode = mode;
                Member = member ?? "";
                Size = size;
            }
        }

        private void DrawExportTab()
        {
            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            EditorGUILayout.LabelField("导出设置（客户端/服务端分别选择导出取值），只影响生成的数据", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            // 表头：字段名 | 类型 | 客户端导出 | 服务端导出
            var headerRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            var w = headerRect.width;
            var nameW = w * 0.26f;
            var typeW = w * 0.20f;
            var rest = w - nameW - typeW;
            var colW = rest * 0.5f;
            EditorGUI.LabelField(new Rect(headerRect.x, headerRect.y, nameW, headerRect.height), "字段名", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(headerRect.x + nameW, headerRect.y, typeW, headerRect.height), "类型", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(headerRect.x + nameW + typeW, headerRect.y, colW, headerRect.height), "客户端导出", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(headerRect.x + nameW + typeW + colW, headerRect.y, colW, headerRect.height), "服务端导出", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            var lineH = EditorGUIUtility.singleLineHeight + 2;
            for (var i = 0; i < mFieldsProp.arraySize; i++)
            {
                if (tpl.fields == null || i >= tpl.fields.Count)
                    continue;
                var prop = mFieldsProp.GetArrayElementAtIndex(i);
                var f = tpl.fields[i];
                if (f == null)
                    continue;

                var row = EditorGUILayout.GetControlRect(true, lineH);
                var x = row.x;
                EditorGUI.LabelField(new Rect(x, row.y, nameW, row.height), f.name);
                EditorGUI.LabelField(new Rect(x + nameW, row.y, typeW, row.height), FieldTypeLabel(f));

                var clientX = x + nameW + typeW;
                // 导出目标：客户端 0 / 服务器 1 / 两者 2
                var clientEnabled = f.exportTarget != ConfigExportTarget.Server;
                var serverEnabled = f.exportTarget != ConfigExportTarget.Client;
                var feas = ConfigTemplateCodeGen.GetExportFeasibility(f);
                DrawSideExportPopup(prop, f, true, new Rect(clientX, row.y, colW - 2, row.height), clientEnabled, feas.ClientBroken);
                DrawSideExportPopup(prop, f, false, new Rect(clientX + colW, row.y, colW - 2, row.height), serverEnabled, feas.ServerBroken);
            }

                EditorGUILayout.Space(4);

            // 导出可行性红字汇总（与生成校验一致）：哪些字段“直接导出”不可导，指出是哪一侧 + 建议
            for (var i = 0; i < mFieldsProp.arraySize; i++)
            {
                if (tpl.fields == null || i >= tpl.fields.Count)
                    continue;
                var f = tpl.fields[i];
                if (f == null)
                    continue;
                var msg = ConfigTemplateCodeGen.ExportFeasibilityMessage(f);
                if (msg != null)
                    EditorGUILayout.HelpBox(msg, MessageType.Error);
            }

            EditorGUILayout.HelpBox(
                "每一侧可选：直接导出（默认） / 转字符串 / string 的 BlobString 或 FixedString 容量 / " +
                "配置引用取成员(ID/GUID/标量字段) / 资源对象导出 名字、路径、Addressable 地址。\n" +
                "FixedString 生成代码用官方 new FixedStringN(source) 初始化，超长抛异常不静默截断。",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "资源/Prefab 字段（Object 等）：只能导出 名字 / 路径 / AA地址，不能直接把对象序列化进 Blob。" +
                "选「AA地址」时请确保该资源已在 Addressables 标记，否则导出空串（运行时才报错）。",
                MessageType.Warning);
            EditorGUILayout.HelpBox(
                "音频字段（框架/音频）导出的是带前缀的字符串 key：clip:<Addressables地址> 走框架默认后端、" +
                "wwise:<事件名> 走 Wwise。运行时由 Audio.Play(ref) 按前缀路由，再交给 Addressables 加载，" +
                "所以这里填的是 key 而不是 clip 名；默认 BlobString（变长、不截断）。",
                MessageType.Info);
        }

        private void DrawSideExportPopup(SerializedProperty fieldProp, ConfigTemplateField f, bool client, Rect rect, bool enabled, bool broken)
        {
            var oldEnabled = GUI.enabled;
            if (!enabled)
                GUI.enabled = false;

            // 该侧当前选择“直接导出”但不可导 → 红底高亮，引导用户改模式
            if (broken)
                EditorGUI.DrawRect(rect, new Color(0.85f, 0.15f, 0.15f, 0.20f));

            var setProp = fieldProp.FindPropertyRelative(client ? "clientExport" : "serverExport");
            var modeProp = setProp.FindPropertyRelative("mode");
            var memberProp = setProp.FindPropertyRelative("memberName");
            var sizeProp = setProp.FindPropertyRelative("fixedStringSize");

            var opts = BuildExportOptions(f);
            var labels = new string[opts.Count];
            var currentMode = modeProp.intValue;
            var currentMember = memberProp.stringValue ?? "";
            var currentSize = sizeProp.intValue;
            var cur = 0;
            for (var j = 0; j < opts.Count; j++)
            {
                labels[j] = opts[j].Label;
                if ((int)opts[j].Mode == currentMode
                    && string.Equals(opts[j].Member, currentMember)
                    && (opts[j].Mode != ConfigFieldExportMode.FixedString || (int)opts[j].Size == currentSize))
                    cur = j;
            }

            EditorGUI.BeginChangeCheck();
            var idx = EditorGUI.Popup(rect, cur, labels);
            if (EditorGUI.EndChangeCheck() && idx >= 0 && idx < opts.Count)
            {
                var o = opts[idx];
                modeProp.intValue = (int)o.Mode;
                memberProp.stringValue = o.Member ?? "";
                sizeProp.intValue = (int)o.Size;
                mTarget.ApplyModifiedProperties();
            }

            GUI.enabled = oldEnabled;
        }

        private static List<ExportOption> BuildExportOptions(ConfigTemplateField f)
        {
            var list = new List<ExportOption>();
            if (f == null)
                return list;

            switch (f.type)
            {
                case ConfigFieldType.Reference:
                {
                    list.Add(new ExportOption("直接导出(整对象)", ConfigFieldExportMode.Direct));
                    var refType = ResolveRefType(f.refTypeFullName);
                    if (refType != null)
                    {
                        foreach (var fi in refType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                        {
                            var ft = fi.FieldType;
                            if (!IsScalarMemberType(ft))
                                continue;
                            list.Add(new ExportOption($"取成员 {fi.Name} ({TypeShortName(ft)})", ConfigFieldExportMode.Member, fi.Name));
                        }
                    }
                    break;
                }
                case ConfigFieldType.String:
                case ConfigFieldType.LocalizedKey:
                case ConfigFieldType.Addressable:
                    list.Add(new ExportOption("直接导出(string)", ConfigFieldExportMode.Direct));
                    list.Add(new ExportOption("BlobString", ConfigFieldExportMode.BlobString));
                    list.Add(new ExportOption("FixedString32Bytes", ConfigFieldExportMode.FixedString, "", ConfigFixedStringSize.Size32));
                    list.Add(new ExportOption("FixedString64Bytes", ConfigFieldExportMode.FixedString, "", ConfigFixedStringSize.Size64));
                    list.Add(new ExportOption("FixedString128Bytes", ConfigFieldExportMode.FixedString, "", ConfigFixedStringSize.Size128));
                    list.Add(new ExportOption("FixedString512Bytes", ConfigFieldExportMode.FixedString, "", ConfigFixedStringSize.Size512));
                    list.Add(new ExportOption("FixedString4096Bytes", ConfigFieldExportMode.FixedString, "", ConfigFixedStringSize.Size4096));
                    break;
                case ConfigFieldType.AudioEvent:
                    // 音频 key 存带前缀的字符串（clip:<AA地址> / wwise:<事件名>），运行时按前缀路由。
                    // BlobString 排第一并作为新建时的默认：变长存储无容量上限，而 key 长度不可预知，
                    // 定长档位静默截断的风险不对等。「直接导出」与之等价（Blob 侧 IsStringLike
                    // 会兜成 BlobString），保留是为了兼容已保存的旧设置。
                    list.Add(new ExportOption("BlobString", ConfigFieldExportMode.BlobString));
                    list.Add(new ExportOption("直接导出(string)", ConfigFieldExportMode.Direct));
                    list.Add(new ExportOption("FixedString64Bytes", ConfigFieldExportMode.FixedString, "", ConfigFixedStringSize.Size64));
                    list.Add(new ExportOption("FixedString128Bytes", ConfigFieldExportMode.FixedString, "", ConfigFixedStringSize.Size128));
                    list.Add(new ExportOption("FixedString512Bytes", ConfigFieldExportMode.FixedString, "", ConfigFixedStringSize.Size512));
                    break;
                default:
                {
                    if (IsAssetExportType(f.type))
                    {
                        list.Add(new ExportOption("名字", ConfigFieldExportMode.AssetName));
                        // ★ 标签里不能出现半角 '/'：EditorGUI.Popup 会把 '/' 当作**子菜单分隔符**，
                        //   之前写成 "路径(Assets/...)" 时被拆成「路径(Assets ▸ …)」两级。改用全角斜杠规避。
                        list.Add(new ExportOption("路径(Assets／...)", ConfigFieldExportMode.AssetPath));
                        list.Add(new ExportOption("AA地址", ConfigFieldExportMode.AssetAddressable));
                        break;
                    }

                    // 数值/布尔/枚举/行为树/Unity结构：默认 + 转字符串
                    list.Add(new ExportOption("直接导出", ConfigFieldExportMode.Direct));
                    if (f.type == ConfigFieldType.Enum
                        || IsNumericType(f.type)
                        || f.type == ConfigFieldType.Bool
                        || f.type == ConfigFieldType.Char
                        || f.type == ConfigFieldType.BehaviourTree
                        || IsVectorStructType(f.type))
                    {
                        list.Add(new ExportOption("转字符串(string)", ConfigFieldExportMode.ToString));
                    }
                    break;
                }
            }

            if (list.Count == 0)
                list.Add(new ExportOption("直接导出", ConfigFieldExportMode.Direct));
            return list;
        }

        private static bool IsAssetExportType(ConfigFieldType type)
        {
            switch (type)
            {
                case ConfigFieldType.Sprite:
                case ConfigFieldType.Texture2D:
                case ConfigFieldType.GameObject:
                case ConfigFieldType.AudioClip:
                case ConfigFieldType.Material:
                case ConfigFieldType.Mesh:
                case ConfigFieldType.PhysicMaterial:
                case ConfigFieldType.Font:
                case ConfigFieldType.Shader:
                case ConfigFieldType.TextAsset:
                case ConfigFieldType.Object:
                case ConfigFieldType.AnimationClip:
                case ConfigFieldType.AnimatorController:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsVectorStructType(ConfigFieldType type)
        {
            switch (type)
            {
                case ConfigFieldType.Vector2:
                case ConfigFieldType.Vector3:
                case ConfigFieldType.Vector4:
                case ConfigFieldType.Vector2Int:
                case ConfigFieldType.Vector3Int:
                case ConfigFieldType.Quaternion:
                case ConfigFieldType.Color:
                case ConfigFieldType.Color32:
                case ConfigFieldType.Rect:
                case ConfigFieldType.RectInt:
                case ConfigFieldType.RectOffset:
                case ConfigFieldType.Bounds:
                case ConfigFieldType.BoundsInt:
                case ConfigFieldType.Gradient:
                case ConfigFieldType.AnimationCurve:
                case ConfigFieldType.LayerMask:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsScalarMemberType(System.Type t)
            => t.IsPrimitive || t.IsEnum || t == typeof(string);

        private static string TypeShortName(System.Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(short)) return "short";
            if (t == typeof(byte)) return "byte";
            if (t == typeof(uint)) return "uint";
            if (t == typeof(char)) return "char";
            if (t == typeof(decimal)) return "decimal";
            return t.Name;
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

        /// <summary>
        /// 图标缩略图绘制（rect 来自上方 GUILayout.Box(GUIStyle.none) 占位的 GetLastRect，精确 88×88）：
        /// 框 + 纹理（8px 内边距 ScaleToFit）+ 居中「无图标」文案 + 整块可点击开选择窗口。
        /// 点击即打开「选择图标」窗口（内置网格 / 项目图标浏览 / 清除均在窗口内，面上零按钮）。
        /// </summary>
        private void DrawIconSwatch(Rect rect, ConfigTemplate tpl)
        {
            var tex = ConfigTemplateIcon.Resolve(tpl);

            GUI.Box(rect, GUIContent.none);
            if (tex != null)
            {
                const float pad = 8f;
                GUI.DrawTexture(
                    new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, rect.height - pad * 2f),
                    tex, ScaleMode.ScaleToFit);
            }

            // 控件每帧恒定绘制，仅按有无图标切换内容（IMGUI 控件集合一致性）；centeredGreyMiniLabel 居中
            GUI.Label(rect, tex != null ? GUIContent.none : new GUIContent("无图标"), EditorStyles.centeredGreyMiniLabel);

            if (GUI.Button(rect, new GUIContent(string.Empty, "点击选择图标（内置图标 / 项目图标 / 清除）"), GUIStyle.none))
                OpenIconPicker();
        }

        /// <summary>打开图标选择窗口；选中 / 清除回调统一写回序列化属性并即时提交传播。</summary>
        private void OpenIconPicker()
        {
            var builtInProp = mTarget.FindProperty("iconBuiltInName");
            var customProp = mTarget.FindProperty("iconCustom");
            ConfigIconPicker.Open(
                name =>
                {
                    builtInProp.stringValue = name;
                    customProp.objectReferenceValue = null;
                    mTarget.ApplyModifiedProperties();
                    CommitIconChange();
                },
                tex2d =>
                {
                    // 与 iconBuiltInName 二选一：选自定义纹理时清掉内置名，避免双值歧义
                    customProp.objectReferenceValue = tex2d;
                    builtInProp.stringValue = "";
                    mTarget.ApplyModifiedProperties();
                    CommitIconChange();
                },
                () =>
                {
                    builtInProp.stringValue = "";
                    customProp.objectReferenceValue = null;
                    mTarget.ApplyModifiedProperties();
                    CommitIconChange();
                });
        }

        /// <summary>
        /// 图标变更即时提交（picker 选中 / 清除共用）：字段落盘 + 三类对象 session 图标
        /// 立即应用 + 数据树刷新。只写内存字段不传播会让三处显示分裂——数据树读模板字段 /
        /// 配置资产的 meta 类型图标，Unity Project 窗口读 session 图标，任选其一不更新
        /// 都会出现「树里一个图标、Unity 里另一个图标」。
        /// </summary>
        private void CommitIconChange()
        {
            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            EditorUtility.SetDirty(tpl);
            AssetDatabase.SaveAssetIfDirty(tpl);

            ConfigIconApplier.ApplyForTemplate(tpl);
            PersistIconDeferred(tpl);
            mWindow?.RefreshTree();
        }

        /// <summary>
        /// 自定义纹理持久化进 .cs.meta（数据树与 Unity 类型图标读 meta，不落 meta 会与
        /// session 图标分裂）。SaveAndReimport 会触发域重载，经 delayCall 推迟到当前
        /// GUI 周期（picker 关闭流程）外执行。
        /// </summary>
        private static void PersistIconDeferred(ConfigTemplate tpl)
        {
            EditorApplication.delayCall += () =>
            {
                if (tpl == null)
                    return;
                ConfigIconApplier.PersistForTemplate(tpl);
            };
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
                if (!ConfigTemplateCodeGen.IsBaseReady(c))
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
                AddEmptyField();
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
                            && ResolveRefType(refProp.stringValue) == null;
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
            var readOnlyProp = prop.FindPropertyRelative("readOnly");
            var isNumeric = IsNumericType((ConfigFieldType)typeProp.intValue);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("描述", GUILayout.Width(50));
            descProp.stringValue = EditorGUILayout.TextField(descProp.stringValue);
            EditorGUILayout.EndHorizontal();

            // 只读：数据编辑器里禁用该字段输入（典型：基类 ID/GUID）
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("只读", GUILayout.Width(50));
            readOnlyProp.boolValue = EditorGUILayout.Toggle(readOnlyProp.boolValue, GUILayout.Width(20));
            EditorGUILayout.LabelField("勾选后数据编辑器中该字段不可编辑（仅影响展示，不影响发号/导出）", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            // 关联参数：条件显示（满足全部条件才在配置面板绘制本字段）
            DrawVisibilityBlock(prop);

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

            if (IsAssetExportType((ConfigFieldType)typeProp.intValue))
            {
                EditorGUILayout.HelpBox(
                    "资源/Prefab 字段：拖入资源，导出时按所选模式转成 名字 / 路径 / AA地址（不能直接序列化对象本身）。" +
                    "选「AA地址」请确保资源已标记 Addressable，否则导出空串。",
                    MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>关联参数候选目标字段（本模板 + 继承链上的 bool/enum 标量字段）。</summary>
        private sealed class ConditionTarget
        {
            public string Name;
            public ConfigFieldType Type;
            public string EnumClassName = "";
        }

        /// <summary>
        /// 「关联参数」块（展开明细区内、只读行之后）：条件显示规则。
        /// 每条 = 左下拉选一个 bool/enum 字段 + 右侧控件（bool→勾选；enum→多选枚举值）；
        /// 多条之间 AND；目标字段丢失/类型不符视为满足（保持显示）。
        /// </summary>
        private void DrawVisibilityBlock(SerializedProperty prop)
        {
            var visProp = prop.FindPropertyRelative("visibility");
            if (visProp == null)
                return;

            var nameProp = prop.FindPropertyRelative("name");
            var selfName = nameProp != null ? nameProp.stringValue ?? "" : "";
            var candidates = CollectConditionTargets(selfName);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("关联参数", GUILayout.Width(50));
            EditorGUILayout.LabelField("满足全部条件才在配置面板显示本字段", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            for (var i = 0; i < visProp.arraySize; i++)
                DrawVisibilityConditionRow(visProp, i, candidates);

            using (new EditorGUI.DisabledScope(candidates.Count == 0))
            {
                if (GUILayout.Button("+ 添加条件", GUILayout.Width(90)))
                    AddVisibilityCondition(visProp);
            }
            if (candidates.Count == 0)
                EditorGUILayout.LabelField("当前模板没有可用的布尔/枚举字段（不含自身与列表字段）", EditorStyles.miniLabel);
        }

        /// <summary>单条关联条件行：[缩进16][目标字段下拉][值控件][× 删除]。</summary>
        private void DrawVisibilityConditionRow(SerializedProperty visProp, int index, List<ConditionTarget> candidates)
        {
            var cond = visProp.GetArrayElementAtIndex(index);
            var targetProp = cond.FindPropertyRelative("targetField");
            var boolProp = cond.FindPropertyRelative("boolValue");
            var enumValuesProp = cond.FindPropertyRelative("enumValues");
            if (targetProp == null)
                return;

            var targetName = targetProp.stringValue ?? "";
            var rowRect = EditorGUILayout.GetControlRect(true, 18);

            const float padL = 16f;
            const float delW = 22f;
            var avail = rowRect.width - padL - delW - 4;
            if (avail < 60f)
                avail = 60f; // 极窄窗口防御： Rect 宽度不取负（与 ColumnWidths 的护栏同范式）
            var ddW = Mathf.Min(170f, avail * 0.45f);
            var dropRect = new Rect(rowRect.x + padL, rowRect.y, ddW, rowRect.height);
            var valueRect = new Rect(dropRect.xMax + 4, rowRect.y, avail - ddW - 4, rowRect.height);
            var delRect = new Rect(rowRect.xMax - delW, rowRect.y, delW, rowRect.height);

            // 左：目标字段下拉。存量无效条件（字段被删/改类型）以"（已失效）"占位留在首位，便于发现与改回。
            var names = new List<string>();
            foreach (var c in candidates)
                names.Add(c.Name);
            var idx = names.IndexOf(targetName);
            var invalid = idx < 0;
            if (invalid)
                names.Insert(0, string.IsNullOrEmpty(targetName) ? "（未选择字段）" : targetName + "（已失效）");

            var newIdx = EditorGUI.Popup(dropRect, Mathf.Max(idx, 0), names.ToArray());
            if (newIdx >= 0 && newIdx < names.Count)
            {
                var keepStale = invalid && newIdx == 0;
                if (!keepStale)
                    targetProp.stringValue = names[newIdx];
            }

            // 右：按目标字段类型给控件（bool→勾选；enum→多选枚举值；无效→黄字提示）
            var target = candidates.Find(c => c.Name == targetName);
            if (target == null)
            {
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 0.8f, 0.35f);
                GUI.Label(valueRect, "目标字段不存在或类型不支持（条件不生效，字段保持显示）", EditorStyles.miniLabel);
                GUI.color = oldColor;
            }
            else if (target.Type == ConfigFieldType.Bool)
            {
                boolProp.boolValue = EditorGUI.Toggle(valueRect, boolProp.boolValue);
            }
            else
            {
                DrawEnumConditionValue(valueRect, target, enumValuesProp);
            }

            // × 删除本条条件
            if (GUI.Button(delRect, "×"))
            {
                visProp.DeleteArrayElementAtIndex(index);
                mTarget.ApplyModifiedProperties();
            }
        }

        /// <summary>enum 目标的值控件：MaskField 多选枚举值（≤32 个；更多给提示不放行）。</summary>
        private static void DrawEnumConditionValue(Rect rect, ConditionTarget target, SerializedProperty enumValuesProp)
        {
            var def = ConfigEnumCodeGen.FindEnum(target.EnumClassName);
            var valueNames = def != null && def.values != null
                ? def.values.Where(v => v != null && !string.IsNullOrEmpty(v.name)).Select(v => v.name).ToList()
                : new List<string>();

            if (def == null || valueNames.Count == 0)
            {
                GUI.Label(rect, "枚举定义不存在或没有枚举值（条件不生效）", EditorStyles.miniLabel);
                return;
            }
            if (valueNames.Count > 32)
            {
                GUI.Label(rect, $"枚举值 {valueNames.Count} 个超过 32，无法多选（请精简枚举或改用布尔字段）", EditorStyles.miniLabel);
                return;
            }

            var options = valueNames.ToArray();
            var mask = 0;
            for (var v = 0; v < options.Length; v++)
            {
                if (EnumValuesContains(enumValuesProp, options[v]))
                    mask |= 1 << v;
            }

            var newMask = EditorGUI.MaskField(rect, mask, options);
            if (newMask != mask)
                WriteEnumValues(enumValuesProp, options, newMask);
        }

        /// <summary>enumValuesProp 列表里是否已含指定值名。</summary>
        private static bool EnumValuesContains(SerializedProperty enumValuesProp, string valueName)
        {
            if (enumValuesProp == null)
                return false;
            for (var i = 0; i < enumValuesProp.arraySize; i++)
            {
                if (enumValuesProp.GetArrayElementAtIndex(i).stringValue == valueName)
                    return true;
            }
            return false;
        }

        /// <summary>MaskField 掩码写回 enumValues（-1 = Everything = 全选，与 Unity MaskFieldGUI 一致）。</summary>
        private static void WriteEnumValues(SerializedProperty enumValuesProp, string[] options, int mask)
        {
            if (enumValuesProp == null)
                return;

            enumValuesProp.arraySize = 0;
            var all = mask == -1;
            for (var v = 0; v < options.Length; v++)
            {
                if (all || (mask & (1 << v)) != 0)
                {
                    var i = enumValuesProp.arraySize;
                    enumValuesProp.arraySize++;
                    enumValuesProp.GetArrayElementAtIndex(i).stringValue = options[v];
                }
            }
            enumValuesProp.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>添加一条空条件（逐属性复位，防 arraySize++ 复制上一条的条件）。</summary>
        private static void AddVisibilityCondition(SerializedProperty visProp)
        {
            var index = visProp.arraySize;
            visProp.arraySize++;
            var cond = visProp.GetArrayElementAtIndex(index);
            cond.FindPropertyRelative("targetField").stringValue = "";
            cond.FindPropertyRelative("boolValue").boolValue = true;
            var enumValues = cond.FindPropertyRelative("enumValues");
            if (enumValues != null)
                enumValues.arraySize = 0;
            visProp.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>候选目标字段：继承链（顶层→直接基类）+ 自身 的 bool/enum 标量字段，排除自身与列表字段。</summary>
        private List<ConditionTarget> CollectConditionTargets(string selfName)
        {
            var result = new List<ConditionTarget>();
            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return result;

            var chain = new List<ConfigTemplate>();
            ConfigTemplateCodeGen.TryBuildInheritanceChain(tpl, chain, out _);

            for (var i = chain.Count - 1; i >= 0; i--)
                CollectTargetsFrom(chain[i].fields, selfName, result);
            CollectTargetsFrom(tpl.fields, selfName, result);

            return result;
        }

        private static void CollectTargetsFrom(List<ConfigTemplateField> fields, string selfName, List<ConditionTarget> result)
        {
            foreach (var f in fields)
            {
                if (f == null || f.isList || f.name == selfName)
                    continue;
                if (f.type != ConfigFieldType.Bool && f.type != ConfigFieldType.Enum)
                    continue;
                result.Add(new ConditionTarget
                {
                    Name = f.name,
                    Type = f.type,
                    EnumClassName = f.enumRefClassName ?? "",
                });
            }
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

        /// <summary>
        /// 添加一个全新字段：所有属性取 <see cref="ConfigTemplateField"/> 的默认值，名称自动防重。
        /// <para>
        /// 不能用裸 <c>arraySize++</c>：Unity 对 <c>List&lt;类&gt;</c> 的 SerializedProperty 增容会
        /// <b>复制上一个元素</b>——新字段会继承上一行的类型、列表标志、导出设置（客户端/服务端
        /// 的 mode/memberName/fixedStringSize 一起被复制）。曾酿成事故：在 string 行后加字段，
        /// 新字段直接就是 string；上一行若是 ToString/资源导出模式，新加的 long 也被导出成 string。
        /// Attr 属性目录 / 枚举值编辑器插入后都有显式复位，这里补同一范式。
        /// </para>
        /// </summary>
        private void AddEmptyField()
        {
            var index = mFieldsProp.arraySize;
            mFieldsProp.arraySize++;
            var f = mFieldsProp.GetArrayElementAtIndex(index);

            // 与 ConfigTemplateField 的字段默认值逐项对齐（类默认即"空字段"）
            f.FindPropertyRelative("name").stringValue = NextDefaultFieldName();
            f.FindPropertyRelative("description").stringValue = "";
            f.FindPropertyRelative("type").intValue = (int)ConfigFieldType.String;
            f.FindPropertyRelative("isList").boolValue = false;
            f.FindPropertyRelative("refTypeFullName").stringValue = "";
            f.FindPropertyRelative("enumRefClassName").stringValue = "";
            f.FindPropertyRelative("exportTarget").intValue = (int)ConfigExportTarget.Both;
            f.FindPropertyRelative("readOnly").boolValue = false;
            f.FindPropertyRelative("hasRange").boolValue = false;
            f.FindPropertyRelative("minValue").floatValue = 0f;
            f.FindPropertyRelative("maxValue").floatValue = 0f;
            f.FindPropertyRelative("notEmptyCheck").intValue = (int)ConfigCheckLevel.None;
            f.FindPropertyRelative("notZeroCheck").intValue = (int)ConfigCheckLevel.None;
            f.FindPropertyRelative("rangeCheck").intValue = (int)ConfigCheckLevel.None;
            ResetExportSetting(f.FindPropertyRelative("clientExport"));
            ResetExportSetting(f.FindPropertyRelative("serverExport"));

            // 关联条件列表复位为空：arraySize++ 会复制上一个元素的 visibility
            // （条件行连目标字段一起被抄过来），与上方逐属性复位同一范式。
            var visibility = f.FindPropertyRelative("visibility");
            if (visibility != null)
                visibility.arraySize = 0;

            mTarget.ApplyModifiedProperties();
        }

        /// <summary>导出设置子对象复位为默认（Direct / 空成员名 / Size128），与 <see cref="ConfigFieldExportSetting"/> 默认值一致。</summary>
        private static void ResetExportSetting(SerializedProperty setting)
        {
            if (setting == null) return;
            var mode = setting.FindPropertyRelative("mode");
            if (mode != null) mode.intValue = (int)ConfigFieldExportMode.Direct;
            var member = setting.FindPropertyRelative("memberName");
            if (member != null) member.stringValue = "";
            var size = setting.FindPropertyRelative("fixedStringSize");
            if (size != null) size.intValue = (int)ConfigFixedStringSize.Size128;
        }

        /// <summary>默认字段名 newField；与既有字段（含继承链）重名则递增数字后缀，避免生成即报「字段名重复」。</summary>
        private string NextDefaultFieldName()
        {
            var taken = new HashSet<string>();
            for (var i = 0; i < mFieldsProp.arraySize; i++)
                taken.Add(mFieldsProp.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue ?? "");

            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl != null)
            {
                var chain = new List<ConfigTemplate>();
                ConfigTemplateCodeGen.TryBuildInheritanceChain(tpl, chain, out _);
                foreach (var baseTpl in chain)
                    foreach (var bf in baseTpl.fields)
                        taken.Add(bf.name ?? "");
            }

            if (!taken.Contains("newField")) return "newField";
            for (var n = 2; ; n++)
            {
                var candidate = "newField" + n;
                if (!taken.Contains(candidate)) return candidate;
            }
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
            // 框架能力
            { ConfigFieldType.BehaviourTree, "框架/行为树" },
            { ConfigFieldType.AnimationClip, "框架/动画片段" },
            { ConfigFieldType.AnimatorController, "框架/动画控制器" },
            { ConfigFieldType.Addressable, "框架/资源地址" },
            { ConfigFieldType.AudioEvent, "框架/音频" },
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
                                && !CfgAssetAttribute.IsHidden(t)) // Hide=true：模板/基类/音频总览等框架基础设施，无实例可引用
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

        // 引用类型解析统一走 CodeGen 的 helper：存储全名失败时按当前命名空间重建再试，
        // 避免 root 切换后（旧全名里的命名空间已失效）误显「引用:未选择」。
        private static Type ResolveRefType(string assemblyQualifiedName)
            => ConfigTemplateCodeGen.ResolveRefTypeForUi(assemblyQualifiedName);

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
            if (f.type == ConfigFieldType.BehaviourTree)
                return "行为树";
            if (f.type == ConfigFieldType.AnimationClip)
                return "动画片段";
            if (f.type == ConfigFieldType.AnimatorController)
                return "动画控制器";
            if (f.type == ConfigFieldType.Addressable)
                return "资源地址";
            if (f.type == ConfigFieldType.AudioEvent)
                return "音频";
            return f.type.ToString();
        }

        private void Generate()
        {
            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            // 先落盘再生成：ApplyModifiedProperties 只把 SerializedProperty 提交到内存对象，
            // 不写盘。不先保存的话：① 生成读的是内存值但资产未落盘（标题长期带 *）；
            // ② 用户改了显示名没保存就生成，生成用的资产名与保存后的不一致。
            SaveInternal(showNotification: false);

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

            // WriteFile 内部为记录 lastGeneratedClassName 会 SetDirty，这里补一次落盘，
            // 使生成后资产不留未保存标记（标题的 * 会消失）。
            if (SaveInternal(showNotification: false))
                mWindow?.ShowNotification(new GUIContent($"已保存并生成 {tpl.className}.cs"));
            else
                mWindow?.ShowNotification(new GUIContent($"已生成 {tpl.className}.cs（模板落盘失败，请手动保存）"));
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

        /// <summary>
        /// 落盘当前模板：净化显示名 → 同步资产名 → SetDirty + SaveAssetIfDirty。
        /// 「保存模板」与「生成代码」共用（生成必须先落盘，否则资产长期带未保存标记）。
        /// </summary>
        /// <param name="showNotification">是否弹「模板已保存」提示（生成流程自己弹合并后的提示）。</param>
        /// <returns>资产是否已成功落盘。</returns>
        private bool SaveInternal(bool showNotification)
        {
            mTarget.ApplyModifiedProperties();

            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return false;

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

            // 图标传播到 Unity 原生 Project 窗口（保存即生效）
            ConfigIconApplier.ApplyForTemplate(tpl);

            // 自定义纹理同步持久化进 .cs.meta（不落 meta 则数据树读的 meta 类型图标
            // 与 Project 窗口的 session 图标分裂）；数据树行图标立即刷新
            PersistIconDeferred(tpl);
            mWindow?.RefreshTree();

            if (showNotification)
                mWindow?.ShowNotification(new GUIContent("模板已保存"));
            return true;
        }

        private void Save()
        {
            SaveInternal(showNotification: true);
        }
    }
}
