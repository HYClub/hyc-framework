using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 结构体数组紧凑表格绘制：指定 ChildPath 的数组，按元素字段分列显示。
    /// 移植自 SD，去除了游戏专用的 "ID" 自动索引逻辑。
    /// </summary>
    [CustomPropertyDrawer(typeof(ListDrawerAttribute))]
    public class ListPropertyDrawer : PropertyDrawer
    {
        private readonly Dictionary<string, ReorderableList> mListDic = new Dictionary<string, ReorderableList>();
        private FieldInfo[] mFields;
        private readonly GUIStyle mHeaderStyle = new GUIStyle("AC BoldHeader");

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (attribute is ListDrawerAttribute listDrawerAttribute)
            {
                var targetPpt = property.FindPropertyRelative(listDrawerAttribute.ChildPath);
                if (targetPpt.isArray)
                    return listDrawerAttribute.LineHeight * targetPpt.arraySize + 100;
            }
            return 100;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty targetPpt = null;
            var showHeader = true;
            if (attribute is ListDrawerAttribute listDrawerAttribute)
            {
                targetPpt = property.FindPropertyRelative(listDrawerAttribute.ChildPath);
                if (targetPpt.isArray)
                {
                    if (mFields == null || mFields.Length == 0)
                    {
                        var targetField = fieldInfo.FieldType.GetField(listDrawerAttribute.ChildPath);
                        var elementType = targetField.FieldType.GetElementType();
                        if (elementType == null)
                            elementType = targetField.FieldType.GetGenericArguments()[0];
                        mFields = elementType.GetFields();
                    }
                }
                showHeader = listDrawerAttribute.ShowHeader;
            }

            EditorGUI.LabelField(new Rect(position.x, position.y, EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight), label);
            if (targetPpt == null || mFields == null || mFields.Length == 0)
                return;

            EditorGUI.BeginChangeCheck();

            if (showHeader)
            {
                var fieldWidth = (position.width - 20) / mFields.Length;
                for (var i = 0; i < mFields.Length; i++)
                {
                    var nameAttributes = mFields[i].GetCustomAttribute<InspectorNameAttribute>();
                    var name = nameAttributes != null ? nameAttributes.displayName : mFields[i].Name;
                    EditorGUI.LabelField(
                        new Rect(position.x + 20 + (fieldWidth - 3) * i, position.y + EditorGUIUtility.singleLineHeight + 2, fieldWidth - 6, 24),
                        name, mHeaderStyle);
                }
            }

            if (!mListDic.TryGetValue(property.propertyPath, out var mList) ||
                !ReferenceEquals(mList.serializedProperty.serializedObject, property.serializedObject))
            {
                mList = new ReorderableList(property.serializedObject, targetPpt, true, false, true, true);
                mList.drawElementCallback = (rect, index, active, focus) =>
                {
                    var item = targetPpt.GetArrayElementAtIndex(index);
                    var w = (rect.width - 20) / mFields.Length;
                    for (var i = 0; i < mFields.Length; i++)
                    {
                        var fieldRect = new Rect(rect.x + (w + 3) * i, rect.y + 2, w - 4, rect.height - 4);
                        var sub = item.FindPropertyRelative(mFields[i].Name);
                        if (sub != null)
                            EditorGUI.PropertyField(fieldRect, sub, GUIContent.none);
                    }
                };
                mList.drawNoneElementCallback = rect =>
                {
                    rect.x += 20;
                    EditorGUI.LabelField(rect, "");
                };
                mListDic[property.propertyPath] = mList;
            }

            mList.DoList(new Rect(position.x, position.y + 44, position.width, position.height - 44));
            if (EditorGUI.EndChangeCheck())
            {
                property.serializedObject.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(property.serializedObject.targetObject);
            }
        }
    }
}
