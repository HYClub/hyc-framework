using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 枚举定义资产编辑器：显示名/枚举名/类型(普通|复合)/导出目标/值列表（名称+描述，值自动算）。
    /// 提供"生成代码"与"导出"按钮。
    /// </summary>
    [CfgEditor(typeof(ConfigEnumDefinition))]
    public class ConfigEnumEditor : ConfigDataEditor
    {
        private SerializedProperty mDisplayName;
        private SerializedProperty mClassName;
        private SerializedProperty mIsFlags;
        private SerializedProperty mExportTarget;
        private SerializedProperty mValues;
        private Vector2 mScroll;

        protected override void Init()
        {
            mDisplayName = mTarget.FindProperty("displayName");
            mClassName = mTarget.FindProperty("className");
            mIsFlags = mTarget.FindProperty("isFlags");
            mExportTarget = mTarget.FindProperty("exportTarget");
            mValues = mTarget.FindProperty("values");
        }

        public override void OnGUI(float viewW, float viewH)
        {
            if (mTarget == null)
                return;
            mTarget.Update();

            var def = mTarget.targetObject as ConfigEnumDefinition;
            var title = def != null ? $"{def.displayName}-{def.className}" : "枚举定义";
            GUILayout.BeginArea(new Rect(6, 4, viewW - 12, 20));
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUILayout.EndArea();
            GUIDrawer.FillRect(new Rect(1, 26, viewW - 2, 1));

            GUILayout.BeginArea(new Rect(6, 32, viewW - 12, viewH - 38));
            mScroll = EditorGUILayout.BeginScrollView(mScroll);

            EditorGUILayout.PropertyField(mDisplayName, new GUIContent("显示名（分类/名称）"));
            EditorGUILayout.PropertyField(mClassName, new GUIContent("枚举名（C# 类名）"));
            EditorGUILayout.PropertyField(mIsFlags, new GUIContent("复合枚举（1,2,4,8...）"));

            // 导出目标
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("导出", GUILayout.Width(120));
            var options = new[] { "客户端", "服务器", "两者" };
            var old = mExportTarget.intValue;
            var idx = EditorGUILayout.Popup(old, options);
            if (idx != old)
            {
                mExportTarget.intValue = idx;
                mTarget.ApplyModifiedProperties();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("值列表（名称+描述，数值自动分配）", EditorStyles.boldLabel);
            var isFlags = mIsFlags.boolValue;
            var label = isFlags ? "数值" : "值";

            // 表头
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(50));
            EditorGUILayout.LabelField("名称", GUILayout.Width(150));
            EditorGUILayout.LabelField("描述");
            if (GUILayout.Button("+", GUILayout.Width(24)))
            {
                mValues.InsertArrayElementAtIndex(mValues.arraySize);
                var last = mValues.GetArrayElementAtIndex(mValues.arraySize - 1);
                last.FindPropertyRelative("name").stringValue = $"Value{mValues.arraySize}";
                last.FindPropertyRelative("description").stringValue = "";
                mTarget.ApplyModifiedProperties();
            }
            EditorGUILayout.EndHorizontal();

            var removeIndex = -1;
            for (var i = 0; i < mValues.arraySize; i++)
            {
                var element = mValues.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                // 自动算出的值（只读展示）
                var autoValue = ConfigEnumDefinition.ValueOf(def, i);
                EditorGUILayout.LabelField(autoValue.ToString(), GUILayout.Width(50));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("name"), GUIContent.none, GUILayout.Width(150));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("description"), GUIContent.none);
                if (GUILayout.Button("-", GUILayout.Width(24)))
                    removeIndex = i;
                EditorGUILayout.EndHorizontal();
            }
            if (removeIndex >= 0)
            {
                mValues.DeleteArrayElementAtIndex(removeIndex);
                mTarget.ApplyModifiedProperties();
            }

            if (mValues.arraySize == 0)
                EditorGUILayout.HelpBox("还没有值，点击 + 添加", MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存", GUILayout.Width(60)))
            {
                mTarget.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log($"[枚举] 已保存 {def.className}");
            }
            if (GUILayout.Button("生成代码", GUILayout.Width(90)))
            {
                // 先保存资产，避免"改了没保存就生成"的困惑
                mTarget.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                if (ConfigEnumCodeGen.WriteFile(def, out var err))
                    Debug.Log($"[枚举] 已生成 {def.className} 代码");
                else
                    EditorUtility.DisplayDialog("生成失败", err, "确定");
            }
            if (GUILayout.Button("导出", GUILayout.Width(90)))
            {
                mTarget.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                if (ConfigEnumCodeGen.Export(def, true, true))
                    Debug.Log($"[枚举] 已导出 {def.className} 到客户端/服务器目录");
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 校验提示
            if (def != null && !ConfigEnumCodeGen.Validate(def, out var verr))
                EditorGUILayout.HelpBox(verr, MessageType.Error);
            else if (def != null && EditorUtility.IsDirty(def))
                EditorGUILayout.HelpBox("有未保存的修改，请点击\u201c保存\u201d或按 Ctrl+S", MessageType.Warning);
            else if (def != null && !ConfigEnumCodeGen.IsGenerated(def))
                EditorGUILayout.HelpBox("尚未生成代码，请点击上方按钮", MessageType.Warning);

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            mTarget.ApplyModifiedProperties();
        }
    }
}
