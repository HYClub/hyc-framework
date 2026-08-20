using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Loc.Editor
{
    /// <summary>Editor configuration for the localization pipeline (folders and
    /// default language). Values persist in EditorPrefs.</summary>
    public static class LocalizationSettings
    {
        private const string PrefExcelFolder = "HYC.Framework.Loc.ExcelFolder";
        private const string PrefOutputFolder = "HYC.Framework.Loc.OutputFolder";
        private const string PrefDefaultLang = "HYC.Framework.Loc.DefaultLang";
        private const string PrefSensitiveFile = "HYC.Framework.Loc.SensitiveFile";

        private static readonly string DefaultExcelFolder = "Assets/Localization";
        private static readonly string DefaultOutputFolder = "Assets/StreamingAssets/Localization";
        private static readonly string DefaultSensitiveFile = "Assets/Localization/filter.txt";

        /// <summary>Folder scanned for .xls/.xlsx localization tables.</summary>
        public static string ExcelFolder
        {
            get => EditorPrefs.GetString(PrefExcelFolder, DefaultExcelFolder);
            set => EditorPrefs.SetString(PrefExcelFolder, value);
        }

        /// <summary>Folder where imported id/lang blob files are written (inside
        /// StreamingAssets so builds ship them automatically).</summary>
        public static string OutputFolder
        {
            get => EditorPrefs.GetString(PrefOutputFolder, DefaultOutputFolder);
            set => EditorPrefs.SetString(PrefOutputFolder, value);
        }

        /// <summary>Default language code written to the <c>lang</c> file.</summary>
        public static string DefaultLanguage
        {
            get => EditorPrefs.GetString(PrefDefaultLang, "en");
            set => EditorPrefs.SetString(PrefDefaultLang, value);
        }

        /// <summary>Optional comma-separated sensitive-word file (txt).</summary>
        public static string SensitiveWordsFile
        {
            get => EditorPrefs.GetString(PrefSensitiveFile, DefaultSensitiveFile);
            set => EditorPrefs.SetString(PrefSensitiveFile, value);
        }
    }

    /// <summary>Editor window for the localization pipeline settings.</summary>
    public sealed class LocalizationSettingsWindow : EditorWindow
    {
        [MenuItem("HYC Framework/Localization/Settings")]
        public static void Open()
        {
            GetWindow<LocalizationSettingsWindow>(true, "Localization Settings");
        }

        private string _excelFolder;
        private string _outputFolder;
        private string _defaultLang;
        private string _sensitiveFile;

        private void OnEnable()
        {
            _excelFolder = LocalizationSettings.ExcelFolder;
            _outputFolder = LocalizationSettings.OutputFolder;
            _defaultLang = LocalizationSettings.DefaultLanguage;
            _sensitiveFile = LocalizationSettings.SensitiveWordsFile;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Localization Pipeline", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);

            _excelFolder = EditorGUILayout.TextField("Excel folder", _excelFolder);
            _outputFolder = EditorGUILayout.TextField("Output folder", _outputFolder);
            _defaultLang = EditorGUILayout.TextField("Default language", _defaultLang);
            _sensitiveFile = EditorGUILayout.TextField("Sensitive words file", _sensitiveFile);

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                LocalizationSettings.ExcelFolder = _excelFolder.Trim();
                LocalizationSettings.OutputFolder = _outputFolder.Trim();
                LocalizationSettings.DefaultLanguage = string.IsNullOrWhiteSpace(_defaultLang) ? "en" : _defaultLang.Trim();
                LocalizationSettings.SensitiveWordsFile = _sensitiveFile.Trim();
                Close();
            }
            if (GUILayout.Button("Cancel"))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
