using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    [CustomPropertyDrawer(typeof(BoolToggleButtonAttribute))]
    public class BoolToggleButtonDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Boolean)
            {
                EditorGUI.LabelField(position, label.text, "字段类型不是bool.");
                return;
            }

            position = EditorGUI.PrefixLabel(position, label);
            var attr = (BoolToggleButtonAttribute)attribute;
            var buttonWidth = position.width / 2f;
            var trueRect = new Rect(position.x, position.y, buttonWidth, position.height);
            var falseRect = new Rect(position.x + buttonWidth, position.y, buttonWidth, position.height);

            var value = property.boolValue;

            if (GUI.Toggle(trueRect, value, attr.TrueLabel, EditorStyles.miniButtonLeft))
            {
                if (!value)
                    property.boolValue = true;
            }
            if (GUI.Toggle(falseRect, !value, attr.FalseLabel, EditorStyles.miniButtonRight))
            {
                if (value)
                    property.boolValue = false;
            }
        }
    }
}
