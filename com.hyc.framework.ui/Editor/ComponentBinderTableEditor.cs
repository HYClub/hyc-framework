using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;

namespace HYC.Framework.UI.Editor
{
    /// <summary>
    /// Inspector for <see cref="ComponentBinderTable"/>: a reorderable field list
    /// (name / node / component / description), duplicate &amp; invalid-name
    /// highlighting, and per-binder or all-prefab code generation buttons.
    /// </summary>
    [CustomEditor(typeof(ComponentBinderTable))]
    public sealed class ComponentBinderTableEditor : UnityEditor.Editor
    {
        private static readonly Component[] Empty = Array.Empty<Component>();
        private static readonly Dictionary<string, HashSet<int>> DuplicateNames = new();
        private static readonly List<int> DuplicateIndexes = new();
        private static readonly Color RedColor = new(.9f, .7f, .7f);

        private ReorderableList _list;
        private SerializedProperty _items;
        private bool _allowEdit = true;

        private void OnEnable()
        {
            var binder = (ComponentBinderTable)target;
            _allowEdit = IsEditableRoot(binder);

            _items = serializedObject.FindProperty(nameof(ComponentBinderTable.Items));
            _list = new ReorderableList(serializedObject, _items)
            {
                draggable = _allowEdit,
                displayAdd = _allowEdit,
                displayRemove = _allowEdit,
            };

            _list.drawHeaderCallback = rect =>
            {
                var w = (rect.width - 20) / 4f;
                var x = rect.x + 20;
                EditorGUI.LabelField(new Rect(x, rect.y, w, rect.height), "Field");
                x += w;
                EditorGUI.LabelField(new Rect(x, rect.y, w, rect.height), "Node");
                x += w;
                EditorGUI.LabelField(new Rect(x, rect.y, w, rect.height), "Component");
                x += w;
                EditorGUI.LabelField(new Rect(x, rect.y, w, rect.height), "Desc");
            };

            _list.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                DrawRow(rect, index);
            };

            _list.onAddCallback = _ =>
            {
                _items.arraySize++;
                _list.index = _items.arraySize - 1;
            };

            _list.onRemoveCallback = _ =>
            {
                if (_list.index >= 0 && _list.index < _items.arraySize)
                {
                    _items.DeleteArrayElementAtIndex(_list.index);
                    _list.index--;
                    if (_list.index < 0) _list.index = _items.arraySize - 1;
                }
            };
        }

        private static bool IsEditableRoot(ComponentBinderTable binder)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null) return true;

            var root = stage.prefabContentsRoot;
            var child = binder.transform;
            while (child != null)
            {
                if (child.gameObject == root) return false; // root binder is edited on the outermost instance
                child = child.parent;
            }

            var outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(binder);
            return outermost == null || outermost == binder.gameObject;
        }

        private void DrawRow(Rect rect, int index)
        {
            var defColor = GUI.color;
            var enabled = GUI.enabled;
            var prop = _items.GetArrayElementAtIndex(index);

            var nameProp = prop.FindPropertyRelative(nameof(ComponentBinderTable.Item.Name));
            var targetProp = prop.FindPropertyRelative(nameof(ComponentBinderTable.Item.Target));
            var compProp = prop.FindPropertyRelative(nameof(ComponentBinderTable.Item.Component));
            var descProp = prop.FindPropertyRelative(nameof(ComponentBinderTable.Item.Desc));

            var targetGo = targetProp.objectReferenceValue as GameObject;
            var curComp = compProp.objectReferenceValue as Component;

            if (curComp && curComp.gameObject != targetGo)
            {
                compProp.objectReferenceValue = null;
                curComp = null;
            }
            if (string.IsNullOrEmpty(nameProp.stringValue) && targetGo != null)
                nameProp.stringValue = targetGo.name;

            var variableName = nameProp.stringValue?.Trim();
            var nameValid = !string.IsNullOrEmpty(variableName)
                && ComponentBinderCodeGenerator.IsVariableName(variableName)
                && !DuplicateIndexes.Contains(index);

            var w = rect.width / 4f;
            var x = rect.x;
            var y = rect.y;

            GUI.color = nameValid ? defColor : RedColor;
            GUI.enabled = _allowEdit;
            EditorGUI.PropertyField(new Rect(x, y, w - 2, rect.height - 2), nameProp, GUIContent.none, false);
            GUI.color = defColor;
            GUI.enabled = enabled;
            x += w;

            GUI.color = targetGo == null ? RedColor : defColor;
            EditorGUI.PropertyField(new Rect(x, y, w - 2, rect.height - 2), targetProp, GUIContent.none, false);
            GUI.color = defColor;
            x += w;

            var components = targetGo != null ? targetGo.GetComponents<Component>() : Empty;
            var componentNames = components.Select(c => new GUIContent(c.GetType().Name)).ToArray();
            var oldIndex = curComp != null ? Array.IndexOf(components, curComp) : -1;

            GUI.color = oldIndex == -1 ? RedColor : defColor;
            var newIndex = EditorGUI.Popup(new Rect(x, y, w - 1, rect.height), oldIndex, componentNames);
            GUI.color = defColor;
            if (newIndex != oldIndex && newIndex >= 0 && newIndex < components.Length)
                compProp.objectReferenceValue = components[newIndex];
            x += w;

            GUI.enabled = _allowEdit;
            EditorGUI.PropertyField(new Rect(x, y, w, rect.height - 2), descProp, GUIContent.none, false);
            GUI.enabled = enabled;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var defColor = GUI.color;
            var setting = ComponentBinderSetting.instance;

            // Recompute duplicates each draw.
            DuplicateNames.Clear();
            DuplicateIndexes.Clear();
            for (int i = 0; i < _items.arraySize; i++)
            {
                var current = _items.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(ComponentBinderTable.Item.Name)).stringValue;
                if (!DuplicateNames.TryGetValue(current, out var indexes))
                    DuplicateNames[current] = indexes = new HashSet<int>();
                indexes.Add(i);
            }
            foreach (var pair in DuplicateNames)
                if (pair.Value.Count > 1) DuplicateIndexes.AddRange(pair.Value);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Field list", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _list.DoLayoutList();
            EditorGUILayout.Space();

            if (_allowEdit)
            {
                EditorGUILayout.LabelField("Code generation", EditorStyles.boldLabel);
                EditorGUILayout.Space();

                var packageName = serializedObject.FindProperty(nameof(ComponentBinderTable.PackageName));
                var customPackage = serializedObject.FindProperty(nameof(ComponentBinderTable.CustomPackageName));
                var className = serializedObject.FindProperty(nameof(ComponentBinderTable.ClassName));
                var customClass = serializedObject.FindProperty(nameof(ComponentBinderTable.CustomClassName));

                DrawToggleField(customPackage, "Namespace", packageName, setting?.CodeOutputPackageName, ComponentBinderCodeGenerator.IsPackageName, true);
                DrawToggleField(customClass, "Class name", className, target.name, ComponentBinderCodeGenerator.IsVariableName, false);

                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Generate", GUILayout.ExpandWidth(false)))
                {
                    ComponentBinderCodeGenerator.GenerateOne((ComponentBinderTable)target);
                    ShowNotification("Generated " + target.name + " binder");
                }
                if (GUILayout.Button("Generate all", GUILayout.ExpandWidth(false)))
                {
                    ComponentBinderCodeGenerator.GenerateAll();
                    ShowNotification("Generated all binders");
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawToggleField(SerializedProperty toggle, string label, SerializedProperty value,
            string fallback, Func<string, bool> validate, bool isNamespace)
        {
            var defColor = GUI.color;
            EditorGUILayout.BeginHorizontal();
            toggle.boolValue = GUILayout.Toggle(toggle.boolValue, "", GUILayout.ExpandWidth(false));
            GUILayout.Label(label, GUILayout.ExpandWidth(false));

            var enabled = GUI.enabled;
            if (toggle.boolValue)
            {
                var valid = validate(value.stringValue);
                GUI.color = valid ? defColor : RedColor;
                EditorGUILayout.PropertyField(value, GUIContent.none, GUILayout.ExpandWidth(true));
            }
            else
            {
                var valid = validate(fallback);
                GUI.color = valid ? defColor : RedColor;
                EditorGUILayout.TextField(fallback);
            }
            GUI.color = defColor;
            GUI.enabled = enabled;
            EditorGUILayout.EndHorizontal();
        }

        private static void ShowNotification(string content)
        {
            var inspector = typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow");
            if (inspector != null)
                EditorWindow.GetWindow(inspector).ShowNotification(new GUIContent(content));
        }
    }
}