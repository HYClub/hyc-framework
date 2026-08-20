using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Loc.Editor
{
    /// <summary>
    /// Inspector drawer for <see cref="LocalizedTextAttribute"/>: a text field
    /// for the key plus a picker button (opens <see cref="LocalizedKeyPickerWindow"/>)
    /// and an optional preview button (opens <see cref="LocalizedTextPreviewWindow"/>).
    /// </summary>
    [CustomPropertyDrawer(typeof(LocalizedTextAttribute))]
    public class LocalizedTextDrawer : PropertyDrawer
    {
        private const float ButtonWidth = 20f;
        private static GUIStyle mIconButton;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            if (mIconButton == null)
            {
                mIconButton = new GUIStyle(EditorStyles.miniButton)
                {
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0)
                };
            }

            var preview = (attribute as LocalizedTextAttribute)?.Preview ?? true;

            var field = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
            var textRect = new Rect(field.x, field.y, field.width - ButtonWidth * (preview ? 2 : 1), field.height);
            var pickRect = new Rect(textRect.xMax, field.y, ButtonWidth, field.height);
            var prevRect = new Rect(pickRect.xMax, field.y, ButtonWidth, field.height);

            property.stringValue = EditorGUI.TextField(textRect, property.stringValue ?? string.Empty);

            if (GUI.Button(pickRect, "\u25BE", mIconButton))
                LocalizedKeyPickerWindow.Open(GUIUtility.GUIToScreenRect(pickRect), property);

            if (preview && GUI.Button(prevRect, "\u25C9", mIconButton))
                LocalizedTextPreviewWindow.Open(GUIUtility.GUIToScreenRect(prevRect), property.stringValue);
        }
    }
}
