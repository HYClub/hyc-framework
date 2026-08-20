using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置检查错误窗口：按资产分组的 TreeView + 搜索 + 级别过滤（Info/Warn/Error 计数）。
    /// 移植自 SD 的 BuildErrorWindow，去除游戏专用交互。
    /// </summary>
    public class BuildErrorWindow : EditorWindow
    {
        private static GUIContent IconInfo, IconWarn, IconError;
        private static GUIContent IconInfoMono, IconWarnMono, IconErrorMono;

        private List<CheckError> mErrors;
        private TreeView mTree;
        private SearchField mSearchField;
        private static readonly bool[] ShowLevel = { true, true, true };

        public static void OpenWindow(List<CheckError> errors)
        {
            var window = GetWindow<BuildErrorWindow>();
            window.titleContent = new GUIContent("配置检查错误");
            window.mErrors = errors;
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private static void LoadIcons()
        {
            if (IconInfo != null)
                return;
            IconInfo = EditorGUIUtility.IconContent("console.infoicon.sml");
            IconWarn = EditorGUIUtility.IconContent("console.warnicon.sml");
            IconError = EditorGUIUtility.IconContent("console.erroricon.sml");
            IconInfoMono = EditorGUIUtility.IconContent("console.infoicon.inactive.sml");
            IconWarnMono = EditorGUIUtility.IconContent("console.warnicon.inactive.sml");
            IconErrorMono = EditorGUIUtility.IconContent("console.erroricon.inactive.sml");
        }

        private void OnGUI()
        {
            LoadIcons();
            if (mErrors == null)
            {
                Close();
                return;
            }

            if (mTree == null)
            {
                mTree = new ErrorTreeView(new TreeViewState(), mErrors);
                mSearchField = new SearchField();
                mSearchField.downOrUpArrowKeyPressed += mTree.SetFocusAndEnsureSelectedItem;
            }

            int info = 0, warn = 0, error = 0;
            foreach (var e in mErrors)
            {
                if (e.Level == CheckErrorLevel.Info) info++;
                else if (e.Level == CheckErrorLevel.Warning) warn++;
                else error++;
            }

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            var searchRect = GUILayoutUtility.GetRect(0, 200, 18, 18, EditorStyles.toolbarSearchField);
            mTree.searchString = mSearchField.OnToolbarGUI(searchRect, mTree.searchString);
            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            ShowLevel[0] = GUILayout.Toggle(ShowLevel[0], new GUIContent(info.ToString(), info > 0 ? IconInfo.image : IconInfoMono.image), "ToolbarButton");
            ShowLevel[1] = GUILayout.Toggle(ShowLevel[1], new GUIContent(warn.ToString(), warn > 0 ? IconWarn.image : IconWarnMono.image), "ToolbarButton");
            ShowLevel[2] = GUILayout.Toggle(ShowLevel[2], new GUIContent(error.ToString(), error > 0 ? IconError.image : IconErrorMono.image), "ToolbarButtonRight");
            if (EditorGUI.EndChangeCheck())
                mTree.Reload();
            GUILayout.EndHorizontal();

            var rect = GUILayoutUtility.GetRect(0, 100000, 0, 100000);
            mTree.OnGUI(rect);
        }

        private class ErrorNode : TreeViewItem
        {
            public CheckError Error;
            public ErrorNode(int id, int depth, string name, CheckError error) : base(id, depth, name)
            {
                Error = error;
            }
        }

        private class ErrorTreeView : TreeView
        {
            private readonly List<CheckError> mErrors;

            public ErrorTreeView(TreeViewState state, List<CheckError> errors) : base(state)
            {
                mErrors = errors;
                Reload();
                ExpandAll();
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem { id = -1, depth = -1 };
                root.children = new List<TreeViewItem>();
                var id = 0;

                // 按 Group 分组
                var group2Node = new Dictionary<string, TreeViewItem>();
                foreach (var e in mErrors)
                {
                    if (!ShowLevel[(int)e.Level])
                        continue;

                    var groupName = string.IsNullOrEmpty(e.Group) ? "???" : e.Group;
                    if (!group2Node.TryGetValue(groupName, out var groupNode))
                    {
                        groupNode = new TreeViewItem(id++, 0, groupName);
                        root.children.Add(groupNode);
                        group2Node[groupName] = groupNode;
                    }

                    var icon = e.Level == CheckErrorLevel.Info ? IconInfo.image
                        : e.Level == CheckErrorLevel.Warning ? IconWarn.image
                        : IconError.image;
                    var node = new ErrorNode(id++, 1, e.Message, e) { icon = icon as Texture2D };
                    groupNode.AddChild(node);
                }

                showAlternatingRowBackgrounds = true;
                SetupDepthsFromParentsAndChildren(root);
                return root;
            }

            /// <summary>单击错误项：在数据编辑器中定位资产并高亮字段。</summary>
            protected override void SelectionChanged(IList<int> selectedIds)
            {
                if (selectedIds == null || selectedIds.Count <= 0)
                    return;
                var item = FindItem(selectedIds[0], rootItem);
                if (item is ErrorNode node && node.Error != null)
                {
                    LocateError(node.Error);
                }
            }

            private void LocateError(CheckError error)
            {
                if (error.Asset == null)
                    return;

                var window = EditorWindow.GetWindow<HYC.Framework.Config.Editor.ConfigDataWindow>(false, "数据配置", true);
                window.Show();
                window.FocusField(error.Asset, error.FieldName);
            }
        }
    }
}
