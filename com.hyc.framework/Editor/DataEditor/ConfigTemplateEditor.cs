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

            // 常驻头部区（原「基础」页内容，取消切页；绘制在滚动区之外，恒定点在顶部）
            DrawBasicHeader();

            EditorGUILayout.Space(8);

            // 无切页：字段表就是全部内容（「导出」「字段检查配置」两页均已并入这张表）
            mScroll = EditorGUILayout.BeginScrollView(mScroll, GUILayout.ExpandHeight(true));

            DrawFieldsTab(ref classValid);

            EditorGUILayout.EndScrollView();

            // 静态提示（常驻底部，不随中间滚动；滚动区高度由 ExpandHeight 动态自适应）
            DrawPageHints();

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

            // 右下角：生成代码 + 保存模板（最底部；滚动区已 ExpandHeight 填充，此处无需再推）
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

        /// <summary>常驻头部区：左侧 88×88 可点击图标 + 右侧显示名 / 类名（改名）/ 基类模板。绘制在滚动区之外，固定常驻。</summary>
        private void DrawBasicHeader()
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

        /// <summary>页面静态提示（常驻底部，不随中间滚动）。</summary>
        private void DrawPageHints()
        {
            EditorGUILayout.HelpBox(
                "每侧下拉：直接导出(具体类型，如 直接导出(Int)) / 转字符串(string) / string 的 BlobString 或 FixedString 容量 / " +
                "配置引用取成员(默认 ID) / 资源对象导出 名字、路径、Addressable 地址 / 不导出(该侧不含此字段)。",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "「检查」列：一个下拉三选一（非空 / 非0 / 范围），各自再选 Info / Warning / Error。" +
                "非空 只对可能为空的类型（字符串 / 引用 / 资源对象 / 行为树）出现——值类型永远有值，无从判空；" +
                "非0 与 范围 只对数值类型可用（均与生成器的发射条件一致），不支持的项菜单里不出现。" +
                "选「范围」后才出现 Min/Max；三个检查互斥，选一项自动清掉其余。",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "资源/Prefab 字段只能导出 名字 / 路径 / AA地址（选 AA地址 须已在 Addressables 标记，否则导出空串、运行期才报错）；" +
                "音频字段导出的是带前缀 key（clip:<AA地址> / wwise:<事件名>），默认 BlobString（变长、不截断）。",
                MessageType.Warning);
        }

        /// <summary>字段列表（含两侧导出 + 检查 + 范围值）。</summary>
        private void DrawFieldsTab(ref bool classValid)
        {
            var tpl = mTarget.targetObject as ConfigTemplate;
            if (tpl == null)
                return;

            classValid = ConfigTemplateCodeGen.IsValidIdentifier(mTarget.FindProperty("className").stringValue);

            DrawMergedFieldTable();
        }

        // ---------- 字段列表：客户端/服务端 导出 + 检查 ----------
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

            var opts = BuildExportOptions(f, client);
            var currentMode = modeProp.intValue;
            var currentMember = memberProp.stringValue ?? "";
            var currentSize = sizeProp.intValue;
            var cur = 0;
            var matched = false;
            for (var j = 0; j < opts.Count; j++)
            {
                if ((int)opts[j].Mode == currentMode
                    && string.Equals(opts[j].Member, currentMember)
                    && (opts[j].Mode != ConfigFieldExportMode.FixedString || (int)opts[j].Size == currentSize))
                {
                    cur = j;
                    matched = true;
                    break;
                }
            }

            // 孤儿兜底：当前存档的 mode/member 在选项里找不到（典型：旧版「直接导出(整对象)」等被移除的项），
            // 追加一个反映真实存储值的合成项，避免 Popup 误把首项当当前值展示。
            if (!matched)
            {
                var orphan = new ExportOption(
                    $"(旧设置: {(ConfigFieldExportMode)currentMode}{(string.IsNullOrEmpty(currentMember) ? "" : "·" + currentMember)})",
                    (ConfigFieldExportMode)currentMode, currentMember,
                    (ConfigFixedStringSize)currentSize);
                cur = opts.Count;
                opts.Add(orphan);
            }

            var labels = new string[opts.Count];
            for (var j = 0; j < opts.Count; j++)
                labels[j] = opts[j].Label;

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

        /// <summary>单项检查（互斥三选一）。</summary>
        private enum CheckKind
        {
            None,
            NotEmpty,
            NotZero,
            Range,
        }

        /// <summary>
        /// 读取当前生效的唯一检查项。三项互斥，但历史存档可能多项同时非 None（互斥前的遗留），
        /// 按 非空 &gt; 非0 &gt; 范围 取一项展示，用户下一次选择时自动归零其余。
        /// notEmptyAllowed = 该字段类型是否支持「非空」（值类型没有空态，遗留的非空设置按不存在处理）。
        /// </summary>
        private static (CheckKind kind, ConfigCheckLevel level) ReadCheckState(SerializedProperty fieldProp, bool notEmptyAllowed)
        {
            var e = notEmptyAllowed ? (ConfigCheckLevel)fieldProp.FindPropertyRelative("notEmptyCheck").intValue : ConfigCheckLevel.None;
            var z = (ConfigCheckLevel)fieldProp.FindPropertyRelative("notZeroCheck").intValue;
            var r = (ConfigCheckLevel)fieldProp.FindPropertyRelative("rangeCheck").intValue;
            if (e != ConfigCheckLevel.None) return (CheckKind.NotEmpty, e);
            if (z != ConfigCheckLevel.None) return (CheckKind.NotZero, z);
            if (r != ConfigCheckLevel.None) return (CheckKind.Range, r);
            return (CheckKind.None, ConfigCheckLevel.None);
        }

        /// <summary>检查按钮文案（如「非0 · Warn」「范围 · Err」「不检查」）。</summary>
        private static string CheckLabel(CheckKind kind, ConfigCheckLevel level)
        {
            if (kind == CheckKind.None) return "不检查";
            var name = kind == CheckKind.NotEmpty ? "非空" : kind == CheckKind.NotZero ? "非0" : "范围";
            var lv = level == ConfigCheckLevel.Info ? "Info"
                : level == ConfigCheckLevel.Warning ? "Warn" : "Err";
            return $"{name} · {lv}";
        }

        // 检查列布局常量（下拉 96 / Min·Max 输入框 56；空间不足时的收缩下限）
        private const float CheckDropdownW = 96f;
        private const float CheckDropdownWMin = 64f;
        private const float MmLabelW = 20f;
        private const float MmGap = 4f;
        private const float MmFieldW = 56f;
        private const float MmFieldWMin = 40f;

        /// <summary>
        /// 检查列布局：下拉按钮宽 + （选了「范围」时）Min/Max 输入框宽。一套计算、下拉与 Min/Max 两边共用，保证永不错位。
        /// 空间充足：下拉 96 + 输入框 56×2；不足时输入框先收缩到 40，再让下拉收缩（保底 64，仍够放「范围 · Err」短文案）。
        /// </summary>
        private static (float dropdownW, float fieldW) CheckCellLayout(Rect checkRect, bool showMinMax)
        {
            var availW = checkRect.width - 2f;
            if (!showMinMax)
                return (Mathf.Min(CheckDropdownW, availW), 0f);
            var fieldW = MmFieldW;
            var mmGroup = 2f * MmLabelW + 3f * MmGap + 2f * fieldW;
            if (CheckDropdownW + 8f + mmGroup <= availW)
                return (CheckDropdownW, fieldW);
            fieldW = MmFieldWMin;
            mmGroup = 2f * MmLabelW + 3f * MmGap + 2f * fieldW;
            return (Mathf.Clamp(availW - 8f - mmGroup, CheckDropdownWMin, CheckDropdownW), fieldW);
        }

        // 检查下拉按级别着色的样式缓存：克隆 EditorStyles.miniPullDown —— 3 参 DropdownButton 内部默认正是它，
        // 克隆后仅替换文字色，背景/边距/字体与不着色时完全一致（深浅主题下都只动文字，不碰控件底）
        private static GUIStyle sCheckStyleInfo;
        private static GUIStyle sCheckStyleWarn;
        private static GUIStyle sCheckStyleErr;

        private static GUIStyle CheckLevelStyle(ref GUIStyle cache, Color text)
        {
            if (cache == null)
            {
                cache = new GUIStyle(EditorStyles.miniPullDown);
                cache.normal.textColor = text;
                cache.hover.textColor = text;
                cache.active.textColor = text;
            }
            return cache;
        }

        /// <summary>
        /// 检查下拉：一个按钮，点开 = 二级菜单——顶层 不检查 / 非空 / 非0 / 范围，各项再出子菜单选级别。
        /// 可选性按字段类型决定（与生成器发射条件一致）：
        /// 非空 只对「可能为空」的类型（字符串族 / 配置引用 / Unity 对象族 / 行为树）有意义——值类型永远有值，无从判空；
        /// 非0 与 范围 只对数值类型有意义。不支持的项在菜单里根本不渲染，从入口上就选不出无效组合。
        /// 按钮文字按级别着色（Info 蓝灰 / Warning 琥珀 / Error 红；不检查用默认色）。
        /// </summary>
        private void DrawCheckDropdown(Rect rect, SerializedProperty fieldProp, bool supportsNotEmpty, bool isNumeric, CheckKind kind, ConfigCheckLevel level)
        {
            var showMinMax = kind == CheckKind.Range && isNumeric;
            var btnW = CheckCellLayout(rect, showMinMax).dropdownW;
            var btnRect = new Rect(rect.x, rect.y, btnW, rect.height);
            var content = new GUIContent($"{CheckLabel(kind, level)} ▾");

            bool clicked;
            if (level == ConfigCheckLevel.Info)
                clicked = EditorGUI.DropdownButton(btnRect, content, FocusType.Keyboard,
                    CheckLevelStyle(ref sCheckStyleInfo, new Color(0.62f, 0.72f, 0.85f)));
            else if (level == ConfigCheckLevel.Warning)
                clicked = EditorGUI.DropdownButton(btnRect, content, FocusType.Keyboard,
                    CheckLevelStyle(ref sCheckStyleWarn, new Color(0.89f, 0.75f, 0.54f)));
            else if (level == ConfigCheckLevel.Error)
                clicked = EditorGUI.DropdownButton(btnRect, content, FocusType.Keyboard,
                    CheckLevelStyle(ref sCheckStyleErr, new Color(0.89f, 0.54f, 0.54f)));
            else
                clicked = EditorGUI.DropdownButton(btnRect, content, FocusType.Keyboard);

            if (clicked)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("不检查"), kind == CheckKind.None,
                    () => ApplyCheck(fieldProp, CheckKind.None, ConfigCheckLevel.None));

                if (supportsNotEmpty)
                    AddCheckLevelItems(menu, fieldProp, "非空", CheckKind.NotEmpty, kind, level);
                if (isNumeric)
                {
                    // 只有数值类型才有 非0 / 范围（生成器也只在这时才发射对应校验）
                    AddCheckLevelItems(menu, fieldProp, "非0", CheckKind.NotZero, kind, level);
                    AddCheckLevelItems(menu, fieldProp, "范围", CheckKind.Range, kind, level);
                }
                menu.DropDown(btnRect);
            }
        }

        private void AddCheckLevelItems(GenericMenu menu, SerializedProperty fieldProp, string title,
            CheckKind kind, CheckKind curKind, ConfigCheckLevel curLevel)
        {
            var levels = new[]
            {
                ("Info", ConfigCheckLevel.Info),
                ("Warning", ConfigCheckLevel.Warning),
                ("Error", ConfigCheckLevel.Error),
            };
            foreach (var pair in levels)
            {
                var lvl = pair.Item2;
                menu.AddItem(new GUIContent($"{title}/{pair.Item1}"), curKind == kind && curLevel == lvl,
                    () => ApplyCheck(fieldProp, kind, lvl));
            }
        }

        /// <summary>落盘：选中的那项写级别、其余置 None（互斥三选一）；hasRange 随「范围」选择开关。</summary>
        private void ApplyCheck(SerializedProperty fieldProp, CheckKind kind, ConfigCheckLevel level)
        {
            fieldProp.FindPropertyRelative("notEmptyCheck").intValue
                = kind == CheckKind.NotEmpty ? (int)level : (int)ConfigCheckLevel.None;
            fieldProp.FindPropertyRelative("notZeroCheck").intValue
                = kind == CheckKind.NotZero ? (int)level : (int)ConfigCheckLevel.None;
            fieldProp.FindPropertyRelative("rangeCheck").intValue
                = kind == CheckKind.Range ? (int)level : (int)ConfigCheckLevel.None;
            fieldProp.FindPropertyRelative("hasRange").boolValue = kind == CheckKind.Range;
            mTarget.ApplyModifiedProperties();
        }

        /// <summary>类型变更后清掉新类型不支持的检查项（非0 / 范围 只对数值类型发射；非空 只对可能为空的类型有意义，留着等于设了不生效）。</summary>
        private static void NormalizeChecksForType(SerializedProperty fieldProp)
        {
            var type = (ConfigFieldType)fieldProp.FindPropertyRelative("type").intValue;
            var zp = fieldProp.FindPropertyRelative("notZeroCheck");
            var rp = fieldProp.FindPropertyRelative("rangeCheck");
            var ep = fieldProp.FindPropertyRelative("notEmptyCheck");
            if (!IsNumericType(type))
            {
                // 非0 / 范围 只对数值类型发射，其余类型清掉
                if (zp.intValue != (int)ConfigCheckLevel.None)
                    zp.intValue = (int)ConfigCheckLevel.None;
                if (rp.intValue != (int)ConfigCheckLevel.None)
                    rp.intValue = (int)ConfigCheckLevel.None;
                var rangeProp = fieldProp.FindPropertyRelative("hasRange");
                if (rangeProp.boolValue)
                    rangeProp.boolValue = false;
            }
            // 值类型（含数值）没有「空」态——遗留的非空设置在此清掉（数值类型的「必须有值」语义由「非0」承载）
            if (!SupportsNotEmpty(type) && ep.intValue != (int)ConfigCheckLevel.None)
                ep.intValue = (int)ConfigCheckLevel.None;
        }

        /// <summary>字段类型变更后的联动复位：两侧导出设置按新类型复位 + 清掉新类型不支持的检查项。</summary>
        private static void OnFieldTypeChanged(SerializedProperty fieldProp)
        {
            DefaultExportSettings(fieldProp);
            NormalizeChecksForType(fieldProp);
        }

        /// <summary>值类型在「直接导出」选项里展示的短类型名（如 直接导出(Int) / 直接导出(Vector3)）。</summary>
        private static string ValueTypeName(ConfigFieldType t)
        {
            switch (t)
            {
                case ConfigFieldType.Int: return "Int";
                case ConfigFieldType.Long: return "Long";
                case ConfigFieldType.Float: return "Float";
                case ConfigFieldType.Double: return "Double";
                case ConfigFieldType.Short: return "Short";
                case ConfigFieldType.Byte: return "Byte";
                case ConfigFieldType.UInt: return "UInt";
                case ConfigFieldType.Char: return "Char";
                case ConfigFieldType.Decimal: return "Decimal";
                case ConfigFieldType.Bool: return "Bool";
                case ConfigFieldType.Enum: return "Enum";
                case ConfigFieldType.BehaviourTree: return "Long";
                case ConfigFieldType.Vector2: return "Vector2";
                case ConfigFieldType.Vector3: return "Vector3";
                case ConfigFieldType.Vector4: return "Vector4";
                case ConfigFieldType.Vector2Int: return "Vector2Int";
                case ConfigFieldType.Vector3Int: return "Vector3Int";
                case ConfigFieldType.Quaternion: return "Quaternion";
                case ConfigFieldType.Color: return "Color";
                case ConfigFieldType.Color32: return "Color32";
                case ConfigFieldType.Rect: return "Rect";
                case ConfigFieldType.RectInt: return "RectInt";
                case ConfigFieldType.RectOffset: return "RectOffset";
                case ConfigFieldType.Bounds: return "Bounds";
                case ConfigFieldType.BoundsInt: return "BoundsInt";
                case ConfigFieldType.Gradient: return "Gradient";
                case ConfigFieldType.AnimationCurve: return "AnimationCurve";
                case ConfigFieldType.LayerMask: return "LayerMask";
                default: return "值";
            }
        }

        /// <summary>某字段类型在每侧导出设置里的「默认 mode/member/size」。</summary>
        /// <remarks>
        /// 默认值按字段类型落：
        /// - 引用其他配置（Reference）→ 一律默认导出成员 ID（取 ID 标量成员）；
        /// - 值类型 → 默认直接导出本身类型（下拉里显式写出类型名，如 直接导出(Int)）；
        /// - 资源/对象 → 默认导出名字；音频 key → 默认 BlobString；字符串系 → 直接导出(string)。
        /// List 结构的默认与元素类型一致：List&lt;Reference&gt; 仍是取成员 ID，List&lt;值&gt; 仍是直接导出。
        /// </remarks>
        private static (ConfigFieldExportMode mode, string member, ConfigFixedStringSize size) DefaultExportMode(ConfigFieldType type, bool client)
        {
            // 服务器侧 Unity 不可 JSON 类型：默认即不导出（见 IsServerUnsupportedType / BuildExportOptions）。
            if (!client && IsServerUnsupportedType(type))
                return (ConfigFieldExportMode.None, "", ConfigFixedStringSize.Size128);
            if (type == ConfigFieldType.Reference)
                return (ConfigFieldExportMode.Member, "ID", ConfigFixedStringSize.Size128);
            if (IsAssetExportType(type))
                return (ConfigFieldExportMode.AssetName, "", ConfigFixedStringSize.Size128);
            if (type == ConfigFieldType.AudioEvent)
                return (ConfigFieldExportMode.BlobString, "", ConfigFixedStringSize.Size128);
            if (type == ConfigFieldType.String || type == ConfigFieldType.LocalizedKey || type == ConfigFieldType.Addressable)
                return (ConfigFieldExportMode.Direct, "", ConfigFixedStringSize.Size128);
            // 数值 / 枚举 / 布尔 / 结构 / 行为树 / …：直接导出自身类型
            return (ConfigFieldExportMode.Direct, "", ConfigFixedStringSize.Size128);
        }

        /// <summary>按字段当前类型，把两侧导出设置复位为各自的默认值（与 <see cref="DefaultExportMode"/> 一致）。</summary>
        private static void DefaultExportSettings(SerializedProperty fieldProp)
        {
            if (fieldProp == null) return;
            var type = (ConfigFieldType)fieldProp.FindPropertyRelative("type").intValue;
            DefaultSideExport(fieldProp.FindPropertyRelative("clientExport"), type, true);
            DefaultSideExport(fieldProp.FindPropertyRelative("serverExport"), type, false);
        }

        private static void DefaultSideExport(SerializedProperty setting, ConfigFieldType type, bool client)
        {
            if (setting == null) return;
            var (mode, member, size) = DefaultExportMode(type, client);
            var m = setting.FindPropertyRelative("mode"); if (m != null) m.intValue = (int)mode;
            var mem = setting.FindPropertyRelative("memberName"); if (mem != null) mem.stringValue = member ?? "";
            var s = setting.FindPropertyRelative("fixedStringSize"); if (s != null) s.intValue = (int)size;
        }

        private static List<ExportOption> BuildExportOptions(ConfigTemplateField f, bool client)
        {
            var list = new List<ExportOption>();
            if (f == null)
                return list;

            // 服务器侧：Unity 对象引用族(Object 系，含自引用循环)与非数值结构(Color/Color32)
            // 以及不可 JSON 序列化的 Gradient/AnimationCurve，服务器无法消费；
            // 下拉因此只提供「不导出」一项，从源头杜绝服务器 JSON 自引用崩溃。
            if (!client && IsServerUnsupportedType(f.type))
            {
                list.Add(new ExportOption("不导出", ConfigFieldExportMode.None));
                return list;
            }

            switch (f.type)
            {
                case ConfigFieldType.Reference:
                {
                    // 默认首项：取成员 ID（引用字段一律默认导出 ID，见 DefaultExportMode）。
                    list.Add(new ExportOption("取成员 ID (long)", ConfigFieldExportMode.Member, "ID"));
                    var refType = ResolveRefType(f.refTypeFullName);
                    if (refType != null)
                    {
                        foreach (var fi in refType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                        {
                            var ft = fi.FieldType;
                            if (!IsScalarMemberType(ft))
                                continue;
                            if (string.Equals(fi.Name, "ID", StringComparison.Ordinal))
                                continue; // ID 已作为首项
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
                    // BlobString 排第一：变长存储无容量上限，key 长度不可预知，定长档位静默截断风险不对等。
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

                    // 数值/布尔/枚举/行为树/Unity结构：默认「直接导出(类型名)」+（可序列化则）转字符串
                    list.Add(new ExportOption($"直接导出({ValueTypeName(f.type)})", ConfigFieldExportMode.Direct));
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

            // 末尾统一追加「不导出」：该侧产物不包含此字段（见 ConfigFieldExportMode.None）。
            list.Add(new ExportOption("不导出", ConfigFieldExportMode.None));
            return list;
        }

        /// <summary>是否为 Unity 资源/对象引用族（可导出为 名字/路径/AA地址）。判定集合见 <see cref="ConfigFieldTypes.IsAssetRef"/>。</summary>
        private static bool IsAssetExportType(ConfigFieldType type)
            => ConfigFieldTypes.IsAssetRef(type);

        /// <summary>
        /// 服务器 JSON 直接导出无意义的 Unity 类型（Object 引用族 + Color/Color32 + Gradient/AnimationCurve）。
        /// 这些类型在服务器侧只提供「不导出」选项、且默认即不导出。
        /// 判定集合见 <see cref="ConfigFieldTypes.IsServerUnsupported"/>（与生成期可行性校验共用）。
        /// </summary>
        private static bool IsServerUnsupportedType(ConfigFieldType type)
            => ConfigFieldTypes.IsServerUnsupported(type);

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

            // 行高：控件 22px + 上下各 3px ≈ 28px（控件保持 22px——Popup 文本不竖排挤成一团）
            var lineH = EditorGUIUtility.singleLineHeight + 10f;
            const float headerH = 30f; // 表头深色带高度（原型提案：18 → 30）

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

            // 表头：深色底带（比行底更深一档）+ 底部 1px 分隔线——与数据行拉开层次，不再「裸贴」在第一行上。
            // 深浅主题分别取色：浅色主题不能用深色带（整片糊黑），用同档浅灰。
            var headerRect = EditorGUILayout.GetControlRect(true, headerH);
            GUIDrawer.FillRect(headerRect, EditorGUIUtility.isProSkin
                ? new Color(0.11f, 0.11f, 0.11f, 1f)     // 深色主题 #1c1c1c
                : new Color(0.78f, 0.78f, 0.78f, 1f));  // 浅色主题同档灰
            GUIDrawer.FillRect(new Rect(headerRect.x, headerRect.yMax - 1, headerRect.width, 1),
                EditorGUIUtility.isProSkin
                    ? new Color(0.06f, 0.06f, 0.06f, 1f)
                    : new Color(0.55f, 0.55f, 0.55f, 1f));
            var opsW = OpsWidth(headerRect.width);
            var (hName, hDesc, hType, hList, hExportC, hExportS, hCheck) = ColumnWidths(headerRect.width, headerRect.y, headerRect.height);
            var oldHeaderContent = GUI.contentColor;
            if (EditorGUIUtility.isProSkin)
                GUI.contentColor = new Color(0.79f, 0.79f, 0.79f); // 表头文字比行文字略暗一档
            EditorGUI.LabelField(CellRect(hName, hName.y, hName.height), "字段名", EditorStyles.boldLabel);
            EditorGUI.LabelField(CellRect(hDesc, hDesc.y, hDesc.height), "描述", EditorStyles.boldLabel);
            EditorGUI.LabelField(CellRect(hType, hType.y, hType.height), "类型", EditorStyles.boldLabel);
            EditorGUI.LabelField(CellRect(hList, hList.y, hList.height), "列表", EditorStyles.boldLabel);
            EditorGUI.LabelField(CellRect(hExportC, hExportC.y, hExportC.height), "客户端导出", EditorStyles.boldLabel);
            EditorGUI.LabelField(CellRect(hExportS, hExportS.y, hExportS.height), "服务端导出", EditorStyles.boldLabel);
            EditorGUI.LabelField(CellRect(hCheck, hCheck.y, hCheck.height), "检查", EditorStyles.boldLabel);
            EditorGUI.LabelField(new Rect(headerRect.xMax - opsW, headerRect.y, opsW, headerRect.height), "操作", HeaderOpsStyle());
            GUI.contentColor = oldHeaderContent;

            // 基类字段行（只读）
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                foreach (var f in chain[i].fields)
                {
                    var row = EditorGUILayout.GetControlRect(true, lineH);
                    DrawRowDecoration(row); // hover 高亮（纯装饰，绘在控件之下）
                    DrawReadOnlyFieldRow(row, chain[i].className, f);
                }
            }

            if (chain.Count > 0)
            {
                // 基类字段（只读）与自身字段之间的横向分割线（2px 中灰，深浅主题下都清晰）
                EditorGUILayout.Space(5);
                GUIDrawer.FillRect(EditorGUILayout.GetControlRect(true, 2),
                    new Color(0.55f, 0.55f, 0.55f, 0.9f));
                EditorGUILayout.Space(5);
            }

            // 自身字段行（主行单行 + 可选展开明细）
            for (var i = 0; i < mFieldsProp.arraySize; i++)
            {
                var index = i;
                var row = EditorGUILayout.GetControlRect(true, lineH);
                DrawRowDecoration(row);
                DrawEditableFieldRow(row, index);

                // 展开明细区
                if (mExpandedFields.Contains(index))
                {
                    var detailProp = mFieldsProp.GetArrayElementAtIndex(index);
                    DrawExpandedFieldDetail(detailProp);
                    EditorGUILayout.Space(4);
                }
            }

            // 添加按钮
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 添加字段", GUILayout.Width(100)))
            {
                AddEmptyField();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 行装饰：hover 高亮。纯装饰绘制——不产生控件、不改控件集合，IMGUI 每帧集合一致性不受影响。
        /// </summary>
        private void DrawRowDecoration(Rect row)
        {
            if (row.Contains(Event.current.mousePosition))
                GUIDrawer.FillRect(row, EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.05f)
                    : new Color(0f, 0f, 0f, 0.04f));
        }

        // 单元格文字内边距：左 6 / 右 4——字不贴列边，列与列之间留缝。
        // 表头标签、只读行标签、可编辑行控件共用一套（字段名列另让出箭头位，列表勾选框贴列起点）。
        private const float CellPadL = 6f;
        private const float CellPadR = 4f;

        /// <summary>单元格内容 rect：列内左右留内边距；列宽不足两距之和时保底 1px（窄窗口不出负宽）。</summary>
        private static Rect CellRect(Rect column, float y, float height) =>
            new Rect(column.x + CellPadL, y, Mathf.Max(1f, column.width - CellPadL - CellPadR), height);

        private static GUIStyle sHeaderOps;

        /// <summary>表头「操作」列样式：加粗 + 居中（与其它表头共用 boldLabel 字体，仅对齐方式不同）。</summary>
        private static GUIStyle HeaderOpsStyle()
        {
            if (sHeaderOps == null)
            {
                sHeaderOps = new GUIStyle(EditorStyles.boldLabel);
                sHeaderOps.alignment = TextAnchor.MiddleCenter;
            }
            return sHeaderOps;
        }

        /// <summary>操作列宽度：三个 22px 按钮 + 2×4px 间距 = 74px 保底；宽面板按 7% 增长。</summary>
        private static float OpsWidth(float total)
        {
            return Mathf.Max(74f, total * 0.07f);
        }

        private (Rect, Rect, Rect, Rect, Rect, Rect, Rect) ColumnWidths(float total, float y, float height)
        {
            var opsW = OpsWidth(total);
            var x = 0f;
            var name = new Rect(x, y, total * 0.12f, height); x += name.width;      // 字段名
            var desc = new Rect(x, y, total * 0.18f, height); x += desc.width;      // 描述
            var type = new Rect(x, y, total * 0.11f, height); x += type.width;      // 类型
            var list = new Rect(x, y, total * 0.04f, height); x += list.width;      // 列表
            var exportC = new Rect(x, y, total * 0.125f, height); x += exportC.width; // 客户端导出
            var exportS = new Rect(x, y, total * 0.125f, height); x += exportS.width; // 服务端导出
            var ops = new Rect(total - opsW, y, opsW, height);
            // 检查列吃掉剩余宽度（≈23%）：需同时容纳检查下拉(96px) + Min/Max 输入框(56px×2 + 标签)。
            // 比原型提案的 20% 略宽——Min/Max 56px 放不下时实施上会收缩，与提案「宽输入框」意图冲突
            var check = new Rect(x, y, Mathf.Max(140f, ops.x - x), height);
            return (name, desc, type, list, exportC, exportS, check);
        }

        // 基类只读行置灰样式（缓存克隆；绝对灰色，深浅主题下都可读）
        private static GUIStyle sReadOnlyLabel;
        private static GUIStyle sReadOnlyMini;

        private static void EnsureReadOnlyStyles()
        {
            if (sReadOnlyLabel == null)
            {
                sReadOnlyLabel = new GUIStyle(EditorStyles.label);
                sReadOnlyLabel.normal.textColor = new Color(0.49f, 0.49f, 0.49f); // #7d7d7d
            }
            if (sReadOnlyMini == null)
            {
                sReadOnlyMini = new GUIStyle(EditorStyles.miniLabel);
                sReadOnlyMini.normal.textColor = new Color(0.34f, 0.34f, 0.34f); // #565656 淡印
            }
        }

        private void DrawReadOnlyFieldRow(Rect row, string sourceClass, ConfigTemplateField f)
        {
            EnsureReadOnlyStyles();
            var (nameR, descR, typeR, listR, _, _, _) = ColumnWidths(row.width, row.y, row.height);

            // 基类字段：只读（label 样式）且整行置灰——与可编辑的输入框明确区分
            EditorGUI.LabelField(CellRect(nameR, nameR.y, nameR.height), f.name, sReadOnlyLabel);
            EditorGUI.LabelField(CellRect(descR, descR.y, descR.height), f.description, sReadOnlyLabel);

            // 类型 + 来源标注（放在类型列内，不超出窗口）
            var typeLabel = FieldTypeLabel(f);
            var srcLabel = $"({sourceClass})";
            var srcWidth = sReadOnlyMini.CalcSize(new GUIContent(srcLabel)).x + 4;
            var typeCell = CellRect(typeR, typeR.y, typeR.height);
            var typeWidth = typeCell.width - srcWidth;
            if (typeWidth < 30)
                typeWidth = 30;
            EditorGUI.LabelField(new Rect(typeCell.x, typeCell.y, typeWidth, typeCell.height), typeLabel, sReadOnlyLabel);
            EditorGUI.LabelField(new Rect(typeCell.x + typeWidth, typeCell.y, srcWidth, typeCell.height), srcLabel, sReadOnlyMini);

            EditorGUI.LabelField(CellRect(listR, listR.y, listR.height), f.isList ? "List" : "", sReadOnlyLabel);
        }

        /// <summary>是否为数值类型（可配 Min/Max 范围、可判非0）。</summary>
        private static bool IsNumericType(ConfigFieldType type)
            => ConfigFieldTypes.IsNumeric(type);

        /// <summary>
        /// 该字段类型是否支持「非空」检查——即字段存在真正的「空」态（null / 空串）。
        /// 判定集合见 <see cref="ConfigFieldTypes.SupportsNotEmpty"/>（与生成器共用同一份，
        /// 保证「UI 可选性 ↔ 生成器发射条件」永不漂移）。
        /// </summary>
        private static bool SupportsNotEmpty(ConfigFieldType type)
            => ConfigFieldTypes.SupportsNotEmpty(type);

        private void DrawEditableFieldRow(Rect row, int index)
        {
            var prop = mFieldsProp.GetArrayElementAtIndex(index);
            var nameProp = prop.FindPropertyRelative("name");
            var descProp = prop.FindPropertyRelative("description");
            var typeProp = prop.FindPropertyRelative("type");
            var listProp = prop.FindPropertyRelative("isList");
            var refProp = prop.FindPropertyRelative("refTypeFullName");
            var enumProp = prop.FindPropertyRelative("enumRefClassName");

            const float ctrlH = 22f; // 控件统一高度（原型提案：随行高一起放宽，16 → 22）
            var ctrlY = row.y + (row.height - ctrlH) * 0.5f; // 垂直居中

            // 展开箭头
            var expanded = mExpandedFields.Contains(index);
            if (GUI.Button(new Rect(row.x + 2, ctrlY, ctrlH, ctrlH), expanded ? "▼" : "▶", EditorStyles.label))
            {
                if (expanded) mExpandedFields.Remove(index);
                else mExpandedFields.Add(index);
            }

            var (nameR, descR, typeR, listR, exportCR, exportSR, checkR) = ColumnWidths(row.width, row.y, row.height);
            // 行首让出箭头宽度：只有字段名列让位，其余列与表头严格对齐
            // （此前所有列统一 +20px、表头不动，造成「列和内容对不上」的错位）
            var nameRect = new Rect(nameR.x + 20f, ctrlY, nameR.width - 20f - 2, ctrlH);
            var descRect = CellRect(descR, ctrlY, ctrlH);
            var typeRect = CellRect(typeR, ctrlY, ctrlH);
            var listRect = new Rect(listR.x, ctrlY, ctrlH, ctrlH);
            var exportCRect = CellRect(exportCR, ctrlY, ctrlH);
            var exportSRect = CellRect(exportSR, ctrlY, ctrlH);
            var checkRect = CellRect(checkR, ctrlY, ctrlH);

            // 该字段的 ConfigTemplateField 对象（导出下拉需要按字段类型算选项与可行性）
            var tpl = mTarget.targetObject as ConfigTemplate;
            var f = tpl != null && tpl.fields != null && index < tpl.fields.Count ? tpl.fields[index] : null;

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
                var bg = new Rect(row.x, row.y, row.width, row.height);
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
            DrawTypeField(typeRect, typeProp, refProp, enumProp, prop);

            // 列表开关切换时两侧导出设置按新形态复位默认（List<引用> 仍默认取成员 ID）
            var oldList = listProp.boolValue;
            listProp.boolValue = EditorGUI.Toggle(listRect, listProp.boolValue);
            if (oldList != listProp.boolValue)
                DefaultExportSettings(prop);

            // 客户端导出 / 服务端导出 两列：各自独立的 mode 下拉（含「不导出」）
            // ★ IMGUI 控件集合必须每帧一致：f 暂缺也照常绘制（下拉退化为当前值的合成项），不可条件跳过
            var feas = ConfigTemplateCodeGen.GetExportFeasibility(f);
            DrawSideExportPopup(prop, f, true, exportCRect, true, feas.ClientBroken);
            DrawSideExportPopup(prop, f, false, exportSRect, true, feas.ServerBroken);

            // 检查列：一个下拉三选一（非空 / 非0 / 范围），各自再选级别
            var ftypeNow = (ConfigFieldType)typeProp.intValue;
            var isNumeric = IsNumericType(ftypeNow);
            var supportsNotEmpty = SupportsNotEmpty(ftypeNow);
            var (checkKind, checkLevel) = ReadCheckState(prop, supportsNotEmpty);
            DrawCheckDropdown(checkRect, prop, supportsNotEmpty, isNumeric, checkKind, checkLevel);

            // 选了「范围」才出现 Min/Max（紧贴下拉右侧；未选范围时这格只有下拉按钮）。
            // 下拉宽与 Min/Max 输入框宽共用 CheckCellLayout 一套让步规则，保证两边永不错位
            if (checkKind == CheckKind.Range && isNumeric)
            {
                var minProp = prop.FindPropertyRelative("minValue");
                var maxProp = prop.FindPropertyRelative("maxValue");
                var (ddW, fieldW) = CheckCellLayout(checkRect, true);
                var mx = checkRect.x + ddW + 8f;
                EditorGUI.LabelField(new Rect(mx, ctrlY, MmLabelW, ctrlH), "Min", EditorStyles.miniLabel);
                minProp.floatValue = EditorGUI.FloatField(new Rect(mx + MmLabelW + MmGap, ctrlY, fieldW, ctrlH), minProp.floatValue);
                EditorGUI.LabelField(new Rect(mx + MmLabelW + MmGap + fieldW + MmGap, ctrlY, MmLabelW, ctrlH), "Max", EditorStyles.miniLabel);
                maxProp.floatValue = EditorGUI.FloatField(
                    new Rect(mx + 2f * (MmLabelW + MmGap) + fieldW + MmGap, ctrlY, fieldW, ctrlH), maxProp.floatValue);
            }

            // 操作按钮（22px，在操作列内居中）
            var opsW = OpsWidth(row.width);
            var btnW = 22f;
            var btnGap = 4f;
            var groupW = btnW * 3f + btnGap * 2f; // 74
            var bx = row.xMax - opsW + Mathf.Max(0f, (opsW - groupW) * 0.5f);
            if (GUI.Button(new Rect(bx, ctrlY, btnW, ctrlH), "↑"))
                MoveOwnField(index, -1);
            if (GUI.Button(new Rect(bx + btnW + btnGap, ctrlY, btnW, ctrlH), "↓"))
                MoveOwnField(index, 1);
            if (GUI.Button(new Rect(bx + 2f * (btnW + btnGap), ctrlY, btnW, ctrlH), "×"))
                DeleteOwnField(index);
        }

        private readonly List<int> mExpandedFields = new List<int>();

        /// <summary>展开明细区：字段描述 + 只读 + 关联参数（范围已内联到主行「范围」列）。</summary>
        private void DrawExpandedFieldDetail(SerializedProperty prop)
        {
            var typeProp = prop.FindPropertyRelative("type");
            var descProp = prop.FindPropertyRelative("description");
            var readOnlyProp = prop.FindPropertyRelative("readOnly");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("描述", GUILayout.Width(56));
            descProp.stringValue = EditorGUILayout.TextField(descProp.stringValue);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // 只读：数据编辑器里禁用该字段输入（典型：基类 ID/GUID）
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("只读", GUILayout.Width(56));
            readOnlyProp.boolValue = EditorGUILayout.Toggle(readOnlyProp.boolValue, GUILayout.Width(22));
            EditorGUILayout.LabelField("勾选后数据编辑器中该字段不可编辑（仅影响展示，不影响发号/导出）", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // 关联参数：条件显示（满足全部条件才在配置面板绘制本字段）
            DrawVisibilityBlock(prop);

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
            EditorGUILayout.LabelField("关联参数", GUILayout.Width(56));
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
            f.FindPropertyRelative("readOnly").boolValue = false;
            f.FindPropertyRelative("hasRange").boolValue = false;
            f.FindPropertyRelative("minValue").floatValue = 0f;
            f.FindPropertyRelative("maxValue").floatValue = 0f;
            f.FindPropertyRelative("notEmptyCheck").intValue = (int)ConfigCheckLevel.None;
            f.FindPropertyRelative("notZeroCheck").intValue = (int)ConfigCheckLevel.None;
            f.FindPropertyRelative("rangeCheck").intValue = (int)ConfigCheckLevel.None;
            // 两侧导出设置按字段默认复位（值类型→直接导出自身类型；引用→取成员 ID；资源→名字；…）
            DefaultExportSettings(f);

            // 关联条件列表复位为空：arraySize++ 会复制上一个元素的 visibility
            // （条件行连目标字段一起被抄过来），与上方逐属性复位同一范式。
            var visibility = f.FindPropertyRelative("visibility");
            if (visibility != null)
                visibility.arraySize = 0;

            mTarget.ApplyModifiedProperties();
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

        private void DrawTypeField(Rect rect, SerializedProperty typeProp, SerializedProperty refProp, SerializedProperty enumProp, SerializedProperty fieldProp)
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
                        var oldType = (ConfigFieldType)typeProp.intValue;
                        typeProp.intValue = (int)item;
                        refProp.stringValue = "";
                        enumProp.stringValue = "";
                        // 类型变了：两侧导出设置按新类型复位默认（引用→取成员ID；值类型→直接导出自身类型；…）
                        if (oldType != item)
                            OnFieldTypeChanged(fieldProp);
                        // 多语言 key 只进客户端：服务端置「不导出」，用户可改
                        if (item == ConfigFieldType.LocalizedKey)
                        {
                            var srv = fieldProp.FindPropertyRelative("serverExport").FindPropertyRelative("mode");
                            if (srv != null) srv.intValue = (int)ConfigFieldExportMode.None;
                        }
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
                            var oldType = (ConfigFieldType)typeProp.intValue;
                            typeProp.intValue = (int)ConfigFieldType.Reference;
                            refProp.stringValue = cfgType.AssemblyQualifiedName;
                            enumProp.stringValue = "";
                            // 类型变了：两侧导出设置复位默认（引用→取成员 ID）
                            if (oldType != ConfigFieldType.Reference)
                                DefaultExportSettings(fieldProp);
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
                            var oldType = (ConfigFieldType)typeProp.intValue;
                            typeProp.intValue = (int)ConfigFieldType.Enum;
                            enumProp.stringValue = e.className;
                            refProp.stringValue = "";
                            // 类型变了：两侧导出设置复位默认（枚举→直接导出 Enum(int)）
                            if (oldType != ConfigFieldType.Enum)
                                DefaultExportSettings(fieldProp);
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
