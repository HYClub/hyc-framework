// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTGraphWindow.cs
// 说明: 行为树编辑器主窗口(宿主)。
//       窗口本身不再自绘工具栏 —— 与 NodeCanvas 一致, 顶部工具栏 / 画布 /
//       minimap / 左栏(Explorer) / 浮动面板 全部由 BTGraphIMGUI(IMGUI)绘制。
//       菜单: HYC Framework/BT/BT Editor
// ============================================================

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using HYC.Framework.BT;

namespace HYC.Framework.BT.Editor
{
    public class BTGraphWindow : EditorWindow
    {
        private BTTreeAsset _current;
        private VisualElement _graphHost;
        private BTGraphIMGUI _graphIMGUI;
        private IMGUIContainer _imguiContainer;

        [MenuItem("HYC Framework/BT/BT Editor")]
        public static void Open()
        {
            var w = GetWindow<BTGraphWindow>();
            w.titleContent = new GUIContent("Canvas");
            w.minSize = new Vector2(700, 300);
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
        /// 当前打开的树资产被删除后的自动恢复（左面板「删除」或 Project 窗口删除都走这里）。
        ///
        /// <b>为什么需要</b>：删除后 <see cref="_current"/> 成为已销毁引用（Unity 重载 == 判 null），
        /// <c>BTGraphIMGUI.DrawGUI()</c> 第一行 <c>if (_asset == null) return;</c> 会拦掉整窗绘制 → 全空白；
        /// 而 <see cref="BuildUI"/> 只在 OnEnable 跑一次，不会自己救回来。这里每帧检测并重开：
        /// 还有其它树就开第一棵，一棵都没有就回内存临时树（与零资产启动行为一致）。
        /// </summary>
        private void Update()
        {
            // 编辑器调试桥: 仅当本窗口打开且处于 play 模式时, 让 BTManager 把节点运行态快照暴露给画布
            // (供 DrawNode 显示状态高亮/图标, 对应 NodeCanvas play 模式的节点状态展示)。
            BTManager.EditorDebugEnabled = EditorApplication.isPlaying;
            if (EditorApplication.isPlaying)
                Repaint();

            if (_current != null) return;
            var all = LoadAllTrees();
            if (all.Length > 0)
                OpenTree(all[0]);
            else
                OpenTransientTree();
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
                OpenTransientTree();   // 工程里还没有树资产 → 开一棵内存临时树, 直接进入完整编辑器(左资源列表此时为空)
        }

        /// <summary>
        /// 零资产兜底：建立一棵<b>仅存在于内存</b>的临时树并打开编辑器。
        /// 不立即落盘 —— 否则用户只是打开窗口看一眼, 也会多出一个 NewTree_xxx.asset。
        /// 真正要保留时, 用 File → 保存 把它存到 Assets/BTTrees/(见 <see cref="SaveCurrentTree"/>)。
        /// </summary>
        private void OpenTransientTree()
        {
            var tree = CreateInstance<BTTreeAsset>();
            // TreeId 不能为 0：运行期按 TreeId 注册/查找(0 = 未设置, 导出与配置引用都会跳过它)
            tree.TreeId = System.DateTime.Now.Ticks % 100000 + 1;
            tree.name = "NewTree";
            // 故意不预置 Root: 零资产的初始态是"干净的空画布 + 中央引导文案"(见 DrawEmptyCanvasHint),
            // 不是"一个孤零零的入口节点 + 一条 Root 未连线报错"。
            OpenTree(tree);
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
            // TreeId 不能为 0：运行期按 TreeId 注册/查找(0 = 未设置, 导出与配置引用都会跳过它)
            tree.TreeId = System.DateTime.Now.Ticks % 100000 + 1;
            tree.name = "NewTree";
            // 不预置 Root: 新树从空画布引导态开始(见 DrawEmptyCanvasHint), 放了节点后校验才介入。
            if (!AssetDatabase.IsValidFolder("Assets/BTTrees"))
                AssetDatabase.CreateFolder("Assets", "BTTrees");
            AssetDatabase.CreateAsset(tree, $"Assets/BTTrees/NewTree_{tree.TreeId}.asset");
            AssetDatabase.SaveAssets();
            OpenTree(tree);
        }

        /// <summary>
        /// 保存当前树(由 IMGUI 工具栏 File → 保存 调用)。
        /// <list type="bullet">
        ///   <item>已是磁盘资产 → 仅 SetDirty + SaveAssets(原地更新)。</item>
        ///   <item>内存临时树(零资产时自动打开的那棵) → 首次落盘到 Assets/BTTrees/。</item>
        /// </list>
        /// </summary>
        private void SaveCurrentTree()
        {
            if (_current == null) return;
            var existing = AssetDatabase.GetAssetPath(_current);
            if (string.IsNullOrEmpty(existing))
            {
                if (!AssetDatabase.IsValidFolder("Assets/BTTrees"))
                    AssetDatabase.CreateFolder("Assets", "BTTrees");
                existing = $"Assets/BTTrees/{_current.name}_{_current.TreeId}.asset";
                AssetDatabase.CreateAsset(_current, existing);
            }
            EditorUtility.SetDirty(_current);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _graphIMGUI?.OnRepaint?.Invoke();
        }

        private void OpenTree(BTTreeAsset tree)
        {
            _current = tree;

            if (_graphHost == null) return;
            _graphHost.Clear();

            if (tree == null)
            {
                _graphHost.Add(new Label("打开的行为树资产无效。可在 Project 窗口右键 Create → HYC/BT/Tree 新建，" +
                                         "或用左侧「行为树资源」面板选择其它树。"));
                return;
            }

            // 零节点树(临时树/新建树/清空画布后重开)统一走空画布引导态(见 DrawEmptyCanvasHint), 不预置 Root。

            // NodeCanvas 风格 IMGUI 全手绘编辑器(工具栏 + 画布 + minimap + 左栏 + 底栏)
            _graphIMGUI = new BTGraphIMGUI(tree);
            _graphIMGUI.OnRequestOpenTree = (t) => OpenTree(t);
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
