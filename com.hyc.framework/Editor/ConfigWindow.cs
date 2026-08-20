using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Editor window for the config pipeline: pick a spreadsheet (xlsx/csv/tsv),
    /// preview parsed rows and header/type rows, then generate the C#
    /// <c>[BlobGenerate]</c> struct into the generated folder.
    /// </summary>
    public sealed class ConfigWindow : EditorWindow
    {
        [MenuItem("HYC Framework/Config/Config Window")]
        public static void Open() => GetWindow<ConfigWindow>("Config");

        private string _sheetPath;
        private List<string[]> _rows = new List<string[]>();
        private string _status = string.Empty;
        private Vector2 _scroll;

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Config Import", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _sheetPath = EditorGUILayout.TextField("Spreadsheet", _sheetPath);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                var picked = EditorUtility.OpenFilePanel("Pick spreadsheet", "", "xlsx,csv,tsv");
                if (!string.IsNullOrEmpty(picked)) _sheetPath = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Load"))
            {
                LoadSheet();
            }
            if (GUILayout.Button("Generate C#", GUILayout.Width(110)))
            {
                Generate();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, _status.StartsWith("OK") ? MessageType.Info : MessageType.Error);

            EditorGUILayout.Space(8);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _rows.Count; i++)
            {
                var cells = _rows[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(i.ToString("D2"), GUILayout.Width(28), GUILayout.ExpandWidth(false));
                foreach (var c in cells)
                {
                    GUILayout.Label(c, GUILayout.MinWidth(60), GUILayout.MaxWidth(140));
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void LoadSheet()
        {
            if (string.IsNullOrEmpty(_sheetPath) || !File.Exists(_sheetPath))
            {
                _status = "ERROR: file not found: " + _sheetPath;
                return;
            }
            var sheets = ExcelReader.Read(_sheetPath);
            if (sheets.Count == 0) { _status = "ERROR: no sheets"; return; }
            _rows = sheets[0].Rows;
            _status = "OK: " + _rows.Count + " rows, " + (sheets[0].Width) + " columns";
        }

        private void Generate()
        {
            if (_rows.Count == 0) { _status = "ERROR: load a spreadsheet first"; return; }
            var sheetName = Path.GetFileNameWithoutExtension(_sheetPath);
            var code = ConfigGenerator.GenerateStruct(sheetName, _rows);
            if (string.IsNullOrEmpty(code)) { _status = "ERROR: need >=3 rows (headers/types/data)"; return; }

            var folder = "Assets/GeneratedConfig";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "GeneratedConfig");
            var path = folder + "/" + ConfigGenerator.ToTitle(sheetName) + "Config.cs";
            File.WriteAllText(path, code);
            AssetDatabase.ImportAsset(path);
            _status = "OK: wrote " + path;
        }
    }
}