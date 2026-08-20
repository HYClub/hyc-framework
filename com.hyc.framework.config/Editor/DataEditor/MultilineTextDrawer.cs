using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    [CustomPropertyDrawer(typeof(MultilineTextAttribute))]
    public class MultilineTextDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var texts = property.stringValue;
            if (!string.IsNullOrEmpty(texts))
            {
                var lines = texts.Split('\n');
                if (lines.Length > 1)
                    return lines.Length * (EditorGUIUtility.singleLineHeight - 3) + 6;
            }
            return base.GetPropertyHeight(property, label);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUI.PrefixLabel(new Rect(position.x, position.y, labelWidth, position.height), label);
            property.stringValue = EditorGUI.TextArea(
                new Rect(position.x + 1 + labelWidth, position.y, position.width - labelWidth - 1, position.height),
                property.stringValue);
        }
    }
}
