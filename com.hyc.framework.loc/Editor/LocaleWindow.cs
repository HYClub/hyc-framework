using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Loc.Editor
{
    /// <summary>
    /// Editor window to browse the loaded localization tables, search keys,
    /// view the per-language text, and export a language to a CSV.
    /// Data comes from the blob files written by the import pipeline into
    /// <see cref="LocalizationSettings.OutputFolder"/>.
    /// </summary>
    public sealed class LocaleWindow : EditorWindow
    {
        [MenuItem("HYC Framework/Localization/Key Browser")]
        public static void Open() => GetWindow<LocaleWindow>("Localization");

        private string _filter = "";
        private Vector2 _scroll;

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Localization Tables", EditorStyles.boldLabel);
            _filter = EditorGUILayout.TextField("Filter", _filter);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Import Excel")) LocalizedExcelReader.ImportAll();
            if (GUILayout.Button("Reload")) Load();
            if (GUILayout.Button("Export CSV")) Export();
            EditorGUILayout.EndHorizontal();

            if (LocalizationManager.IDs == null || LocalizationManager.Langs == null)
            {
                EditorGUILayout.HelpBox("No localization data loaded. Run Import Excel first.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int lang = 0; lang < LocalizationManager.Langs.Length; lang++)
            {
                DrawLanguage(LocalizationManager.Langs[lang], lang);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawLanguage(string langName, int langIndex)
        {
            var texts = LocalizationManager.Texts;
            var ids = LocalizationManager.IDs;
            if (langIndex < 0 || langIndex >= texts.Length) return;

            EditorGUILayout.LabelField(langName, EditorStyles.boldLabel);
            var row = langIndex < texts.Length ? texts[langIndex] : null;
            for (int key = 0; key < ids.Length; key++)
            {
                var id = ids[key];
                if (!string.IsNullOrEmpty(_filter) && !id.Contains(_filter)) continue;
                var text = row != null && key < row.Length ? row[key] : null;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(id, GUILayout.Width(220));
                EditorGUILayout.LabelField(SourceExcel(id), GUILayout.Width(180));
                EditorGUILayout.LabelField(string.IsNullOrEmpty(text) ? "" : text);
                EditorGUILayout.EndHorizontal();
            }
        }

        private static string SourceExcel(string key)
        {
            var ids = LocalizationManager.IDs;
            var excelNames = LocalizationManager.ExcelNames;
            var excelIndexes = LocalizationManager.IDExcelNameIndexs;
            if (ids == null || excelNames == null || excelIndexes == null || excelIndexes.Length != ids.Length)
                return string.Empty;

            var index = System.Array.IndexOf(ids, key);
            if (index == -1) return string.Empty;
            var excelIndex = excelIndexes[index];
            return excelIndex >= 0 && excelIndex < excelNames.Length ? excelNames[excelIndex] : string.Empty;
        }

        private void Load()
        {
            var folder = Path.GetFullPath(LocalizationSettings.OutputFolder);
            if (!Directory.Exists(folder))
            {
                Debug.LogWarning($"Localization folder not found: {folder}. Run Import Excel first.");
                return;
            }

            LocalizationManager.Reload(folder);

            var langCount = LocalizationManager.Langs == null ? 0 : LocalizationManager.Langs.Length;
            var idCount = LocalizationManager.IDs == null ? 0 : LocalizationManager.IDs.Length;
            Debug.Log($"Localization loaded: {idCount} keys, {langCount} languages from {folder}");
        }

        private void Export()
        {
            if (LocalizationManager.IDs == null) return;
            var path = EditorUtility.SaveFilePanel("Export CSV", "", "localization.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var lines = new List<string>();
            var ids = LocalizationManager.IDs;
            var langs = LocalizationManager.Langs;
            var texts = LocalizationManager.Texts ?? System.Array.Empty<string[]>();

            var header = new List<string> { "key" };
            foreach (var lang in langs) header.Add(lang);
            lines.Add(string.Join("\t", header));

            for (int key = 0; key < ids.Length; key++)
            {
                var row = new List<string> { ids[key] };
                for (int lang = 0; lang < langs.Length; lang++)
                {
                    row.Add(lang < texts.Length && key < texts[lang].Length ? texts[lang][key] ?? "" : "");
                }
                lines.Add(string.Join("\t", row));
            }

            File.WriteAllLines(path, lines.ToArray());
            Debug.Log("Exported " + ids.Length + " rows to " + path);
        }
    }
}