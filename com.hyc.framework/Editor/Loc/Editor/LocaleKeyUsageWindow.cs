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
    ///   as a string literal anywhere in <c>Assets/</c> or <c>Packages/</c>
    ///   (candidate for removal).
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

        private static readonly string[] ScannedExtensions =
            { ".cs", ".prefab", ".unity", ".asset", ".json", ".txt", ".csv" };

        private static readonly Regex Quoted = new Regex("\"([^\"]*)\"");
        private static readonly Regex RefCs = new Regex("(?:ToLocal|GetText|GetTextByLang|ToLocalByLang)\\(\\s*\"([^\"]+)\"");
        private static readonly Regex RefYaml = new Regex("\"Key\":\\s*\"([^\"]+)\"");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Localization Key Usage", EditorStyles.boldLabel);
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
                $"Missing (referenced, not in table): {_missing.Count}", MessageType.Info);

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
            if (LocalizationManager.IDs == null) return;

            var defined = new HashSet<string>(LocalizationManager.IDs);
            var allStrings = new HashSet<string>();
            var referenced = new HashSet<string>();

            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/")) continue;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (!ScannedExtensions.Contains(ext)) continue;

                string text;
                try { text = File.ReadAllText(path); }
                catch { continue; }

                foreach (Match m in Quoted.Matches(text))
                    if (m.Groups[1].Success) allStrings.Add(m.Groups[1].Value);

                if (ext == ".cs")
                    Collect(RefCs, text, referenced);
                else
                    Collect(RefYaml, text, referenced);
            }

            _unused.AddRange(defined.Where(k => !allStrings.Contains(k)).OrderBy(k => k));
            _missing.AddRange(referenced.Where(r => !defined.Contains(r)).OrderBy(r => r));
        }

        private static void Collect(Regex rx, string text, HashSet<string> into)
        {
            foreach (Match m in rx.Matches(text))
                if (m.Groups[1].Success) into.Add(m.Groups[1].Value);
        }
    }
}
