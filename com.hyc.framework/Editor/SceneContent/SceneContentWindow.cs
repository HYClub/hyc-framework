using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 场景内容编辑器窗口。选取一个 <see cref="SceneContentAuthoring"/>（关卡 prefab / GO），
    /// 内嵌其自定义 Inspector（实例条目编辑 + 标记组件内省 + 刷新/采集/导出按钮），
    /// 并提供「列出工程内全部」快速选择。
    /// </summary>
    public class SceneContentWindow : EditorWindow
    {
        [MenuItem("HYC Framework/Scene Content/Scene Content Window", false, 70)]
        public static void Open()
        {
            GetWindow<SceneContentWindow>("Scene Content").Show();
        }

        [MenuItem("HYC Framework/Scene Content/Export All Blobs", false, 71)]
        public static void ExportAllBlobsMenu()
        {
            SceneContentExporter.ExportAll();
        }

        [MenuItem("HYC Framework/Scene Content/Export Blob (Selected)", false, 72)]
        public static void ExportSelectedMenu()
        {
            var a = Selection.activeObject as SceneContentAuthoring;
            if (a == null)
            {
                Debug.LogWarning("[SceneContent] 请先在 Project/Scene 中选一个 SceneContentAuthoring 再导出。");
                return;
            }
            SceneContentExporter.Export(a);
        }

        private SceneContentAuthoring _selected;
        // 本命名空间含 Editor 段，裸 Editor 会解析到命名空间自身（CS0118）——按框架先例全限定
        private UnityEditor.Editor _editor;
        private Vector2 _scroll;
        private bool _showAll;
        private List<SceneContentAuthoring> _allList;

        private void OnDisable()
        {
            // Editor.CreateEditor 产出的实例不会随窗口自动回收，显式释放
            if (_editor != null)
            {
                DestroyImmediate(_editor);
                _editor = null;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Scene Content 编辑器", EditorStyles.boldLabel);

            var newSel = (SceneContentAuthoring)EditorGUILayout.ObjectField(
                "关卡描述器", _selected, typeof(SceneContentAuthoring), true);
            if (newSel != _selected)
            {
                _selected = newSel;
                _editor = _selected != null ? UnityEditor.Editor.CreateEditor(_selected) : null;
            }

            // 开关常驻：列表显隐只切状态，不跨帧增删控件（IMGUI 控件集合每帧恒定）
            var newShow = EditorGUILayout.ToggleLeft("列出工程内全部 SceneContentAuthoring", _showAll);
            if (newShow != _showAll)
            {
                _showAll = newShow;
                if (_showAll) RefreshAllList();
            }
            if (_showAll)
                DrawAllList();

            if (_selected == null)
            {
                EditorGUILayout.HelpBox("拖入一个挂了 SceneContentAuthoring 的关卡 prefab / GO，或打开上方开关从工程里选。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);
            _editor.OnInspectorGUI();
        }

        private void RefreshAllList()
        {
            _allList = new List<SceneContentAuthoring>();
            var guids = AssetDatabase.FindAssets("t:" + typeof(SceneContentAuthoring).FullName);
            foreach (var g in guids)
            {
                var a = AssetDatabase.LoadAssetAtPath<SceneContentAuthoring>(AssetDatabase.GUIDToAssetPath(g));
                if (a != null) _allList.Add(a);
            }
        }

        private void DrawAllList()
        {
            if (_allList == null || _allList.Count == 0)
            {
                EditorGUILayout.HelpBox("工程内尚未发现任何 SceneContentAuthoring。", MessageType.Info);
                return;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var a in _allList)
            {
                if (GUILayout.Button(a.name))
                {
                    _selected = a;
                    _editor = UnityEditor.Editor.CreateEditor(_selected);
                    if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(a)))
                        EditorGUIUtility.PingObject(a);
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
