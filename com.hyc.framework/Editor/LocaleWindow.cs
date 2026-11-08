using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Loc.Editor
{
    /// <summary>
    /// Localization editor. Maintains an in-memory table of keys × languages
    /// that can be edited without any Excel file present: authors add key rows
    /// and language columns, fill text, then "Save" writes a single canonical
    /// <c>.xlsx</c> via NPOI and re-runs the import to refresh the blobs.
    /// Opening an existing <c>.xlsx</c> loads it for round-trip editing.
    /// Single canonical file model: the grid maps 1:1 to one chosen Excel file.
    /// </summary>
    public sealed class LocaleWindow : EditorWindow
    {
        // priority=31：Localization 档内，紧跟 Import(30)。
        [MenuItem("HYC Framework/Localization/Editors/Localization Editor", false, 31)]
        public static void Open() => GetWindow<LocaleWindow>("Localization");

        private const float KeyColWidth = 220f;
        private const float LangColWidth = 200f;
        private const float DelColWidth = 22f;

        private readonly List<string> _keys = new List<string>();
        private readonly List<string> _langs = new List<string>();
        private readonly Dictionary<string, Dictionary<string, string>> _cells =
            new Dictionary<string, Dictionary<string, string>>();

        private string _sourceFile;
        private string _defaultLang = LocaleUtil.DefaultCode;
        private string _filter = "";
        private Vector2 _scroll;
        private readonly List<System.Action> _deferred = new List<System.Action>();

        private void OnEnable() => LoadInitial();

        /// <summary>Prefers the canonical Excel (Localization.xlsx in the Excel
        /// folder); falls back to the loaded blob; otherwise starts blank.</summary>
        private void LoadInitial()
        {
            var canonical = Path.Combine(Path.GetFullPath(LocalizationSettings.ExcelFolder), "Localization.xlsx");
            if (File.Exists(canonical)) { LoadFromExcel(canonical); return; }
            if ((LocalizationManager.IDs?.Length ?? 0) > 0) { LoadFromBlob(); return; }

            _keys.Clear();
            _cells.Clear();
            _langs.Clear();
            _langs.Add(LocalizationSettings.DefaultLanguage);
            _defaultLang = LocalizationSettings.DefaultLanguage;
            _sourceFile = null;
        }

        private void LoadFromBlob()
        {
            var ids = LocalizationManager.IDs;
            var langs = LocalizationManager.Langs;
            var texts = LocalizationManager.Texts;
            _keys.Clear();
            _cells.Clear();
            _langs.Clear();
            _keys.AddRange(ids);
            _langs.AddRange(langs);
            for (var k = 0; k < ids.Length; k++)
            {
                var row = new Dictionary<string, string>();
                for (var l = 0; l < langs.Length; l++)
                    row[langs[l]] = texts != null && l < texts.Length && k < texts[l].Length
                        ? texts[l][k] ?? string.Empty : string.Empty;
                _cells[ids[k]] = row;
            }
            _defaultLang = LocalizationSettings.DefaultLanguage;
            _sourceFile = null;
        }

        private void LoadFromExcel(string path)
        {
            LocalizedExcelReader.ParseExcelFile(path, out var keys, out var langs, out var cells);
            _keys.Clear(); _keys.AddRange(keys);
            _langs.Clear(); _langs.AddRange(langs);
            _cells.Clear();
            foreach (var kv in cells) _cells[kv.Key] = kv.Value;
            _sourceFile = path;
            _defaultLang = LocalizationSettings.DefaultLanguage;
        }

        private void OnGUI()
        {
            _deferred.Clear();
            EditorGUILayout.LabelField("Localization Editor", EditorStyles.boldLabel);

            // —— 工具栏 ——
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Key")) _deferred.Add(AddKey);
            if (GUILayout.Button("从 Excel 加载")) _deferred.Add(LoadExcelDialog);
            if (GUILayout.Button("保存到 Excel")) _deferred.Add(Save);
            if (GUILayout.Button("导入并刷新 Blob")) LocalizedExcelReader.ImportAll();
            if (GUILayout.Button("Reload")) LocalizationManager.Reload(Path.GetFullPath(LocalizationSettings.OutputFolder));
            if (GUILayout.Button("导出 CSV")) ExportCsv();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _filter = EditorGUILayout.TextField("筛选", _filter);
            if (_langs.Count > 0)
            {
                EditorGUILayout.LabelField("默认语言", GUILayout.Width(60));
                var dlIndex = Mathf.Max(0, _langs.IndexOf(_defaultLang));
                var newDl = EditorGUILayout.Popup(dlIndex, _langs.ToArray(), GUILayout.Width(120));
                if (newDl != dlIndex && newDl < _langs.Count) _defaultLang = _langs[newDl];
            }
            EditorGUILayout.LabelField(_sourceFile == null ? "源：未保存" : "源：" + _sourceFile,
                GUILayout.Width(320));
            EditorGUILayout.EndHorizontal();

            if (_langs.Count == 0)
            {
                EditorGUILayout.HelpBox("还没有语言列。点「+ Key」或「从 Excel 加载」开始；空白工程默认带一列默认语言。",
                    MessageType.Info);
                ExecuteDeferred();
                return;
            }

            DrawStats();
            DrawGrid();
            ExecuteDeferred();
        }

        private void ExecuteDeferred()
        {
            foreach (var action in _deferred) action();
            _deferred.Clear();
        }

        private void DrawStats()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(_keys.Count == 0 ? "0 keys" : $"{_keys.Count} keys");
            sb.Append($" · {_langs.Count} languages ({string.Join(" / ", _langs)})");
            sb.Append(" · 缺失:");
            foreach (var l in _langs)
            {
                var missing = 0;
                foreach (var k in _keys)
                    if (!_cells.TryGetValue(k, out var row) || string.IsNullOrEmpty(row.TryGetValue(l, out var v) ? v : string.Empty))
                        missing++;
                sb.Append($" {l} {missing}");
            }
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);
        }

        private void DrawGrid()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // 表头
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Key", EditorStyles.boldLabel, GUILayout.Width(KeyColWidth));
            for (var i = 0; i < _langs.Count; i++)
            {
                var idx = i; // 按迭代捕获，避免闭包共享循环变量
                EditorGUILayout.BeginHorizontal(GUILayout.Width(LangColWidth));
                EditorGUILayout.LabelField(_langs[i], EditorStyles.boldLabel);
                if (GUILayout.Button("×", GUILayout.Width(DelColWidth)))
                    _deferred.Add(() => DelLang(idx));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.LabelField("", GUILayout.Width(DelColWidth));
            EditorGUILayout.EndHorizontal();

            // 行
            for (var ri = 0; ri < _keys.Count; ri++)
            {
                var k = _keys[ri];
                if (!string.IsNullOrEmpty(_filter) &&
                    k.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                EditorGUILayout.BeginHorizontal();
                var editedKey = EditorGUILayout.TextField(k, GUILayout.Width(KeyColWidth));
                if (!string.Equals(editedKey, k, System.StringComparison.Ordinal))
                    _deferred.Add(() => RenameKey(k, editedKey));

                if (!_cells.TryGetValue(k, out var row))
                {
                    row = new Dictionary<string, string>();
                    _cells[k] = row;
                }
                for (var li = 0; li < _langs.Count; li++)
                {
                    var lang = _langs[li];
                    if (!row.TryGetValue(lang, out var val)) { val = string.Empty; row[lang] = val; }
                    var missing = string.IsNullOrEmpty(val);
                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = missing ? new Color(0.85f, 0.62f, 0.28f, 0.22f) : Color.white;
                    var newVal = EditorGUILayout.TextField(val, GUILayout.Width(LangColWidth));
                    GUI.backgroundColor = prev;
                    if (!string.Equals(newVal, val, System.StringComparison.Ordinal))
                        row[lang] = newVal;
                }

                if (GUILayout.Button("×", GUILayout.Width(DelColWidth)))
                {
                    var r = ri; // 按迭代捕获
                    _deferred.Add(() => DelKey(r));
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        // —— 数据操作（在 _deferred 中执行，避免在 OnGUI 布局阶段改集合） ——

        private void AddKey()
        {
            var n = 1; string k;
            do { k = "new_key_" + n++; } while (_cells.ContainsKey(k));
            _keys.Add(k);
            var row = new Dictionary<string, string>();
            foreach (var l in _langs) row[l] = string.Empty;
            _cells[k] = row;
        }

        private void RenameKey(string oldKey, string candidate)
        {
            var newKey = (candidate ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(newKey) || newKey == oldKey) return;
            if (_cells.ContainsKey(newKey)) { Debug.LogWarning($"[Localization] Key 重复，已忽略：{newKey}"); return; }
            var idx = _keys.IndexOf(oldKey);
            if (idx < 0) return;
            _keys[idx] = newKey;
            var row = _cells[oldKey];
            _cells.Remove(oldKey);
            _cells[newKey] = row;
        }

        private void DelKey(int index)
        {
            if (index < 0 || index >= _keys.Count) return;
            var k = _keys[index];
            _keys.RemoveAt(index);
            _cells.Remove(k);
        }

        private void DelLang(int index)
        {
            if (index < 0 || index >= _langs.Count) return;
            var l = _langs[index];
            _langs.RemoveAt(index);
            foreach (var row in _cells.Values) row.Remove(l);
            if (_defaultLang == l) _defaultLang = _langs.Count > 0 ? _langs[0] : LocaleUtil.DefaultCode;
        }

        private void Save()
        {
            var path = EditorUtility.SaveFilePanel("保存本地化 Excel",
                LocalizationSettings.ExcelFolder, "Localization", "xlsx");
            if (string.IsNullOrEmpty(path)) return;

            LocalizationSettings.DefaultLanguage = _defaultLang;
            LocalizedExcelReader.WriteWorkbook(path, _keys, _langs, ToReadOnly());
            _sourceFile = path;
            LocalizedExcelReader.ImportAll();
            ShowNotification(new GUIContent("已保存并导入：" + Path.GetFileName(path)));
        }

        private void LoadExcelDialog()
        {
            var path = EditorUtility.OpenFilePanel("打开本地化 Excel",
                LocalizationSettings.ExcelFolder, "xlsx,xls");
            if (string.IsNullOrEmpty(path)) return;
            LoadFromExcel(path);
        }

        private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ToReadOnly()
        {
            var map = new Dictionary<string, IReadOnlyDictionary<string, string>>();
            foreach (var kv in _cells) map[kv.Key] = kv.Value;
            return map;
        }

        private void ExportCsv()
        {
            var path = EditorUtility.SaveFilePanel("导出 CSV", "", "localization", "csv");
            if (string.IsNullOrEmpty(path)) return;
            var lines = new List<string> { "key\t" + string.Join("\t", _langs) };
            foreach (var k in _keys)
            {
                _cells.TryGetValue(k, out var row);
                var vals = new List<string> { k };
                foreach (var l in _langs)
                    vals.Add(row != null && row.TryGetValue(l, out var v) ? v ?? string.Empty : string.Empty);
                lines.Add(string.Join("\t", vals));
            }
            File.WriteAllLines(path, lines.ToArray());
            Debug.Log($"[Localization] 导出 {_keys.Count} 行 → {path}");
        }
    }
}
