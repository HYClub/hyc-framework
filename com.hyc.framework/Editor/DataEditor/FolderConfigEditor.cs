using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 文件夹视图编辑器：当选中左侧树的文件夹节点（<see cref="ConfigDataTreeFolderNode"/>）时，
    /// 由 <see cref="ConfigDataContainer"/> 接管右侧面板，显示该文件夹的概览（子文件夹数 / 资产总数 /
    /// 类型分布）+ 可管理资产列表（搜索 / 类型筛选 / 排序 / 打开 / 重命名 / 复制 / 删除 / 导出）。
    /// 所有写操作复用 <see cref="ConfigDataTree"/> 既有的 ID 派发 / 回收 / 重命名（FileUtil 搬家）逻辑，
    /// 不另造一份。
    /// </summary>
    public class FolderConfigEditor : ConfigDataEditor
    {
        private string mFolderGuid;
        private string mFolderPath;
        private string mFolderName;
        private string mCrumb;

        private string mSearch = "";
        private string mTypeFilter = "全部";
        private string mSort = "名称";

        private Vector2 mScroll;
        private readonly Dictionary<string, DateTime> mModifiedTime = new Dictionary<string, DateTime>();
        private int mLastFileCount = -1;

        private GUIStyle mTitleStyle;
        private GUIStyle mBigNumStyle;

        public override void Open(ConfigDataWindow window, TreeView tree, ConfigDataTreeNode treeNode, SerializedObject so)
        {
            base.Open(window, tree, treeNode, so);

            var folder = treeNode as ConfigDataTreeFolderNode;
            mFolderGuid = folder?.guid ?? "";
            mFolderPath = AssetDatabase.GUIDToAssetPath(mFolderGuid);
            mFolderName = folder?.displayName ?? Path.GetFileName(mFolderPath);

            // 面包屑：配置根目录 / 子路径
            var root = ConfigDataSettings.RootFolder;
            if (!string.IsNullOrEmpty(mFolderPath) && mFolderPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                mCrumb = "配置根目录 / " + mFolderPath.Substring(root.Length).TrimStart('/').Replace("/", " / ");
            else
                mCrumb = mFolderPath;

            RefreshMeta(tree as ConfigDataTree);
        }

        public override void Reload()
        {
            RefreshMeta(mTree as ConfigDataTree);
        }

        /// <summary>刷新资产修改时间缓存（避免每帧打文件系统）。文件夹内容变化（增删/改名/复制）后由 Reload 触发。</summary>
        private void RefreshMeta(ConfigDataTree tree)
        {
            mModifiedTime.Clear();
            if (tree == null)
                return;
            tree.GetFolderChildren(mFolderGuid, out _, out var files);
            var projectRoot = Path.GetDirectoryName(Application.dataPath).Replace("\\", "/");
            foreach (var f in files)
            {
                var p = AssetDatabase.GUIDToAssetPath(f.guid);
                if (string.IsNullOrEmpty(p))
                    continue;
                var full = Path.Combine(projectRoot, p).Replace("\\", "/");
                try
                {
                    mModifiedTime[f.guid] = File.GetLastWriteTime(full);
                }
                catch
                {
                    mModifiedTime[f.guid] = DateTime.MinValue;
                }
            }
            mLastFileCount = files.Count;
        }

        public override void OnGUI(float viewW, float viewH)
        {
            var tree = mTree as ConfigDataTree;
            if (tree == null)
                return;

            EnsureStyles();

            tree.GetFolderChildren(mFolderGuid, out var subfolders, out var files);
            if (files.Count != mLastFileCount)
                RefreshMeta(tree);

            // 面包屑 + 标题 + 元信息
            EditorGUILayout.LabelField(mCrumb, EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField("📂  " + mFolderName, mTitleStyle);
            EditorGUILayout.LabelField($"{subfolders.Count} 个子文件夹 · {files.Count} 个配置资产", EditorStyles.miniLabel);

            // 工具栏（固定，不随滚动）
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            mSearch = EditorGUILayout.TextField(mSearch, EditorStyles.toolbarTextField, GUILayout.Width(180));
            DrawTypeFilter(files);
            DrawSort();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ 新建配置", EditorStyles.toolbarButton))
                ConfigCreateWindow.ShowWindow(mFolderPath);
            if (GUILayout.Button("+ 新建子文件夹", EditorStyles.toolbarButton))
            {
                var ng = AssetDatabase.CreateFolder(mFolderPath, "未命名文件夹");
                if (string.IsNullOrEmpty(ng))
                    EditorUtility.DisplayDialog("创建失败", "创建子文件夹失败", "确定");
                else
                    mWindow?.RefreshTree();
            }
            EditorGUILayout.EndHorizontal();

            // 滚动区：概览卡 + 子文件夹 + 资产列表（整页滚动，矮窗不裁切顶部）
            mScroll = EditorGUILayout.BeginScrollView(mScroll);

            DrawOverviewCards(subfolders, files);

            // 子文件夹区（始终显示）
            EditorGUILayout.LabelField("子文件夹", EditorStyles.boldLabel);
            if (subfolders.Count == 0)
            {
                EditorGUILayout.LabelField("（无）", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                foreach (var sf in subfolders)
                {
                    var childPath = AssetDatabase.GUIDToAssetPath(sf.guid);
                    var count = string.IsNullOrEmpty(childPath)
                        ? 0
                        : AssetDatabase.FindAssets("", new[] { childPath }).Length;
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("📁  " + sf.displayName, GUILayout.ExpandWidth(true));
                    GUILayout.Label($"{count} 项", GUILayout.Width(70));
                    if (GUILayout.Button("打开", EditorStyles.miniButton, GUILayout.Width(48))) OpenFolder(tree, sf);
                    if (GUILayout.Button("重命名", EditorStyles.miniButton, GUILayout.Width(48))) tree.BeginRenameNode(sf);
                    if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(48))) tree.DeleteFolder(sf);
                    EditorGUILayout.EndHorizontal();
                }
            }

            var list = FilterAndSort(files);
            EditorGUILayout.LabelField($"配置资产 ({list.Count})", EditorStyles.boldLabel);

            // 表头：名称列设上限避免吃满宽度，操作列占满剩余并右对齐
            float typeW = 100f, modW = 92f, statusW = 60f;
            float nameW = Mathf.Clamp(viewW * 0.24f, 120f, 200f);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("名称", GUILayout.Width(nameW));
            GUILayout.Label("类型", GUILayout.Width(typeW));
            GUILayout.Label("修改时间", GUILayout.Width(modW));
            GUILayout.Label("状态", GUILayout.Width(statusW));
            GUILayout.Label("操作", GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            if (list.Count == 0)
                EditorGUILayout.LabelField("（无匹配资产）", EditorStyles.centeredGreyMiniLabel);

            foreach (var f in list)
            {
                EditorGUILayout.BeginHorizontal();
                // 名称：点击 = 打开资产
                if (GUILayout.Button(f.displayName, EditorStyles.label, GUILayout.Width(nameW)))
                    OpenAsset(tree, f);
                // 类型：点击 = 设为类型筛选
                var typeSel = mTypeFilter == f.DisplayPrefix;
                if (GUILayout.Button(f.DisplayPrefix, typeSel ? EditorStyles.toolbarButton : EditorStyles.miniButton, GUILayout.Width(typeW)))
                    mTypeFilter = typeSel ? "全部" : f.DisplayPrefix;
                GUILayout.Label(GetModifiedStr(f.guid), GUILayout.Width(modW));
                var dirty = IsDirty(f);
                GUILayout.Label(dirty ? "未保存" : "正常", dirty ? EditorStyles.boldLabel : EditorStyles.miniLabel, GUILayout.Width(statusW));
                // 操作：占满剩余宽度，按钮右对齐
                EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("打开", EditorStyles.miniButton, GUILayout.Width(44))) OpenAsset(tree, f);
                if (GUILayout.Button("重命名", EditorStyles.miniButton, GUILayout.Width(48))) tree.BeginRenameNode(f);
                if (GUILayout.Button("复制", EditorStyles.miniButton, GUILayout.Width(44))) tree.CloneAsset(f);
                if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(44))) tree.DeleteAsset(f);
                if (GUILayout.Button("导出", EditorStyles.miniButton, GUILayout.Width(44))) ExportAsset(f);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.HelpBox("点击资产名或「打开」= 在左侧树选中并打开该资产（复用现有行为）。", MessageType.None);
        }

        private void EnsureStyles()
        {
            if (mTitleStyle == null)
            {
                mTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 20 };
                mBigNumStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22 };
            }
        }

        /// <summary>顶部概览卡（与原型 A 一致）：子文件夹数 / 配置资产总数 / 资产类型分布 chips。</summary>
        private void DrawOverviewCards(List<ConfigDataTreeFolderNode> subfolders, List<ConfigDataTreeFileNode> files)
        {
            var groups = files.GroupBy(f => f.DisplayPrefix).ToDictionary(g => g.Key, g => g.Count());

            EditorGUILayout.BeginHorizontal();
            // 卡片 1：子文件夹
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.MinWidth(96));
            EditorGUILayout.LabelField(subfolders.Count.ToString(), mBigNumStyle);
            EditorGUILayout.LabelField("子文件夹", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            // 卡片 2：配置资产总数
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.MinWidth(96));
            EditorGUILayout.LabelField(files.Count.ToString(), mBigNumStyle);
            EditorGUILayout.LabelField("配置资产总数", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            // 卡片 3：资产类型分布
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("资产类型分布", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (var kv in groups)
            {
                var label = $"{kv.Key} ({kv.Value})";
                var selected = mTypeFilter == kv.Key;
                if (GUILayout.Button(label, selected ? EditorStyles.toolbarButton : EditorStyles.miniButton))
                    mTypeFilter = selected ? "全部" : kv.Key;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTypeFilter(List<ConfigDataTreeFileNode> files)
        {
            var opts = new List<string> { "全部" };
            foreach (var f in files)
                if (!opts.Contains(f.DisplayPrefix))
                    opts.Add(f.DisplayPrefix);
            var idx = Mathf.Max(0, opts.IndexOf(mTypeFilter));
            var newIdx = EditorGUILayout.Popup(idx, opts.ToArray(), GUILayout.Width(110));
            if (newIdx != idx)
                mTypeFilter = opts[newIdx];
        }

        private void DrawSort()
        {
            var opts = new[] { "名称", "修改时间", "类型" };
            var idx = Mathf.Max(0, Array.IndexOf(opts, mSort));
            var newIdx = EditorGUILayout.Popup(idx, opts, GUILayout.Width(80));
            if (newIdx != idx)
                mSort = opts[newIdx];
        }

        private List<ConfigDataTreeFileNode> FilterAndSort(List<ConfigDataTreeFileNode> files)
        {
            var list = files.Where(f =>
            {
                if (!string.IsNullOrEmpty(mSearch) &&
                    f.displayName.IndexOf(mSearch, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                if (mTypeFilter != "全部" && f.DisplayPrefix != mTypeFilter)
                    return false;
                return true;
            }).ToList();

            if (mSort == "名称")
                list.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));
            else if (mSort == "修改时间")
                list.Sort((a, b) => GetTime(a).CompareTo(GetTime(b)));
            else if (mSort == "类型")
                list.Sort((a, b) => string.Compare(a.DisplayPrefix, b.DisplayPrefix, StringComparison.OrdinalIgnoreCase) != 0
                    ? string.Compare(a.DisplayPrefix, b.DisplayPrefix, StringComparison.OrdinalIgnoreCase)
                    : string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        private DateTime GetTime(ConfigDataTreeFileNode f)
            => mModifiedTime.TryGetValue(f.guid, out var t) ? t : DateTime.MinValue;

        private string GetModifiedStr(string guid)
            => mModifiedTime.TryGetValue(guid, out var t) && t != DateTime.MinValue
                ? t.ToString("MM-dd HH:mm")
                : "-";

        private static bool IsDirty(ConfigDataTreeFileNode f)
        {
            var asset = f.GetAsset();
            return asset != null && EditorUtility.IsDirty(asset);
        }

        private static void OpenAsset(ConfigDataTree tree, ConfigDataTreeFileNode f)
        {
            var asset = f.GetAsset();
            if (asset != null)
                tree.SelectAsset(asset);
        }

        private void OpenFolder(ConfigDataTree tree, ConfigDataTreeFolderNode sf)
        {
            tree.SetSelection(new List<int> { sf.id });
            tree.FrameItem(sf.id);
            mWindow?.Repaint();
        }

        private static void ExportAsset(ConfigDataTreeFileNode f)
        {
            var asset = f.GetAsset();
            if (asset != null)
                ConfigExportService.ExportSingle(asset, true, true);
        }
    }
}
