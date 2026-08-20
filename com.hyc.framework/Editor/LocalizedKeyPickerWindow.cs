using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Loc.Editor
{
    /// <summary>
    /// Pop-up key picker for the <see cref="LocalizedTextDrawer"/>. Lists the
    /// loaded localization keys grouped by their source Excel file; click a key
    /// to assign it to the target string property.
    /// </summary>
    public sealed class LocalizedKeyPickerWindow : EditorWindow
    {
        private SerializedProperty mProperty;
        private string mFilter = string.Empty;
        private Vector2 mScroll;
        private static GUIStyle mGroupStyle;
        private static GUIStyle mRowStyle;
        private static GUIStyle mHintStyle;

        public static void Open(Rect buttonRect, SerializedProperty property)
        {
            var window = CreateInstance<LocalizedKeyPickerWindow>();
            window.mProperty = property;
            window.mFilter = property.stringValue ?? string.Empty;
            window.ShowAsDropDown(buttonRect, new Vector2(Mathf.Max(buttonRect.width, 340), 320));
        }

        private void OnEnable()
        {
            mGroupStyle = new GUIStyle(EditorStyles.boldLabel) { padding = new RectOffset(6, 6, 3, 1) };
            mRowStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(12, 6, 2, 2) };
            mHintStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
        }

        private void OnGUI()
        {
            var filter = EditorGUILayout.TextField(string.Empty, mFilter, GUI.skin.FindStyle("ToolbarSearchTextField"));
            if (filter != mFilter) mFilter = filter;

            if (LocalizationManager.IDs == null || LocalizationManager.IDs.Length == 0)
            {
                EditorGUILayout.HelpBox("No localization data. Run HYC Framework/Localization/Import Excel first.", MessageType.Info);
                return;
            }

            mScroll = EditorGUILayout.BeginScrollView(mScroll);
            DrawGrouped(filter);
            EditorGUILayout.EndScrollView();
        }

        private void DrawGrouped(string filter)
        {
            var ids = LocalizationManager.IDs;
            var excelNames = LocalizationManager.ExcelNames;
            var excelIndexes = LocalizationManager.IDExcelNameIndexs;
            var hasExcelInfo = excelNames != null && excelIndexes != null && excelIndexes.Length == ids.Length;
            var needle = filter.Trim().ToLowerInvariant();

            var groups = new Dictionary<string, List<string>>();
            for (var i = 0; i < ids.Length; i++)
            {
                var key = ids[i];
                if (needle.Length > 0 && !key.ToLowerInvariant().Contains(needle)) continue;

                var groupName = hasExcelInfo && excelIndexes[i] >= 0 && excelIndexes[i] < excelNames.Length
                    ? excelNames[excelIndexes[i]]
                    : string.Empty;
                if (!groups.TryGetValue(groupName, out var list))
                {
                    list = new List<string>();
                    groups.Add(groupName, list);
                }
                list.Add(key);
            }

            foreach (var group in groups)
            {
                var header = string.IsNullOrEmpty(group.Key) ? "(unknown source)" : group.Key;
                EditorGUILayout.LabelField(header, mGroupStyle);
                foreach (var key in group.Value)
                {
                    if (GUILayout.Button(key, mRowStyle))
                    {
                        mProperty.serializedObject.Update();
                        mProperty.stringValue = key;
                        mProperty.serializedObject.ApplyModifiedProperties();
                        Close();
                    }
                }
            }

            if (groups.Count == 0)
                EditorGUILayout.LabelField("No keys match the filter.", mHintStyle);
        }
    }
}
