// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTNodeUI.cs
// 说明: 行为树节点视觉 - 在 GraphView 画布上显示一个节点
//       按节点类型显示不同参数面板
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
    public class BTNodeUI : Node
    {
        public BTNodeData Data { get; private set; }
        private BTTreeAsset _asset;
        private Action<BTNodeUI> _onChanged;
        public readonly List<Port> OutputPorts = new List<Port>();
        public bool IsDynamicPorts => Data.Type == BTNodeType.Sequence || Data.Type == BTNodeType.Selector
            || Data.Type == BTNodeType.RandomSelector || Data.Type == BTNodeType.RandomSequence;
        public bool IsTwoPorts => Data.Type == BTNodeType.Conditional;

        /// <summary>节点分类(决定视觉样式)。</summary>
        public enum NodeCategory { Entry, Composite, Decorator, Condition, Action, Custom }

        public NodeCategory Category
        {
            get
            {
                // 自定义节点: 用注册表的 Category(0=Custom, 1=Condition, 2=Action)
                if (Data.Type == BTNodeType.GameCustom && Data.LongParams.Count > 0
                    && BTGameNodeRegistry.TryGet(_asset.Kind, Data.LongParams[0], out var info)
                    && info.Category > 0)
                {
                    return info.Category == 1 ? NodeCategory.Condition : NodeCategory.Action;
                }
                return GetCategory(Data.Type, _asset);
            }
        }

        /// <summary>节点分类(全局静态, 供样式/调试用)。</summary>
        public static NodeCategory GetCategory(BTNodeType type, BTTreeAsset asset)
        {
            switch (type)
            {
                case BTNodeType.Root:
                case BTNodeType.End: return NodeCategory.Entry;
                case BTNodeType.Sequence:
                case BTNodeType.Selector:
                case BTNodeType.Parallel:
                case BTNodeType.RandomSelector:
                case BTNodeType.RandomSequence: return NodeCategory.Composite;
                case BTNodeType.Invert:
                case BTNodeType.Repeat:
                case BTNodeType.UntilSuccess:
                case BTNodeType.UntilFail:
                case BTNodeType.AlwaysSuccess:
                case BTNodeType.AlwaysFail:
                case BTNodeType.CooldownGate:
                case BTNodeType.Conditional:
                case BTNodeType.TimeLimit:
                case BTNodeType.SubTree: return NodeCategory.Decorator;
                case BTNodeType.CheckDistance:
                case BTNodeType.CheckBlackboard: return NodeCategory.Condition;
                case BTNodeType.GameCustom: return NodeCategory.Custom;
                default: return NodeCategory.Action;
            }
        }

        public BTNodeUI(BTNodeData data, BTTreeAsset asset, Action<BTNodeUI> onChanged)
        {
            Data = data;
            _asset = asset;
            _onChanged = onChanged;

            title = GetNodeTitle(data.Type);
            // 条件节点标题加 "?"(判断语义)
            if (GetCategory(data.Type, asset) == NodeCategory.Condition)
                title = GetNodeTitle(data.Type) + "?";
            SetPosition(new Rect(data.Position.x, data.Position.y, 180, 100));
            // 分类样式类(bt-composite/bt-decorator/bt-condition/bt-action/bt-entry/bt-custom)
            AddToClassList("bt-node-" + Category.ToString().ToLower());
            // 自定义节点: 标题用注册表名称(如 "找最近敌人")
            if (data.Type == BTNodeType.GameCustom && data.LongParams.Count > 0
                && BTGameNodeRegistry.TryGet(asset.Kind, data.LongParams[0], out var _info))
            {
                title = _info.Name;
            }

            // 输入/输出端口(控制流)。Root 无输入(唯一入口), End 无输出(终止)
            if (Data.Type != BTNodeType.Root)
            {
                // 输入端口: Single(一个输入只连一个节点, 避免多连)
                var inputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(float));
                inputPort.portName = "";
                inputContainer.Add(inputPort);
            }
            if (Data.Type != BTNodeType.End)
            {
                if (IsTwoPorts)
                {
                    // Conditional: 2 个固定端口(0=条件, 1=子树)
                    for (int i = 0; i < 2; i++) AddOutputPort();
                }
                else
                {
                    // 动态节点: 初始 1 个, 连上自动补; 其他: 单个
                    AddOutputPort();
                }
            }

            BuildParameterPanel();
            RefreshExpandedState();

            // 双击: 自定义节点打开对应 C# 源文件
            RegisterCallback<UnityEngine.UIElements.MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2 && Data.Type == BTNodeType.GameCustom)
                    OpenCustomNodeSource();
            });

            // 右键菜单: 断点/备注
            RegisterCallback<UnityEngine.UIElements.ContextualMenuPopulateEvent>(evt =>
            {
                int assetIndex = _asset.Nodes.IndexOf(Data);
                bool hasBp = HYC.Framework.BT.BTManager.IsBreakpoint(_asset.TreeId, assetIndex);
                evt.menu.AppendAction(hasBp ? "取消断点" : "设置断点", _ =>
                {
                    HYC.Framework.BT.BTManager.ToggleBreakpoint(_asset.TreeId, assetIndex);
                    UpdateBreakpointVisual(hasBp);
                });
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("复制", _ => { _graphRef?.CopyNode(this); });
                evt.menu.AppendAction("删除", _ => { _graphRef?.DeleteNode(this); });
            });
        }

        /// <summary>双击自定义节点: 打开对应 BTCustomNode 类的 .cs 源文件。</summary>
        private void OpenCustomNodeSource()
        {
            if (Data.LongParams.Count == 0) return;
            long sub = Data.LongParams[0];
            foreach (var t in HYC.Framework.BT.Editor.BTCustomNodeScanner.AllNodeTypes)
            {
                var node = (BTCustomNode)System.Activator.CreateInstance(t);
                if (node.TreeKind == _asset.Kind && node.SubType == sub)
                {
                    // 按类名找 .cs 脚本资产
                    var guids = UnityEditor.AssetDatabase.FindAssets($"{t.Name} t:MonoScript");
                    foreach (var g in guids)
                    {
                        var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                        if (path.EndsWith(".cs"))
                        {
                            UnityEditor.AssetDatabase.OpenAsset(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.MonoScript>(path));
                            return;
                        }
                    }
                }
            }
            UnityEngine.Debug.LogWarning($"[BT] 未找到子类型 #{sub} 对应的节点类源码");
        }

        private BTGraphView _graphRef;
        public void BindGraph(BTGraphView gv) => _graphRef = gv;

        /// <summary>断点视觉: 红点标记。</summary>
        private void UpdateBreakpointVisual(bool hadBreakpoint)
        {
            int assetIndex = _asset.Nodes.IndexOf(Data);
            bool now = HYC.Framework.BT.BTManager.IsBreakpoint(_asset.TreeId, assetIndex);
            if (now && !hadBreakpoint)
                AddToClassList("bt-breakpoint");
            else if (!now && hadBreakpoint)
                RemoveFromClassList("bt-breakpoint");
        }

        /// <summary>添加一个输出端口。返回端口。</summary>
        public Port AddOutputPort()
        {
            var port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(float));
            port.portName = OutputPorts.Count.ToString();
            OutputPorts.Add(port);
            outputContainer.Add(port);
            RefreshExpandedState();
            return port;
        }

        /// <summary>移除最末一个空输出端口。</summary>
        public void RemoveLastOutputPort()
        {
            if (OutputPorts.Count <= 1) return; // 至少保留 1 个空端口
            var last = OutputPorts[OutputPorts.Count - 1];
            // 只移除未连接的端口
            if (last.connections.Count() == 0)
            {
                OutputPorts.RemoveAt(OutputPorts.Count - 1);
                outputContainer.Remove(last);
                RefreshExpandedState();
            }
        }

        /// <summary>
        /// 动态端口: 确保端口数 = 已连接 + 1 个空端口。
        /// 连满自动补, 删线后收缩多余空端口。
        /// </summary>
        public void SyncDynamicPorts(int connectedCount)
        {
            if (!IsDynamicPorts) return;
            int target = connectedCount + 1; // 已连 + 1 空
            // 补: 端口不足则加
            while (OutputPorts.Count < target) AddOutputPort();
            // 收缩: 从末尾删未连接的空端口, 直到只剩 1 个空
            while (OutputPorts.Count > target)
            {
                var last = OutputPorts[OutputPorts.Count - 1];
                if (last.connections.Count() == 0)
                {
                    OutputPorts.RemoveAt(OutputPorts.Count - 1);
                    outputContainer.Remove(last);
                }
                else break; // 末尾端口已连接, 不删
            }
            RefreshExpandedState();
        }

        private void BuildParameterPanel()
        {
            var container = new VisualElement();
            container.style.marginTop = 6;

            switch (Data.Type)
            {
                case BTNodeType.Root:
                    container.Add(new Label("树的入口(唯一)"));
                    break;
                case BTNodeType.End:
                    container.Add(new Label("执行到此处终止"));
                    break;
                case BTNodeType.GameCustom:
                    if (Data.LongParams.Count == 0) Data.LongParams.Add(0);
                    long sub = Data.LongParams[0];
                    if (BTGameNodeRegistry.TryGet(_asset.Kind, sub, out var info))
                    {
                        // 按注册描述显示参数(参数从 Long[1]/Float[0] 开始)
                        if (info.Params != null)
                        {
                            for (int pi = 0; pi < info.Params.Length; pi++)
                            {
                                var pdesc = info.Params[pi];
                                BuildParamField(container, pdesc, pi);
                            }
                        }
                        else
                        {
                            container.Add(new Label(info.Description ?? ""));
                        }
                    }
                    else
                    {
                        // 未注册: 显示子类型下拉(列出所有同类自定义节点) + 警告
                        var options = new System.Collections.Generic.List<string>();
                        var values = new System.Collections.Generic.List<long>();
                        foreach (var t in HYC.Framework.BT.Editor.BTCustomNodeScanner.AllNodeTypes)
                        {
                            var n = (BTCustomNode)System.Activator.CreateInstance(t);
                            if (n.TreeKind != _asset.Kind) continue;
                            options.Add($"{n.NodeName} (#{n.SubType})");
                            values.Add(n.SubType);
                        }
                        if (options.Count == 0)
                        {
                            container.Add(new Label("无可用自定义节点"));
                        }
                        else
                        {
                            int cur = System.Math.Max(0, values.IndexOf(sub));
                            var popup = new PopupField<string>("子类型", options, cur);
                            popup.RegisterValueChangedCallback(e =>
                            {
                                int idx = options.IndexOf(e.newValue);
                                if (idx >= 0) { Data.LongParams[0] = values[idx]; Notify(); }
                            });
                            container.Add(popup);
                        }
                    }
                    break;

                case BTNodeType.Repeat:
                    EnsureLong(1, 3);
                    AddIntField(container, "次数", (int)Data.LongParams[0], v => { Data.LongParams[0] = v; Notify(); });
                    break;

                case BTNodeType.CooldownGate:
                    EnsureFloat(1, 1f);
                    AddFloatField(container, "冷却秒", Data.FloatParams[0], v => { Data.FloatParams[0] = v; Notify(); });
                    break;

                case BTNodeType.Wait:
                    EnsureFloat(1, 1f);
                    AddFloatField(container, "等待秒", Data.FloatParams[0], v => { Data.FloatParams[0] = v; Notify(); });
                    break;

                case BTNodeType.CheckDistance:
                    EnsureFloat(1, 1f);
                    EnsureLong(2, 0);
                    AddFloatField(container, "阈值", Data.FloatParams[0], v => { Data.FloatParams[0] = v; Notify(); });
                    AddEnumField(container, "比较", new[] { "小于", "大于" }, (int)Data.LongParams[1], v => { Data.LongParams[1] = v; Notify(); });
                    break;

                case BTNodeType.CheckBlackboard:
                    EnsureLong(2, 0);
                    AddEnumField(container, "期望", new[] { "False", "True" }, (int)Data.LongParams[1], v => { Data.LongParams[1] = v; Notify(); });
                    break;

                case BTNodeType.Conditional:
                    container.Add(new Label("端口0=条件, 端口1=子树"));
                    break;

                case BTNodeType.TimeLimit:
                    EnsureFloat(1, 1f);
                    AddFloatField(container, "时限秒", Data.FloatParams[0], v => { Data.FloatParams[0] = v; Notify(); });
                    break;

                case BTNodeType.SubTree:
                    EnsureLong(1, 0);
                    // 树 ID 下拉: 列出所有树
                    var trees = BTDataWindowHelpers.LoadAllTreeIds();
                    AddEnumField(container, "子树", trees.names, System.Array.IndexOf(trees.ids, Data.LongParams[0]), v =>
                    {
                        Data.LongParams[0] = trees.ids[v]; Notify();
                    });
                    break;

                case BTNodeType.Invert:
                case BTNodeType.AlwaysSuccess:
                case BTNodeType.AlwaysFail:
                case BTNodeType.UntilSuccess:
                case BTNodeType.UntilFail:
                    container.Add(new Label("包一个子节点"));
                    break;

                case BTNodeType.Sequence:
                case BTNodeType.Selector:
                case BTNodeType.Parallel:
                    container.Add(new Label("子节点按连线执行"));
                    break;

                default:
                    container.Add(new Label(GetNodeTitle(Data.Type)));
                    break;
            }

            // 节点备注(说明文字)
            var noteField = new TextField("备注") { value = Data.Note, multiline = true };
            noteField.RegisterValueChangedCallback(e => { Data.Note = e.newValue; Notify(); });
            container.Add(noteField);

            mainContainer.Add(container);
        }

        /// <summary>按参数描述构建一个字段。pi=参数序号: Long[1+pi] 或 Float[pi]。</summary>
        private void BuildParamField(VisualElement container, BTGameNodeParamDesc desc, int pi)
        {
            switch (desc.Kind)
            {
                case BTGameNodeParamKind.Float:
                    EnsureFloat(pi + 1, desc.DefaultFloat);
                    AddFloatField(container, desc.Name, Data.FloatParams[pi], v => { Data.FloatParams[pi] = v; Notify(); });
                    break;
                case BTGameNodeParamKind.Int:
                    EnsureLong(pi + 2, desc.DefaultLong); // Long[0]=subtype, 参数从 Long[1] 起
                    AddIntField(container, desc.Name, (int)Data.LongParams[pi + 1], v => { Data.LongParams[pi + 1] = v; Notify(); });
                    break;
                case BTGameNodeParamKind.Enum:
                    EnsureLong(pi + 2, desc.DefaultLong);
                    var opts = desc.Options != null && desc.Options.Length > 0 ? desc.Options : new[] { "无" };
                    AddEnumField(container, desc.Name, opts, (int)Data.LongParams[pi + 1], v => { Data.LongParams[pi + 1] = v; Notify(); });
                    break;
                case BTGameNodeParamKind.Attribute:
                {
                    EnsureLong(pi + 2, desc.DefaultLong);
                    // 属性枚举下拉: 用树的属性枚举脚本
                    var attrNames = BTAttributeEnum.GetNames(_asset);
                    var attrValues = BTAttributeEnum.GetValues(_asset);
                    if (attrNames.Length == 0)
                    {
                        container.Add(new Label("未设置属性枚举"));
                        break;
                    }
                    int curIdx = Array.IndexOf(attrValues, (int)Data.LongParams[pi + 1]);
                    if (curIdx < 0) curIdx = 0;
                    var popup = new PopupField<string>(desc.Name, new List<string>(attrNames), curIdx);
                    popup.RegisterValueChangedCallback(e =>
                    {
                        int idx = Array.IndexOf(attrNames, e.newValue);
                        if (idx >= 0) { Data.LongParams[pi + 1] = attrValues[idx]; Notify(); }
                    });
                    container.Add(popup);
                    break;
                }
            }
        }

        private void EnsureFloat(int count, float def)
        {
            while (Data.FloatParams.Count < count) Data.FloatParams.Add(def);
        }

        private void EnsureLong(int count, long def)
        {
            while (Data.LongParams.Count < count) Data.LongParams.Add(def);
        }

        private void AddFloatField(VisualElement container, string label, float initial, Action<float> onChanged)
        {
            var f = new FloatField(label);
            f.value = initial;
            f.RegisterValueChangedCallback(e => onChanged(e.newValue));
            container.Add(f);
        }

        private void AddIntField(VisualElement container, string label, int initial, Action<int> onChanged)
        {
            var f = new IntegerField(label);
            f.value = initial;
            f.RegisterValueChangedCallback(e => onChanged(e.newValue));
            container.Add(f);
        }

        private void AddEnumField(VisualElement container, string label, string[] options, int initial, Action<int> onChanged)
        {
            var f = new PopupField<string>(label, new List<string>(options), Mathf.Clamp(initial, 0, options.Length - 1));
            f.RegisterValueChangedCallback(e =>
            {
                int idx = Array.IndexOf(options, e.newValue);
                if (idx >= 0) onChanged(idx);
            });
            container.Add(f);
        }

        private void Notify() => _onChanged?.Invoke(this);

        // ---- 运行状态高亮(调试用) ----
        public enum RuntimeState { None, Running, Success, Failed }

        /// <summary>设置节点运行状态(高亮)。编辑器调试时由外部调用。</summary>
        public void SetRuntimeState(RuntimeState state)
        {
            RemoveFromClassList("bt-run-none");
            RemoveFromClassList("bt-run-running");
            RemoveFromClassList("bt-run-success");
            RemoveFromClassList("bt-run-failed");
            AddToClassList("bt-run-" + state.ToString().ToLower());
        }

        public static string GetNodeTitle(BTNodeType type)
        {
            switch (type)
            {
                case BTNodeType.Root: return "开始";
                case BTNodeType.End: return "结束";
                case BTNodeType.Sequence: return "顺序";
                case BTNodeType.Selector: return "选择";
                case BTNodeType.Parallel: return "并行";
                case BTNodeType.Invert: return "反转";
                case BTNodeType.Repeat: return "重复";
                case BTNodeType.UntilSuccess: return "直到成功";
                case BTNodeType.UntilFail: return "直到失败";
                case BTNodeType.AlwaysSuccess: return "总是成功";
                case BTNodeType.AlwaysFail: return "总是失败";
                case BTNodeType.CooldownGate: return "冷却门";
                case BTNodeType.Conditional: return "条件包";
                case BTNodeType.TimeLimit: return "限时";
                case BTNodeType.RandomSelector: return "随机选择";
                case BTNodeType.RandomSequence: return "随机顺序";
                case BTNodeType.SubTree: return "子树";
                case BTNodeType.CheckDistance: return "距离判断";
                case BTNodeType.CheckBlackboard: return "黑板判断";
                case BTNodeType.Wait: return "等待";
                case BTNodeType.NoOp: return "空操作";
                case BTNodeType.GameCustom: return "自定义";
                default: return type.ToString();
            }
        }
    }
}
