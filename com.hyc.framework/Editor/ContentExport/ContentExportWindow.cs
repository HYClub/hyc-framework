using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.ContentExport.Editor
{
    /// <summary>
    /// ECS 内容导出窗口：顶部页签切 9 页，操作按钮放在各自页内。
    /// Groups 页为左树 + 可拖分栏 + 右详情，其余页满宽；样式全部取 Unity 默认 EditorStyles。
    /// </summary>
    public sealed class ContentExportWindow : EditorWindow
    {
        private enum Tab
        {
            Groups,
            Plan,
            Naming,
            Impact,
            Diff,
            Merge,
            Publish,
            Settings,
            Log
        }

        private static readonly string[] TabNames =
        {
            "Groups", "导出计划", "固定命名", "影响分析", "对比 Diff",
            "catalog 合并", "热更新 / 发布", "设置", "输出 Log"
        };

        private static readonly string[] DiffFilters = { "全部", "新增", "删除", "变更", "未变" };
        // 顺序与 ContentExportLogLevel 枚举一致（Info / Success / Warning / Error），过滤按下标减一映射。
        private static readonly string[] LogFilters = { "全部", "Info", "Success", "Warning", "Error" };

        private const float TabTop = 3f;
        private const float ToolRowHeight = 20f;

        // 页签高度跟随行高，高 DPI 下不被裁切。
        private static float TabHeight => EditorGUIUtility.singleLineHeight + 4f;
        private static float ContentTop => TabTop + TabHeight + 6f;

        private Tab _tab;
        private ContentExportGroupsDocument _doc;
        private ContentExportReleaseDocument _releases;
        private ContentExportLayoutCheck.LayoutCheckResult _layout;
        private CatalogSnapshot _catalog;
        private List<string> _orphans;
        private List<string> _missing;
        private ContentExportReport _lastReport;
        private string _target = "";

        private GroupTreeView _tree;
        private SearchField _searchField;
        private static float _listWidth = 300f;
        private bool _treeDirty;

        private string _selNodeId = "";
        private string _selEntryId = "";
        private Vector2 _scroll;
        private int _diffFilter;
        private int _logFilter;

        // ---------------------------------------------------------------- 生命周期

        public static void ShowWindow()
        {
            var window = GetWindow<ContentExportWindow>(false, "ECS Content Export", true);
            window.minSize = new Vector2(1040f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshAll();
        }

        private void OnGUI()
        {
            EnsureTree();
            if (_treeDirty)
            {
                _tree.SetData(_doc);
                _treeDirty = false;
            }

            var width = EditorGUIUtility.currentViewWidth;

            GUILayout.BeginArea(new Rect(0f, TabTop, width, TabHeight));
            var picked = GUILayout.Toolbar((int)_tab, TabNames);
            GUILayout.EndArea();

            if (picked != (int)_tab)
            {
                _tab = (Tab)picked;
                _scroll = Vector2.zero;
                Repaint();
            }

            if (_tab == Tab.Groups)
                DrawGroupsPage(width);
            else
                DrawSinglePage(width);
        }

        // ---------------------------------------------------------------- 页面骨架

        private void DrawSinglePage(float width)
        {
            var rect = new Rect(0f, ContentTop, width, Mathf.Max(1f, position.height - ContentTop));
            GUILayout.BeginArea(rect);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Plan: DrawPlanPage(); break;
                case Tab.Naming: DrawNamingPage(); break;
                case Tab.Impact: DrawImpactPage(); break;
                case Tab.Diff: DrawDiffPage(); break;
                case Tab.Merge: DrawMergePage(); break;
                case Tab.Publish: DrawPublishPage(); break;
                case Tab.Settings: DrawSettingsPage(); break;
                default: DrawLogPage(); break;
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawGroupsPage(float width)
        {
            var maxWidth = Mathf.Max(200f, width - 340f);
            _listWidth = Mathf.Clamp(_listWidth, 200f, maxWidth);

            GUILayout.BeginArea(new Rect(2f, ContentTop, _listWidth - 6f, ToolRowHeight));
            GUILayout.BeginHorizontal();
            _tree.searchString = _searchField.OnToolbarGUI(_tree.searchString);
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(22f)))
                NewGroup();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            var treeRect = new Rect(0f, ContentTop + ToolRowHeight, _listWidth,
                Mathf.Max(1f, position.height - ContentTop - ToolRowHeight));
            _tree.OnGUI(treeRect);
            HandleAssetDrop(treeRect);

            var splitRect = new Rect(treeRect.xMax - 4f, ContentTop, 8f, position.height - ContentTop);
            EditorGUIUtility.AddCursorRect(splitRect, MouseCursor.ResizeHorizontal);
            _listWidth += HYC.Framework.Config.Editor.GUIDrawer.SlideRect(splitRect, MouseCursor.ResizeHorizontal).x;

            var rightRect = new Rect(treeRect.xMax, ContentTop,
                width - treeRect.xMax, Mathf.Max(1f, position.height - ContentTop));
            if (rightRect.width < 2f)
                return;

            GUILayout.BeginArea(rightRect);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawGroupDetail();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- Groups 详情

        private void DrawGroupDetail()
        {
            var node = ContentExportGroups.FindNode(_doc, _selNodeId);
            if (node == null)
            {
                GUILayout.Label("未选择组", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(node.Name, EditorStyles.boldLabel);
            GUILayout.Label(node.Entries.Count + " 项", EditorStyles.miniLabel, GUILayout.Width(48f));
            GUILayout.Label(node.SetName ?? "", EditorStyles.miniLabel, GUILayout.Width(88f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("导出本组", EditorStyles.miniButton, GUILayout.Width(72f)))
                RunAndReport(() => ContentExportCLI.ExportGroupById(node.Id));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4f);
            DrawEntries(node);

            GUILayout.Space(6f);
            DrawInspector(node);
        }

        private void DrawEntries(ContentExportGroupNode node)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("", EditorStyles.miniLabel, GUILayout.Width(18f));
            GUILayout.Label("Asset", EditorStyles.miniLabel, GUILayout.Width(190f));
            GUILayout.Label("Type", EditorStyles.miniLabel, GUILayout.Width(56f));
            GUILayout.Label("Address", EditorStyles.miniLabel, GUILayout.Width(150f));
            GUILayout.Label("Set", EditorStyles.miniLabel, GUILayout.Width(80f));
            EditorGUILayout.EndHorizontal();

            if (node.Entries.Count == 0)
            {
                GUILayout.Label("空组", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            for (var i = 0; i < node.Entries.Count; i++)
            {
                var entry = node.Entries[i];
                EditorGUILayout.BeginHorizontal();

                var included = EditorGUILayout.Toggle(entry.Included, GUILayout.Width(18f));
                if (included != entry.Included)
                {
                    entry.Included = included;
                    ContentExportGroups.Save(_doc);
                }

                var style = entry.Id == _selEntryId ? EditorStyles.boldLabel : EditorStyles.label;
                if (GUILayout.Button(entry.Name, style, GUILayout.Width(190f)))
                {
                    _selEntryId = entry.Id;
                    _selNodeId = node.Id;
                }

                GUILayout.Label(entry.Type ?? "", EditorStyles.miniLabel, GUILayout.Width(56f));
                GUILayout.Label(entry.Address ?? "", EditorStyles.miniLabel, GUILayout.Width(150f));
                GUILayout.Label(entry.SetName ?? "", EditorStyles.miniLabel, GUILayout.Width(80f));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawInspector(ContentExportGroupNode node)
        {
            var entry = FindEntry(node, _selEntryId);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (entry != null)
            {
                GUILayout.Label(entry.Name, EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var address = EditorGUILayout.TextField("Address", entry.Address);
                var setName = EditorGUILayout.TextField("Set", entry.SetName);
                var template = EditorGUILayout.TextField(
                    new GUIContent("命名模板", "<guid> / <group> / <name>"), entry.NamingTemplate);
                var included = EditorGUILayout.Toggle("Include In Build", entry.Included);
                var pinned = EditorGUILayout.Toggle("Pin Logical ID", entry.PinLogicalId);
                if (EditorGUI.EndChangeCheck())
                {
                    entry.Address = address;
                    entry.SetName = setName;
                    entry.NamingTemplate = template;
                    entry.Included = included;
                    entry.PinLogicalId = pinned;
                    ContentExportGroups.Save(_doc);
                    _treeDirty = true;
                }
            }
            else
            {
                GUILayout.Label(node.Name + "（组）", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var name = EditorGUILayout.TextField("组名", node.Name);
                var setName = EditorGUILayout.TextField("Set", node.SetName);
                var template = EditorGUILayout.TextField(
                    new GUIContent("命名模板", "<guid> / <group> / <name>"), node.NamingTemplate);
                if (EditorGUI.EndChangeCheck())
                {
                    node.Name = name;
                    node.SetName = setName;
                    node.NamingTemplate = template;
                    ContentExportGroups.Save(_doc);
                    _treeDirty = true;
                }
            }

            EditorGUILayout.EndVertical();
        }

        // ---------------------------------------------------------------- 导出计划

        private void DrawPlanPage()
        {
            var entries = FlattenEntries(_doc);
            var included = CountIncluded(entries);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Export Selected (" + included + ")", GUILayout.Width(158f)))
                RunAndReport(ContentExportCLI.ExportSelectedNow);
            if (GUILayout.Button("Export All (" + entries.Count + ")", GUILayout.Width(132f)))
                RunAndReport(ContentExportCLI.ExportAllNow);
            if (GUILayout.Button("校验布局", GUILayout.Width(76f)))
                RunAndReport(ContentExportCLI.VerifyLayout);
            GUILayout.FlexibleSpace();
            GUILayout.Label(Shorten(_target, 46), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6f);
            DrawLastReport();

            GUILayout.Space(8f);
            GUILayout.Label("产物清单", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("", EditorStyles.miniLabel, GUILayout.Width(18f));
            GUILayout.Label("Asset", EditorStyles.miniLabel, GUILayout.Width(190f));
            GUILayout.Label("Address", EditorStyles.miniLabel, GUILayout.Width(160f));
            GUILayout.Label("Set", EditorStyles.miniLabel, GUILayout.Width(80f));
            GUILayout.Label("Type", EditorStyles.miniLabel, GUILayout.Width(56f));
            EditorGUILayout.EndHorizontal();

            if (entries.Count == 0)
            {
                GUILayout.Label("无条目", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                EditorGUILayout.BeginHorizontal();
                var next = EditorGUILayout.Toggle(entry.Included, GUILayout.Width(18f));
                if (next != entry.Included)
                {
                    entry.Included = next;
                    ContentExportGroups.Save(_doc);
                    _treeDirty = true;
                }
                GUILayout.Label(entry.Name, EditorStyles.label, GUILayout.Width(190f));
                GUILayout.Label(entry.Address ?? "", EditorStyles.miniLabel, GUILayout.Width(160f));
                GUILayout.Label(entry.SetName ?? "", EditorStyles.miniLabel, GUILayout.Width(80f));
                GUILayout.Label(entry.Type ?? "", EditorStyles.miniLabel, GUILayout.Width(56f));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        // ---------------------------------------------------------------- 固定命名

        private void DrawNamingPage()
        {
            if (_doc == null || _doc.Groups.Count == 0)
            {
                GUILayout.Label("无组", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            for (var i = 0; i < _doc.Groups.Count; i++)
                DrawNamingNode(_doc.Groups[i], 0);
        }

        private void DrawNamingNode(ContentExportGroupNode node, int depth)
        {
            EditorGUI.indentLevel += depth;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(node.Name, EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var template = EditorGUILayout.TextField(
                new GUIContent("模板", "<guid> / <group> / <name>"), node.NamingTemplate);
            if (EditorGUI.EndChangeCheck())
            {
                node.NamingTemplate = template;
                ContentExportGroups.Save(_doc);
            }
            GUILayout.Label(PreviewName(node), EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel -= depth;

            for (var i = 0; i < node.Children.Count; i++)
                DrawNamingNode(node.Children[i], depth + 1);
        }

        // ---------------------------------------------------------------- 影响分析

        private void DrawImpactPage()
        {
            GUILayout.Label("镜像布局", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (_layout == null)
            {
                GUILayout.Label("未检查", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("Entities 版本",
                    string.IsNullOrEmpty(_layout.EntitiesVersion) ? "-" : _layout.EntitiesVersion);
                EditorGUILayout.LabelField("官方指纹", Shorten(_layout.OfficialFingerprint, 48));
                EditorGUILayout.LabelField("镜像指纹", Shorten(_layout.MirrorFingerprint, 48));
                EditorGUILayout.LabelField("状态", _layout.Matched ? "一致" : "漂移");
            }
            EditorGUILayout.EndVertical();

            var entries = FlattenEntries(_doc);
            var sets = new HashSet<string>();
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Included && !string.IsNullOrEmpty(entries[i].SetName))
                    sets.Add(entries[i].SetName);
            }

            GUILayout.Space(8f);
            GUILayout.Label("本次导出", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("条目总数", entries.Count + " 条");
            EditorGUILayout.LabelField("本次勾选", CountIncluded(entries) + " 条");
            EditorGUILayout.LabelField("content set", sets.Count + " 个");
            EditorGUILayout.LabelField("孤儿文件", (_orphans != null ? _orphans.Count : 0) + " 个");
            EditorGUILayout.EndVertical();

            var orphanCount = _orphans != null ? _orphans.Count : 0;
            GUILayout.Space(8f);
            GUILayout.Label("孤儿文件 (" + orphanCount + ")", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (orphanCount == 0)
            {
                GUILayout.Label("无", EditorStyles.miniLabel);
            }
            else
            {
                var max = Mathf.Min(orphanCount, 40);
                for (var i = 0; i < max; i++)
                    GUILayout.Label(Shorten(_orphans[i], 88), EditorStyles.miniLabel);
                if (orphanCount > max)
                    GUILayout.Label("共 " + orphanCount + " 个", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        // ---------------------------------------------------------------- 对比 Diff

        private void DrawDiffPage()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Dry Run Diff", GUILayout.Width(120f)))
                RunAndReport(ContentExportCLI.DryRunDiffNow);
            GUILayout.FlexibleSpace();
            _diffFilter = GUILayout.Toolbar(_diffFilter, DiffFilters, GUILayout.Width(300f));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6f);

            if (_lastReport == null)
            {
                GUILayout.Label("未运行", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            if (_diffFilter == 0)
            {
                DrawStringList("新增", _lastReport.DiffAdded);
                DrawStringList("删除", _lastReport.DiffRemoved);
                DrawStringList("变更", _lastReport.DiffChanged);
                DrawStringList("未变", _lastReport.DiffUnchanged);
                return;
            }

            switch (_diffFilter)
            {
                case 1: DrawStringList("新增", _lastReport.DiffAdded); break;
                case 2: DrawStringList("删除", _lastReport.DiffRemoved); break;
                case 3: DrawStringList("变更", _lastReport.DiffChanged); break;
                default: DrawStringList("未变", _lastReport.DiffUnchanged); break;
            }
        }

        private void DrawStringList(string title, List<string> items)
        {
            var count = items != null ? items.Count : 0;
            GUILayout.Space(6f);
            GUILayout.Label(title + " (" + count + ")", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (count == 0)
            {
                GUILayout.Label("无", EditorStyles.miniLabel);
            }
            else
            {
                var max = Mathf.Min(count, 40);
                for (var i = 0; i < max; i++)
                    GUILayout.Label(Shorten(items[i], 88), EditorStyles.miniLabel);
                if (count > max)
                    GUILayout.Label("共 " + count + " 条", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        // ---------------------------------------------------------------- catalog 合并

        private void DrawMergePage()
        {
            var entries = FlattenEntries(_doc);

            GUILayout.Label("条目", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("基线条目", (_catalog != null ? _catalog.Count : 0) + " 条");
            EditorGUILayout.LabelField("本次勾选", CountIncluded(entries) + " 条");
            EditorGUILayout.LabelField("基线路径",
                Shorten(_catalog != null ? _catalog.SourcePath : "", 60));
            EditorGUILayout.EndVertical();

            GUILayout.Space(8f);
            GUILayout.Label("上次合并", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (_lastReport == null || _lastReport.MergeAdded + _lastReport.MergeOverwritten +
                _lastReport.MergeKept + _lastReport.MergeRemoved == 0)
            {
                GUILayout.Label("无", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField("覆盖", _lastReport.MergeOverwritten.ToString());
                EditorGUILayout.LabelField("新增", _lastReport.MergeAdded.ToString());
                EditorGUILayout.LabelField("保留", _lastReport.MergeKept.ToString());
                EditorGUILayout.LabelField("真删", _lastReport.MergeRemoved.ToString());
                EditorGUILayout.LabelField("set 重排", _lastReport.MergeReindexed.ToString());
                EditorGUILayout.LabelField("合并后", _lastReport.EntryCount + " 条");
            }
            EditorGUILayout.EndVertical();
        }

        // ---------------------------------------------------------------- 热更新 / 发布

        private void DrawPublishPage()
        {
            var current = _releases != null && !string.IsNullOrEmpty(_releases.CurrentVersion)
                ? _releases.CurrentVersion
                : "未发布";

            GUILayout.Label("当前", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("版本", ContentExportSettings.Version ?? "");
            EditorGUILayout.LabelField("渠道", ContentExportSettings.Channel ?? "");
            EditorGUILayout.LabelField("灰度", ContentExportSettings.RolloutPercent + " %");
            EditorGUILayout.LabelField("指针", current);
            EditorGUILayout.EndVertical();

            GUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("校验本地完整性", EditorStyles.miniButton, GUILayout.Width(120f)))
                VerifyIntegrity();
            if (GUILayout.Button("刷新", EditorStyles.miniButton, GUILayout.Width(52f)))
                RefreshAll();
            GUILayout.Label(_missing == null ? "" : ("缺失 " + _missing.Count + " 个"), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (_missing != null && _missing.Count > 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                var max = Mathf.Min(_missing.Count, 30);
                for (var i = 0; i < max; i++)
                    GUILayout.Label(Shorten(_missing[i], 80), EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            GUILayout.Space(8f);
            GUILayout.Label("版本历史", EditorStyles.boldLabel);
            if (_releases == null || _releases.Records.Count == 0)
            {
                GUILayout.Label("无记录", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            for (var i = _releases.Records.Count - 1; i >= 0; i--)
            {
                var record = _releases.Records[i];
                var isCurrent = record.Version == _releases.CurrentVersion;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(record.Version, EditorStyles.boldLabel, GUILayout.Width(120f));
                GUILayout.Label(record.Channel ?? "", EditorStyles.miniLabel, GUILayout.Width(70f));
                GUILayout.Label(record.EntryCount + " 条", EditorStyles.miniLabel, GUILayout.Width(60f));
                GUILayout.Label(isCurrent ? "当前" : record.CreatedAt ?? "", EditorStyles.miniLabel, GUILayout.Width(120f));
                GUILayout.FlexibleSpace();
                if (!isCurrent)
                {
                    if (GUILayout.Button("切换指针", EditorStyles.miniButton, GUILayout.Width(70f)))
                        RunAndReport(() => ContentExportCLI.SwitchPointerToPath(record.FullCatalogRelativePath));
                    if (GUILayout.Button("回滚", EditorStyles.miniButton, GUILayout.Width(52f)))
                        RunAndReport(() => ContentExportCLI.RollbackTo(record.Version));
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Label(Shorten(record.FullCatalogRelativePath ?? "", 96), EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
        }

        // ---------------------------------------------------------------- 设置

        private void DrawSettingsPage()
        {
            // 设置单一真源在 HYC Settings（静态字段 + HYCSettingsGroup 特性自动挂载），
            // 这里只读展示并跳转，不复制第二份可编辑 UI。
            GUILayout.Label("Content Export / General", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 170f;
            EditorGUILayout.LabelField("构建产物目录", ContentExportSettings.SourceFolder ?? "");
            EditorGUILayout.LabelField("发布目录", ContentExportSettings.TargetFolder ?? "");
            EditorGUILayout.LabelField("CDN URL Root", ContentExportSettings.RemoteUrlRoot ?? "");
            EditorGUILayout.LabelField("Default Content Set", ContentExportSettings.DefaultSetName ?? "");
            EditorGUILayout.LabelField("业务版本号", ContentExportSettings.Version ?? "");
            EditorGUILayout.LabelField("渠道", ContentExportSettings.Channel ?? "");
            EditorGUILayout.LabelField("灰度比例", ContentExportSettings.RolloutPercent + " %");
            EditorGUILayout.LabelField("导出后切换 catalogs.bin", ContentExportSettings.SwitchPointer ? "是" : "否");
            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUILayout.EndVertical();

            GUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开 HYC Settings", EditorStyles.miniButton, GUILayout.Width(120f)))
                HYC.Framework.Editor.HYCEditorSettingsWindow.Open();
            if (GUILayout.Button("刷新", EditorStyles.miniButton, GUILayout.Width(52f)))
                RefreshAux();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- 输出 Log

        private void DrawLogPage()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(52f)))
            {
                ContentExportLog.Clear();
                _lastReport = null;
            }
            if (GUILayout.Button("打开报告", EditorStyles.miniButton, GUILayout.Width(72f)))
            {
                var path = ContentExportReportWriter.FilePath;
                if (System.IO.File.Exists(path))
                    EditorUtility.RevealInFinder(path);
            }
            GUILayout.FlexibleSpace();
            _logFilter = GUILayout.Toolbar(_logFilter, LogFilters, GUILayout.Width(290f));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6f);

            var lines = ContentExportLog.Snapshot();
            if (lines.Count == 0)
            {
                GUILayout.Label("无日志", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (_logFilter != 0 && (int)line.Level != _logFilter - 1)
                    continue;
                GUILayout.Label(line.Time + "  [" + line.Label + "]  " + line.Message, EditorStyles.miniLabel);
            }
        }

        // ---------------------------------------------------------------- 通用小部件

        private void DrawLastReport()
        {
            if (_lastReport == null)
            {
                GUILayout.Label("未运行", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var type = _lastReport.Success ? MessageType.Info : MessageType.Error;
            var text = _lastReport.Command + " · " + (_lastReport.Success ? "成功" : "失败") +
                       " · " + _lastReport.Timestamp +
                       "\n条目 " + _lastReport.EntryCount + " · 增 " + _lastReport.Added +
                       " / 删 " + _lastReport.Removed + " / 改 " + _lastReport.Changed +
                       " / 未变 " + _lastReport.Unchanged;
            if (!string.IsNullOrEmpty(_lastReport.Message))
                text += "\n" + _lastReport.Message;
            EditorGUILayout.HelpBox(text, type);
        }

        // ---------------------------------------------------------------- 数据

        private void RefreshAll()
        {
            _doc = ContentExportGroups.Load();
            _treeDirty = true;
            RefreshAux();
        }

        private void RefreshAux()
        {
            _releases = ContentExportReleases.Load();
            _layout = ContentExportLayoutCheck.Verify();
            _target = ContentExportSettings.ResolveProjectPath(ContentExportSettings.TargetFolder);
            var catalogPath = ContentCatalogIO.ResolveFullCatalogPath(_target);
            _catalog = string.IsNullOrEmpty(catalogPath) ? null : ContentCatalogIO.ReadCatalog(catalogPath);
            _orphans = _catalog == null
                ? new List<string>()
                : ContentCatalogIO.FindOrphanFiles(_target, _catalog);
            _missing = null;
            _lastReport = ReadLastReport();
        }

        private void VerifyIntegrity()
        {
            ContentExportLog.Clear();
            _missing = ContentExportPipeline.VerifyLocalIntegrity(_target, _catalog);
            ContentExportLog.Add(ContentExportLogLevel.Info,
                "本地完整性校验：catalog " + (_catalog != null ? _catalog.Count : 0) +
                " 条，缺失 hash 文件 " + _missing.Count + " 个。");
        }

        private ContentExportReport ReadLastReport()
        {
            try
            {
                var path = ContentExportReportWriter.FilePath;
                if (!System.IO.File.Exists(path))
                    return null;
                var text = System.IO.File.ReadAllText(path).TrimStart('\uFEFF');
                return Newtonsoft.Json.JsonConvert.DeserializeObject<ContentExportReport>(text);
            }
            catch
            {
                return null;
            }
        }

        private void RunAndReport(System.Action action)
        {
            ContentExportLog.Clear();
            action();
            RefreshAll();
            Repaint();
        }

        private void EnsureTree()
        {
            if (_searchField == null)
                _searchField = new SearchField();
            if (_tree != null)
                return;
            _tree = new GroupTreeView(new TreeViewState());
            _tree.OnSelected += OnTreeSelected;
            _tree.OnToggleInclude += _ => ContentExportGroups.Save(_doc);
            _tree.OnContext += ShowTreeContext;
            _searchField.downOrUpArrowKeyPressed += _tree.SetFocusAndEnsureSelectedItem;
            _treeDirty = true;
        }

        private void OnTreeSelected(object payload)
        {
            if (payload is ContentExportGroupNode node)
            {
                _selNodeId = node.Id;
                _selEntryId = "";
            }
            else if (payload is ContentExportGroupEntry entry)
            {
                _selEntryId = entry.Id;
                var parent = FindNodeOfEntry(entry.Id);
                if (parent != null)
                    _selNodeId = parent.Id;
            }
            Repaint();
        }

        private void ShowTreeContext(object payload)
        {
            var menu = new GenericMenu();
            if (payload is ContentExportGroupNode node)
            {
                menu.AddItem(new GUIContent("导出此组"), false,
                    () => RunAndReport(() => ContentExportCLI.ExportGroupById(node.Id)));
                menu.AddItem(new GUIContent("新建子组"), false, () => NewChildGroup(node));
                menu.AddItem(new GUIContent("删除此组"), false, () => DeleteGroup(node));
            }
            else if (payload is ContentExportGroupEntry entry)
            {
                menu.AddItem(new GUIContent("导出此条目"), false,
                    () => RunAndReport(() => ContentExportCLI.ExportEntryById(entry.Id)));
                menu.AddItem(new GUIContent("删除此条目"), false, () => RemoveEntry(entry.Id));
            }
            else
            {
                menu.AddItem(new GUIContent("新建组"), false, NewGroup);
                menu.AddItem(new GUIContent("刷新"), false, () =>
                {
                    RefreshAll();
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }

        private void HandleAssetDrop(Rect treeRect)
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;
            if (!treeRect.Contains(evt.mousePosition))
                return;
            if (DragAndDrop.objectReferences == null || DragAndDrop.objectReferences.Length == 0)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type != EventType.DragPerform)
            {
                Repaint();
                return;
            }

            DragAndDrop.AcceptDrag();
            var node = ContentExportGroups.FindNode(_doc, _selNodeId);
            if (node != null)
                AddDroppedAssets(node, DragAndDrop.objectReferences);
            evt.Use();
        }

        // ---------------------------------------------------------------- 组操作

        private void NewGroup()
        {
            if (_doc == null)
                _doc = ContentExportGroups.CreateDefault();
            var node = new ContentExportGroupNode
            {
                Id = NewId("g-"),
                Name = "New Group",
                NamingTemplate = "scene_<guid>.entities",
                SetName = string.IsNullOrEmpty(ContentExportSettings.DefaultSetName)
                    ? "all"
                    : ContentExportSettings.DefaultSetName
            };
            _doc.Groups.Add(node);
            Select(node.Id, "");
            ContentExportGroups.Save(_doc);
            _treeDirty = true;
        }

        private void NewChildGroup(ContentExportGroupNode parent)
        {
            if (parent == null)
                return;
            var node = new ContentExportGroupNode
            {
                Id = NewId("g-"),
                Name = "Sub Group",
                NamingTemplate = parent.NamingTemplate,
                SetName = parent.SetName
            };
            parent.Children.Add(node);
            Select(node.Id, "");
            ContentExportGroups.Save(_doc);
            _treeDirty = true;
        }

        private void DeleteGroup(ContentExportGroupNode node)
        {
            if (node == null || _doc == null)
                return;
            RemoveNode(_doc.Groups, node);
            if (_selNodeId == node.Id)
                Select("", "");
            ContentExportGroups.Save(_doc);
            _treeDirty = true;
        }

        private void RemoveEntry(string entryId)
        {
            if (string.IsNullOrEmpty(entryId) || _doc == null)
                return;
            RemoveEntryRecursive(_doc.Groups, entryId);
            if (_selEntryId == entryId)
                Select(_selNodeId, "");
            ContentExportGroups.Save(_doc);
            _treeDirty = true;
        }

        private void AddDroppedAssets(ContentExportGroupNode node, UnityEngine.Object[] references)
        {
            var added = 0;
            for (var i = 0; i < references.Length; i++)
            {
                var obj = references[i];
                if (obj == null)
                    continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path))
                    continue;

                var bare = System.IO.Path.GetFileNameWithoutExtension(path);
                var folder = (System.IO.Path.GetDirectoryName(path) ?? "").Replace('\\', '/');
                var relative = folder.StartsWith("Assets/") ? folder.Substring("Assets/".Length) : folder;
                if (!string.IsNullOrEmpty(relative) && !relative.EndsWith("/"))
                    relative += "/";
                var setName = string.IsNullOrEmpty(node.SetName) ? "all" : node.SetName;

                node.Entries.Add(new ContentExportGroupEntry
                {
                    Id = NewId("e-"),
                    Name = System.IO.Path.GetFileName(path),
                    AssetPath = path,
                    Address = setName + "/" + bare,
                    SourceRelativePath = relative,
                    Type = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
                    SetName = setName,
                    Included = true,
                    PinLogicalId = true
                });
                added++;
            }

            if (added == 0)
                return;
            ContentExportGroups.Save(_doc);
            _treeDirty = true;
            Repaint();
        }

        private void Select(string nodeId, string entryId)
        {
            _selNodeId = nodeId ?? "";
            _selEntryId = entryId ?? "";
        }

        // ---------------------------------------------------------------- 静态工具

        private static bool RemoveNode(List<ContentExportGroupNode> list, ContentExportGroupNode target)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == target)
                {
                    list.RemoveAt(i);
                    return true;
                }
                if (RemoveNode(list[i].Children, target))
                    return true;
            }
            return false;
        }

        private static bool RemoveEntryRecursive(List<ContentExportGroupNode> list, string entryId)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var node = list[i];
                for (var j = 0; j < node.Entries.Count; j++)
                {
                    if (node.Entries[j].Id != entryId)
                        continue;
                    node.Entries.RemoveAt(j);
                    return true;
                }
                if (RemoveEntryRecursive(node.Children, entryId))
                    return true;
            }
            return false;
        }

        private static List<ContentExportGroupEntry> FlattenEntries(ContentExportGroupsDocument doc)
        {
            var list = new List<ContentExportGroupEntry>();
            if (doc != null)
                for (var i = 0; i < doc.Groups.Count; i++)
                    FlattenRec(doc.Groups[i], list);
            return list;
        }

        private static void FlattenRec(ContentExportGroupNode node, List<ContentExportGroupEntry> list)
        {
            for (var i = 0; i < node.Entries.Count; i++)
                list.Add(node.Entries[i]);
            for (var i = 0; i < node.Children.Count; i++)
                FlattenRec(node.Children[i], list);
        }

        private static int CountIncluded(List<ContentExportGroupEntry> entries)
        {
            var total = 0;
            for (var i = 0; i < entries.Count; i++)
                if (entries[i].Included)
                    total++;
            return total;
        }

        private static ContentExportGroupEntry FindEntry(ContentExportGroupNode node, string entryId)
        {
            if (node == null || string.IsNullOrEmpty(entryId))
                return null;
            for (var i = 0; i < node.Entries.Count; i++)
                if (node.Entries[i].Id == entryId)
                    return node.Entries[i];
            for (var i = 0; i < node.Children.Count; i++)
            {
                var hit = FindEntry(node.Children[i], entryId);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        private ContentExportGroupNode FindNodeOfEntry(string entryId)
        {
            if (_doc == null)
                return null;
            for (var i = 0; i < _doc.Groups.Count; i++)
            {
                var hit = FindNodeOfEntryRec(_doc.Groups[i], entryId);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        private static ContentExportGroupNode FindNodeOfEntryRec(ContentExportGroupNode node, string entryId)
        {
            for (var i = 0; i < node.Entries.Count; i++)
                if (node.Entries[i].Id == entryId)
                    return node;
            for (var i = 0; i < node.Children.Count; i++)
            {
                var hit = FindNodeOfEntryRec(node.Children[i], entryId);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        private static string PreviewName(ContentExportGroupNode node)
        {
            var template = string.IsNullOrEmpty(node.NamingTemplate)
                ? "<group>_<guid>.entities"
                : node.NamingTemplate;
            var sample = node.Entries.Count > 0
                ? System.IO.Path.GetFileNameWithoutExtension(node.Entries[0].Name)
                : node.Name;
            return template
                .Replace("<guid>", "3f9a1c")
                .Replace("<group>", node.Name ?? "group")
                .Replace("<name>", sample ?? "asset");
        }

        private static string NewId(string prefix)
        {
            return prefix + System.Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (value.Length <= max)
                return value;
            return value.Substring(0, max - 1) + "…";
        }

        // ================================================================
        //  Groups 树
        // ================================================================

        /// <summary>Unity 的 TreeViewItem 只有 id / depth / displayName / children…，节点载荷自己挂。</summary>
        private sealed class ExportTreeNode : TreeViewItem
        {
            public object payload;
        }

        private sealed class GroupTreeView : TreeView
        {
            public event System.Action<object> OnSelected;
            public event System.Action<object> OnToggleInclude;
            public event System.Action<object> OnContext;

            private ContentExportGroupsDocument _doc;

            public GroupTreeView(TreeViewState state) : base(state)
            {
                rowHeight = EditorGUIUtility.singleLineHeight + 6f;
                showAlternatingRowBackgrounds = true;
            }

            public void SetData(ContentExportGroupsDocument doc)
            {
                _doc = doc;
                Reload();
                ExpandAll();
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new ExportTreeNode { id = 0, depth = -1, displayName = "root" };
                var next = 1;
                if (_doc != null)
                    for (var i = 0; i < _doc.Groups.Count; i++)
                        BuildGroup(root, _doc.Groups[i], 0, ref next);
                if (root.children == null)
                    root.children = new List<TreeViewItem>();
                SetupDepthsFromParentsAndChildren(root);
                return root;
            }

            private static void BuildGroup(TreeViewItem parent, ContentExportGroupNode node, int depth, ref int next)
            {
                var item = new ExportTreeNode
                {
                    id = next++, depth = depth, displayName = node.Name, payload = node
                };
                parent.AddChild(item);
                for (var i = 0; i < node.Children.Count; i++)
                    BuildGroup(item, node.Children[i], depth + 1, ref next);
                for (var i = 0; i < node.Entries.Count; i++)
                    item.AddChild(new ExportTreeNode
                    {
                        id = next++, depth = depth + 1, displayName = node.Entries[i].Name,
                        payload = node.Entries[i]
                    });
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                var nodeItem = args.item as ExportTreeNode;
                var payload = nodeItem != null ? nodeItem.payload : null;
                args.label = string.Empty;
                base.RowGUI(args);

                var indent = GetContentIndent(args.item);
                var row = args.rowRect;

                if (payload is ContentExportGroupEntry entry)
                {
                    var toggleRect = new Rect(row.x + indent, row.y + 1f, 16f, row.height - 2f);
                    var included = EditorGUI.Toggle(toggleRect, entry.Included);
                    if (included != entry.Included)
                    {
                        entry.Included = included;
                        OnToggleInclude?.Invoke(entry);
                    }
                    var nameRect = new Rect(toggleRect.xMax + 2f, row.y, Mathf.Max(1f, row.width - indent - 104f), row.height);
                    EditorGUI.LabelField(nameRect, entry.Name, EditorStyles.label);
                    var setRect = new Rect(row.xMax - 100f, row.y, 96f, row.height);
                    EditorGUI.LabelField(setRect, entry.SetName ?? "", EditorStyles.miniLabel);
                }
                else if (payload is ContentExportGroupNode node)
                {
                    var nameRect = new Rect(row.x + indent, row.y, Mathf.Max(1f, row.width - indent - 104f), row.height);
                    EditorGUI.LabelField(nameRect, node.Name, EditorStyles.boldLabel);
                    var countRect = new Rect(row.xMax - 100f, row.y, 28f, row.height);
                    EditorGUI.LabelField(countRect, node.Entries.Count.ToString(), EditorStyles.miniLabel);
                    var setRect = new Rect(row.xMax - 70f, row.y, 66f, row.height);
                    EditorGUI.LabelField(setRect, node.SetName ?? "", EditorStyles.miniLabel);
                }
            }

            protected override void SelectionChanged(IList<int> selectedIds)
            {
                base.SelectionChanged(selectedIds);
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    OnSelected?.Invoke(null);
                    return;
                }
                var item = FindItem(selectedIds[0], rootItem) as ExportTreeNode;
                OnSelected?.Invoke(item != null ? item.payload : null);
            }

            protected override void ContextClickedItem(int id)
            {
                var item = FindItem(id, rootItem) as ExportTreeNode;
                if (item != null)
                    OnContext?.Invoke(item.payload);
            }
        }
    }
}
