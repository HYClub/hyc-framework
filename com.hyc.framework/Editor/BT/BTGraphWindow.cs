// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTGraphWindow.cs
// 说明: 行为树编辑器主窗口(宿主)。
//       窗口本身不再自绘工具栏 —— 与 NodeCanvas 一致, 顶部工具栏 / 画布 /
//       minimap / 左栏(Explorer) / 浮动面板 全部由 BTGraphIMGUI(IMGUI)绘制。
//       菜单: Tools/HYC/BT Editor
// ============================================================

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HYC.Framework.BT.Editor
{
    public class BTGraphWindow : EditorWindow
    {
        private BTTreeAsset _current;
        private VisualElement _graphHost;
        private BTGraphIMGUI _graphIMGUI;
        private IMGUIContainer _imguiContainer;

        [MenuItem("Tools/HYC/BT Editor")]
        public static void Open()
        {
            var w = GetWindow<BTGraphWindow>();
            w.titleContent = new GUIContent("Canvas");
            w.minSize = new Vector2(700, 300);
            w.Show();
        }

        private void OnEnable()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();

            // 画布宿主: 0 UI Toolkit 装饰, 全部交给 IMGUI(对齐 NodeCanvas GraphEditor 的纯 IMGUI 画布)
            _graphHost = new VisualElement();
            _graphHost.style.flexGrow = 1;
            _graphHost.style.flexBasis = 0;
            rootVisualElement.Add(_graphHost);

            var all = LoadAllTrees();
            if (all.Length > 0)
                OpenTree(all[0]);
            else
                ShowEmptyState();
        }

        private static BTTreeAsset[] LoadAllTrees()
        {
            var guids = AssetDatabase.FindAssets("t:BTTreeAsset");
            return guids.Select(g => AssetDatabase.LoadAssetAtPath<BTTreeAsset>(AssetDatabase.GUIDToAssetPath(g)))
                        .Where(t => t != null)
                        .OrderBy(t => t.TreeId)
                        .ToArray();
        }

        /// <summary>新建行为树资产(由 IMGUI 工具栏 File 菜单调用)。</summary>
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

            if (_graphHost == null) return;
            _graphHost.Clear();

            if (tree == null)
            {
                _graphHost.Add(new Label("没有行为树资产, 用工具栏 File → 新建行为树 创建"));
                return;
            }

            // NodeCanvas 风格 IMGUI 全手绘编辑器(工具栏 + 画布 + minimap + 左栏 + 底栏)
            _graphIMGUI = new BTGraphIMGUI(tree);
            _graphIMGUI.OnRequestOpenTree = (t) => OpenTree(t);
            _graphIMGUI.OnRequestNewTree = CreateNewTree;
            _imguiContainer = new IMGUIContainer(() => _graphIMGUI.DrawGUI());
            _imguiContainer.style.flexGrow = 1;
            _imguiContainer.style.flexBasis = 0;
            _graphIMGUI.OnRepaint = () => _imguiContainer.MarkDirtyRepaint();
            _graphHost.Add(_imguiContainer);
        }

        private void ShowEmptyState()
        {
            if (_graphHost == null) return;
            _graphHost.Clear();
            _graphHost.Add(new Label("没有行为树资产, 用工具栏 File → 新建行为树 创建"));
        }
    }
}
