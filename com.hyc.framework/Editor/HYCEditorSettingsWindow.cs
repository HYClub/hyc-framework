using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Editor
{
    /// <summary>
    /// Project &amp; user settings editor. Demonstrates the settings framework:
    /// values persist via EditorPrefs, grouped in the same "HYC Framework" menu.
    /// </summary>
    public sealed class HYCEditorSettingsWindow : EditorWindow
    {
        // priority=200：进入"项目级低频"档。上一档 Tools 是 100，
        // 差值 100 ≥ 11 ⇒ Unity 自动在 Tools 与本项之间画分隔线。
        [MenuItem("HYC Framework/Settings/Project Settings", false, 200)]
        public static void Open() => GetWindow<HYCEditorSettingsWindow>("HYC Settings");

        private const string PrefVerbose = "HYC.Framework.Editor.VerboseLogs";
        private const string PrefAutoGen = "HYC.Framework.Editor.AutoGenerate";

        private bool _verbose;
        private bool _autoGen;

        private void OnEnable()
        {
            _verbose = EditorPrefs.GetBool(PrefVerbose, true);
            _autoGen = EditorPrefs.GetBool(PrefAutoGen, true);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("HYC Framework Editor Settings", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _verbose = EditorGUILayout.Toggle("Verbose editor logs", _verbose);
            _autoGen = EditorGUILayout.Toggle("Auto-generate on save", _autoGen);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(PrefVerbose, _verbose);
                EditorPrefs.SetBool(PrefAutoGen, _autoGen);
            }

            if (GUILayout.Button("Run Config Validators"))
            {
                ConfigValidator.ValidateAll();
            }
        }
    }
}