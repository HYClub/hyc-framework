using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    [CustomPropertyDrawer(typeof(SizeAttribute))]
    public class SizeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Vector3:
                    OnGUIForVector3(position, property, label);
                    break;
                case SerializedPropertyType.Vector3Int:
                    OnGUIForVector3Int(position, property, label);
                    break;
                case SerializedPropertyType.Vector2:
                    OnGUIForVector2(position, property, label);
                    break;
                case SerializedPropertyType.Vector2Int:
                    OnGUIForVector2Int(position, property, label);
                    break;
                default:
                    EditorGUI.PropertyField(position, property, label);
                    break;
            }
        }

        private void OnGUIForVector3(Rect position, SerializedProperty property, GUIContent label)
        {
            var value = property.vector3Value;
            var fieldRect = EditorGUI.PrefixLabel(position, label);
            var fieldS = 2f;
            var fieldW = (fieldRect.width - fieldS * 2) * 0.3333f;
            var fieldX = fieldRect.x;
            var attr = attribute as SizeAttribute;

            EditorGUI.BeginChangeCheck();
            var x = EditorGUI.FloatField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.x);
            fieldX += fieldW + fieldS;
            var y = EditorGUI.FloatField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.y);
            fieldX += fieldW + fieldS;
            var z = EditorGUI.FloatField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.z);

            x = Mathf.Clamp(x, attr.MinX, attr.MaxX);
            y = Mathf.Clamp(y, attr.MinY, attr.MaxY);
            z = Mathf.Clamp(z, attr.MinZ, attr.MaxZ);

            if (EditorGUI.EndChangeCheck())
                property.vector3Value = new Vector3(x, y, z);
        }

        private void OnGUIForVector3Int(Rect position, SerializedProperty property, GUIContent label)
        {
            var value = property.vector3IntValue;
            var fieldRect = EditorGUI.PrefixLabel(position, label);
            var fieldS = 2f;
            var fieldW = (fieldRect.width - fieldS * 2) * 0.3333f;
            var fieldX = fieldRect.x;
            var attr = attribute as SizeAttribute;

            EditorGUI.BeginChangeCheck();
            var x = EditorGUI.IntField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.x);
            fieldX += fieldW + fieldS;
            var y = EditorGUI.IntField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.y);
            fieldX += fieldW + fieldS;
            var z = EditorGUI.IntField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.z);

            x = Mathf.Clamp(x, (int)attr.MinX, (int)attr.MaxX);
            y = Mathf.Clamp(y, (int)attr.MinY, (int)attr.MaxY);
            z = Mathf.Clamp(z, (int)attr.MinZ, (int)attr.MaxZ);

            if (EditorGUI.EndChangeCheck())
                property.vector3IntValue = new Vector3Int(x, y, z);
        }

        private void OnGUIForVector2(Rect position, SerializedProperty property, GUIContent label)
        {
            var value = property.vector2Value;
            var fieldRect = EditorGUI.PrefixLabel(position, label);
            var fieldS = 2f;
            var fieldW = (fieldRect.width - fieldS) * 0.5f;
            var fieldX = fieldRect.x;
            var attr = attribute as SizeAttribute;

            EditorGUI.BeginChangeCheck();
            var x = EditorGUI.FloatField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.x);
            fieldX += fieldW + fieldS;
            var y = EditorGUI.FloatField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.y);

            x = Mathf.Clamp(x, attr.MinX, attr.MaxX);
            y = Mathf.Clamp(y, attr.MinY, attr.MaxY);

            if (EditorGUI.EndChangeCheck())
                property.vector2Value = new Vector2(x, y);
        }

        private void OnGUIForVector2Int(Rect position, SerializedProperty property, GUIContent label)
        {
            var value = property.vector2IntValue;
            var fieldRect = EditorGUI.PrefixLabel(position, label);
            var fieldS = 2f;
            var fieldW = (fieldRect.width - fieldS) * 0.5f;
            var fieldX = fieldRect.x;
            var attr = attribute as SizeAttribute;

            EditorGUI.BeginChangeCheck();
            var x = EditorGUI.IntField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.x);
            fieldX += fieldW + fieldS;
            var y = EditorGUI.IntField(new Rect(fieldX, fieldRect.y, fieldW, fieldRect.height), GUIContent.none, value.y);

            x = Mathf.Clamp(x, (int)attr.MinX, (int)attr.MaxX);
            y = Mathf.Clamp(y, (int)attr.MinY, (int)attr.MaxY);

            if (EditorGUI.EndChangeCheck())
                property.vector2IntValue = new Vector2Int(x, y);
        }
    }
}
