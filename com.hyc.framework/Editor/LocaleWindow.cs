using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Loc.Editor
{
    /// <summary>
    /// Editor window to browse the loaded localization tables, search keys,
    /// view the per-language text side by side (one row per key, one column
    /// per language), and export a language to a CSV.
    /// Data comes from the blob files written by the import pipeline into
    /// <see cref="LocalizationSettings.OutputFolder"/>.
    /// </summary>
    public sealed class LocaleWindow : EditorWindow
    {
        // priority=31：Localization 档内，紧跟 Import(30)。
        [MenuItem("HYC Framework/Localization/Editors/Key Browser", false, 31)]
        public static void Open() => GetWindow<LocaleWindow>("Localization");

        private const float KeyColWidth = 240f;
        private const float SourceColWidth = 170f;
        // 语言列宽下限：窄窗口下等分结果低于此值时按此值取宽，内容超出即整体横向滚动。
        private const float MinLangColWidth = 200f;
        // 滚动区垂直滚动条占位宽：表头画在滚动区外，预留同宽才能与行轨左右对齐。
        private const float ScrollBarAllowance = 15f;
        private const float ViewPadding = 12f;

        private string _filter = "";
        private Vector2 _scroll;
        private readonly List<int> _rows = new List<int>();

        private static Texture2D _headerBg;
        private static Texture2D _missingBg;
        private GUIStyle _headerStyle;
        private GUIStyle _headerWarnStyle;
        private GUIStyle _missingStyle;
        private GUIStyle _dimStyle;

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Localization Tables", EditorStyles.boldLabel);
            _filter = EditorGUILayout.TextField("Filter", _filter);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Import Excel")) LocalizedExcelReader.ImportAll();
            if (GUILayout.Button("Reload")) Load();
            if (GUILayout.Button("Export CSV")) Export();
            EditorGUILayout.EndHorizontal();

            var ids = LocalizationManager.IDs;
            var langs = LocalizationManager.Langs;
            if (ids.Length == 0 || langs.Length == 0)
            {
                EditorGUILayout.HelpBox("No localization data loaded. Run Import Excel first.", MessageType.Info);
                return;
            }

            EnsureStyles();

            var texts = LocalizationManager.Texts;
            var excelNames = LocalizationManager.ExcelNames;
            var idExcelIndexes = LocalizationManager.IDExcelNameIndexs;

            // 过滤后的 key 下标：匹配 key 或来源表名，大小写不敏感。
            _rows.Clear();
            for (int key = 0; key < ids.Length; key++)
            {
                if (!string.IsNullOrEmpty(_filter)
                    && ids[key].IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0
                    && SourceOf(key, excelNames, idExcelIndexes).IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _rows.Add(key);
            }

            // 语言列宽 = max(等分余量, 保底宽)；保底宽生效时内容宽超窗，整体横向滚动。
            float langWidth = Mathf.Max(MinLangColWidth,
                (position.width - ViewPadding - KeyColWidth - SourceColWidth - ScrollBarAllowance) / langs.Length);

            DrawStatsLine(ids, langs, texts);

            // 表头画在滚动区外：列名与已填计数不随行滚动；语言列数由 Langs 派生，帧间恒定。
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Key", _headerStyle, GUILayout.Width(KeyColWidth));
            EditorGUILayout.LabelField("Source", _headerStyle, GUILayout.Width(SourceColWidth));
            for (int lang = 0; lang < langs.Length; lang++)
            {
                int filled = CountFilled(ids, texts, lang);
                EditorGUILayout.LabelField($"{langs[lang]}  {filled}/{ids.Length}",
                    filled < ids.Length ? _headerWarnStyle : _headerStyle, GUILayout.Width(langWidth));
            }
            GUILayout.Space(ScrollBarAllowance);
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var key in _rows)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent(ids[key], ids[key]), GUILayout.Width(KeyColWidth));
                EditorGUILayout.LabelField(SourceOf(key, excelNames, idExcelIndexes), GUILayout.Width(SourceColWidth));
                for (int lang = 0; lang < langs.Length; lang++)
                {
                    var row = lang < texts.Length ? texts[lang] : null;
                    var text = row != null && key < row.Length ? row[key] : null;
                    if (string.IsNullOrEmpty(text))
                        EditorGUILayout.LabelField("—", _missingStyle, GUILayout.Width(langWidth));
                    else
                        EditorGUILayout.LabelField(new GUIContent(text, text), GUILayout.Width(langWidth));
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>统计行：key 数（过滤时显示 显示/总数）、语言清单、每种语言缺译条数。</summary>
        private void DrawStatsLine(string[] ids, string[] langs, string[][] texts)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(_rows.Count == ids.Length ? $"{ids.Length} keys" : $"showing {_rows.Count} / {ids.Length} keys");
            sb.Append($" · {langs.Length} languages ({string.Join(" / ", langs)})");
            sb.Append(" · missing:");
            for (int lang = 0; lang < langs.Length; lang++)
            {
                int missing = ids.Length - CountFilled(ids, texts, lang);
                sb.Append(lang == 0 ? $" {langs[lang]} {missing}" : $" · {langs[lang]} {missing}");
            }
            EditorGUILayout.LabelField(sb.ToString(), _dimStyle);
        }

        private static int CountFilled(string[] ids, string[][] texts, int langIndex)
        {
            var row = langIndex < texts.Length ? texts[langIndex] : null;
            if (row == null) return 0;
            int filled = 0;
            int n = Mathf.Min(ids.Length, row.Length);
            for (int key = 0; key < n; key++)
                if (!string.IsNullOrEmpty(row[key])) filled++;
            return filled;
        }

        private static string SourceOf(int keyIndex, string[] excelNames, int[] idExcelIndexes)
        {
            if (excelNames == null || idExcelIndexes == null || keyIndex < 0 || keyIndex >= idExcelIndexes.Length)
                return string.Empty;
            var excelIndex = idExcelIndexes[keyIndex];
            return excelIndex >= 0 && excelIndex < excelNames.Length ? excelNames[excelIndex] : string.Empty;
        }

        private void EnsureStyles()
        {
            _headerBg ??= SolidTex(new Color(58f / 255f, 58f / 255f, 58f / 255f));
            _missingBg ??= SolidTex(new Color(201f / 255f, 138f / 255f, 43f / 255f, 0.16f));

            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel);
                _headerStyle.normal.background = _headerBg;
                _headerStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            }
            if (_headerWarnStyle == null)
            {
                _headerWarnStyle = new GUIStyle(_headerStyle);
                _headerWarnStyle.normal.textColor = new Color(0.85f, 0.62f, 0.28f);
            }
            if (_missingStyle == null)
            {
                _missingStyle = new GUIStyle(EditorStyles.label);
                _missingStyle.normal.background = _missingBg;
                _missingStyle.normal.textColor = new Color(0.85f, 0.62f, 0.28f);
            }
            if (_dimStyle == null)
            {
                _dimStyle = new GUIStyle(EditorStyles.label);
                _dimStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
            }
        }

        private static Texture2D SolidTex(Color color)
        {
            var tex = new Texture2D(2, 2);
            var pixels = new Color[4];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
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
