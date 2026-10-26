// ============================================================
// HYC Framework
// 文件: Editor/PlaySceneToolbarButton.cs
// 说明: 在 Unity 主工具栏的 play 控件右侧注入一个「仅场景运行」按钮。
//
// 行为：点击 → 跳过整套框架引导，只跑当前场景。
//   1) 置位 SessionState（跨域重载存活）+ BootstrapGate.ShouldSkip（直接置位，覆盖关闭域重载的情形）
//   2) isPlaying = true
//   3) 进 Play 后宿主 bootstrap 调 FrameworkBootstrap / FrameworkModeBootstrap.Initialize，
//      框架在顶部读到 ShouldSkip=true → 返回 false → Entities 自动建默认世界 → 单场景运行。
//   4) 退出 Play 时清标志，保证下次普通 Play 不被误伤。
//
// 注入手法（对照经实战验证的 unity-toolbar-extender 2021.1+ 分支）：
//   内部类 UnityEditor.Toolbar 持有 m_Root(VisualElement)。2022.3.35f1c1 实证三个区：
//   ToolbarZoneLeftAlign（左）/ ToolbarZonePlayMode（中）/ ToolbarZoneRightAlign（右）。
//   按钮挂到中间区 ToolbarZonePlayMode，外套 flexGrow=1 + justifyContent=Center 的 dock 实现水平居中。
//   不依赖任何宿主代码。
//
// 鲁棒性：工具栏窗口与 m_Root 是懒构建的，可能第一帧 FindObjectsOfTypeAll 拿到 Toolbar 但 m_Root 尚未就绪，
//   因此这里每帧重试直到 m_Root / 目标 zone 可用，并在每个失败点打日志，避免「静默永久放弃注入」。
// ============================================================

using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HYC.Framework.Runtime;

namespace HYC.Framework.Editor
{
    [InitializeOnLoad]
    public static class PlaySceneToolbarButton
    {
        private const string SessionKey = "HYC.Framework.PlaySceneOnly";
        private const string ElementName = "HYCPlaySceneOnlyButton";
        // 中间区真名（在本机 2022.3.35f1c1 的 UnityEditor.CoreModule.dll 里 UTF-16 字符串实证：
        // 只有 LeftAlign / PlayMode / RightAlign 三个区）。
        private const string ToolbarZoneMiddle = "ToolbarZonePlayMode";
        private const string ToolbarZoneLeftAlign = "ToolbarZoneLeftAlign";

        // 工具栏窗口一直没出现时的放弃阈值（防无限空转）。
        private const int MaxMissAttempts = 600;

        private static Type s_toolbarType;
        private static int s_missAttempts;

        static PlaySceneToolbarButton()
        {
            s_toolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");

            // 域重载（进 Play）/ 编辑器启动后，把 SessionState 同步进运行时闸门。
            BootstrapGate.ShouldSkip = SessionState.GetBool(SessionKey, false);

            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // 退出 Play 即清标志，确保下次普通 Play 走完整引导。
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                BootstrapGate.ShouldSkip = false;
                SessionState.SetBool(SessionKey, false);
            }
        }

        private static void OnUpdate()
        {
            if (s_toolbarType == null)
            {
                Debug.LogError("[HYC] UnityEditor.Toolbar 类型未找到，无法注入「仅场景运行」按钮。");
                EditorApplication.update -= OnUpdate;
                return;
            }

            // 工具栏窗口是懒创建的：可能前若干帧还不存在。
            var toolbars = Resources.FindObjectsOfTypeAll(s_toolbarType);
            if (toolbars.Length == 0)
            {
                if (++s_missAttempts > MaxMissAttempts)
                {
                    Debug.LogWarning("[HYC] 多次尝试仍未找到工具栏窗口，放弃注入「仅场景运行」按钮。");
                    EditorApplication.update -= OnUpdate;
                }
                return;
            }
            s_missAttempts = 0;

            var toolbar = (ScriptableObject)toolbars[0];

            // m_Root 同样是懒构建：拿到 Toolbar 不代表 VisualElement 树已就绪，需重试。
            var rootField = toolbar.GetType().GetField("m_Root",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (rootField == null)
            {
                Debug.LogError("[HYC] 工具栏 m_Root 字段未找到，无法注入按钮。");
                EditorApplication.update -= OnUpdate;
                return;
            }

            var root = rootField.GetValue(toolbar) as VisualElement;
            if (root == null)
                return; // 本帧 m_Root 尚未就绪，下一帧再试。

            // 挂载点：中间区 ToolbarZonePlayMode（按钮水平居中），找不到再依次回退左对齐区 / m_Root。
            var zone = root.Q(ToolbarZoneMiddle);
            if (zone == null)
            {
                Debug.LogWarning("[HYC] ToolbarZonePlayMode 未找到，回退 ToolbarZoneLeftAlign。");
                zone = root.Q(ToolbarZoneLeftAlign);
            }
            if (zone == null)
            {
                Debug.LogWarning("[HYC] ToolbarZoneLeftAlign 也未找到，回退到 m_Root 直接挂载按钮。");
                zone = root;
            }

            // 已注入过（同一工具栏窗口 / 布局重建后仍在同一窗口）则不再重复添加。
            if (zone.Q(ElementName) != null)
            {
                EditorApplication.update -= OnUpdate;
                return;
            }

            // 包一层 flexGrow=1 + 居中的 dock：填满中间区并把按钮推到水平居中位置。
            var dock = new VisualElement
            {
                name = ElementName + "Dock",
                style =
                {
                    flexGrow = 1,
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                }
            };
            var container = new IMGUIContainer(DrawButton)
            {
                name = ElementName,
                style =
                {
                    flexGrow = 0,
                    flexShrink = 0,
                    flexBasis = StyleKeyword.Auto,
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    width = 76f,
                    height = 22f,
                }
            };
            dock.Add(container);

            zone.Add(dock);
            EditorApplication.update -= OnUpdate;
        }

        private static void DrawButton()
        {
            var icon = EditorGUIUtility.IconContent("PlayButton");
            var content = new GUIContent("Scene", icon.image, "Play Scene Only (skip framework bootstrap)");
            if (GUILayout.Button(content, GUILayout.Width(72), GUILayout.Height(20)))
            {
                BootstrapGate.ShouldSkip = true;
                SessionState.SetBool(SessionKey, true);
                EditorApplication.isPlaying = true;
            }
        }
    }
}
