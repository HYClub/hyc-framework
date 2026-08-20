using System;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    [CustomPropertyDrawer(typeof(EnumToggleButtonAttribute))]
    public class EnumToggleButtonDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (fieldInfo == null || !fieldInfo.FieldType.IsEnum)
            {
                EditorGUI.LabelField(position, label.text, "字段类型不是枚举.");
                return;
            }

            position = EditorGUI.PrefixLabel(position, label);

            var enumType = fieldInfo.FieldType;
            var enumNames = Enum.GetNames(enumType);
            var count = enumNames.Length;
            var currentValue = property.enumValueIndex;

            var buttonWidth = position.width / count;
            var buttonRect = new Rect(position.x, position.y, buttonWidth, position.height);

            for (var i = 0; i < count; i++)
            {
                var field = enumType.GetField(enumNames[i]);
                var displayName = enumNames[i];

                var inspectNameAttr = field.GetCustomAttributes(typeof(InspectorNameAttribute), true);
                if (inspectNameAttr.Length > 0)
                    displayName = (inspectNameAttr[0] as InspectorNameAttribute).displayName;

                var style = i == 0
                    ? EditorStyles.miniButtonLeft
                    : i == count - 1 ? EditorStyles.miniButtonRight : EditorStyles.miniButtonMid;

                var isSelected = currentValue == i;
                if (GUI.Toggle(buttonRect, isSelected, displayName, style))
                {
                    if (currentValue != i)
                        property.enumValueIndex = i;
                }
                buttonRect.x += buttonWidth;
            }
        }
    }
}
