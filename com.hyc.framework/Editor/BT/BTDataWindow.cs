// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTDataWindow.cs
// 说明: 行为树数据编辑器主窗口(对齐配置数据编辑器范式)
//       左侧: 树列表(按类型分组)  右侧: 画布编辑 + 树属性 + 导出
//       菜单: Tools/HYC/BT Data Editor
// ============================================================

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HYC.Framework.BT.Editor
{
    public class BTDataWindow : EditorWindow
    {
        private TreeView _treeView;
        private VisualElement _canvasHost;
        private VisualElement _inspectorHost;
        private VisualElement _graphView;
        private Label _statusLabel;
        private BTTreeAsset _current;
        private string _theme = BTGraphView.DefaultTheme;
        private VisualElement _blackboardHost;

        // 树资产列表(全量)
        private List<BTTreeAsset> _allTrees = new List<BTTreeAsset>();
        private const int KindSkill = 1;
        private const int KindAI = 2;
        private const int KindOther = 3;
        private const int ItemIdBase = 1000;

        [MenuItem("Tools/HYC/BT Data Editor")]
        public static void Open()
        {
            var w = GetWindow<BTDataWindow>();
            w.titleContent = new GUIContent("行为树数据编辑器");
            w.minSize = new Vector2(1100, 650);
            w.Show();
        }

        private void OnEnable()
        {
            BuildUI();
            ReloadTrees();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private float _debugPollTimer;

        /// <summary>每帧轮询运行时轨迹(Play 模式调试高亮)。</summary>
        private void OnEditorUpdate()
        {
            if (!UnityEditor.EditorApplication.isPlaying) return;
            if (_graphView is not BTGraphView gv) return;
            if (gv.Asset == null) return;

            _debugPollTimer += UnityEngine.Time.unscaledDeltaTime;
            if (_debugPollTimer < 0.1f) return; // 10Hz
            _debugPollTimer = 0f;

            // 读 TD 驱动系统轨迹(通过类型)
            var driverType = System.Type.GetType("Battle.AIBT.BattleBTDriverSystem, Assembly-CSharp");
            if (driverType == null) return;
            var treeIdField = driverType.GetField("DebugTreeId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var nodesField = driverType.GetField("DebugTraceNodes", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var resultsField = driverType.GetField("DebugTraceResults", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var countField = driverType.GetField("DebugTraceCount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (treeIdField == null) return;

            long treeId = (long)treeIdField.GetValue(null);
            if (treeId != gv.Asset.TreeId) return; // 只高亮当前编辑的树

            int count = (int)countField.GetValue(null);
            var nodes = (int[])nodesField.GetValue(null);
            var results = (HYC.Framework.BT.BTNodeState[])resultsField.GetValue(null);
            gv.ApplyRuntimeTrace(nodes, results, count);

            // 运行统计显示(执行次数)
            var exeField = driverType.GetField("DebugTotalExecutions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (exeField != null)
            {
                long total = (long)exeField.GetValue(null);
                _statusLabel.text = $"运行中: 树 {treeId} | 总执行 {total} 次";
            }
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();

            // 工具栏
            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(CreateNewTree) { text = "新建树" });
            toolbar.Add(new ToolbarButton(ReloadTrees) { text = "刷新" });
            toolbar.Add(new ToolbarButton(ValidateTree) { text = "校验" });
            toolbar.Add(new ToolbarSpacer());

            // 主题切换
            toolbar.Add(new Label("主题:"));
            var themePopup = new PopupField<string>(new List<string>(BTGraphView.ThemeNames), 0);
            themePopup.RegisterValueChangedCallback(e =>
            {
                _theme = e.newValue;
                if (_current != null) OpenTree(_current); // 重建画布应用主题
            });
            toolbar.Add(themePopup);

            _statusLabel = new Label("就绪");
            toolbar.Add(_statusLabel);
            rootVisualElement.Add(toolbar);

            // 左右分栏
            var split = new TwoPaneSplitView(0, 260, TwoPaneSplitViewOrientation.Horizontal);

            // 左侧: TreeView
            var left = new VisualElement();
            _treeView = new TreeView();
            _treeView.style.flexGrow = 1;
            _treeView.makeItem = () => new Label();
            _treeView.bindItem = (element, index) =>
            {
                var label = element as Label;
                var item = _treeView.GetItemDataForIndex<string>(index);
                label.text = item;
                label.style.paddingLeft = 4;
            };
            _treeView.onSelectionChange += objs =>
            {
                if (_treeView.selectedIndex < 0) return;
                int id = _treeView.GetIdForIndex(_treeView.selectedIndex);
                var asset = FindTree(id);
                if (asset != null) OpenTree(asset);
            };
            left.Add(_treeView);
            split.Add(left);

            // 右侧: 画布(flexGrow) + 黑板边栏(固定宽)
            var right = new VisualElement();
            var canvasRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            _canvasHost = new VisualElement();
            _canvasHost.style.flexGrow = 1;
            canvasRow.Add(_canvasHost);
            _blackboardHost = new VisualElement();
            _blackboardHost.style.width = 200;
            _blackboardHost.style.borderLeftWidth = 1;
            _blackboardHost.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f);
            canvasRow.Add(_blackboardHost);
            right.Add(canvasRow);

            _inspectorHost = new VisualElement();
            _inspectorHost.style.maxHeight = 120;
            _inspectorHost.style.borderTopWidth = 1;
            _inspectorHost.style.borderTopColor = new Color(0.2f, 0.2f, 0.2f);
            right.Add(_inspectorHost);
            split.Add(right);

            rootVisualElement.Add(split);
        }

        /// <summary>重载树列表(按类型分组)。</summary>
        private void ReloadTrees()
        {
            _allTrees = new List<BTTreeAsset>(LoadAllTrees());

            var roots = new List<TreeViewItemData<string>>();
            var skillChildren = new List<TreeViewItemData<string>>();
            var aiChildren = new List<TreeViewItemData<string>>();
            var otherChildren = new List<TreeViewItemData<string>>();

            int id = ItemIdBase;
            foreach (var t in _allTrees)
            {
                var item = new TreeViewItemData<string>(id, $"{t.TreeId}: {t.name}");
                id++;
                switch (t.Kind)
                {
                    case BTTreeKind.Skill: skillChildren.Add(item); break;
                    case BTTreeKind.AI: aiChildren.Add(item); break;
                    default: otherChildren.Add(item); break;
                }
            }

            if (skillChildren.Count > 0)
                roots.Add(new TreeViewItemData<string>(KindSkill, $"技能树 ({skillChildren.Count})", skillChildren));
            if (aiChildren.Count > 0)
                roots.Add(new TreeViewItemData<string>(KindAI, $"角色AI树 ({aiChildren.Count})", aiChildren));
            if (otherChildren.Count > 0)
                roots.Add(new TreeViewItemData<string>(KindOther, $"其他树 ({otherChildren.Count})", otherChildren));

            _treeView.SetRootItems(roots);
            _treeView.Rebuild();
        }

        private BTTreeAsset FindTree(int id)
        {
            // id 从 ItemIdBase 递增, 对应 _allTrees 的顺序
            int idx = id - ItemIdBase;
            return idx >= 0 && idx < _allTrees.Count ? _allTrees[idx] : null;
        }

        private void OpenTree(BTTreeAsset tree)
        {
            _current = tree;

            _canvasHost.Clear();
            var gv = new BTGraphView(tree, () => { });
            gv.ApplyTheme(_theme);
            _graphView = gv;
            _graphView.style.flexGrow = 1;
            _canvasHost.Add(_graphView);
            DrawBlackboardPanel(tree);

            DrawInspector(tree);
            _statusLabel.text = $"编辑: {tree.TreeId}: {tree.name}";
        }

        /// <summary>黑板面板: 列出变量(类型/默认值/删除), 可新增。</summary>
        private void DrawBlackboardPanel(BTTreeAsset tree)
        {
            _blackboardHost.Clear();
            _blackboardHost.Add(new Label("黑板") { style = { unityFontStyleAndWeight = FontStyle.Bold, paddingTop = 6, paddingLeft = 6 } });

            if (tree.Blackboard == null)
                tree.Blackboard = new List<BTBlackboardParam>();

            for (int i = 0; i < tree.Blackboard.Count; i++)
            {
                int idx = i;
                var p = tree.Blackboard[i];
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingTop = 2 } };
                var keyField = new TextField { value = p.Key, style = { width = 80 } };
                keyField.RegisterValueChangedCallback(e => { p.Key = e.newValue; EditorUtility.SetDirty(tree); });
                var typeField = new EnumField(p.ValueType) { style = { width = 70 } };
                typeField.RegisterValueChangedCallback(e => { p.ValueType = (BTBlackboardValueType)e.newValue; EditorUtility.SetDirty(tree); });
                var delBtn = new Button(() =>
                {
                    tree.Blackboard.RemoveAt(idx);
                    EditorUtility.SetDirty(tree);
                    AssetDatabase.SaveAssets();
                    DrawBlackboardPanel(tree);
                }) { text = "×", style = { width = 20 } };
                row.Add(keyField);
                row.Add(typeField);
                row.Add(delBtn);
                _blackboardHost.Add(row);
            }

            _blackboardHost.Add(new Button(() =>
            {
                tree.Blackboard.Add(new BTBlackboardParam { Key = "newKey", ValueType = BTBlackboardValueType.Int });
                EditorUtility.SetDirty(tree);
                AssetDatabase.SaveAssets();
                DrawBlackboardPanel(tree);
            }) { text = "+ 变量", style = { marginLeft = 6, marginTop = 4 } });
        }

        private void ValidateTree()
        {
            if (_graphView is BTGraphView gv)
            {
                var issues = HYC.Framework.BT.Editor.BTValidator.Validate(gv.Asset);
                if (issues.Count == 0)
                {
                    _statusLabel.text = "校验通过 ✓";
                    return;
                }
                int errors = issues.Count(i => i.IsError);
                int warns = issues.Count - errors;
                var msg = string.Join("\n", issues.Select(i => (i.IsError ? "[错误] " : "[警告] ") + i.Message));
                _statusLabel.text = $"校验: {errors} 错误 {warns} 警告";
                Debug.LogWarning("[BT 校验] " + gv.Asset.name + ":" + "\n" + msg);
            }
        }

        private void DrawInspector(BTTreeAsset tree)
        {
            _inspectorHost.Clear();

            var so = new SerializedObject(tree);
            var foldout = new Foldout { text = "树属性" };
            foldout.value = true;

            var idField = new LongField("TreeId") { value = tree.TreeId };
            idField.RegisterValueChangedCallback(e =>
            {
                tree.TreeId = e.newValue;
                EditorUtility.SetDirty(tree);
                ReloadTrees();
            });
            foldout.Add(idField);

            var kindField = new EnumField("类型", tree.Kind);
            kindField.RegisterValueChangedCallback(e =>
            {
                tree.Kind = (BTTreeKind)e.newValue;
                EditorUtility.SetDirty(tree);
                AssetDatabase.SaveAssets();
                ReloadTrees();
            });
            foldout.Add(kindField);

            // 属性枚举: 拖入枚举脚本(树节点属性下拉的数据源)
            var attrField = new ObjectField("属性枚举");
            attrField.objectType = typeof(UnityEditor.MonoScript);
            attrField.allowSceneObjects = false;
            attrField.value = tree.AttributeEnumScript;
            attrField.RegisterValueChangedCallback(e =>
            {
                tree.AttributeEnumScript = e.newValue as UnityEditor.MonoScript;
                EditorUtility.SetDirty(tree);
                AssetDatabase.SaveAssets();
                // 刷新画布让属性下拉更新
                OpenTree(tree);
            });
            foldout.Add(attrField);

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingTop = 4 } };
            var exportBtn = new Button(ExportBlob) { text = "导出 Blob" };
            var registerBtn = new Button(RegisterRuntime) { text = "注册运行时" };
            row.Add(exportBtn);
            row.Add(registerBtn);
            foldout.Add(row);

            _inspectorHost.Add(foldout);
        }

        private void ExportBlob()
        {
            if (_current == null) return;
            if (BTBlobBuilder.Build(_current, out var blob))
            {
                HYC.Framework.BT.BTManager.Register(_current.TreeId, blob);
                _statusLabel.text = $"已导出并注册 treeId={_current.TreeId}";
                Debug.Log($"[BT] 导出成功 treeId={_current.TreeId} nodes={_current.Nodes.Count}");
            }
            else
            {
                _statusLabel.text = "导出失败: 树数据无效";
            }
        }

        private void RegisterRuntime()
        {
            if (_current == null) return;
            if (BTBlobBuilder.Build(_current, out var blob))
            {
                HYC.Framework.BT.BTManager.Register(_current.TreeId, blob);
                _statusLabel.text = $"已注册 treeId={_current.TreeId}";
            }
        }

        private void CreateNewTree()
        {
            var tree = CreateInstance<BTTreeAsset>();
            tree.TreeId = System.DateTime.Now.Ticks % 100000;
            tree.name = "NewTree";
            tree.Kind = BTTreeKind.Other;
            if (!AssetDatabase.IsValidFolder("Assets/BTTrees"))
                AssetDatabase.CreateFolder("Assets", "BTTrees");
            AssetDatabase.CreateAsset(tree, $"Assets/BTTrees/NewTree_{tree.TreeId}.asset");
            AssetDatabase.SaveAssets();

            // 自动创建 Root 入口节点
            var root = new BTNodeData { NodeId = 1, Type = BTNodeType.Root, Position = new Vector2(40, 200) };
            tree.Nodes.Add(root);
            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();

            ReloadTrees();
            OpenTree(tree);
        }

        private static BTTreeAsset[] LoadAllTrees()
        {
            var guids = AssetDatabase.FindAssets("t:BTTreeAsset");
            return guids.Select(g => AssetDatabase.LoadAssetAtPath<BTTreeAsset>(AssetDatabase.GUIDToAssetPath(g)))
                        .Where(t => t != null).ToArray();
        }
    }
}
