using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    [CustomPropertyDrawer(typeof(MaxAttribute))]
    public class MaxDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(position, property, label);
            if (EditorGUI.EndChangeCheck())
            {
                if (float.TryParse(property.boxedValue.ToString(), out var newValue))
                {
                    var clamped = Mathf.Min((attribute as MaxAttribute).max, newValue);
                    if (property.propertyType == SerializedPropertyType.Integer)
                        property.longValue = (long)clamped;
                    else if (property.propertyType == SerializedPropertyType.Float)
                        property.doubleValue = clamped;
                }
            }
            property.serializedObject.ApplyModifiedProperties();
        }
    }
}
