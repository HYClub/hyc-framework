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
        /// <summary>自动恢复的最大尝试次数（超过则停手并报错，见 Update）。</summary>
        private const int MaxRecoverAttempts = 3;

        private BTTreeAsset _current;
        private VisualElement _graphHost;
        private BTGraphIMGUI _graphIMGUI;
        private IMGUIContainer _imguiContainer;

        /// <summary>「_current 已失效 → 重建」连续尝试次数，_current 有效时归零。</summary>
        private int _recoverAttempts;

        // priority=10：Config(0) 之后，与之差 10 ⇒ 同属"内容制作"档且不画线。
        [MenuItem("HYC Framework/BT/BT Editor", false, 10)]
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
        ///
        /// <b>重试上限</b>：若自动恢复连续失败（例如 CreateInstance 抛异常），每帧 LoadAllTrees
        /// 就是一次全工程 FindAssets + 一次失败的 CreateInstance，越积越糟。故最多尝试 3 次，
        /// 之后停手并报一次错误，让用户自己重开窗口。
        /// </summary>
        private void Update()
        {
            // 编辑器调试桥: 仅当本窗口打开且处于 play 模式时, 让 BTManager 把节点运行态快照暴露给画布
            // (供 DrawNode 显示状态高亮/图标, 对应 NodeCanvas play 模式的节点状态展示)。
            BTManager.EditorDebugEnabled = EditorApplication.isPlaying;
            if (EditorApplication.isPlaying)
                Repaint();

            if (_current != null)
            {
                _recoverAttempts = 0;
                return;
            }

            if (_recoverAttempts >= MaxRecoverAttempts)
            {
                if (_recoverAttempts == MaxRecoverAttempts)
                    Debug.LogError($"[BT] 当前行为树已丢失且自动恢复连续失败 {MaxRecoverAttempts} 次，" +
                                   "已停止每帧重试。请重新打开 BT 窗口，或用 File → 新建 手动创建。");
                return;
            }
            _recoverAttempts++;

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
            tree.TreeId = BTAssetFactory.NextTreeId();
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
            var tree = BTAssetFactory.Create("NewTree", "Assets/BTTrees");
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
