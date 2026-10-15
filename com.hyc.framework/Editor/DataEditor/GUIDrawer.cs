using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using HYC.Framework.Audio;

namespace HYC.Framework.Config.Editor
{
    public static class GUIDrawer
    {
        public static void FillRect(Rect rect)
        {
            var oldColor = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.3f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        public static void FillRect(Rect rect, Color color)
        {
            var oldColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        public static void DrawFields(SerializedObject target)
        {
            foreach (var field in GUIType.Get(target.targetObject.GetType()).Fields)
                DrawField(target, field);
        }

        public static void DrawFields(SerializedObject target, string group)
        {
            foreach (var field in GUIType.Get(target.targetObject.GetType()).GetGroupBy(group))
                DrawField(target, field);
        }

        public static void DrawField(SerializedObject target, GUITypeField field)
        {
            if (CheckVisible(target.targetObject, field))
            {
                var prop = target.FindProperty(field.Name);
                if (prop != null)
                    DrawFieldGUI(prop, field);
            }
        }

        public static void DrawFields(SerializedProperty property)
        {
            if (property == null || property.isArray)
                return;
            foreach (var field in GUIType.Get(property.boxedValue.GetType()).Fields)
                DrawField(property, field);
        }

        public static void DrawFields(SerializedProperty property, string group)
        {
            foreach (var field in GUIType.Get(property.boxedValue.GetType()).GetGroupBy(group))
                DrawField(property, field);
        }

        public static void DrawField(SerializedProperty property, GUITypeField field)
        {
            if (CheckVisible(property.boxedValue, field))
            {
                if (field.FlatDisplay && !field.Type.IsPrimitive)
                    DrawFields(property.FindPropertyRelative(field.Name));
                else
                    DrawFieldGUI(property.FindPropertyRelative(field.Name), field);
            }
        }

        public static bool CheckVisible(object target, GUITypeField field)
        {
            if (field.HideInInspector)
                return false;
            if (field.Visible != null && !CheckVisibleMembers(target, field.Visible))
                return false;
            if (field.VisibleWhen != null && field.VisibleWhen.Length > 0
                && !CheckVisibleWhen(target, field.VisibleWhen))
                return false;
            return true;
        }

        /// <summary>[Visible] 成员求值（原 CheckVisible 内联逻辑抽出，行为不变）。</summary>
        private static bool CheckVisibleMembers(object target, VisibleAttribute visible)
        {
            var and = visible.Logic == VisibleAttribute.LogicType.And;
            var show = and;

            foreach (var memberName in visible.Methods)
            {
                if (string.IsNullOrEmpty(memberName))
                    continue;

                var fieldMember = target.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fieldMember != null && fieldMember.FieldType == typeof(bool))
                {
                    var result = (bool)fieldMember.GetValue(target);
                    show = and ? show && result : show || result;
                    continue;
                }

                var property = target.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (property != null && property.CanRead && property.PropertyType == typeof(bool))
                {
                    var result = (bool)property.GetValue(target);
                    show = and ? show && result : show || result;
                    continue;
                }

                var method = target.GetType().GetMethod(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (method != null && method.ReturnType == typeof(bool) && method.GetParameters().Length == 0)
                {
                    var result = (bool)method.Invoke(target, Array.Empty<object>());
                    show = and ? show && result : show || result;
                    continue;
                }

                var eq = memberName.IndexOf("==", StringComparison.Ordinal);
                var neq = memberName.IndexOf("!=", StringComparison.Ordinal);
                var opIndex = eq >= 0 ? eq : neq;
                if (opIndex > 0)
                {
                    var fieldName = memberName.Substring(0, opIndex).Trim();
                    var compareValue = memberName.Substring(opIndex + 2).Trim();
                    var cmpField = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (cmpField == null)
                        continue;

                    var value = cmpField.GetValue(target);
                    var valueStr = value == null ? "" : (value is bool b ? (b ? "true" : "false") : value.ToString());
                    var result = eq >= 0 ? string.Equals(valueStr, compareValue) : !string.Equals(valueStr, compareValue);
                    show = and ? show && result : show || result;
                }
            }

            return show;
        }

        /// <summary>
        /// [CfgVisibleWhen] 条件求值（AND）：目标字段缺失或类型不符<b>视为满足</b>（保持显示）——
        /// 配置面板不能因为目标字段被改名/删除就把字段吞掉。仅影响数据编辑器面板。
        /// 反射读目标对象当前值：SerializedProperty 改值经 ApplyModifiedProperties 生效后，
        /// 下一轮 Layout+Repaint 即反映到面板，不存在同帧布局不一致。
        /// </summary>
        private static bool CheckVisibleWhen(object target, CfgVisibleWhenAttribute[] conditions)
        {
            foreach (var cond in conditions)
            {
                if (cond == null || string.IsNullOrEmpty(cond.FieldName))
                    continue;

                var fieldMember = target.GetType().GetField(cond.FieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fieldMember == null)
                    continue; // 目标字段丢失（改名/删除后未重新生成）→ 视为满足

                var value = fieldMember.GetValue(target);
                if (fieldMember.FieldType == typeof(bool))
                {
                    if (!(value is bool b) || b != cond.BoolValue)
                        return false;
                }
                else if (fieldMember.FieldType.IsEnum)
                {
                    if (!cond.MatchesEnum(value == null ? "" : value.ToString()))
                        return false;
                }
                // 其它类型（List&lt;T&gt; 等）→ 视为满足
            }
            return true;
        }

        private static bool CheckVisible(object target, string memberName)
        {
            if (string.IsNullOrEmpty(memberName))
                return false;

            var fieldMember = target.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fieldMember != null && fieldMember.FieldType == typeof(bool))
                return (bool)fieldMember.GetValue(target);

            var property = target.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (property != null && property.CanRead && property.PropertyType == typeof(bool))
                return (bool)property.GetValue(target);

            var method = target.GetType().GetMethod(memberName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method != null && method.ReturnType == typeof(bool) && method.GetParameters().Length == 0)
                return (bool)method.Invoke(target, Array.Empty<object>());

            return false;
        }

        private static void DrawFieldGUI(SerializedProperty prop, GUITypeField field)
        {
            if (prop == null)
                return;

            // 高亮检测：检查错误定位到该字段时短暂黄底
            if (ConfigDataContainer.IsHighlighting(prop.serializedObject.targetObject, field.Name))
            {
                var hlRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight + 4);
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 0.92f, 0.4f, 0.35f);
                GUI.DrawTexture(hlRect, EditorGUIUtility.whiteTexture);
                GUI.color = oldColor;
                EditorGUILayout.Space(-EditorGUIUtility.singleLineHeight - 4);
            }

            var enabled = GUI.enabled;
            if (field.ReadOnly)
                GUI.enabled = false;

            // [Header] Unity 只在默认 PropertyField 流程里画；走自定义绘制分支时这里补画。
            // ⚠️ 下面的条件必须与紧随其后的 if/else-if 链一一对应 —— 少一条 ⇒ 该字段的 Header 丢失，
            //    多一条 ⇒ Header 画两遍。新增自定义分支时两处一起改。
            var isCustomBranch = (field.IsAddressable && prop.propertyType == SerializedPropertyType.String)
                                 || (field.IsBehaviourTree && prop.propertyType == SerializedPropertyType.Integer)
                                 || (field.Multiple && field.Type == typeof(string))
                                 || (field.IsLocKey && prop.propertyType == SerializedPropertyType.String)
                                 || (field.IsAudioField && prop.propertyType == SerializedPropertyType.String);
            if (isCustomBranch && !string.IsNullOrEmpty(field.Header))
            {
                // 缩进 16px：与 Unity 自己画的 [Header] 对齐（它默认带 label 缩进），
                // 否则自定义字段的标题会顶格、比其它字段的标题左移一截（用户截图指出的现象）。
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);
                EditorGUILayout.LabelField(field.Header, EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
            }

            if (field.IsAddressable && prop.propertyType == SerializedPropertyType.String)
            {
                DrawAddressableField(prop, field);
            }
            else if (field.IsBehaviourTree && prop.propertyType == SerializedPropertyType.Integer)
            {
                DrawBehaviourTreeField(prop, field);
            }
            else if (field.Multiple && field.Type == typeof(string))
            {
                // 多行字段：label 行也空出 16px 图标位对齐
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);
                EditorGUILayout.LabelField(field.Label);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(prop, GUIContent.none);
            }
            else if (field.IsLocKey && prop.propertyType == SerializedPropertyType.String)
            {
                DrawLocKeyField(prop, field);
            }
            else if (field.IsAudioField && prop.propertyType == SerializedPropertyType.String)
            {
                DrawAudioField(prop, field);
            }
            else
            {
                foreach (var info in field.Infos)
                {
                    if (!string.IsNullOrEmpty(info.Condition) && !CheckVisible(prop.serializedObject.targetObject, info.Condition))
                        continue;
                    EditorGUILayout.HelpBox(info.Message, (MessageType)info.Level);
                }

                if (field.Line)
                {
                    var rect = EditorGUILayout.GetControlRect(true, 2);
                    FillRect(new Rect(rect.x + 18, rect.y + 1, rect.width - 18, 1));
                }

                // 普通字段：前空 16px（LocKey 的图标位）→ label 文本与 LocKey 对齐，
                // label 宽 200 → 输入框与 LocKey 输入框对齐（都从 x=216 起）
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);
                var oldLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 200;
                if (field.Min != null || field.Max != null)
                    DrawRangedValue(prop, field);
                else
                    EditorGUILayout.PropertyField(prop, field.Label);
                EditorGUIUtility.labelWidth = oldLabelWidth;
                EditorGUILayout.EndHorizontal();
            }

            if (field.SpaceAtEnd > 0)
                EditorGUILayout.Space(field.SpaceAtEnd);

            GUI.enabled = enabled;
        }

        private static void DrawRangedValue(SerializedProperty prop, GUITypeField field)
        {
            var type = field.Type;

            if (type == typeof(int))
            {
                var old = prop.intValue;
                var val = EditorGUILayout.IntField(field.Label, old);
                if (val != old)
                {
                    if (field.Min != null && val < field.Min.min) val = (int)field.Min.min;
                    if (field.Max != null && val > field.Max.max) val = (int)field.Max.max;
                    prop.intValue = val;
                }
            }
            else if (type == typeof(float))
            {
                var old = prop.floatValue;
                var val = EditorGUILayout.FloatField(field.Label, old);
                if (val != old)
                {
                    if (field.Min != null && val < field.Min.min) val = field.Min.min;
                    if (field.Max != null && val > field.Max.max) val = field.Max.max;
                    prop.floatValue = val;
                }
            }
            else if (type == typeof(double))
            {
                var old = prop.doubleValue;
                var val = EditorGUILayout.DoubleField(field.Label, old);
                if (val != old)
                {
                    if (field.Min != null && val < field.Min.min) val = field.Min.min;
                    if (field.Max != null && val > field.Max.max) val = field.Max.max;
                    prop.doubleValue = val;
                }
            }
            else if (type == typeof(long))
            {
                var old = prop.longValue;
                var val = EditorGUILayout.LongField(field.Label, old);
                if (val != old)
                {
                    if (field.Min != null && val < field.Min.min) val = (long)field.Min.min;
                    if (field.Max != null && val > field.Max.max) val = (long)field.Max.max;
                    prop.longValue = val;
                }
            }
            else if (type == typeof(short))
            {
                var old = prop.intValue;
                var val = EditorGUILayout.IntField(field.Label, old);
                if (val != old)
                {
                    if (field.Min != null && val < field.Min.min) val = (short)field.Min.min;
                    if (field.Max != null && val > field.Max.max) val = (short)field.Max.max;
                    prop.intValue = val;
                }
            }
            else if (type == typeof(byte))
            {
                var old = prop.intValue;
                var val = EditorGUILayout.IntField(field.Label, old);
                if (val != old)
                {
                    if (field.Min != null && val < field.Min.min) val = (byte)field.Min.min;
                    if (field.Max != null && val > field.Max.max) val = (byte)field.Max.max;
                    prop.intValue = val;
                }
            }
            else
            {
                EditorGUILayout.PropertyField(prop, field.Label);
            }
        }

        /// <summary>联想窗口是否在打开中（用于输入框失焦时关闭）。</summary>
        private static bool sSuggestOpen;

        /// <summary>
        /// 多语言 key 字段专用绘制：输入框 + 联想 + 选择弹窗 + 翻译 tooltip + 即时校验。
        /// loc 包未安装时退化为普通 string 输入。
        /// </summary>
        /// <summary>Addressable 字段：拖入资源自动填它的 AA 地址。</summary>
        private static void DrawAddressableField(SerializedProperty prop, GUITypeField field)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            var oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 200;

            string current = prop.stringValue;
            UnityEngine.Object currentObj = null;
            if (!string.IsNullOrEmpty(current))
            {
                try
                {
                    var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<UnityEngine.Object>(current);
                    currentObj = handle.WaitForCompletion();
                }
                catch { currentObj = null; }
            }

            var newObj = EditorGUILayout.ObjectField(field.Label, currentObj, typeof(UnityEngine.Object), false);
            if (newObj != currentObj)
            {
                if (newObj != null)
                {
                    var address = GetAddressableAddress(newObj);
                    prop.stringValue = string.IsNullOrEmpty(address) ? newObj.name : address;
                }
                else
                {
                    prop.stringValue = "";
                }
            }

            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(current))
                EditorGUILayout.LabelField("地址: " + current, EditorStyles.miniLabel);
        }

        /// <summary>取资源的 Addressable 地址(从 AA 设置查)。未注册返回 null。
        /// 用 <c>includeImplicit</c>：支持把整个文件夹加进 AA 后的子资源（folder entry 的 sub-entry）也能解析到地址。</summary>
        public static string GetAddressableAddress(UnityEngine.Object obj)
        {
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(obj));
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
            {
                var entry = settings.FindAssetEntry(guid, true);
                if (entry != null) return entry.address;
            }
            return null;
        }

        /// <summary>
        /// 框架音频字段（<c>[AudioField]</c>）：<b>单行下拉</b>，选项来自音频总览条目。
        /// 写入带前缀的字符串 key（<c>clip:&lt;AA地址&gt;</c> / <c>wwise:&lt;事件名&gt;</c>），
        /// 前缀决定路由到哪个后端；存字符串而非对象引用才能换后端不改表、且能直接进 Blob。
        ///
        /// ⚠️ 不要改用 <c>ObjectField(typeof(AudioClip))</c>：那是"Unity 原生 AudioClip 字段"的控件，
        /// 空值时 Unity 会显示固定文案 "None (Audio Clip)"，让人误以为本字段存对象引用。
        /// 本字段显示的是总览条目名，与 AudioClip 对象无关。
        /// </summary>
        private static void DrawAudioField(SerializedProperty prop, GUITypeField field)
        {
            var catalog = AudioCatalogAssetLocator.Load();
            var entries = catalog != null ? catalog.entries : null;
            var isEmpty = entries == null || entries.Count == 0;

            var current = prop.stringValue ?? "";

            // 单行下拉：选项来自音频总览条目。显示「分组 | 条目名」，写入带前缀的字符串 key。
            // 刻意不用 ObjectField(AudioClip) —— 那是"Unity 原生 AudioClip 字段"的控件，
            // 会在空值时显示 Unity 固定文案 "None (Audio Clip)"，让人误以为本字段存对象引用。
            // 本字段（框架/音频）存的是 key，显示的是条目名，两者不该混淆。
            var labels = new System.Collections.Generic.List<string>();
            var values = new System.Collections.Generic.List<string>();
            // 空总览时把提示塞进下拉本身（不另起 HelpBox），保证控件严格只有一行。
            labels.Add(isEmpty ? "(总览暂无条目，请先在 Audio Manager 添加)" : "(未选择)");
            values.Add("");
            if (!isEmpty)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    var wantWwise = pass == 1;
                    for (int e = 0; e < entries.Count; e++)
                    {
                        var entry = entries[e];
                        if (entry == null) continue;
                        var target = wantWwise ? entry.wwiseEvent : entry.addressableKey;
                        if (string.IsNullOrEmpty(target)) continue;
                        var name = string.IsNullOrEmpty(entry.displayName) ? target : entry.displayName;
                        // 名字可被用户改名，含半角 '/' 时 EditorGUILayout.Popup 会把它当子菜单分隔符 → 显示成两级。用全角斜杠规避。
                        labels.Add($"{(wantWwise ? "Wwise" : "Clip")} | {entry.channel} | {name.Replace('/', '／')}");
                        values.Add(AudioRefPrefix.Wrap(target, wantWwise));
                    }
                }
            }

            var cur = values.IndexOf(current);
            if (cur < 0)
            {
                // 当前值不在总览里（手填或条目被删）：补占位项，绝不静默显示成第一项。
                // 地址本身含 '/'（Assets/... 路径），同样要规避 Popup 的子菜单分隔。
                labels.Add($"⚠ 未登记 | {current.Replace('/', '／')}");
                values.Add(current);
                cur = values.Count - 1;
            }

            // 与其它字段统一布局：16px 缩进 + 200px 标签列 → label 列与输入框两列都对齐。
            // 普通字段走 DrawFieldGUI 的 else 分支；Addressable / BehaviourTree 也各自套了这层。
            // 之前这里直接 Popup(field.Label,...) → label 顶格、下拉起点也不对，故与上方错开。
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            var oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 200;
            var picked = EditorGUILayout.Popup(field.Label, cur, labels.ToArray());
            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUILayout.EndHorizontal();

            if (picked >= 0 && picked < values.Count && values[picked] != prop.stringValue)
                prop.stringValue = values[picked];
        }

        /// <summary>行为树字段：列出项目内所有 BTTreeAsset, 选择后写入 TreeId。</summary>
        private static void DrawBehaviourTreeField(SerializedProperty prop, GUITypeField field)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            var oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 200;

            long current = prop.longValue;
            var allTrees = AssetDatabase.FindAssets("t:HYC.Framework.BT.Editor.BTTreeAsset");
            var names = new System.Collections.Generic.List<string> { "(无)" };
            var ids = new System.Collections.Generic.List<long> { 0 };
            foreach (var g in allTrees)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var tree = AssetDatabase.LoadAssetAtPath<HYC.Framework.BT.Editor.BTTreeAsset>(path);
                if (tree == null) continue;
                names.Add($"{tree.TreeId}: {tree.name}");
                ids.Add(tree.TreeId);
            }

            int idx = names.Count > 0 ? ids.IndexOf(current) : 0;
            if (idx < 0) idx = 0;
            int chosen = EditorGUILayout.Popup(field.Label, idx, names.ToArray());
            if (chosen >= 0 && chosen < ids.Count && ids[chosen] != current)
                prop.longValue = ids[chosen];

            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawLocKeyField(SerializedProperty prop, GUITypeField field)
        {
            // 输入框失焦 → 关闭联想窗口
            if (EditorGUIUtility.editingTextField)
                sSuggestOpen = true;
            else if (sSuggestOpen)
            {
                sSuggestOpen = false;
                LocKeySuggestWindow.CloseAll();
            }

            var lineRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight + 4);

            // 统一布局：[ⓘ 16px][label 200px][输入框][▾]
            // 与普通字段（labelWidth=216）输入框对齐；无图标时 16px 前缀留空
            var text = prop.stringValue ?? "";
            var translation = LocAccess.GetText(text);
            var hasKey = LocAccess.HasKey(text);
            var tip = string.IsNullOrEmpty(text)
                ? "多语言 key"
                : !LocAccess.IsLocInstalled
                    ? "loc 包未安装，无法校验"
                    : hasKey
                        ? translation ?? "(当前语言无翻译)"
                        : "key 不存在";
            const float iconW = 16f;
            const float labelW = 200f;
            const float btnW = 22f;

            // 小叹号图标（label 前）：引导悬停查看翻译 tooltip
            var iconRect = new Rect(lineRect.x, lineRect.y, iconW, lineRect.height);
            var icon = EditorGUIUtility.IconContent("console.infoicon.sml");
            if (icon != null)
            {
                icon.tooltip = "鼠标悬停查看当前语言翻译：" + tip;
                GUI.Label(iconRect, icon);
            }

            var labelRect = new Rect(lineRect.x + iconW, lineRect.y, labelW, lineRect.height);
            GUI.Label(labelRect, new GUIContent(field.Label.text, tip), EditorStyles.label);

            var inputRect = new Rect(labelRect.xMax + 2, lineRect.y,
                lineRect.width - iconW - labelW - btnW - 4, lineRect.height);
            var pickRect = new Rect(inputRect.xMax + 2, lineRect.y, btnW, lineRect.height);

            // 输入框（联想）
            var old = text;
            var newText = EditorGUI.TextField(inputRect, old);
            if (newText != old)
            {
                prop.stringValue = newText;
                prop.serializedObject.ApplyModifiedProperties();
            }

            // 输入变化 → 打开/刷新联想
            if (GUI.changed && newText != old)
            {
                if (string.IsNullOrEmpty(newText.Trim()))
                    LocKeySuggestWindow.CloseAll();
                else
                    LocKeySuggestWindow.Open(GUIUtility.GUIToScreenRect(new Rect(inputRect.x, inputRect.yMax, inputRect.width, 20)),
                        newText, picked =>
                        {
                            prop.stringValue = picked;
                            prop.serializedObject.ApplyModifiedProperties();
                        });
            }

            // 弹窗选择按钮（折叠多级菜单，含"搜索 key…"入口）
            if (GUI.Button(pickRect, "\u25BE"))
            {
                LocKeySuggestWindow.CloseAll();
                LocKeyPickerWindow.OpenFoldMenu(GUIUtility.GUIToScreenRect(pickRect), text, picked =>
                {
                    prop.stringValue = picked;
                    prop.serializedObject.ApplyModifiedProperties();
                });
            }

            // 即时校验提示
            if (!string.IsNullOrEmpty(text) && LocAccess.IsLocInstalled && !hasKey)
            {
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 0.45f, 0.45f);
                var errRect = new Rect(inputRect.x, lineRect.yMax, inputRect.width, 16);
                GUI.Label(errRect, "⚠ key 不存在", EditorStyles.miniLabel);
                GUI.color = oldColor;
            }
            else if (field.SpaceAtEnd > 0)
            {
                EditorGUILayout.Space(field.SpaceAtEnd - 4 > 0 ? field.SpaceAtEnd - 4 : 0);
            }
        }

        public static Vector2 SlideRect(Rect rect, MouseCursor cursor = MouseCursor.SlideArrow)
        {
            if (!GUI.enabled)
                return Vector2.zero;

            EditorGUIUtility.AddCursorRect(rect, cursor);
            var controlID = GUIUtility.GetControlID(FocusType.Passive);
            if (GUI.enabled && Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
            {
                GUIUtility.hotControl = controlID;
                EditorGUIUtility.SetWantsMouseJumping(1);
                Event.current.Use();
            }
            else if (GUIUtility.hotControl == controlID)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    Event.current.Use();
                    GUI.changed = true;
                    return Event.current.delta;
                }

                if (Event.current.type == EventType.MouseUp)
                {
                    GUIUtility.hotControl = 0;
                    EditorGUIUtility.SetWantsMouseJumping(0);
                    Event.current.Use();
                }
            }

            return Vector2.zero;
        }
    }
}
