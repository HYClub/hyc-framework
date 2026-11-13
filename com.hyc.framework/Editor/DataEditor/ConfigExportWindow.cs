using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置导出进度窗（非阻塞）。订阅 <see cref="ConfigExportRunner"/> 的进度/日志/完成事件，
    /// 展示总体进度、当前类型、分级日志与取消/关闭按钮；长相与 HTML 原型
    /// （<c>config_export_progress_ui.html</c>）一致。UI 沿用 IMGUI 以对齐同目录的 <see cref="BuildErrorWindow"/>。
    /// </summary>
    public class ConfigExportWindow : EditorWindow
    {
        private static ConfigExportWindow sInstance;

        private ConfigExportProgress mProgress;
        private readonly List<string> mLogLines = new List<string>();
        private Vector2 mLogScroll;
        private bool mRunning;

        private static readonly Dictionary<CheckErrorLevel, string> sLevelColor = new Dictionary<CheckErrorLevel, string>
        {
            { CheckErrorLevel.Info, "#9a9a9a" },
            { CheckErrorLevel.Warning, "#e5c07b" },
            { CheckErrorLevel.Error, "#e06c75" },
        };

        public static void ShowWindow()
        {
            var w = GetWindow<ConfigExportWindow>(false, "配置导出", true);
            w.titleContent = new GUIContent("配置导出");
            w.minSize = new Vector2(480, 360);
            w.Show();
        }

        public static void RepaintWindow()
            => sInstance?.Repaint();

        private void OnEnable()
        {
            sInstance = this;
            ConfigExportRunner.OnProgress += OnProgress;
            ConfigExportRunner.OnLog += OnLog;
            ConfigExportRunner.OnComplete += OnComplete;
        }

        private void OnDisable()
        {
            ConfigExportRunner.OnProgress -= OnProgress;
            ConfigExportRunner.OnLog -= OnLog;
            ConfigExportRunner.OnComplete -= OnComplete;
            if (sInstance == this) sInstance = null;
        }

        private void OnProgress(ConfigExportProgress p)
        {
            mProgress = p;
            mRunning = p.phase == ConfigExportPhase.Prepare
                    || p.phase == ConfigExportPhase.Exporting
                    || p.phase == ConfigExportPhase.Finalizing;
            Repaint();
        }

        private void OnLog(string msg, CheckErrorLevel level)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss");
            var color = sLevelColor.TryGetValue(level, out var c) ? c : "#d4d4d4";
            mLogLines.Add($"<color={color}>[{ts}] {msg}</color>");
            if (mLogLines.Count > 400) mLogLines.RemoveAt(0);
            Repaint();
        }

        private void OnComplete(bool success, int exported, int failed, List<CheckError> errors)
        {
            mRunning = false;
            Repaint();
        }

        private void OnGUI()
        {
            if (mProgress.totalJobs == 0 && mLogLines.Count == 0)
            {
                GUILayout.Label("准备导出…");
                return;
            }

            EditorGUILayout.Space();

            // 操作头
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            var head = mProgress.phase == ConfigExportPhase.Cancelled ? "已取消"
                     : mProgress.phase == ConfigExportPhase.Done ? "成功"
                     : mProgress.phase == ConfigExportPhase.Failed ? "失败" : "进行中";
            GUILayout.Label(head, EditorStyles.boldLabel);
            GUILayout.Space(8);
            GUILayout.Label(mProgress.currentLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{mProgress.completedJobs} / {mProgress.totalJobs} 类型");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // 总体进度条
            var frac = mProgress.totalJobs > 0 ? (float)mProgress.completedJobs / mProgress.totalJobs : 0f;
            var label = mProgress.phase == ConfigExportPhase.Prepare ? "准备中…"
                      : mProgress.phase == ConfigExportPhase.Finalizing ? "刷新中…"
                      : $"{frac * 100:0}%";
            var rect = GUILayoutUtility.GetRect(0, 18);
            EditorGUI.ProgressBar(rect, frac, label);
            GUILayout.Label(mProgress.currentStage);

            EditorGUILayout.Space();

            // 日志（分级着色，富文本）
            mLogScroll = EditorGUILayout.BeginScrollView(mLogScroll, GUILayout.ExpandHeight(true));
            var style = new GUIStyle(EditorStyles.label) { richText = true, fontSize = 11, wordWrap = true };
            foreach (var line in mLogLines)
                GUILayout.Label(line, style);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            // 页脚
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (mRunning)
            {
                if (GUILayout.Button("取消", GUILayout.Width(80)))
                    ConfigExportRunner.RequestCancel();
            }
            else
            {
                if (GUILayout.Button("关闭", GUILayout.Width(80)))
                    Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
