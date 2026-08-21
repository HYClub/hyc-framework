// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTGraphView.cs
// 说明: 行为树画布 - 显示/编辑一棵树(节点/连线/右键加节点)
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace HYC.Framework.BT.Editor
{
    public class BTGraphView : GraphView
    {
        public const string DefaultTheme = "ModernDark";
        public static readonly string[] ThemeNames = { "ModernDark", "LightClean", "Blueprint" };
        public string CurrentTheme { get; private set; } = DefaultTheme;

        public BTTreeAsset Asset { get; private set; }
        private Action _onDirty;
        private StyleSheet _baseStyle;
        private MiniMap _miniMap;
        private Button _miniMapToggle;
        private bool _miniMapCollapsed;

        public BTGraphView(BTTreeAsset asset, Action onDirty)
        {
            Asset = asset;
            _onDirty = onDirty;

            SetupView();
            LoadGraph();
            graphViewChanged += OnGraphViewChanged;
        }

        private void SetupView()
        {
            _baseStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.hyc.framework/Editor/BT/BTGraph.uss");
            if (_baseStyle != null) styleSheets.Add(_baseStyle);
            var grid = new GridBackground();
            Insert(0, grid);

            // 迷你地图(右上角, 可折叠成小图标)
            _miniMap = new MiniMap { anchored = false };
            _miniMap.AddToClassList("bt-minimap");
            _miniMap.style.width = 160;
            _miniMap.style.height = 120;
            _miniMap.style.position = UnityEngine.UIElements.Position.Absolute;
            _miniMap.style.left = new UnityEngine.UIElements.Length(0);
            _miniMap.style.top = new UnityEngine.UIElements.Length(0);
            // 非 anchored: 用 layout 回调定位右上角
            _miniMap.RegisterCallback<UnityEngine.UIElements.GeometryChangedEvent>(evt =>
            {
                // 定位到当前窗口右上角(用 GraphView 自身宽度)
                float gw = layout.width;
                if (gw <= 0) return;
                _miniMap.style.left = new UnityEngine.UIElements.Length(gw - _miniMap.layout.width - 8);
                _miniMap.style.top = new UnityEngine.UIElements.Length(44);
            });
            Add(_miniMap);

            // 折叠按钮(小图标切换)
            _miniMapToggle = new Button(ToggleMiniMap)
            {
                text = "▦",
                tooltip = "切换迷你地图",
            };
            _miniMapToggle.style.position = UnityEngine.UIElements.Position.Absolute;
            _miniMapToggle.style.top = new UnityEngine.UIElements.Length(8);
            _miniMapToggle.style.right = new UnityEngine.UIElements.Length(8);
            _miniMapToggle.style.width = 24;
            _miniMapToggle.style.height = 24;
            Add(_miniMapToggle);

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        private List<BTNodeData> _clipboard = new List<BTNodeData>();

        private void OnKeyDown(KeyDownEvent evt)
        {
            bool ctrl = evt.ctrlKey || evt.commandKey;
            if (!ctrl) return;

            if (evt.keyCode == KeyCode.C)
            {
                // 复制选中节点
                _clipboard.Clear();
                foreach (var s in selection)
                {
                    if (s is BTNodeUI n) _clipboard.Add(n.Data);
                }
                if (_clipboard.Count > 0) evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.V && _clipboard.Count > 0)
            {
                // 粘贴(生成新 NodeId, 偏移位置)
                var offset = new Vector2(30, 30);
                foreach (var src in _clipboard)
                {
                    var data = new BTNodeData
                    {
                        NodeId = Guid.NewGuid().GetHashCode() & 0x7fffffff,
                        Type = src.Type,
                        Position = src.Position + offset,
                    };
                    data.FloatParams.AddRange(src.FloatParams);
                    data.LongParams.AddRange(src.LongParams);
                    data.StringParams.AddRange(src.StringParams);
                    data.Note = src.Note;
                    Asset.Nodes.Add(data);
                    AddElement(new BTNodeUI(data, Asset, _ => MarkDirty()));
                }
                MarkDirty();
                evt.StopPropagation();
            }
        }

        /// <summary>折叠/展开迷你地图。</summary>
        private void ToggleMiniMap()
        {
            _miniMapCollapsed = !_miniMapCollapsed;
            if (_miniMapCollapsed)
            {
                // 折叠: 只显示小图标按钮
                _miniMap.style.display = UnityEngine.UIElements.DisplayStyle.None;
                _miniMapToggle.text = "▦";
            }
            else
            {
                _miniMap.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
                _miniMapToggle.text = "×";
            }
        }

        /// <summary>复制单个节点到剪贴板。</summary>
        public void CopyNode(BTNodeUI node)
        {
            _clipboard.Clear();
            _clipboard.Add(node.Data);
        }

        /// <summary>删除单个节点(含连线)。</summary>
        public void DeleteNode(BTNodeUI node)
        {
            Asset.Connections.RemoveAll(c => c.SourceNodeId == node.Data.NodeId || c.TargetNodeId == node.Data.NodeId);
            Asset.Nodes.RemoveAll(n => n.NodeId == node.Data.NodeId);
            RemoveElement(node);
            MarkDirty();
        }

        /// <summary>重新校验树并显示状态栏消息。</summary>
        public string ValidateAndGetSummary()
        {
            var issues = BTValidator.Validate(Asset);
            if (issues.Count == 0) return "校验通过";
            int errors = issues.Count(i => i.IsError);
            int warns = issues.Count - errors;
            return errors > 0 ? $"校验: {errors} 错误 {warns} 警告" : $"校验: {warns} 警告";
        }

        /// <summary>清空全部样式表(通过 count 循环移除官方 API)。</summary>
        private void ClearStyleSheets()
        {
            // styleSheets 不可枚举, 但 RemoveAt 不可用; 用已知主题文件路径过滤
            // 这里直接重建: 标记所有样式, 逐个 Remove 已知的
            var known = new List<StyleSheet>();
            var basePath = "Packages/com.hyc.framework/Editor/BT/BTGraph.uss";
            known.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(basePath));
            foreach (var tn in ThemeNames)
                known.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>($"Packages/com.hyc.framework/Editor/BT/Themes/BTGraph-{tn}.uss"));
            foreach (var ss in known)
            {
                if (ss != null && styleSheets.Contains(ss))
                    styleSheets.Remove(ss);
            }
        }

        /// <summary>切换主题(加载对应 USS)。</summary>
        public void ApplyTheme(string theme)
        {
            CurrentTheme = ThemeNames.Contains(theme) ? theme : DefaultTheme;
            // 移除所有样式, 重新加载基础 + 主题(styleSheets 不可枚举, 用反射清空)
            ClearStyleSheets();
            // 重新加载基础样式 + 新主题
            if (_baseStyle != null) styleSheets.Add(_baseStyle);
            var themeSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                $"Packages/com.hyc.framework/Editor/BT/Themes/BTGraph-{CurrentTheme}.uss");
            if (themeSheet != null)
                styleSheets.Add(themeSheet);
        }

        /// <summary>应用运行时轨迹(节点高亮)。traceNodes=节点索引, results=对应状态。</summary>
        public void ApplyRuntimeTrace(int[] traceNodes, HYC.Framework.BT.BTNodeState[] results, int count)
        {
            // 先清空所有节点高亮
            foreach (var n in this.nodes)
            {
                var node = n as BTNodeUI;
                if (node != null) node.SetRuntimeState(BTNodeUI.RuntimeState.None);
            }
            // 应用新轨迹
            for (int i = 0; i < count && i < traceNodes.Length; i++)
            {
                int idx = traceNodes[i];
                if (idx < 0 || idx >= Asset.Nodes.Count) continue;
                var ui = FindNodeByAssetIndex(idx);
                if (ui == null) continue;
                var st = (BTNodeUI.RuntimeState)(int)results[i];
                ui.SetRuntimeState(st);
            }
        }

        private BTNodeUI FindNodeByAssetIndex(int assetIndex)
        {
            if (assetIndex < 0 || assetIndex >= Asset.Nodes.Count) return null;
            long nodeId = Asset.Nodes[assetIndex].NodeId;
            foreach (var n in nodes)
            {
                var ui = n as BTNodeUI;
                if (ui != null && ui.Data.NodeId == nodeId) return ui;
            }
            return null;
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 1) return;
            var mousePos = evt.mousePosition;
            var graphPos = contentViewContainer.WorldToLocal(mousePos);

            var menu = new GenericMenu();
            AddNodeMenus(menu, graphPos);
            menu.ShowAsContext();
        }

        private void AddNodeMenus(GenericMenu menu, Vector2 pos)
        {
            menu.AddItem(new GUIContent("入口/开始"), false, () => AddNode(BTNodeType.Root, pos));
            menu.AddItem(new GUIContent("出口/结束"), false, () => AddNode(BTNodeType.End, pos));
            menu.AddSeparator("");

            menu.AddItem(new GUIContent("组合/顺序"), false, () => AddNode(BTNodeType.Sequence, pos));
            menu.AddItem(new GUIContent("组合/选择"), false, () => AddNode(BTNodeType.Selector, pos));
            menu.AddItem(new GUIContent("组合/并行"), false, () => AddNode(BTNodeType.Parallel, pos));
            menu.AddItem(new GUIContent("组合/随机选择"), false, () => AddNode(BTNodeType.RandomSelector, pos));
            menu.AddItem(new GUIContent("组合/随机顺序"), false, () => AddNode(BTNodeType.RandomSequence, pos));
            menu.AddSeparator("");

            menu.AddItem(new GUIContent("装饰/反转"), false, () => AddNode(BTNodeType.Invert, pos));
            menu.AddItem(new GUIContent("装饰/重复"), false, () => AddNode(BTNodeType.Repeat, pos));
            menu.AddItem(new GUIContent("装饰/直到成功"), false, () => AddNode(BTNodeType.UntilSuccess, pos));
            menu.AddItem(new GUIContent("装饰/直到失败"), false, () => AddNode(BTNodeType.UntilFail, pos));
            menu.AddItem(new GUIContent("装饰/总是成功"), false, () => AddNode(BTNodeType.AlwaysSuccess, pos));
            menu.AddItem(new GUIContent("装饰/总是失败"), false, () => AddNode(BTNodeType.AlwaysFail, pos));
            menu.AddItem(new GUIContent("装饰/冷却门"), false, () => AddNode(BTNodeType.CooldownGate, pos));
            menu.AddItem(new GUIContent("装饰/条件包"), false, () => AddNode(BTNodeType.Conditional, pos));
            menu.AddItem(new GUIContent("装饰/限时"), false, () => AddNode(BTNodeType.TimeLimit, pos));
            menu.AddItem(new GUIContent("装饰/子树"), false, () => AddNode(BTNodeType.SubTree, pos));
            menu.AddSeparator("");

            menu.AddItem(new GUIContent("条件/距离判断"), false, () => AddNode(BTNodeType.CheckDistance, pos));
            menu.AddItem(new GUIContent("条件/黑板判断"), false, () => AddNode(BTNodeType.CheckBlackboard, pos));
            menu.AddSeparator("");

            menu.AddItem(new GUIContent("动作/等待"), false, () => AddNode(BTNodeType.Wait, pos));
            menu.AddItem(new GUIContent("动作/空操作"), false, () => AddNode(BTNodeType.NoOp, pos));
            menu.AddSeparator("");

            // 游戏层自定义节点: 列出所有已扫描的类
            menu.AddSeparator("游戏层/");
            var nodeTypes = BTCustomNodeScanner.AllNodeTypes;
            if (nodeTypes.Count == 0)
            {
                menu.AddItem(new GUIContent("游戏层/新建自定义节点..."), false, () => BTNodeCreatorWindow.Open());
                menu.AddDisabledItem(new GUIContent("游戏层/(暂无, 点新建)"));
            }
            else
            {
                foreach (var t in nodeTypes)
                {
                    var node = (BTCustomNode)System.Activator.CreateInstance(t);
                    if (node.TreeKind != Asset.Kind) continue; // 只显示当前树类型的
                    menu.AddItem(new GUIContent($"游戏层/{node.NodeName}"), false,
                        () => AddCustomNode(t));
                }
                menu.AddSeparator("游戏层/");
                menu.AddItem(new GUIContent("游戏层/新建自定义节点..."), false, () => BTNodeCreatorWindow.Open());
            }
        }

        /// <summary>添加一个自定义节点(按类创建, 子类型来自类)。</summary>
        private void AddCustomNode(System.Type nodeType)
        {
            var node = (BTCustomNode)System.Activator.CreateInstance(nodeType);
            var data = new BTNodeData
            {
                NodeId = Guid.NewGuid().GetHashCode() & 0x7fffffff,
                Type = BTNodeType.GameCustom,
                Position = new Vector2(300, 200),
            };
            data.LongParams.Add(node.SubType);
            Asset.Nodes.Add(data);
            AddElement(new BTNodeUI(data, Asset, _ => MarkDirty()));
            MarkDirty();
        }

        private void AddNode(BTNodeType type, Vector2 pos)
        {
            var data = new BTNodeData
            {
                NodeId = Guid.NewGuid().GetHashCode() & 0x7fffffff,
                Type = type,
                Position = pos,
            };
            Asset.Nodes.Add(data);
            AddElement(new BTNodeUI(data, Asset, _ => MarkDirty()));
            MarkDirty();
        }

        private void LoadGraph()
        {
            foreach (var n in nodes.ToList()) RemoveElement(n);
            foreach (var e in edges.ToList()) RemoveElement(e);

            // 第一遍: 创建节点
            foreach (var data in Asset.Nodes)
            {
                var ui = new BTNodeUI(data, Asset, _ => MarkDirty());
                ui.BindGraph(this);
                AddElement(ui);
            }

            // 第二遍: 按连线同步动态端口数(顺序节点显示 已连+1 个端口)
            foreach (var n in nodes)
            {
                var node = n as BTNodeUI;
                if (node == null || !node.IsDynamicPorts) continue;
                int connected = CountConnections(node.Data.NodeId);
                node.SyncDynamicPorts(connected);
            }

            // 第三遍: 连线(按 PortIndex 选源端口)
            foreach (var conn in Asset.Connections)
            {
                var src = nodes.FirstOrDefault(n => (n as BTNodeUI)?.Data.NodeId == conn.SourceNodeId);
                var dst = nodes.FirstOrDefault(n => (n as BTNodeUI)?.Data.NodeId == conn.TargetNodeId);
                if (src == null || dst == null) continue;

                var srcNode = src as BTNodeUI;
                var dstNode = dst as BTNodeUI;
                if (srcNode == null || dstNode == null) continue;

                // 按 PortIndex 取源端口(动态节点用 OutputPorts[PortIndex])
                Port outPort = null;
                if (srcNode.OutputPorts.Count > conn.PortIndex)
                    outPort = srcNode.OutputPorts[conn.PortIndex];
                else if (srcNode.OutputPorts.Count > 0)
                    outPort = srcNode.OutputPorts[0];
                var inPort = dstNode.inputContainer.Q<Port>();
                if (outPort == null || inPort == null) continue;

                var edge = new Edge { output = outPort, input = inPort };
                outPort.Connect(edge);
                inPort.Connect(edge);
                AddElement(edge);
                // 边画完后再设色(edgeControl 此时已创建); 若仍为 null 交给下一次连线事件
                ApplyEdgeStyle(edge);
            }

            // 边画在节点之上(默认边 Layer 在节点 Layer 之下, 会被节点背景盖住)
            BringEdgesToFront();
        }

        /// <summary>设置边的颜色(蓝色)。edgeControl 创建晚于 AddElement, 延迟重试。</summary>
        private static void ApplyEdgeStyle(UnityEditor.Experimental.GraphView.Edge edge)
        {
            if (edge == null) return;
            if (edge.edgeControl == null)
            {
                // edgeControl 尚未创建, 下一帧再设
                edge.schedule.Execute(() => ApplyEdgeStyle(edge)).ExecuteLater(50);
                return;
            }
            var blue = new UnityEngine.Color(0.0f, 0.55f, 0.86f, 1f);
            edge.edgeControl.inputColor = blue;
            edge.edgeControl.outputColor = blue;
            edge.edgeControl.toCapColor = blue;
            edge.edgeControl.fromCapColor = blue;
            edge.edgeControl.MarkDirtyRepaint();
        }

        /// <summary>把全部边移到节点之上(解决节点背景盖住线)。延迟到 GraphView 完成布局后执行。</summary>
        private void BringEdgesToFront()
        {
            schedule.Execute(() =>
            {
                var cvc = contentViewContainer;
                if (cvc == null) return;
                for (int i = 0; i < cvc.childCount; i++)
                {
                    var layer = cvc[i];
                    bool hasEdge = false;
                    for (int j = 0; j < layer.childCount; j++)
                    {
                        if (layer[j] is Edge) { hasEdge = true; break; }
                    }
                    if (hasEdge && i != cvc.childCount - 1)
                    {
                        cvc.Remove(layer);
                        cvc.Add(layer);
                        break;
                    }
                }
            }).ExecuteLater(50);
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (change.edgesToCreate != null)
            {
                foreach (var edge in change.edgesToCreate)
                {
                    ApplyEdgeStyle(edge);
                    var src = edge.output.node as BTNodeUI;
                    var dst = edge.input.node as BTNodeUI;
                    if (src == null || dst == null) continue;
                    // PortIndex = 源节点输出端口序号(决定执行顺序)
                    int portIdx = src.OutputPorts.IndexOf(edge.output as Port);
                    if (portIdx < 0) portIdx = 0;
                    if (!Asset.Connections.Any(c => c.SourceNodeId == src.Data.NodeId && c.TargetNodeId == dst.Data.NodeId))
                    {
                        Asset.Connections.Add(new BTConnectionData { SourceNodeId = src.Data.NodeId, TargetNodeId = dst.Data.NodeId, PortIndex = portIdx });
                    }
                    src.SyncDynamicPorts(CountConnections(src.Data.NodeId));
                }
                MarkDirty();
            }

            if (change.elementsToRemove != null)
            {
                foreach (var element in change.elementsToRemove)
                {
                    if (element is Edge edge)
                    {
                        var src = edge.output?.node as BTNodeUI;
                        var dst = edge.input?.node as BTNodeUI;
                        if (src != null && dst != null)
                        {
                            Asset.Connections.RemoveAll(c => c.SourceNodeId == src.Data.NodeId && c.TargetNodeId == dst.Data.NodeId);
                            src.SyncDynamicPorts(CountConnections(src.Data.NodeId));
                        }
                    }
                    else if (element is BTNodeUI node)
                    {
                        if (node.Data.Type == BTNodeType.Root) continue; // Root 不可删除
                        Asset.Connections.RemoveAll(c => c.SourceNodeId == node.Data.NodeId || c.TargetNodeId == node.Data.NodeId);
                        Asset.Nodes.RemoveAll(n => n.NodeId == node.Data.NodeId);
                    }
                }
                MarkDirty();
            }

            return change;
        }

        private void MarkDirty()
        {
            EditorUtility.SetDirty(Asset);
            AssetDatabase.SaveAssets();
            _onDirty?.Invoke();
        }

        private int CountConnections(long sourceNodeId)
        {
            return Asset.Connections.Count(c => c.SourceNodeId == sourceNodeId);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var result = new List<Port>();
            foreach (var p in ports)
            {
                if (p == startPort || p.node == startPort.node) continue;
                // 方向相反才能连(输出→输入 / 输入←输出)
                if (p.direction == startPort.direction) continue;
                // 目标端口是 Single 且已连接则不兼容
                if (p.capacity == Port.Capacity.Single && p.connections.Count() > 0) continue;
                result.Add(p);
            }
            return result;
        }
    }
}
