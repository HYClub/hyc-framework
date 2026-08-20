using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    [CustomPropertyDrawer(typeof(PositionAttribute))]
    public class PositionPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var fieldRect = EditorGUI.PrefixLabel(position, label);
            var valueType = property.boxedValue.GetType();
            var isVector2 = valueType == typeof(Vector2) || valueType == typeof(Vector2Int);
            var isVector3 = valueType == typeof(Vector3) || valueType == typeof(Vector3Int);
            var attr = attribute as PositionAttribute;

            var enable = GUI.enabled;

            if (isVector2)
            {
                var value = property.vector2Value;
                var fieldS = 2f;
                var fieldW = (fieldRect.width - fieldS) * 0.5f;
                var fieldX = fieldRect.x;

                EditorGUI.BeginChangeCheck();
                GUI.enabled = enable && attr.xEnable;
                value.x = EditorGUI.FloatField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.x);
                fieldX += fieldW + fieldS;

                GUI.enabled = enable && attr.yEnable;
                value.y = EditorGUI.FloatField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.y);

                if (EditorGUI.EndChangeCheck())
                    property.vector2Value = value;
                GUI.enabled = enable;
            }
            else if (isVector3)
            {
                var value = property.vector3Value;
                var fieldS = 2f;
                var fieldW = (fieldRect.width - fieldS * 2) * 0.333f;
                var fieldX = fieldRect.x;

                EditorGUI.BeginChangeCheck();
                GUI.enabled = enable && attr.xEnable;
                value.x = EditorGUI.FloatField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.x);
                fieldX += fieldW + fieldS;

                GUI.enabled = enable && attr.yEnable;
                value.y = EditorGUI.FloatField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.y);
                fieldX += fieldW + fieldS;

                GUI.enabled = enable && attr.zEnable;
                value.z = EditorGUI.FloatField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.z);

                if (EditorGUI.EndChangeCheck())
                    property.vector3Value = value;
                GUI.enabled = enable;
            }
            else
            {
                EditorGUI.PropertyField(position, property, label);
            }
        }
    }
}
