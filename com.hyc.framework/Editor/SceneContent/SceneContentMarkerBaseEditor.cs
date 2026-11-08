using System.Collections.Generic;
using System.Reflection;
using HYC.Framework.Config;
using HYC.Framework.SceneContent;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 全部生成标记组件共用的 Inspector。
    /// <para>
    /// targetType 取公共基类 <see cref="SceneContentMarkerBase"/> 且 editorForChildClasses = true，
    /// 于是所有生成的 <c>&lt;类名&gt;.cs</c> 自动共用本编辑器（Unity 官方同类做法：Timeline
    /// <c>MarkerInspector</c> / Cinemachine <c>CinemachineComponentBaseEditor</c>）。
    /// </para>
    /// <para>
    /// 本面板只做「只读信息 + 跳回类型设计」，不在组件上就地编辑类型 / 派生内定参数：
    /// 派生内定参数（localPos/worldPos/path/localEuler）由 transform 与关卡根现场算，按 built-in 名
    /// 在可编辑区跳过、只在下方只读区块出现一次；用户每实例字段仍可在原生区域填写
    /// （受生成代码上的 <c>[ReadOnly]</c> / <c>[CfgVisibleWhen]</c> 约束）。
    /// 真正改「类型 / 字段规则」请点「在 Scene Content 编辑器中打开」回到窗口。
    /// </para>
    /// </summary>
    [CustomEditor(typeof(SceneContentMarkerBase), true)]
    public class SceneContentMarkerBaseEditor : UnityEditor.Editor
    {
        private bool _showFields = true;
        private bool _showBuiltIn;
        private bool _showPayload;

        // Inspector 每帧重绘，按内容 id 缓存反查结果，避免每帧 FindAssets 全工程扫。
        // 找不到时不写缓存，新建定义资产后下一帧会重试；资产被删时 Unity 伪 null 判定会自动失效。
        private static readonly Dictionary<int, SceneContentTypeDef> DefCache =
            new Dictionary<int, SceneContentTypeDef>();

        private static readonly Color Accent = new Color(0.35f, 0.55f, 0.95f);

        private static GUIStyle _headerTitle;
        private static GUIStyle HeaderTitle => _headerTitle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
        };

        private static GUIStyle _metaLabel;
        private static GUIStyle MetaLabel => _metaLabel ??= new GUIStyle(EditorStyles.miniLabel)
        {
            padding = new RectOffset(2, 2, 0, 0),
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (!(target is ISceneContentMarker marker) || !(target is SceneContentMarkerBase component))
                return;

            var typeName = target.GetType().Name;
            var def = ResolveDef(marker.ContentTypeId, typeName, out var idMismatch);

            // —— 原生每实例字段（内定参数在此跳过，只出现在下方只读区块）——
            DrawInstanceFields();

            // —— 头部信息卡 ——
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(typeName, HeaderTitle);
            EditorGUILayout.LabelField("由 Scene Content 生成器生成 · 请勿手改", MetaLabel);
            DrawSeparator();

            // —— 主操作：跳回窗口并选中类型定义 ——
            using (new EditorGUILayout.HorizontalScope())
            {
                var old = GUI.backgroundColor;
                GUI.backgroundColor = Accent;
                using (new EditorGUI.DisabledScope(def == null))
                    if (GUILayout.Button("在 Scene Content 编辑器中打开", GUILayout.Height(26)))
                        SceneContentWindow.OpenAndSelect(def);
                GUI.backgroundColor = old;
            }

            // —— 元信息（轻量）——
            if (def != null)
                EditorGUILayout.LabelField("类型定义", def.displayName, MetaLabel);
            EditorGUILayout.LabelField("内容 id", marker.ContentTypeId.ToString(), MetaLabel);

            if (idMismatch)
                EditorGUILayout.HelpBox(
                    "生成代码与类型定义不同步（内容 id 对不上）：改过类名或定义后没重新生成代码，请点上方按钮回到窗口点「生成全部代码」。",
                    MessageType.Warning);
            else if (def == null)
                EditorGUILayout.HelpBox(
                    $"未找到内容 id {marker.ContentTypeId} 对应的类型定义资产，可能已被删除。",
                    MessageType.Warning);

            DrawFields(def);
            DrawBuiltIn(component);
            DrawPayload(component);

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawSeparator()
        {
            var r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.25f));
        }

        /// <summary>
        /// 逐字段绘制用户每实例字段：消费生成器打上的 [ReadOnly] / [CfgVisibleWhen]，与配置数据编辑器
        /// 同一套语义（AND 求值，目标字段缺失或类型不符视为满足，绝不把字段吞掉）。只读只禁用输入，不影响导出。
        /// 内定参数（built-in）按名跳过——它们由 transform 现场算，不在此处编辑、不重复出现。
        /// </summary>
        private void DrawInstanceFields()
        {
            var targetType = target.GetType();
            var prop = serializedObject.GetIterator();
            if (!prop.NextVisible(true))
                return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            do
            {
                if (prop.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(prop, true);
                    continue;
                }

                if (IsBuiltIn(prop.propertyPath))
                    continue;

                var field = targetType.GetField(prop.propertyPath, flags);
                if (field == null)
                {
                    EditorGUILayout.PropertyField(prop, true);
                    continue;
                }

                var visible = true;
                foreach (var raw in field.GetCustomAttributes(typeof(CfgVisibleWhenAttribute), true))
                {
                    if (!CheckCondition(target, raw as CfgVisibleWhenAttribute))
                    {
                        visible = false;
                        break;
                    }
                }
                if (!visible)
                    continue;

                var readOnly = field.GetCustomAttributes(typeof(ReadOnlyAttribute), true).Length > 0;
                using (new EditorGUI.DisabledScope(readOnly))
                    EditorGUILayout.PropertyField(prop, true);
            }
            while (prop.NextVisible(false));
        }

        private static bool IsBuiltIn(string name)
        {
            foreach (var n in SceneContentTypeCodeGen.BuiltInParameterNames)
                if (string.Equals(n, name, System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>单条条件求值：bool 比值、enum 比值名；目标丢失 / 类型不符视为满足。</summary>
        private static bool CheckCondition(object target, CfgVisibleWhenAttribute cond)
        {
            if (cond == null || string.IsNullOrEmpty(cond.FieldName))
                return true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = target.GetType().GetField(cond.FieldName, flags);
            if (field == null)
                return true;
            var value = field.GetValue(target);
            if (field.FieldType == typeof(bool))
                return value is bool b && b == cond.BoolValue;
            if (field.FieldType.IsEnum)
                return cond.MatchesEnum(value == null ? "" : value.ToString());
            return true;
        }

        // ---------------------------------------------------------------- 参数清单

        private void DrawFields(SceneContentTypeDef def)
        {
            var fields = def != null ? def.fields : null;
            var count = fields != null ? fields.Count : 0;
            _showFields = EditorGUILayout.Foldout(_showFields, $"字段定义（{count}）· 上方可填");
            if (!_showFields)
                return;
            if (count == 0)
            {
                EditorGUILayout.LabelField("  （没有用户字段，只导出内定参数）", EditorStyles.miniLabel);
                return;
            }
            foreach (var f in fields)
            {
                if (f == null)
                    continue;
                var desc = string.IsNullOrWhiteSpace(f.description) ? "" : $"   （{f.description}）";
                EditorGUILayout.LabelField(
                    $"  • {f.name} : {FieldTypeText(f)}   客户端 {SideText(f.clientExport)} / 服务端 {SideText(f.serverExport)}{desc}",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawBuiltIn(SceneContentMarkerBase component)
        {
            var names = SceneContentTypeCodeGen.BuiltInParameterNames;
            _showBuiltIn = EditorGUILayout.Foldout(_showBuiltIn,
                $"内定参数（不填 · 导出时自动带上）· {names.Length}");
            if (!_showBuiltIn)
                return;

            foreach (var n in names)
                EditorGUILayout.LabelField($"  • {n} — {BuiltInHint(n)}", EditorStyles.miniLabel);

            var t = component.transform;
            var root = component.PreviewLevelRoot;
            var path = root != null ? SceneContentRefPath.GetPath(root, t) : null;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector3Field("localPos 相对", t.localPosition);
                EditorGUILayout.Vector3Field("worldPos 绝对", t.position);
                EditorGUILayout.TextField("path", path ?? "(无关卡根)");
                EditorGUILayout.Vector3Field("localEuler 本地欧拉", t.localEulerAngles);
            }
        }

        private void DrawPayload(SceneContentMarkerBase component)
        {
            _showPayload = EditorGUILayout.Foldout(_showPayload, "客户端 payload 预览");
            if (!_showPayload)
                return;
            if (!(target is ISceneContentMarkerPayload payload))
            {
                EditorGUILayout.LabelField("  （未实现 ISceneContentMarkerPayload）", EditorStyles.miniLabel);
                return;
            }
            string json;
            try
            {
                json = payload.ToPayloadJson(component.PreviewLevelRoot, SceneContentSide.Client);
            }
            catch (System.Exception e)
            {
                json = "payload 预览失败：" + e.Message;
            }
            EditorGUILayout.SelectableLabel(json, EditorStyles.textArea, GUILayout.MinHeight(56));
        }

        // ---------------------------------------------------------------- 反查与格式化

        private static SceneContentTypeDef ResolveDef(int id, string typeName, out bool idMismatch)
        {
            idMismatch = false;
            if (DefCache.TryGetValue(id, out var cached) && cached != null)
                return cached;

            var all = SceneContentTypeCodeGen.LoadAllTypes();
            SceneContentTypeDef found = null;
            foreach (var d in all)
            {
                if (d != null && d.ContentTypeId == id)
                {
                    found = d;
                    break;
                }
            }
            if (found == null)
            {
                foreach (var d in all)
                {
                    if (d != null && d.className == typeName)
                    {
                        found = d;
                        idMismatch = true;
                        break;
                    }
                }
            }
            // 只在 id 一致时缓存；id 对不上是「代码没重新生成」的临时状态，下帧要能自愈
            if (found != null && !idMismatch)
                DefCache[id] = found;
            return found;
        }

        private static string FieldTypeText(SceneContentFieldDef f)
        {
            var extra = !string.IsNullOrEmpty(f.enumRefClassName)
                ? f.enumRefClassName
                : (!string.IsNullOrEmpty(f.refTypeFullName) ? ShortTypeName(f.refTypeFullName) : null);
            var type = extra ?? f.type.ToString();
            return f.isList ? $"List<{type}>" : type;
        }

        private static string ShortTypeName(string full)
        {
            var comma = full.LastIndexOf(',');
            var name = comma >= 0 ? full.Substring(0, comma) : full;
            var dot = name.LastIndexOf('.');
            return dot >= 0 && dot + 1 < name.Length ? name.Substring(dot + 1) : name;
        }

        private static string SideText(SceneContentFieldExport export)
            => export == null ? "—" : (export.IsExported ? export.mode.ToString() : "不导出（隐藏）");

        private static string BuiltInHint(string name)
        {
            switch (name)
            {
                case "localPos": return "相对关卡根的本地坐标 [x,y,z]";
                case "worldPos": return "世界坐标 [x,y,z]";
                case "path": return "相对关卡根的层级路径";
                case "localEuler": return "本地欧拉角 [x,y,z]";
                default: return "自动导出";
            }
        }
    }
}
