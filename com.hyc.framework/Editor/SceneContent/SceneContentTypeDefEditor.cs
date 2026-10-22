using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 场景内容类型定义的自定义 Inspector。
    /// <para>
    /// 三块内容：基本信息 / 字段列表 / 关卡 prefab 扫描。动作按钮：保存、生成代码、校验、反填字段。
    /// </para>
    /// <para>
    /// <b>不用数据编辑器的 <c>ConfigDataEditor</c> 面板机制</b>：定义资产刻意不进配置数据树
    /// （见 <see cref="SceneContentTypeDef"/> 注释），所以直接挂普通 Inspector，
    /// 资产放哪都能用，与配置模块零耦合。
    /// </para>
    /// <para>
    /// IMGUI 约定：每帧控件集合固定——枚举引用字段始终绘制，非枚举类型时只置灰（<see cref="EditorGUI.BeginDisabledGroup"/>），
    /// 不做「按类型增删控件」，避免布局阶段与绘制阶段控件错位。
    /// </para>
    /// </summary>
    [CustomEditor(typeof(SceneContentTypeDef))]
    public class SceneContentTypeDefEditor : UnityEditor.Editor
    {
        private SerializedProperty _displayName;
        private SerializedProperty _className;
        private SerializedProperty _scanPrefab;
        private SerializedProperty _fields;
        private ReorderableList _fieldList;

        private string _message;
        private bool _messageIsError;

        private List<SceneContentMarkerInfo> _scanResults;
        private GameObject _scannedPrefab;

        private void OnEnable()
        {
            _displayName = serializedObject.FindProperty("displayName");
            _className = serializedObject.FindProperty("className");
            _scanPrefab = serializedObject.FindProperty("scanPrefab");
            RebuildFieldList();
        }

        /// <summary>重建字段列表（数组结构变更后必须重建：旧的 SerializedProperty 会失效）。</summary>
        private void RebuildFieldList()
        {
            _fields = serializedObject.FindProperty("fields");
            _fieldList = new ReorderableList(serializedObject, _fields, true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r, $"字段（{_fields.arraySize}）"),
                elementHeight = 84f,
            };
            _fieldList.drawElementCallback = DrawFieldElement;
        }

        private void DrawFieldElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var el = _fields.GetArrayElementAtIndex(index);
            var pName = el.FindPropertyRelative("name");
            var pType = el.FindPropertyRelative("type");
            var pList = el.FindPropertyRelative("isList");
            var pDesc = el.FindPropertyRelative("description");
            var pEnum = el.FindPropertyRelative("enumRefClassName");
            var pClient = el.FindPropertyRelative("clientExport")?.FindPropertyRelative("mode");
            var pServer = el.FindPropertyRelative("serverExport")?.FindPropertyRelative("mode");

            var w = rect.width;

            // 第 1 行：字段名 | 类型 | 列表
            EditorGUI.PropertyField(new Rect(rect.x, rect.y + 2, w * 0.40f - 4, 18), pName, GUIContent.none);
            EditorGUI.PropertyField(new Rect(rect.x + w * 0.40f, rect.y + 2, w * 0.36f - 4, 18), pType, GUIContent.none);
            EditorGUI.PropertyField(new Rect(rect.x + w * 0.76f, rect.y + 2, w * 0.24f - 2, 18), pList, new GUIContent("列表"));

            // 第 2 行：说明
            EditorGUI.PropertyField(new Rect(rect.x, rect.y + 22, w - 2, 18), pDesc, new GUIContent("说明"));

            // 第 3 行：两侧导出方式（数据被打坏时跳过绘制，不让整个面板崩掉）
            if (pClient != null)
                EditorGUI.PropertyField(new Rect(rect.x, rect.y + 42, w * 0.5f - 4, 18), pClient, new GUIContent("客户端"));
            if (pServer != null)
                EditorGUI.PropertyField(new Rect(rect.x + w * 0.5f, rect.y + 42, w * 0.5f - 2, 18), pServer, new GUIContent("服务端"));

            // 第 4 行：枚举引用（仅 Enum 类型可用，其余置灰但保持绘制）
            var isEnum = pType.intValue == (int)SceneContentFieldType.Enum;
            EditorGUI.BeginDisabledGroup(!isEnum);
            EditorGUI.PropertyField(new Rect(rect.x, rect.y + 62, w - 2, 18), pEnum, new GUIContent("枚举定义"));
            EditorGUI.EndDisabledGroup();
        }

        public override void OnInspectorGUI()
        {
            if (serializedObject == null || serializedObject.targetObject == null)
                return;

            serializedObject.Update();

            DrawHeader();
            DrawBasicInfo();
            DrawFieldList();
            DrawScanSection();
            DrawActions();

            var changed = GUI.changed;
            serializedObject.ApplyModifiedProperties();
            if (changed && serializedObject.targetObject != null)
                EditorUtility.SetDirty(serializedObject.targetObject);
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
            EditorGUILayout.PropertyField(_scanPrefab, new GUIContent("扫描 prefab"));
            EditorGUILayout.Space(4);
        }

        private void DrawFieldList()
        {
            _fieldList.DoLayoutList();
            EditorGUILayout.Space(4);
        }

        private void DrawScanSection()
        {
            var prefab = _scanPrefab.objectReferenceValue as GameObject;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("关卡扫描", EditorStyles.boldLabel, GUILayout.Width(60));
                EditorGUI.BeginDisabledGroup(prefab == null);
                if (GUILayout.Button("扫描", EditorStyles.miniButton, GUILayout.Width(56)))
                    Scan(prefab);
                EditorGUI.EndDisabledGroup();
                GUILayout.FlexibleSpace();
            }

            if (prefab == null)
            {
                EditorGUILayout.LabelField("把关卡 prefab 拖到「扫描 prefab」上，可列出它挂了哪些场景内容。",
                    EditorStyles.miniLabel);
                return;
            }

            if (_scanResults == null || _scannedPrefab != prefab)
            {
                EditorGUILayout.LabelField("点「扫描」读取该 prefab 上的标记组件。", EditorStyles.miniLabel);
                return;
            }

            if (_scanResults.Count == 0)
            {
                EditorGUILayout.LabelField("该 prefab 上没有任何场景内容标记组件。", EditorStyles.miniLabel);
                return;
            }

            var def = (SceneContentTypeDef)serializedObject.targetObject;
            foreach (var info in _scanResults)
            {
                var mark = info.MatchesTarget ? "★ " : "· ";
                EditorGUILayout.LabelField(
                    $"{mark}{info.TypeName}  id={info.ContentTypeId}  路径={(string.IsNullOrEmpty(info.Path) ? "(根)" : info.Path)}",
                    EditorStyles.miniLabel);
            }

            var hasMatch = _scanResults.Exists(i => i.MatchesTarget);
            EditorGUI.BeginDisabledGroup(!hasMatch);
            if (GUILayout.Button("按本类型的组件反填字段", EditorStyles.miniButton))
                ReverseFillFields(def, _scanResults.Find(i => i.MatchesTarget));
            EditorGUI.EndDisabledGroup();

            if (!hasMatch)
            {
                EditorGUILayout.LabelField(
                    $"该 prefab 上没有本类型（id={def.ContentTypeId}）的组件；先「生成代码」再把组件挂上去。",
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.Space(4);
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("保存", GUILayout.Width(72)))
                {
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(serializedObject.targetObject);
                    AssetDatabase.SaveAssetIfDirty(serializedObject.targetObject);
                    _message = "已保存。";
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

        private void Scan(GameObject prefab)
        {
            var def = (SceneContentTypeDef)serializedObject.targetObject;
            _scanResults = SceneContentPrefabScanner.Scan(prefab, def.ContentTypeId);
            _scannedPrefab = prefab;
        }

        private void ReverseFillFields(SceneContentTypeDef def, SceneContentMarkerInfo info)
        {
            if (info == null || info.Component == null)
                return;

            var newFields = SceneContentPrefabScanner.BuildFieldDefs(info.Component, out var warnings);
            if (newFields.Count == 0)
            {
                _message = $"组件 {info.TypeName} 上没有可映射的公开字段。" +
                           (warnings.Count > 0 ? "\n" + string.Join("\n", warnings) : "");
                _messageIsError = true;
                return;
            }

            if (def.fields != null && def.fields.Count > 0)
            {
                if (!EditorUtility.DisplayDialog("反填字段",
                        $"将用组件 {info.TypeName} 的 {newFields.Count} 个公开字段替换现有 {def.fields.Count} 个字段定义，继续？",
                        "替换", "取消"))
                    return;
            }

            serializedObject.ApplyModifiedProperties();
            def.fields = newFields;
            EditorUtility.SetDirty(def);
            serializedObject.Update();
            RebuildFieldList();

            _message = $"已反填 {newFields.Count} 个字段。" +
                       (warnings.Count > 0 ? "\n注意：\n" + string.Join("\n", warnings) : "");
            _messageIsError = warnings.Count > 0;
        }

        private void GenerateCode()
        {
            serializedObject.ApplyModifiedProperties();
            var def = (SceneContentTypeDef)serializedObject.targetObject;
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
            _message = SceneContentTypeCodeGen.Validate(def, out var error) ? "校验通过。" : "校验失败：" + error;
            _messageIsError = !string.IsNullOrEmpty(error);
        }
    }
}
