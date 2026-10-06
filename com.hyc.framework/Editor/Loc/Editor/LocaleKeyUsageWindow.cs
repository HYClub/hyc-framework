using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Loc.Editor
{
    /// <summary>
    /// Scans the project for localization key usage and reports:
    /// - <b>Unused</b>: keys defined in the loaded tables but never referenced
    ///   as a string literal anywhere in the scanned scope (default <c>Assets/</c>;
    ///   tick <c>Also scan Packages/</c> to include packages — much slower).
    ///   Candidate for removal.
    /// - <b>Missing</b>: keys referenced by <c>.ToLocal()</c>/<c>GetText()</c>
    ///   calls in scripts or <c>LocalizedText.Key</c> fields in prefabs/scenes,
    ///   but absent from the loaded tables (will show "未找到Key").
    ///
    /// Note: a key used only via a non-literal variable, or only inside an Excel
    /// sheet (not imported yet), may be reported as unused — treat the list as a
    /// hint, not a hard deletion signal.
    /// </summary>
    public sealed class LocaleKeyUsageWindow : EditorWindow
    {
        [MenuItem("HYC Framework/Localization/Scan Key Usage")]
        public static void Open() => GetWindow<LocaleKeyUsageWindow>("Loc Key Usage");

        private readonly List<string> _unused = new List<string>();
        private readonly List<string> _missing = new List<string>();
        private Vector2 _scroll;
        private bool _scanned;

        /// <summary>是否连 Packages/ 一起扫。包体量大（含库源码），默认关闭；只想确认包内是否引用某 key 时再打开。</summary>
        private bool _includePackages;

        /// <summary>待查 key 命中数（供 GUI 显示，不参与判定）。</summary>
        private int _scannedFileCount;

        private static readonly HashSet<string> ScannedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".cs", ".prefab", ".unity", ".asset", ".json", ".txt", ".csv" };

        private static readonly Regex Quoted = new Regex("\"([^\"]*)\"");
        private static readonly Regex RefCs = new Regex("(?:ToLocal|GetText|GetTextByLang|ToLocalByLang)\\(\\s*\"([^\"]+)\"");
        private static readonly Regex RefYaml = new Regex("\"Key\":\\s*\"([^\"]+)\"");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Localization Key Usage", EditorStyles.boldLabel);
            _includePackages = EditorGUILayout.ToggleLeft("Also scan Packages/ (slower)", _includePackages);
            if (GUILayout.Button("Scan"))
            {
                Scan();
                _scanned = true;
            }
            if (!_scanned) return;

            if (LocalizationManager.IDs == null)
            {
                EditorGUILayout.HelpBox("No localization data loaded. Run Import Excel / Reload first.", MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                $"Unused (defined, never referenced): {_unused.Count}    " +
                $"Missing (referenced, not in table): {_missing.Count}\n" +
                $"Scanned files: {_scannedFileCount}（范围：{( _includePackages ? "Assets/ + Packages/" : "仅 Assets/")}）", MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_unused.Count > 0)
            {
                EditorGUILayout.LabelField("Unused", EditorStyles.boldLabel);
                foreach (var k in _unused) EditorGUILayout.LabelField(k);
            }
            if (_missing.Count > 0)
            {
                EditorGUILayout.LabelField("Missing", EditorStyles.boldLabel);
                foreach (var k in _missing) EditorGUILayout.LabelField(k);
            }
            EditorGUILayout.EndScrollView();
        }

        private void Scan()
        {
            _unused.Clear();
            _missing.Clear();
            _scannedFileCount = 0;
            if (LocalizationManager.IDs == null) return;

            var defined = new HashSet<string>(LocalizationManager.IDs, StringComparer.Ordinal);
            // 只关心「已定义的 key 有没有被引用」，不必把项目里所有引号字符串都收进来——
            // 那等于把整个工程的字符串字面量装进 HashSet，几十万条白占内存。
            // 这里改为扫到字符串时立刻查 defined，命中才记。
            var referencedInTable = new HashSet<string>(StringComparer.Ordinal);
            var referenced = new HashSet<string>(StringComparer.Ordinal);

            var paths = AssetDatabase.GetAllAssetPaths();
            try
            {
                for (var i = 0; i < paths.Length; i++)
                {
                    var path = paths[i];
                    if (!path.StartsWith("Assets/", StringComparison.Ordinal)
                        && !(_includePackages && path.StartsWith("Packages/", StringComparison.Ordinal)))
                        continue;
                    var ext = Path.GetExtension(path);
                    if (string.IsNullOrEmpty(ext) || !ScannedExtensions.Contains(ext))
                        continue;

                    string text;
                    try { text = File.ReadAllText(path); }
                    catch { continue; }

                    _scannedFileCount++;
                    foreach (Match m in Quoted.Matches(text))
                    {
                        if (!m.Groups[1].Success) continue;
                        var s = m.Groups[1].Value;
                        if (defined.Contains(s)) referencedInTable.Add(s);
                    }

                    if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                        Collect(RefCs, text, referenced);
                    else
                        Collect(RefYaml, text, referenced);

                    // 每 200 个文件报一次进度：大工程全量扫要几秒，不给反馈像卡死
                    if ((i & 0x7F) == 0 && paths.Length > 256)
                        EditorUtility.DisplayProgressBar("Scan Key Usage", path, (float)i / paths.Length);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _unused.AddRange(defined.Where(k => !referencedInTable.Contains(k)).OrderBy(k => k, StringComparer.Ordinal));
            _missing.AddRange(referenced.Where(r => !defined.Contains(r)).OrderBy(r => r, StringComparer.Ordinal));
        }

        private static void Collect(Regex rx, string text, HashSet<string> into)
        {
            foreach (Match m in rx.Matches(text))
                if (m.Groups[1].Success) into.Add(m.Groups[1].Value);
        }
    }
}
