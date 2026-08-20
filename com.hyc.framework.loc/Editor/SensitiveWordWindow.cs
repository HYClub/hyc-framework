using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Loc.Editor
{
    /// <summary>
    /// Editor window to test the sensitive-word filter loaded by the
    /// localization pipeline (menu: HYC Framework/Localization/Sensitive Words).
    /// </summary>
    public sealed class SensitiveWordWindow : EditorWindow
    {
        [MenuItem("HYC Framework/Localization/Sensitive Words")]
        public static void Open()
        {
            GetWindow<SensitiveWordWindow>(false, "Sensitive Words");
        }

        private string mText;
        private bool mChecked;
        private string mWords = string.Empty;
        private string mFiltered = string.Empty;

        private void OnGUI()
        {
            var enabled = GUI.enabled;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Test", EditorStyles.toolbarButton))
            {
                if (!string.IsNullOrEmpty(mText))
                {
                    mChecked = mText.Validate();
                    mWords = string.Join(", ", mText.GetAllMaskWords());
                    mFiltered = mText.Filter();
                }
                else
                {
                    mChecked = false;
                    mWords = string.Empty;
                    mFiltered = string.Empty;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            GUI.enabled = false;
            EditorGUILayout.IntField("Dictionary size", LocalizationManager.SensitiveWords.Length);
            GUI.enabled = enabled;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Test text");
            mText = EditorGUILayout.TextArea(mText, GUILayout.ExpandHeight(true));

            EditorGUILayout.Space(8);
            GUI.enabled = false;
            EditorGUILayout.Toggle("Contains sensitive words", mChecked);
            EditorGUILayout.LabelField("Words found");
            EditorGUILayout.TextArea(mWords, GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Filtered result");
            EditorGUILayout.TextArea(mFiltered, GUILayout.ExpandHeight(true));
            GUI.enabled = enabled;
        }
    }
}
