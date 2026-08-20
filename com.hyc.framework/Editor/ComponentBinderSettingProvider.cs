using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.UI.Editor
{
    /// <summary>
    /// Project settings page for the UI binder generator
    /// (Project Settings → HYC Framework → UI Binder).
    /// </summary>
    public sealed class ComponentBinderSettingProvider : SettingsProvider
    {
        private static readonly Color RedColor = new Color(.9f, .7f, .7f);
        private readonly GUIContent[] _searchItems = { new("Entire project"), new("Specific folder") };
        private readonly GUIContent[] _outputItems = { new("Into project"), new("External path") };

        private SerializedObject _target;
        private SerializedProperty _findAll;
        private SerializedProperty _findFolder;
        private SerializedProperty _outputMethod;
        private SerializedProperty _outputFolder;
        private SerializedProperty _outputPath;
        private SerializedProperty _outputPackage;
        private SerializedProperty _prefix;
        private SerializedProperty _suffix;

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new ComponentBinderSettingProvider("Project/HYC Framework/UI Binder", SettingsScope.Project,
                new[] { "binder", "ui", "绑定" });
        }

        public ComponentBinderSettingProvider(string path, SettingsScope scope, IEnumerable<string> keywords)
            : base(path, scope, keywords) { }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement root)
        {
            _target = new SerializedObject(ComponentBinderSetting.instance);
            _findAll = _target.FindProperty(nameof(ComponentBinderSetting.FindAllPrefab));
            _findFolder = _target.FindProperty(nameof(ComponentBinderSetting.PrefabFolder));
            _outputMethod = _target.FindProperty(nameof(ComponentBinderSetting.CodeOutputMethod));
            _outputFolder = _target.FindProperty(nameof(ComponentBinderSetting.CodeOutputFolder));
            _outputPath = _target.FindProperty(nameof(ComponentBinderSetting.CodeOutputFolderPath));
            _outputPackage = _target.FindProperty(nameof(ComponentBinderSetting.CodeOutputPackageName));
            _prefix = _target.FindProperty(nameof(ComponentBinderSetting.ClassNamePrefix));
            _suffix = _target.FindProperty(nameof(ComponentBinderSetting.ClassNameSuffix));
        }

        public override void OnGUI(string searchContext)
        {
            var defColor = GUI.color;
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.Space();
            _findAll.boolValue = EditorGUILayout.Popup(new GUIContent("Search scope"), _findAll.boolValue ? 0 : 1, _searchItems) == 0;
            if (!_findAll.boolValue)
            {
                GUI.color = _findFolder.objectReferenceValue == null ? RedColor : defColor;
                EditorGUILayout.PropertyField(_findFolder, new GUIContent("Search folder"));
                GUI.color = defColor;
            }

            EditorGUILayout.Space();
            _outputMethod.boolValue = EditorGUILayout.Popup(new GUIContent("Output to"), _outputMethod.boolValue ? 0 : 1, _outputItems) == 0;
            if (_outputMethod.boolValue)
            {
                GUI.color = _outputFolder.objectReferenceValue == null ? RedColor : defColor;
                EditorGUILayout.PropertyField(_outputFolder, new GUIContent("Output folder"));
                GUI.color = defColor;
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Output folder", _outputPath.stringValue, GUI.skin.textField, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("...", GUILayout.ExpandWidth(false)))
                {
                    var picked = EditorUtility.SaveFolderPanel("Pick output folder", _outputPath.stringValue, "");
                    if (!string.IsNullOrEmpty(picked)) _outputPath.stringValue = picked;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            GUI.color = ComponentBinderCodeGenerator.IsPackageName(_outputPackage.stringValue) ? defColor : RedColor;
            EditorGUILayout.PropertyField(_outputPackage, new GUIContent("Namespace"));
            GUI.color = defColor;

            DrawNameField(_prefix, "Class name prefix");
            DrawNameField(_suffix, "Class name suffix");

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate all"))
            {
                ComponentBinderCodeGenerator.GenerateAll();
            }
            EditorGUILayout.Space();

            EditorGUI.indentLevel--;
            if (EditorGUI.EndChangeCheck())
            {
                _target.ApplyModifiedProperties();
                ComponentBinderSetting.Save();
            }
        }

        private void DrawNameField(SerializedProperty prop, string label)
        {
            var defColor = GUI.color;
            var value = prop.stringValue?.Trim();
            GUI.color = string.IsNullOrEmpty(value) || ComponentBinderCodeGenerator.IsVariableName(value) ? defColor : RedColor;
            EditorGUILayout.PropertyField(prop, new GUIContent(label));
            GUI.color = defColor;
        }
    }
}