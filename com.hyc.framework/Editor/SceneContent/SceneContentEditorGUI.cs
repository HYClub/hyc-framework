using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>SceneContentAuthoring 的共享编辑器 UI（Inspector 与 Window 共用）。</summary>
    public static class SceneContentEditorGUI
    {
        /// <summary>工具栏：刷新到场景 / 采集自场景 / 导出 Blob。</summary>
        public static void DrawToolbar(SceneContentAuthoring a)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("刷新到场景", EditorStyles.toolbarButton))
                SceneContentPreviewer.Refresh(a);
            if (GUILayout.Button("采集自场景", EditorStyles.toolbarButton))
                SceneContentCapturer.Capture(a, false);
            if (GUILayout.Button("导出 Blob", EditorStyles.toolbarButton))
                SceneContentExporter.Export(a);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// B. 标记组件内省（即「列出来」）：重新扫描关卡 prefab 上的
        /// <see cref="ISceneContentMarker"/>，按「类型名 + contentTypeId」分组显示计数与实例坐标。
        /// </summary>
        public static void DrawMarkerIntrospection(SceneContentAuthoring a)
        {
            var markers = new List<Component>();
            foreach (var c in a.GetComponentsInChildren<Component>(true))
            {
                if (c != null && c is ISceneContentMarker) markers.Add(c);
            }

            EditorGUILayout.LabelField($"B. 标记组件（自动发现）：{markers.Count} 个", EditorStyles.boldLabel);
            if (markers.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "在关卡 prefab（或其子物体）上挂实现了 ISceneContentMarker 的组件（如角色出生点 / 怪物出生点），这里会自动按类型分组列出数量与坐标。",
                    MessageType.Info);
                return;
            }

            var groups = markers
                .GroupBy(c => c.GetType().Name + " #" + ((ISceneContentMarker)c).ContentTypeId)
                .OrderBy(g => g.Key)
                .ToList();
            foreach (var g in groups)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("  " + g.Key, EditorStyles.miniBoldLabel, GUILayout.MinWidth(160));
                EditorGUILayout.LabelField("× " + g.Count(), EditorStyles.miniLabel, GUILayout.Width(48));
                EditorGUILayout.EndHorizontal();
                // 仅展示前 3 个实例坐标，避免过长
                foreach (var c in g.Take(3))
                {
                    var t = c.transform;
                    EditorGUILayout.LabelField(
                        $"    ({t.localPosition.x:F1}, {t.localPosition.y:F1}, {t.localPosition.z:F1})",
                        EditorStyles.miniLabel);
                }
                if (g.Count() > 3)
                    EditorGUILayout.LabelField($"    … 其余 {g.Count() - 3} 个", EditorStyles.miniLabel);
            }
        }
    }
}
