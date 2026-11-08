using System.Collections.Generic;
using System.IO;
using HYC.Framework.Config;
using HYC.Framework.Config.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 场景内容类型定义的自定义编辑器：<b>只在 Scene Content 窗口内嵌时</b>画基本信息 / 字段列表
    /// 与动作按钮；Project 里点开 .asset 时资产 Inspector 主体留空（见 <see cref="IsEmbedded"/>）。
    /// <para>
    /// 两块内容：基本信息 / 字段列表。动作按钮：保存、生成代码、校验。
    /// （「扫描 prefab / 反填字段」已移除——我们在做新功能，没有旧组件要迁移；
    /// 只读的内容总览在 Scene Content 窗口的「内容总览」页签里。）
    /// </para>
    /// <para>
    /// 字段行对齐配置模板编辑器：类型下拉复用 <see cref="ConfigFieldTypeMenu"/>，
    /// 展开明细里配「只读」与「关联参数（显隐规则）」——生成器把它们转成
    /// <c>[ReadOnly]</c> / <c>[CfgVisibleWhen]</c>，由统一 Inspector 消费。
    /// </para>
    /// <para>
    /// IMGUI 约定：每帧控件集合固定——第 4 行「引用说明」始终绘制，只随类型换文案；
    /// 展开区高度由 <c>elementHeightCallback</c> 与 <c>drawElementCallback</c> 共用同一个
    /// 展开索引集合与同一份条件数量计算。
    /// </para>
    /// </summary>
    [CustomEditor(typeof(SceneContentTypeDef))]
    public class SceneContentTypeDefEditor : UnityEditor.Editor
    {
        private const float BaseElementHeight = 102f;
        private const float RowHeight = 18f;

        private SerializedProperty _displayName;
        private SerializedProperty _className;
        private SerializedProperty _fields;
        private ReorderableList _fieldList;
        // 多个行可同时展开；不能用单个 index —— ReorderableList 逐行调用 drawElementCallback，
        // 前面的行会把单值状态重置掉，导致后面的 Foldout 永远展不开。
        private readonly HashSet<int> _expandedFields = new HashSet<int>();

        private string _message;
        private bool _messageIsError;

        /// <summary>
        /// 是否由 <see cref="SceneContentWindow"/> 内嵌绘制。
        /// <para>
        /// 字段设计只在窗口内进行：Project 里点开 .asset 的原生 Inspector 不画任何编辑内容。
        /// 本类因 <c>[CustomEditor]</c> 同时被资产 Inspector 与窗口的 <c>CreateEditor</c> 取用，
        /// 用这个标志区分上下文——窗口创建实例后置 true，资产 Inspector 的实例保持 false。
        /// </para>
        /// </summary>
        public bool IsEmbedded { get; set; }

        private void OnEnable()
        {
            _displayName = serializedObject.FindProperty("displayName");
            _className = serializedObject.FindProperty("className");
            RebuildFieldList();
        }

        /// <summary>重建字段列表（数组结构变更后必须重建：旧的 SerializedProperty 会失效）。</summary>
        private void RebuildFieldList()
        {
            _fields = serializedObject.FindProperty("fields");
            _fieldList = new ReorderableList(serializedObject, _fields, true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r, $"字段（{_fields.arraySize}）"),
                elementHeightCallback = ElementHeight,
            };
            _fieldList.drawElementCallback = DrawFieldElement;
        }

        /// <summary>展开明细区按条件条数变高；高度计算必须与绘制用同一份数据，否则布局错位。</summary>
        private float ElementHeight(int index)
        {
            if (!_expandedFields.Contains(index) || _fields == null || index < 0 || index >= _fields.arraySize)
                return BaseElementHeight;
            var vis = _fields.GetArrayElementAtIndex(index).FindPropertyRelative("visibility");
            var count = vis != null ? vis.arraySize : 0;
            return BaseElementHeight + RowHeight /*只读*/ + RowHeight /*关联参数标题*/
                   + count * RowHeight + 20f /*添加条件*/ + 6f;
        }

        private void DrawFieldElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var el = _fields.GetArrayElementAtIndex(index);
            var pName = el.FindPropertyRelative("name");
            var pType = el.FindPropertyRelative("type");
            var pList = el.FindPropertyRelative("isList");
            var pDesc = el.FindPropertyRelative("description");
            var pEnum = el.FindPropertyRelative("enumRefClassName");
            var pRef = el.FindPropertyRelative("refTypeFullName");
            var pClient = el.FindPropertyRelative("clientExport")?.FindPropertyRelative("mode");
            var pServer = el.FindPropertyRelative("serverExport")?.FindPropertyRelative("mode");

            var w = rect.width;
            var typeRect = new Rect(rect.x + w * 0.40f, rect.y + 2, w * 0.36f - 4, 18);

            // 第 1 行：字段名 | 类型下拉（完整 Config Editor 菜单） | 列表
            EditorGUI.PropertyField(new Rect(rect.x, rect.y + 2, w * 0.40f - 4, 18), pName, GUIContent.none);
            DrawTypeField(typeRect, el, pType, pEnum, pRef);
            EditorGUI.PropertyField(new Rect(rect.x + w * 0.76f, rect.y + 2, w * 0.24f - 2, 18), pList, new GUIContent("列表"));

            // 第 2 行：说明
            EditorGUI.PropertyField(new Rect(rect.x, rect.y + 22, w - 2, 18), pDesc, new GUIContent("说明"));

            // 第 3 行：两侧导出方式（数据被打坏时跳过绘制，不让整个面板崩掉）
            if (pClient != null)
                EditorGUI.PropertyField(new Rect(rect.x, rect.y + 42, w * 0.5f - 4, 18), pClient, new GUIContent("客户端"));
            if (pServer != null)
                EditorGUI.PropertyField(new Rect(rect.x + w * 0.5f, rect.y + 42, w * 0.5f - 2, 18), pServer, new GUIContent("服务端"));

            // 第 4 行：引用说明（始终绘制；枚举 / 配置引用由下拉自动写入，不再手填）
            EditorGUI.LabelField(new Rect(rect.x, rect.y + 62, w - 2, 18), RefDetail(pType, pEnum, pRef));

            // 第 5 行：展开明细（只读 / 关联参数），与配置模板编辑器同款 Foldout
            var foldRect = new Rect(rect.x, rect.y + 82, w - 2, 18);
            var wasExpanded = _expandedFields.Contains(index);
            var expanded = EditorGUI.Foldout(foldRect, wasExpanded, "只读 / 关联参数（显隐规则）");
            if (expanded != wasExpanded)
            {
                if (expanded) _expandedFields.Add(index);
                else _expandedFields.Remove(index);
            }
            if (!expanded)
                return;

            var def = serializedObject.targetObject as SceneContentTypeDef;
            var selfName = pName != null ? pName.stringValue ?? "" : "";
            var candidates = CollectCandidates(def, selfName);
            var y = rect.y + 102f;

            var ro = el.FindPropertyRelative("readOnly");
            if (ro != null)
            {
                ro.boolValue = EditorGUI.Toggle(new Rect(rect.x + 16, y, 18, 18), ro.boolValue);
                EditorGUI.LabelField(new Rect(rect.x + 38, y, w - 40, 18),
                    "只读（组件 Inspector 中不可编辑，仅影响展示，不影响导出）", EditorStyles.miniLabel);
            }
            y += RowHeight;

            EditorGUI.LabelField(new Rect(rect.x + 16, y, w - 18, 18),
                "关联参数 · 满足全部条件才在组件 Inspector 显示本字段", EditorStyles.miniLabel);
            y += RowHeight;

            var vis = el.FindPropertyRelative("visibility");
            if (vis != null)
            {
                for (var i = 0; i < vis.arraySize; i++)
                {
                    DrawConditionRow(new Rect(rect.x + 16, y, w - 20, RowHeight), vis, i, candidates);
                    y += RowHeight;
                }

                using (new EditorGUI.DisabledScope(candidates.Count == 0))
                {
                    if (GUI.Button(new Rect(rect.x + 16, y, 100, 18), "+ 添加条件"))
                        AddCondition(vis);
                }
                if (candidates.Count == 0)
                    EditorGUI.LabelField(new Rect(rect.x + 122, y, w - 124, 18),
                        "没有可用的布尔 / 枚举字段（不含自身与列表字段）", EditorStyles.miniLabel);
            }
        }

        /// <summary>单条关联条件：[目标字段下拉][值控件][×]。与配置模板编辑器同一套语义。</summary>
        private static void DrawConditionRow(Rect rect, SerializedProperty visProp, int index,
            List<SceneContentFieldDef> candidates)
        {
            var cond = visProp.GetArrayElementAtIndex(index);
            var targetProp = cond.FindPropertyRelative("targetField");
            var boolProp = cond.FindPropertyRelative("boolValue");
            var enumValuesProp = cond.FindPropertyRelative("enumValues");
            if (targetProp == null)
                return;

            const float delW = 22f;
            var ddW = Mathf.Min(150f, rect.width * 0.45f);
            var dropRect = new Rect(rect.x, rect.y, ddW, rect.height);
            var valueRect = new Rect(dropRect.xMax + 4, rect.y, rect.width - ddW - delW - 8, rect.height);
            var delRect = new Rect(rect.xMax - delW, rect.y, delW, rect.height);

            var names = new List<string>();
            foreach (var c in candidates)
                names.Add(c.name);
            var targetName = targetProp.stringValue ?? "";
            var idx = names.IndexOf(targetName);
            var invalid = idx < 0;
            if (invalid)
                names.Insert(0, string.IsNullOrEmpty(targetName) ? "（未选择字段）" : targetName + "（已失效）");

            var newIdx = EditorGUI.Popup(dropRect, Mathf.Max(idx, 0), names.ToArray());
            if (newIdx >= 0 && newIdx < names.Count && !(invalid && newIdx == 0))
                targetProp.stringValue = names[newIdx];

            var target = candidates.Find(c => c.name == (targetProp.stringValue ?? ""));
            if (target == null)
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 0.8f, 0.35f);
                GUI.Label(valueRect, "目标字段不存在或类型不支持（条件不生效，字段保持显示）", EditorStyles.miniLabel);
                GUI.color = old;
            }
            else if (target.type == ConfigFieldType.Bool)
            {
                boolProp.boolValue = EditorGUI.Toggle(valueRect, boolProp.boolValue);
            }
            else
            {
                DrawEnumConditionValue(valueRect, target, enumValuesProp);
            }

            if (GUI.Button(delRect, "×"))
                visProp.DeleteArrayElementAtIndex(index);
        }

        /// <summary>enum 目标的值控件：MaskField 多选枚举值（≤32 个）。</summary>
        private static void DrawEnumConditionValue(Rect rect, SceneContentFieldDef target, SerializedProperty enumValuesProp)
        {
            var def = ConfigEnumCodeGen.FindEnum(target.enumRefClassName);
            var valueNames = new List<string>();
            if (def != null && def.values != null)
            {
                foreach (var v in def.values)
                {
                    if (v != null && !string.IsNullOrEmpty(v.name))
                        valueNames.Add(v.name);
                }
            }
            if (valueNames.Count == 0)
            {
                GUI.Label(rect, "枚举定义不存在或没有枚举值（条件不生效）", EditorStyles.miniLabel);
                return;
            }
            if (valueNames.Count > 32)
            {
                GUI.Label(rect, $"枚举值 {valueNames.Count} 个超过 32，无法多选", EditorStyles.miniLabel);
                return;
            }

            var options = valueNames.ToArray();
            var mask = 0;
            for (var i = 0; i < options.Length; i++)
            {
                if (EnumValuesContains(enumValuesProp, options[i]))
                    mask |= 1 << i;
            }
            var newMask = EditorGUI.MaskField(rect, mask, options);
            if (newMask != mask)
                WriteEnumValues(enumValuesProp, options, newMask);
        }

        private static bool EnumValuesContains(SerializedProperty prop, string valueName)
        {
            if (prop == null)
                return false;
            for (var i = 0; i < prop.arraySize; i++)
            {
                if (prop.GetArrayElementAtIndex(i).stringValue == valueName)
                    return true;
            }
            return false;
        }

        private static void WriteEnumValues(SerializedProperty prop, string[] options, int mask)
        {
            if (prop == null)
                return;
            prop.arraySize = 0;
            var all = mask == -1;
            for (var i = 0; i < options.Length; i++)
            {
                if (!all && (mask & (1 << i)) == 0)
                    continue;
                var index = prop.arraySize;
                prop.arraySize++;
                prop.GetArrayElementAtIndex(index).stringValue = options[i];
            }
        }

        /// <summary>添加空条件：逐属性复位，防 arraySize++ 复制上一条。</summary>
        private static void AddCondition(SerializedProperty visProp)
        {
            var index = visProp.arraySize;
            visProp.arraySize++;
            var cond = visProp.GetArrayElementAtIndex(index);
            cond.FindPropertyRelative("targetField").stringValue = "";
            cond.FindPropertyRelative("boolValue").boolValue = true;
            var enumValues = cond.FindPropertyRelative("enumValues");
            if (enumValues != null)
                enumValues.arraySize = 0;
        }

        /// <summary>候选目标字段：本类型内 bool / enum 标量字段，排除自身与列表字段。</summary>
        private static List<SceneContentFieldDef> CollectCandidates(SceneContentTypeDef def, string selfName)
        {
            var result = new List<SceneContentFieldDef>();
            if (def == null || def.fields == null)
                return result;
            foreach (var f in def.fields)
            {
                if (f == null || f.isList || f.name == selfName)
                    continue;
                if (f.type == ConfigFieldType.Bool || f.type == ConfigFieldType.Enum)
                    result.Add(f);
            }
            return result;
        }

        /// <summary>画类型下拉：菜单由公共数据源构建，选中后写回本字段的序列化属性。</summary>
        private void DrawTypeField(Rect rect, SerializedProperty el, SerializedProperty pType,
            SerializedProperty pEnum, SerializedProperty pRef)
        {
            var current = new ConfigFieldTypeMenu.Selection
            {
                type = (ConfigFieldType)pType.intValue,
                enumRefClassName = pEnum.stringValue,
                refTypeFullName = pRef.stringValue,
            };

            if (!EditorGUI.DropdownButton(rect, new GUIContent(ConfigFieldTypeMenu.GetLabel(current)), FocusType.Keyboard))
                return;

            var menu = ConfigFieldTypeMenu.BuildMenu(current,
                SceneContentTypeCodeGen.UnsupportedReason,
                sel => ApplyTypeSelection(el, sel));
            menu.DropDown(rect);
        }

        /// <summary>选中菜单项后写类型与引用信息；类型变了才复位两侧导出设置。</summary>
        private void ApplyTypeSelection(SerializedProperty el, ConfigFieldTypeMenu.Selection sel)
        {
            var pType = el.FindPropertyRelative("type");
            var pEnum = el.FindPropertyRelative("enumRefClassName");
            var pRef = el.FindPropertyRelative("refTypeFullName");
            var oldType = (ConfigFieldType)pType.intValue;

            pType.intValue = (int)sel.type;
            pEnum.stringValue = sel.enumRefClassName ?? "";
            pRef.stringValue = sel.refTypeFullName ?? "";

            if (oldType != sel.type)
                ResetExportSettings(el, sel.type);

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        /// <summary>按新类型给两侧导出方式一个合法默认值（GameObject 走层级路径，其余直接导出）。</summary>
        private static void ResetExportSettings(SerializedProperty el, ConfigFieldType type)
        {
            var client = el.FindPropertyRelative("clientExport")?.FindPropertyRelative("mode");
            var server = el.FindPropertyRelative("serverExport")?.FindPropertyRelative("mode");
            if (client != null)
                client.intValue = (int)(type == ConfigFieldType.GameObject
                    ? SceneContentExportMode.HierarchyPath
                    : SceneContentExportMode.Direct);
            if (server != null)
                server.intValue = (int)SceneContentExportMode.None;
        }

        /// <summary>第 4 行文案：枚举 / 配置引用显示当前选择，其余给出操作提示。</summary>
        private static string RefDetail(SerializedProperty pType, SerializedProperty pEnum, SerializedProperty pRef)
        {
            switch ((ConfigFieldType)pType.intValue)
            {
                case ConfigFieldType.Enum:
                    return string.IsNullOrEmpty(pEnum.stringValue)
                        ? "枚举：未选择，在类型下拉「引用/枚举引用」里挑"
                        : "枚举定义：" + pEnum.stringValue;
                case ConfigFieldType.Reference:
                {
                    if (string.IsNullOrEmpty(pRef.stringValue))
                        return "配置引用：未选择，在类型下拉「引用/配置引用」里挑";
                    var t = ConfigTemplateCodeGen.ResolveRefTypeForUi(pRef.stringValue);
                    return "配置引用：" + (t != null ? ConfigTypeDisplay.GetFullLabel(t) : pRef.stringValue);
                }
                default:
                    return "类型下拉含值类型 / Unity 数学 / 框架能力；用不上的项已置灰";
            }
        }

        public override void OnInspectorGUI()
        {
            if (serializedObject == null || serializedObject.targetObject == null)
                return;

            // 资产自身的 Inspector 主体留空：字段设计只在 Scene Content 窗口内嵌的这一份上做，
            // 不让同一套字段编辑 UI 在两处出现。
            if (!IsEmbedded)
                return;

            serializedObject.Update();

            DrawHeader();
            DrawBasicInfo();
            DrawFieldList();
            DrawActions();

            var changed = GUI.changed;
            serializedObject.ApplyModifiedProperties();
            if (changed && serializedObject.targetObject != null)
                EditorUtility.SetDirty(serializedObject.targetObject);
        }

        /// <summary>
        /// 自定义头部：画资产图标 + 资产名（只读），**不画 Unity 默认头部的「?」帮助图标**——
        /// 资产名由 <see cref="SceneContentTypeCodeGen.SyncTypeAssetName"/> 按显示名自动同步，不在此手改，
        /// 避免与显示名脱节。
        /// </summary>
        protected override void OnHeaderGUI()
        {
            var obj = serializedObject.targetObject;
            if (obj == null)
            {
                base.OnHeaderGUI();
                return;
            }
            var content = EditorGUIUtility.ObjectContent(obj, obj.GetType());
            var def = obj as SceneContentTypeDef;
            var headerName = (def != null && !string.IsNullOrEmpty(def.displayName)) ? def.displayName : obj.name;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.inspectorDefaultMargins))
            {
                EditorGUILayout.LabelField(
                    new GUIContent(content.image, headerName),
                    EditorStyles.boldLabel,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight + 6));
            }
            EditorGUILayout.Space(2);
        }

        private void DrawHeader()
        {
            var def = (SceneContentTypeDef)serializedObject.targetObject;
            EditorGUILayout.LabelField($"场景内容类型 · {def.displayName}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"内容 id：{def.ContentTypeId}", EditorStyles.miniLabel);
            var path = SceneContentTypeCodeGen.FilePathFor(def.className);
            EditorGUILayout.LabelField(
                SceneContentTypeCodeGen.IsGenerated(def) ? $"已生成：{path}" : $"未生成：{path}",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
        }

        private void DrawBasicInfo()
        {
            EditorGUILayout.PropertyField(_displayName, new GUIContent("显示名"));
            EditorGUILayout.PropertyField(_className, new GUIContent("类名"));
            EditorGUILayout.Space(4);
        }

        private void DrawFieldList()
        {
            _fieldList.DoLayoutList();
            EditorGUILayout.Space(4);
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("保存", GUILayout.Width(72)))
                {
                    serializedObject.ApplyModifiedProperties();
                    var def = (SceneContentTypeDef)serializedObject.targetObject;
                    var cleaned = SceneContentTypeCodeGen.NormalizeIdentifiers(def);
                    if (cleaned)
                        serializedObject.Update();
                    // 保存即同步资产名 = 显示名（与数据配置 SaveInternal 同逻辑，保留 GUID 不破坏引用）
                    var before = AssetDatabase.GetAssetPath(def);
                    var after = SceneContentTypeCodeGen.SyncTypeAssetName(def);
                    EditorUtility.SetDirty(def);
                    AssetDatabase.SaveAssetIfDirty(def);
                    _message = cleaned ? "已保存（并清理了类名 / 字段名里的不可见字符）。" : "已保存。";
                    if (after != null && !string.Equals(after, before, System.StringComparison.OrdinalIgnoreCase))
                        _message += $" 资产已重命名为 {Path.GetFileName(after)}。";
                    _messageIsError = false;
                }

                if (GUILayout.Button("生成代码", GUILayout.Width(96)))
                    GenerateCode();

                if (GUILayout.Button("校验", GUILayout.Width(72)))
                    ValidateOnly();
            }

            if (!string.IsNullOrEmpty(_message))
                EditorGUILayout.HelpBox(_message, _messageIsError ? MessageType.Error : MessageType.Info);
        }

        // ---------------------------------------------------------------- 动作

        private void GenerateCode()
        {
            serializedObject.ApplyModifiedProperties();
            var def = (SceneContentTypeDef)serializedObject.targetObject;
            if (SceneContentTypeCodeGen.NormalizeIdentifiers(def))
                serializedObject.Update();
            SceneContentTypeCodeGen.SyncTypeAssetName(def); // 生成前先落盘并同步资产名（与数据配置生成前先保存一致）
            if (SceneContentTypeCodeGen.WriteFile(def, out var error))
            {
                _message = $"已生成：{SceneContentTypeCodeGen.FilePathFor(def.className)}";
                _messageIsError = false;
            }
            else
            {
                _message = "生成失败：" + error;
                _messageIsError = true;
            }
        }

        private void ValidateOnly()
        {
            serializedObject.ApplyModifiedProperties();
            var def = (SceneContentTypeDef)serializedObject.targetObject;
            var cleaned = SceneContentTypeCodeGen.NormalizeIdentifiers(def);
            if (cleaned)
                serializedObject.Update();
            var ok = SceneContentTypeCodeGen.Validate(def, out var error);
            _message = (ok ? "校验通过。" : "校验失败：" + error)
                     + (cleaned ? "（已自动清理不可见字符，请点保存）" : "");
            _messageIsError = !string.IsNullOrEmpty(error);
        }
    }
}
