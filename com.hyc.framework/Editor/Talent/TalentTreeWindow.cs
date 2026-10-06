// ============================================================
// HYC Framework - 天赋树模块(Editor)
// 文件: Editor/Talent/TalentTreeWindow.cs
// 说明: 天赋树编辑器主窗口(宿主)。与 Editor/BT/BTGraphWindow.cs 结构完全一致:
//       窗口本身<b>不自绘任何东西</b> —— 工具栏 / 左栏 / 画布 / 右栏 / 底栏 /
//       minimap 全部由 TalentGraphIMGUI(纯 IMGUI)绘制, 本窗口只负责
//         · 挂载 IMGUIContainer
//         · 当前编辑哪棵树(打开 / 新建 / 保存 / 零资产兜底)
//         · 把 Undo / Repaint 转给画布
//       菜单: HYC Framework/Talent/Talent Tree Editor
// ============================================================

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HYC.Framework.Talent.Editor
{
    public class TalentTreeWindow : EditorWindow
    {
        private const string AssetFolder = "Assets/TalentTrees";

        private TalentTreeAsset _current;
        private VisualElement _graphHost;
        private TalentGraphIMGUI _graphIMGUI;
        private IMGUIContainer _imguiContainer;

        // priority=50："内容制作"档末位。与下一档 Tools(100) 差 50 ≥ 11 ⇒ 此处有分隔线。
        [MenuItem("HYC Framework/Talent/Talent Tree Editor", false, 50)]
        public static void Open()
        {
            var w = GetWindow<TalentTreeWindow>();
            w.titleContent = new GUIContent("Talent Tree");
            w.minSize = new Vector2(900, 480);
            w.Show();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndo;
            BuildUI();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
        }

        private void OnUndo()
        {
            _graphIMGUI?.NotifyUndo();
        }

        /// <summary>
        /// 当前资产被删掉(左栏/Project 窗口)后的自动恢复。
        /// 删除后 <see cref="_current"/> 成为已销毁引用(Unity 重载 == 判 null),
        /// 而 BuildUI 只在 OnEnable 跑一次, 不会自己救回来 —— 不守这一下就是整窗空白。
        /// </summary>
        private void Update()
        {
            if (_current != null) return;
            var all = LoadAllTrees();
            if (all.Length > 0) OpenTree(all[0]);
            else OpenTransientTree();
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();

            // 画布宿主: 0 UI Toolkit 装饰, 全部交给 IMGUI(与 BT 编辑器一致)
            _graphHost = new VisualElement();
            _graphHost.style.flexGrow = 1;
            _graphHost.style.flexBasis = 0;
            rootVisualElement.Add(_graphHost);

            var all = LoadAllTrees();
            if (all.Length > 0) OpenTree(all[0]);
            else OpenTransientTree();
        }

        /// <summary>
        /// 零资产兜底: 建一棵<b>只在内存里</b>的临时树, 直接进入完整编辑器(左栏资源列表为空)。
        /// 不立刻落盘 —— 否则用户只是打开窗口看一眼, 也会多出一个 NewTalentTree.asset。
        /// 要留下就用 File → 保存 存到 Assets/TalentTrees/。
        /// </summary>
        private void OpenTransientTree()
        {
            var tree = ScriptableObject.CreateInstance<TalentTreeAsset>();
            tree.displayName = "新天赋树";
            tree.EnsureTreeId();
            tree.name = "NewTalentTree";
            OpenTree(tree);
        }

        private static TalentTreeAsset[] LoadAllTrees()
        {
            return AssetDatabase.FindAssets("t:" + nameof(TalentTreeAsset))
                .Select(g => AssetDatabase.LoadAssetAtPath<TalentTreeAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null)
                .OrderBy(t => t.name)
                .ToArray();
        }

        /// <summary>新建天赋树(由 IMGUI 工具栏 File 菜单调用)。</summary>
        private void CreateNewTree()
        {
            var tree = ScriptableObject.CreateInstance<TalentTreeAsset>();
            tree.displayName = "新天赋树";
            tree.EnsureTreeId();
            tree.name = "NewTalentTree";

            if (!AssetDatabase.IsValidFolder(AssetFolder))
                AssetDatabase.CreateFolder("Assets", "TalentTrees");
            var path = AssetDatabase.GenerateUniqueAssetPath(AssetFolder + "/NewTalentTree.asset");
            AssetDatabase.CreateAsset(tree, path);
            EnsureNodeSubAssets(tree);
            AssetDatabase.SaveAssets();
            OpenTree(tree);
        }

        /// <summary>
        /// 保存当前树(由 IMGUI 工具栏 File → 保存 调用)。
        /// 已是磁盘资产 → 原地 SetDirty + SaveAssets; 内存临时树 → 首次落盘到 Assets/TalentTrees/。
        /// </summary>
        private void SaveCurrentTree()
        {
            if (_current == null) return;
            _current.EnsureTreeId();

            var existing = AssetDatabase.GetAssetPath(_current);
            if (string.IsNullOrEmpty(existing))
            {
                if (!AssetDatabase.IsValidFolder(AssetFolder))
                    AssetDatabase.CreateFolder("Assets", "TalentTrees");
                existing = AssetDatabase.GenerateUniqueAssetPath(AssetFolder + "/" + _current.name + ".asset");
                AssetDatabase.CreateAsset(_current, existing);
            }

            // 临时树期间创建的节点是内存 SO, 落盘时补挂成子资源, 否则保存后丢失
            EnsureNodeSubAssets(_current);

            EditorUtility.SetDirty(_current);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 保存后 treeId 可能刚生成 → 立刻重跑校验(否则底栏还挂着"treeId 未生成")
            _graphIMGUI?.NotifyUndo();
            _graphIMGUI?.OnRepaint?.Invoke();
        }

        /// <summary>
        /// 已落盘但还没挂成子资源的节点, 补 AddObjectToAsset(临时树编辑期节点是内存 SO, 保存时才落定)。
        /// </summary>
        private static void EnsureNodeSubAssets(TalentTreeAsset tree)
        {
            if (tree == null) return;
            foreach (var n in tree.nodes)
            {
                if (n != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(n)))
                    AssetDatabase.AddObjectToAsset(n, tree);
            }
        }

        private void OpenTree(TalentTreeAsset tree)
        {
            _current = tree;

            if (_graphHost == null) return;
            _graphHost.Clear();

            if (tree == null)
            {
                _graphHost.Add(new Label("打开的天赋树资产无效。可在 Project 窗口右键 Create → HYC Framework/Talent/Talent Tree 新建。"));
                return;
            }

            // 每棵树一个全新的画布实例 → 视图变换 / 选中 / 手势状态全部复位,
            // 并且会自动取景一次(见 TalentGraphIMGUI._pendingFit)。
            _graphIMGUI = new TalentGraphIMGUI(tree);
            _graphIMGUI.OnRequestOpenTree = OpenTree;
            _graphIMGUI.OnRequestNewTree = CreateNewTree;
            _graphIMGUI.OnRequestSave = SaveCurrentTree;

            _imguiContainer = new IMGUIContainer(() => _graphIMGUI.DrawGUI());
            _imguiContainer.style.flexGrow = 1;
            _imguiContainer.style.flexBasis = 0;
            _graphIMGUI.OnRepaint = () => _imguiContainer.MarkDirtyRepaint();

            _graphHost.Add(_imguiContainer);
        }
    }
}
