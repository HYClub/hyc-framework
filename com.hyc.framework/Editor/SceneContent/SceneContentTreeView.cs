using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 左栏树节点。<see cref="payload"/> 由窗口解释（类型定义 / 总览 GO / 分组为 null），
    /// 树本身不依赖窗口的私有数据结构。
    /// </summary>
    public sealed class SceneContentTreeNode : TreeViewItem
    {
        public int parentId;
        public object payload;
        public bool isGroup;
        public bool showPlus;
    }

    /// <summary>
    /// 左栏 TreeView。刻意照抄 <c>ConfigDataTree</c> 的行样式：
    /// <c>showAlternatingRowBackgrounds = true</c> 让整个 rect（**包括没有数据的空行**）都铺隔行底色，
    /// <c>rowHeight = singleLineHeight + 6</c>、行图标、垂直居中标签。
    /// 行内 ＋ 的画法照抄官方 Input System 包 <c>InputActionTreeView.RowGUI</c>。
    /// </summary>
    public sealed class SceneContentTreeView : TreeView
    {
        public static SceneContentTreeView Create() => new SceneContentTreeView(new TreeViewState());

        private List<SceneContentTreeNode> _nodes = new List<SceneContentTreeNode>();

        public event Action<SceneContentTreeNode> OnItemSelected;
        public event Action<SceneContentTreeNode> OnItemContextClicked;
        public event Action<SceneContentTreeNode> OnPlusClicked;
        public event Func<SceneContentTreeNode, bool> OnCanStartDrag;
        public event Action<IList<int>> OnStartDrag;

        private SceneContentTreeView(TreeViewState state) : base(state)
        {
        }

        /// <summary>窗口把带 parentId 的扁平节点交给树，树负责建层级并刷新。</summary>
        public void SetData(List<SceneContentTreeNode> nodes)
        {
            _nodes = nodes ?? new List<SceneContentTreeNode>();
            Reload();
            ExpandAll();
        }

        public SceneContentTreeNode Selected
        {
            get
            {
                var ids = GetSelection();
                if (ids.Count == 0)
                    return null;
                return FindItem(ids[0], rootItem) as SceneContentTreeNode;
            }
        }

        /// <summary>按 id 取节点；窗口在拖拽回调里用它取回 payload。</summary>
        public SceneContentTreeNode GetNode(int id) => FindItem(id, rootItem) as SceneContentTreeNode;

        /// <summary>按 payload 恢复选中（数据重建后调用）。</summary>
        public void SelectPayload(object payload)
        {
            foreach (var n in _nodes)
            {
                if (n.payload == payload)
                {
                    SetSelection(new[] { n.id });
                    return;
                }
            }
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new SceneContentTreeNode { id = -1, depth = -1, displayName = "Root" };
            var byId = new Dictionary<int, SceneContentTreeNode>();
            foreach (var n in _nodes)
                byId[n.id] = n;

            foreach (var n in _nodes)
            {
                if (n.parentId != 0 && byId.TryGetValue(n.parentId, out var parent))
                    parent.AddChild(n);
                else
                    root.AddChild(n);
            }
            if (root.children == null)
                root.children = new List<TreeViewItem>();

            rowHeight = EditorGUIUtility.singleLineHeight + 6;
            showAlternatingRowBackgrounds = true;
            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        private static GUIStyle _labelStyle;

        private static GUIStyle LabelStyle
        {
            get
            {
                if (_labelStyle == null)
                {
                    _labelStyle = new GUIStyle(EditorStyles.label);
                    _labelStyle.alignment = TextAnchor.MiddleLeft;
                }
                return _labelStyle;
            }
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var label = args.label;
            args.label = string.Empty;
            base.RowGUI(args);

            var node = args.item as SceneContentTreeNode;
            var plusW = node != null && node.showPlus ? 26f : 0f;
            var indent = GetContentIndent(args.item) + 16;
            var textRect = new Rect(args.rowRect.x + indent, args.rowRect.y,
                args.rowRect.width - indent - plusW, args.rowRect.height);
            GUI.Label(textRect, label, LabelStyle);

            if (plusW > 0f)
            {
                var buttonRect = new Rect(args.rowRect.xMax - 24, args.rowRect.y + 2,
                    22, args.rowRect.height - 4);
                if (GUI.Button(buttonRect, "＋", GUIStyle.none))
                    OnPlusClicked?.Invoke(node);
            }
        }

        protected override bool DoesItemMatchSearch(TreeViewItem item, string search)
        {
            if (base.DoesItemMatchSearch(item, search))
                return true;
            return false;
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            base.SelectionChanged(selectedIds);
            OnItemSelected?.Invoke(Selected);
        }

        protected override void ContextClickedItem(int id)
        {
            var node = FindItem(id, rootItem) as SceneContentTreeNode;
            if (node != null && !node.isGroup)
                OnItemContextClicked?.Invoke(node);
        }

        protected override bool CanStartDrag(CanStartDragArgs args)
        {
            var node = args.draggedItem as SceneContentTreeNode;
            if (node == null || node.isGroup)
                return false;
            return OnCanStartDrag != null && OnCanStartDrag(node);
        }

        protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
        {
            OnStartDrag?.Invoke(args.draggedItemIDs);
        }
    }
}
