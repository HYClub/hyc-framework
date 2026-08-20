using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Loc.Editor
{
    /// <summary>
    /// Pop-up that shows every language version of a localization key plus the
    /// source Excel file. Opened from the <see cref="LocalizedTextDrawer"/>
    /// preview button.
    /// </summary>
    public sealed class LocalizedTextPreviewWindow : EditorWindow
    {
        private string mKey;
        private Vector2 mScroll;

        public static void Open(Rect buttonRect, string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            var window = CreateInstance<LocalizedTextPreviewWindow>();
            window.mKey = key;
            window.ShowAsDropDown(buttonRect, new Vector2(Mathf.Max(buttonRect.width, 460), 280));
        }

        private void OnGUI()
        {
            mScroll = EditorGUILayout.BeginScrollView(mScroll);

            var source = FindSourceExcel(mKey);
            if (source != null)
                EditorGUILayout.LabelField("Source: " + source, EditorStyles.miniLabel);

            var langs = LocalizationManager.Langs;
            for (var i = 0; i < langs.Length; i++)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(langs[i], EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(LocalizationManager.GetTextByLang(mKey, langs[i]), EditorStyles.textArea, GUILayout.MinHeight(28));
            }

            EditorGUILayout.EndScrollView();
        }

        private static string FindSourceExcel(string key)
        {
            var ids = LocalizationManager.IDs;
            var excelNames = LocalizationManager.ExcelNames;
            var excelIndexes = LocalizationManager.IDExcelNameIndexs;
            if (ids == null || excelNames == null || excelIndexes == null) return null;

            var index = System.Array.IndexOf(ids, key);
            if (index == -1 || excelIndexes.Length != ids.Length) return null;

            var excelIndex = excelIndexes[index];
            return excelIndex >= 0 && excelIndex < excelNames.Length ? excelNames[excelIndex] : null;
        }
    }
}
