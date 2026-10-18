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

        /// <summary>
        /// 本次导出操作里是否已经汇报过内容（开过明细窗 / 批模式打过日志）。
        /// 用于「导出后总是汇报检查结果」只在**零检查项**时给一次确认——
        /// 否则逐类型、逐资产会弹出 N 个重复对话框。
        /// </summary>
        private static bool s_reportedInOperation;

        /// <summary>
        /// 顶层导出操作开始时调用，清掉标记。由 <see cref="ConfigExportService"/> 的三个入口
        /// （单实例 / 文件夹 / 全量清理导出）在各自导出循环之前调用。
        /// </summary>
        public static void BeginExportOperation() => s_reportedInOperation = false;

        /// <summary>
        /// 顶层导出操作结束时调用：本次一条检查项都没有、且开启了
        /// <see cref="ConfigDataSettings.AlwaysShowCheckWindow"/> 时，弹一句「未发现问题」确认。
        /// <para>已汇报过内容（有检查项）则不重复弹；批模式不弹；导出本身失败也不弹
        /// ——避免「目录无效」这类根本没进检查的失败被谎报成「检查通过」。</para>
        /// </summary>
        public static void EndExportOperation(string context, bool succeeded)
        {
            if (s_reportedInOperation || !succeeded)
                return;
            if (Application.isBatchMode)
                return;
            if (!ConfigDataSettings.AlwaysShowCheckWindow)
                return;
            EditorUtility.DisplayDialog(context, "检查完成：未发现问题。", "确定");
        }

        /// <summary>
        /// 报告检查结果：交互模式开错误窗口；**批模式（CI）走 Console**——
        /// EditorWindow 在 batchmode 下不渲染，开窗无意义，错误必须落日志才能被 CI 捕获。
        /// 零检查项直接返回（弹窗反馈由 <see cref="EndExportOperation"/> 在操作结束时统一给）。
        /// </summary>
        public static void Report(List<CheckError> errors, string context)
        {
            if (errors == null || errors.Count == 0)
                return;

            s_reportedInOperation = true;

            if (Application.isBatchMode)
            {
                for (var i = 0; i < errors.Count; i++)
                {
                    var e = errors[i];
                    var msg = $"[{context}] [{e.Group}] {e.Message}";
                    if (e.Level == CheckErrorLevel.Error) Debug.LogError(msg);
                    else if (e.Level == CheckErrorLevel.Warning) Debug.LogWarning(msg);
                    else Debug.Log(msg);
                }
                return;
            }

            OpenWindow(errors);
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
