// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTGraphWindow.cs
// 说明: 行为树编辑器主窗口 - 树列表 + 画布 + 黑板 + 导出 Blob
//       菜单: Tools/HYC/BT Editor
// ============================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HYC.Framework.BT.Editor
{
    public class BTGraphWindow : EditorWindow
    {
        private BTTreeAsset _current;
        private VisualElement _graphView;
        private VisualElement _graphHost;
        private VisualElement _blackboardRoot;

        [MenuItem("Tools/HYC/BT Editor")]
        public static void Open()
        {
            var w = GetWindow<BTGraphWindow>();
            w.titleContent = new GUIContent("行为树编辑器");
            w.minSize = new Vector2(900, 600);
            w.Show();
        }

        private void OnEnable()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(CreateNewTree) { text = "新建树" });
            toolbar.Add(new ToolbarButton(ExportBlob) { text = "导出 Blob" });
            toolbar.Add(new ToolbarButton(RegisterRuntime) { text = "注册到运行时" });
            toolbar.Add(new ToolbarSpacer());

            var treeNames = new List<string>(TreeNames());
            var treePopup = new PopupField<string>(treeNames, 0);
            treePopup.RegisterValueChangedCallback(e =>
            {
                var idx = treeNames.IndexOf(e.newValue);
                if (idx >= 0) OpenTree(LoadAllTrees()[idx]);
            });
            toolbar.Add(treePopup);
            rootVisualElement.Add(toolbar);

            var split = new TwoPaneSplitView(0, 180, TwoPaneSplitViewOrientation.Horizontal);
            _blackboardRoot = new VisualElement();
            split.Add(_blackboardRoot);

            _graphHost = new VisualElement();
            split.Add(_graphHost);
            rootVisualElement.Add(split);

            // 默认打开第一棵树
            var all = LoadAllTrees();
            if (all.Length > 0)
                OpenTree(all[0]);
            else
                ShowEmptyState();
        }

        private string[] TreeNames()
        {
            return LoadAllTrees().Select(t => $"{t.TreeId}: {t.name}").ToArray();
        }

        private static BTTreeAsset[] LoadAllTrees()
        {
            var guids = AssetDatabase.FindAssets("t:BTTreeAsset");
            return guids.Select(g => AssetDatabase.LoadAssetAtPath<BTTreeAsset>(AssetDatabase.GUIDToAssetPath(g)))
                        .Where(t => t != null).ToArray();
        }

        private void CreateNewTree()
        {
            var tree = CreateInstance<BTTreeAsset>();
            tree.TreeId = System.DateTime.Now.Ticks % 100000;
            tree.name = "NewTree";
            if (!AssetDatabase.IsValidFolder("Assets/BTTrees"))
                AssetDatabase.CreateFolder("Assets", "BTTrees");
            AssetDatabase.CreateAsset(tree, $"Assets/BTTrees/NewTree_{tree.TreeId}.asset");
            AssetDatabase.SaveAssets();
            OpenTree(tree);
        }

        private void OpenTree(BTTreeAsset tree)
        {
            _current = tree;

            _blackboardRoot.Clear();
            DrawBlackboardPanel();

            if (_graphHost == null) return;
            _graphHost.Clear();
            _graphView = new BTGraphView(tree, () => { });
            _graphView.style.flexGrow = 1;
            _graphHost.Add(_graphView);
        }

        private void DrawBlackboardPanel()
        {
            _blackboardRoot.Clear();
            _blackboardRoot.style.width = 180;
            _blackboardRoot.style.borderRightWidth = 1;
            _blackboardRoot.style.borderRightColor = new Color(0.2f, 0.2f, 0.2f);

            _blackboardRoot.Add(new Label("黑板") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

            if (_current.Blackboard == null)
                _current.Blackboard = new List<BTBlackboardParam>();

            foreach (var p in _current.Blackboard)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingTop = 2 } };
                var keyField = new TextField("") { value = p.Key, style = { width = 80 } };
                keyField.RegisterValueChangedCallback(e => { p.Key = e.newValue; EditorUtility.SetDirty(_current); });
                var typeField = new EnumField(p.ValueType) { style = { width = 70 } };
                typeField.RegisterValueChangedCallback(e => { p.ValueType = (BTBlackboardValueType)e.newValue; EditorUtility.SetDirty(_current); });
                row.Add(keyField);
                row.Add(typeField);
                _blackboardRoot.Add(row);
            }

            _blackboardRoot.Add(new ToolbarButton(() =>
            {
                _current.Blackboard.Add(new BTBlackboardParam { Key = "newKey", ValueType = BTBlackboardValueType.Int });
                EditorUtility.SetDirty(_current);
                AssetDatabase.SaveAssets();
                DrawBlackboardPanel();
            }) { text = "+ 参数" });
        }

        private void ShowEmptyState()
        {
            _graphView.Clear();
            _graphView.Add(new Label("没有行为树资产, 点\"新建树\"创建"));
        }

        /// <summary>导出 Blob: 校验树并注册到运行时(编辑态测试用)。</summary>
        private void ExportBlob()
        {
            if (_current == null) return;

            if (!BTBlobBuilder.Build(_current, out var blob))
            {
                EditorUtility.DisplayDialog("导出失败", "树数据无效(无节点/结构错误)", "确定");
                return;
            }
            HYC.Framework.BT.BTManager.Register(_current.TreeId, blob);
            Debug.Log($"[BT] 已导出并注册 treeId={_current.TreeId} nodes={_current.Nodes.Count} conns={_current.Connections.Count}");
            EditorUtility.DisplayDialog("导出成功", $"treeId={_current.TreeId} 已注册到运行时", "确定");
        }

        /// <summary>直接注册到运行时 BTManager(编辑态测试用)。</summary>
        private void RegisterRuntime()
        {
            if (_current == null) return;
            if (BTBlobBuilder.Build(_current, out var blob))
            {
                HYC.Framework.BT.BTManager.Register(_current.TreeId, blob);
                Debug.Log($"[BT] 已注册 treeId={_current.TreeId} 到运行时");
            }
        }
    }
}
