using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>Base node of the data editor tree; resolves the underlying asset.</summary>
    public abstract class ConfigDataTreeNode : TreeViewItem
    {
        public virtual UnityEngine.Object GetAsset() => null;
    }

    /// <summary>Folder node in the data editor tree.</summary>
    public sealed class ConfigDataTreeFolderNode : ConfigDataTreeNode
    {
        public string guid;

        public ConfigDataTreeFolderNode(string guid)
        {
            this.guid = guid;
        }
    }

    /// <summary>Config asset file node in the data editor tree.</summary>
    public sealed class ConfigDataTreeFileNode : ConfigDataTreeNode
    {
        public string guid;
        public Type type;

        public ConfigDataTreeFileNode(string guid, Type type)
        {
            this.guid = guid;
            this.type = type;
        }

        public override UnityEngine.Object GetAsset()
            => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(guid));

        /// <summary>
        /// Prefix text shown in the tree: "[装备] 测试装备". Templates always show
        /// "[配置模板]". displayName itself stays the original asset name so rename
        /// edits the clean name.
        /// </summary>
        public string DisplayPrefix
        {
            get
            {
                if (type == typeof(ConfigTemplate))
                    return "配置模板";
                if (type == typeof(ConfigEnumDefinition))
                    return "枚举";
                return ConfigTypeDisplay.GetShortName(type);
            }
        }

        /// <summary>
        /// Full tree label for this node. Templates render
        /// "[配置模板] DisplayName (ClassName)"; enums render "[枚举] DisplayName (ClassName)".
        /// 未生成代码的模板/枚举追加 " (未生成)"；资产有未保存修改时追加 " *"。
        /// </summary>
        public string BuildDisplayLabel(string originalLabel)
        {
            var label = BuildDisplayLabelCore(originalLabel);
            var asset = GetAsset();
            if (asset != null && EditorUtility.IsDirty(asset))
                label += " *";
            return label;
        }

        private string BuildDisplayLabelCore(string originalLabel)
        {
            if (type == typeof(ConfigTemplate))
            {
                var tpl = GetAsset() as ConfigTemplate;
                if (tpl != null)
                {
                    var label = $"[配置模板] {tpl.displayName} ({tpl.className})";
                    if (!ConfigTemplateCodeGen.IsGenerated(tpl))
                        label += " (未生成)";
                    return label;
                }
                return "[配置模板] " + originalLabel;
            }
            if (type == typeof(ConfigEnumDefinition))
            {
                var def = GetAsset() as ConfigEnumDefinition;
                if (def != null)
                {
                    var label = $"[枚举] {def.displayName} ({def.className})";
                    if (!ConfigEnumCodeGen.IsGenerated(def))
                        label += " (未生成)";
                    return label;
                }
                return "[枚举] " + originalLabel;
            }
            return $"[{DisplayPrefix}] {originalLabel}";
        }

        /// <summary>
        /// Full display path used for search, e.g. "示例/装备" (templates use their
        /// own displayName field). Matches against the last segment too.
        /// </summary>
        public string SearchName
        {
            get
            {
                if (type == typeof(ConfigTemplate))
                {
                    var tpl = GetAsset() as ConfigTemplate;
                    return tpl != null ? tpl.displayName : "";
                }
                if (type == typeof(ConfigEnumDefinition))
                {
                    var def = GetAsset() as ConfigEnumDefinition;
                    return def != null ? def.displayName : "";
                }
                var attr = type?.GetCustomAttributes(typeof(CfgAssetAttribute), true).FirstOrDefault() as CfgAssetAttribute;
                return attr != null ? attr.Name : "";
            }
        }
    }

    /// <summary>
    /// Left-side TreeView of the QK data editor: shows the config root folder's
    /// directory hierarchy plus every config asset inside, with right-click
    /// create/rename/duplicate/ping and drag-to-move.
    /// </summary>
    public class ConfigDataTree : TreeView
    {
        public static ConfigDataTree Create()
        {
            return new ConfigDataTree(new TreeViewState());
        }

        private ConfigDataTree(TreeViewState state) : base(state)
        {
        }

        /// <summary>Selection change callback (wired by the window).</summary>
        public event Action OnSelectionChanged;

        /// <summary>Current selected asset node (file or folder), or null.</summary>
        public ConfigDataTreeNode SelectedItem
        {
            get
            {
                var ids = GetSelection();
                if (ids.Count <= 0) return null;
                return FindItem(ids[0], rootItem) as ConfigDataTreeNode;
            }
        }

        /// <summary>是否存在任一资产处于未保存（dirty）状态（用于列表 * 标记）。</summary>
        public bool HasDirtyAssets()
        {
            var root = rootItem;
            if (root == null)
                return false;
            bool any = false;
            WalkDirty(root, ref any);
            return any;
        }

        private void WalkDirty(TreeViewItem item, ref bool any)
        {
            if (any)
                return;
            if (item is ConfigDataTreeFileNode f)
            {
                var asset = f.GetAsset();
                if (asset != null && EditorUtility.IsDirty(asset))
                {
                    any = true;
                    return;
                }
            }
            if (item.children != null)
            {
                foreach (var c in item.children)
                    WalkDirty(c, ref any);
            }
        }

        #region 节点ID

        private static int mFileID = 1;
        private static int mFolderID = int.MaxValue / 3 * 2;
        private static readonly Dictionary<string, int> mGuid2ID = new Dictionary<string, int>();
        private static readonly Dictionary<string, TreeViewItem> mGuid2FolderNode = new Dictionary<string, TreeViewItem>();

        private static int GetFolderID(string folder)
        {
            var guid = AssetDatabase.AssetPathToGUID(folder);
            if (mGuid2ID.TryGetValue(guid, out var id))
                return id;
            mFolderID++;
            mGuid2ID[guid] = mFolderID;
            return mFolderID;
        }

        private static int GetFileID(string file)
        {
            var guid = AssetDatabase.AssetPathToGUID(file);
            if (mGuid2ID.TryGetValue(guid, out var id))
                return id;
            mFileID++;
            mGuid2ID[guid] = mFileID;
            return mFileID;
        }

        #endregion

        #region 构建树

        protected override TreeViewItem BuildRoot()
        {
            mGuid2FolderNode.Clear();

            var folder = ConfigDataSettings.RootFolder;
            ConfigDataSettings.EnsureRootFolder();

            var guid = AssetDatabase.AssetPathToGUID(folder);
            var root = new ConfigDataTreeFolderNode(guid) { id = -1, depth = -1 };
            mGuid2FolderNode[guid] = root;

            InstallFolders(root, folder);
            InstallFiles(root, folder);

            mGuid2FolderNode.Clear();

            // TreeView requires a non-null children list even when empty.
            if (root.children == null)
                root.children = new List<TreeViewItem>();

            rowHeight = EditorGUIUtility.singleLineHeight + 6;
            showAlternatingRowBackgrounds = true;

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        private void InstallFolders(TreeViewItem parent, string folder)
        {
            foreach (var path in AssetDatabase.GetSubFolders(folder))
            {
                var name = Path.GetFileName(path);
                var guid = AssetDatabase.AssetPathToGUID(path);

                var node = new ConfigDataTreeFolderNode(guid)
                {
                    id = GetFolderID(path),
                    depth = parent.depth + 1,
                    displayName = name,
                    icon = (Texture2D)AssetDatabase.GetCachedIcon(path),
                };
                parent.AddChild(node);
                mGuid2FolderNode[guid] = node;

                InstallFolders(node, path);
            }
        }

        private void InstallFiles(TreeViewItem parent, string folder)
        {
            var items = GetCfgItems();
            if (items == null || items.Length <= 0)
                return;

            var filters = string.Join(" ", items.Select(r => $"t:{r.Type.Name}").Distinct().ToArray());
            var guids = AssetDatabase.FindAssets(filters, new[] { folder });
            foreach (var guid in guids)
            {
                var filePath = AssetDatabase.GUIDToAssetPath(guid);
                var fileFolder = AssetDatabase.AssetPathToGUID(Path.GetDirectoryName(filePath));
                if (!mGuid2FolderNode.TryGetValue(fileFolder, out var folderNode))
                    continue;

                var type = AssetDatabase.GetMainAssetTypeAtPath(filePath);
                var node = new ConfigDataTreeFileNode(guid, type)
                {
                    id = GetFileID(filePath),
                    depth = folderNode.depth + 1,
                    displayName = Path.GetFileNameWithoutExtension(filePath),
                    icon = (Texture2D)AssetDatabase.GetCachedIcon(filePath),
                };

                // 模板节点：应用模板配置的图标（自定义或内置）
                if (type == typeof(ConfigTemplate))
                {
                    var tpl = node.GetAsset() as ConfigTemplate;
                    var tplIcon = ConfigTemplateIcon.Resolve(tpl);
                    if (tplIcon != null)
                        node.icon = tplIcon as Texture2D;
                }

                folderNode.AddChild(node);
            }
        }

        /// <summary>All config asset types declared with <c>[CfgAsset]</c>, sorted by Order.</summary>
        private static CfgAssetItem[] GetCfgItems()
        {
            return TypeCache.GetTypesWithAttribute<CfgAssetAttribute>()
                .Where(r => typeof(ScriptableObject).IsAssignableFrom(r) && !r.IsAbstract && !r.IsGenericType)
                .SelectMany(r => r.GetCustomAttributes<CfgAssetAttribute>()
                    .Select(a => new CfgAssetItem { Name = a.Name, Order = a.Order, Unique = a.Unique, Type = r }))
                .OrderBy(r => r.Order)
                .ToArray();
        }

        private class CfgAssetItem
        {
            public string Name;
            public int Order;
            public Type Type;
            public bool Unique;
        }

        public static void ClearCfgItemsCache()
        {
        }

        #endregion

        #region 行绘制

        private static GUIStyle _labelStyle;

        private static GUIStyle LabelStyle
        {
            get
            {
                if (_labelStyle == null)
                {
                    _labelStyle = new GUIStyle(EditorStyles.label);
                    _labelStyle.alignment = TextAnchor.MiddleLeft; // 行高有留白，垂直居中
                }
                return _labelStyle;
            }
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var label = args.label;
            args.label = string.Empty;
            base.RowGUI(args);

            // 显示层动态加前缀，节点原名保持不动（重命名编辑的是原名）
            if (args.item is ConfigDataTreeFileNode file)
                label = file.BuildDisplayLabel(label);

            var indent = GetContentIndent(args.item) + 16;
            var textRect = new Rect(args.rowRect.x + indent, args.rowRect.y, args.rowRect.width - indent, args.rowRect.height);
            GUI.Label(textRect, label, LabelStyle);
        }

        #endregion

        #region 搜索

        protected override bool DoesItemMatchSearch(TreeViewItem item, string search)
        {
            if (base.DoesItemMatchSearch(item, search))
                return true;

            if (item is ConfigDataTreeFileNode file)
            {
                // 显示名路径（"装备/时装"）与最后一段（"时装"）都参与匹配
                var searchName = file.SearchName;
                if (!string.IsNullOrEmpty(searchName) &&
                    searchName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (!string.IsNullOrEmpty(file.DisplayPrefix) &&
                    file.DisplayPrefix.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        #endregion

        #region 右键菜单

        /// <summary>右键点击树空白区域（根目录菜单）。</summary>
        protected override void ContextClicked()
        {
            var root = rootItem;
            if (root is ConfigDataTreeFolderNode folder)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("创建配置"), false, () => OpenCreateWindow(folder));
                menu.AddItem(new GUIContent("创建配置模板"), false, () => CreateTemplateAsset(root));
                menu.AddItem(new GUIContent("创建枚举"), false, () => CreateEnumAsset(root));
                menu.AddItem(new GUIContent("创建目录"), false, () => CreateFolder(root));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("导出内部所有"), false, () => ExportFolderAll(folder));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("刷新"), false, () => Reload());
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("选择根目录资源"), false, () => PingGuid(folder.guid));
                menu.ShowAsContext();
            }
        }

        /// <summary>导出文件夹下所有配置（客户端+服务器）。</summary>
        private void ExportFolderAll(ConfigDataTreeFolderNode folder)
        {
            var path = AssetDatabase.GUIDToAssetPath(folder.guid);
            var count = ConfigExportService.ExportFolder(path, true, true);
            if (count <= 0)
                EditorUtility.DisplayDialog("导出", "未找到可导出的配置", "确定");
            else
                EditorUtility.DisplayDialog("导出", $"已导出 {count} 种配置到客户端/服务器目录", "确定");
        }

        /// <summary>导出单个配置实例（客户端+服务器）。</summary>
        private void ExportSingleAsset(ConfigDataTreeFileNode file)
        {
            var asset = file.GetAsset();
            if (ConfigExportService.ExportSingle(asset, true, true))
                EditorUtility.DisplayDialog("导出", $"已导出 {asset.name} 到客户端/服务器目录", "确定");
        }

        /// <summary>构建文件节点的右键菜单（供测试与后续扩展复用）。</summary>
        internal GenericMenu BuildFileContextMenu(ConfigDataTreeFileNode file)
        {
            var menu = new GenericMenu();
            if (file.type == typeof(ConfigTemplate))
            {
                // 模板节点：精简菜单 + 生成代码
                var tpl = file.GetAsset() as ConfigTemplate;
                if (tpl != null)
                {
                    var generated = ConfigTemplateCodeGen.IsGenerated(tpl);
                    menu.AddItem(new GUIContent(generated ? "重新生成代码" : "生成代码"), false, () => GenerateTemplateCode(tpl));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("刷新"), false, () => Reload());
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("重命名"), false, () => BeginRename(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("原地复制"), false, () => CloneAsset(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("删除"), false, () => DeleteAsset(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("选择资源"), false, () => PingGuid(file.guid));
            }
            else if (file.type == typeof(ConfigEnumDefinition))
            {
                // 枚举节点：生成代码 + 导出
                var def = file.GetAsset() as ConfigEnumDefinition;
                if (def != null)
                {
                    var generated = ConfigEnumCodeGen.IsGenerated(def);
                    menu.AddItem(new GUIContent(generated ? "重新生成代码" : "生成代码"), false, () => GenerateEnumCode(def));
                    menu.AddItem(new GUIContent("导出"), false, () => ExportEnum(def));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("刷新"), false, () => Reload());
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("重命名"), false, () => BeginRename(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("原地复制"), false, () => CloneAsset(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("删除"), false, () => DeleteAsset(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("选择资源"), false, () => PingGuid(file.guid));
            }
            else
            {
                menu.AddItem(new GUIContent("创建配置"), false, () => OpenCreateWindow(file));
                menu.AddItem(new GUIContent("创建配置模板"), false, () => CreateTemplateAsset(file));
                menu.AddItem(new GUIContent("创建枚举"), false, () => CreateEnumAsset(file));
                menu.AddItem(new GUIContent("创建目录"), false, () => CreateFolder(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("导出"), false, () => ExportSingleAsset(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("刷新"), false, () => Reload());
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("重命名"), false, () => BeginRename(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("原地复制"), false, () => CloneAsset(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("删除"), false, () => DeleteAsset(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("选择资源"), false, () => PingGuid(file.guid));
            }
            return menu;
        }

        /// <summary>生成枚举代码并刷新树（带错误提示）。</summary>
        private void GenerateEnumCode(ConfigEnumDefinition def)
        {
            // 先保存资产，避免"改了没保存就生成"的困惑
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            if (!ConfigEnumCodeGen.WriteFile(def, out var error))
            {
                EditorUtility.DisplayDialog("生成失败", error, "确定");
                return;
            }
            Reload();
        }

        /// <summary>导出枚举到客户端/服务器目录。</summary>
        private void ExportEnum(ConfigEnumDefinition def)
        {
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            if (ConfigEnumCodeGen.Export(def, true, true))
                EditorUtility.DisplayDialog("导出", $"已导出枚举 {def.className} 到客户端/服务器目录", "确定");
            else
                EditorUtility.DisplayDialog("导出失败", $"导出枚举 {def.className} 失败", "确定");
        }

        /// <summary>生成模板代码并刷新树（带错误提示）。</summary>
        private void GenerateTemplateCode(ConfigTemplate tpl)
        {
            if (!ConfigTemplateCodeGen.WriteFile(tpl, out var error))
            {
                EditorUtility.DisplayDialog("生成失败", error, "确定");
                return;
            }
            Reload();
        }

        protected override void ContextClickedItem(int id)
        {
            var item = FindItem(id, rootItem);
            if (item == null)
                return;

            var menu = new GenericMenu();

            if (item is ConfigDataTreeFolderNode folder)
            {
                menu.AddItem(new GUIContent("创建配置"), false, () => OpenCreateWindow(folder));
                menu.AddItem(new GUIContent("创建配置模板"), false, () => CreateTemplateAsset(item));
                menu.AddItem(new GUIContent("创建枚举"), false, () => CreateEnumAsset(item));
                menu.AddItem(new GUIContent("创建目录"), false, () => CreateFolder(item));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("刷新"), false, () => Reload());
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("重命名"), false, () => BeginRename(item));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("删除"), false, () => DeleteFolder(folder));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("选择资源"), false, () => PingGuid(folder.guid));
            }
            else if (item is ConfigDataTreeFileNode file)
            {
                menu = BuildFileContextMenu(file);
            }

            menu.ShowAsContext();

            // 消费右键事件，避免 ContextClicked（空白区）再次触发并覆盖本菜单
            if (Event.current != null)
                Event.current.Use();
        }

        protected override void DoubleClickedItem(int id)
        {
            var item = FindItem(id, rootItem);
            if (item is ConfigDataTreeFileNode file)
                PingGuid(file.guid);
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            OnSelectionChanged?.Invoke();
        }

        private void OpenCreateWindow(TreeViewItem item)
        {
            string targetFolder;

            if (item is ConfigDataTreeFolderNode folder)
            {
                targetFolder = AssetDatabase.GUIDToAssetPath(folder.guid);
            }
            else if (item is ConfigDataTreeFileNode file)
            {
                targetFolder = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(file.guid));
            }
            else
            {
                targetFolder = ConfigDataSettings.RootFolder;
            }

            ConfigCreateWindow.ShowWindow(targetFolder);
        }

        /// <summary>Creates a new template asset in the node's folder and selects it in the tree.</summary>
        private void CreateTemplateAsset(TreeViewItem item)
        {
            string targetFolder = ConfigDataSettings.RootFolder;

            if (item is ConfigDataTreeFolderNode folder)
            {
                targetFolder = AssetDatabase.GUIDToAssetPath(folder.guid);
            }
            else if (item is ConfigDataTreeFileNode file)
            {
                targetFolder = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(file.guid));
            }

            var tpl = ConfigTemplateCodeGen.CreateTemplateAsset(targetFolder);
            Reload();
            SelectAsset(tpl);
        }

        /// <summary>Creates a new enum definition asset in the node's folder and selects it in the tree.</summary>
        private void CreateEnumAsset(TreeViewItem item)
        {
            string targetFolder = ConfigDataSettings.RootFolder;

            if (item is ConfigDataTreeFolderNode folder)
            {
                targetFolder = AssetDatabase.GUIDToAssetPath(folder.guid);
            }
            else if (item is ConfigDataTreeFileNode file)
            {
                targetFolder = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(file.guid));
            }

            var def = ConfigEnumCodeGen.CreateEnumAsset(targetFolder);
            Reload();
            SelectAsset(def);
        }

        /// <summary>Reloads the tree and selects/frames the node for <paramref name="asset"/>.</summary>
        public void SelectAsset(UnityEngine.Object asset)
        {
            if (asset == null)
                return;

            Reload();

            var path = AssetDatabase.GetAssetPath(asset);
            var guid = AssetDatabase.AssetPathToGUID(path);
            var node = FindFileNode(rootItem, guid);
            if (node == null)
                return;

            var expanded = new List<int>();
            var parent = node.parent;
            while (parent != null)
            {
                expanded.Add(parent.id);
                parent = parent.parent;
            }
            SetExpanded(expanded);
            SetSelection(new List<int> { node.id });
            FrameItem(node.id);
            OnSelectionChanged?.Invoke();
        }

        private static ConfigDataTreeFileNode FindFileNode(TreeViewItem item, string guid)
        {
            if (item is ConfigDataTreeFileNode f && f.guid == guid)
                return f;

            if (item.children != null)
            {
                foreach (var child in item.children)
                {
                    var result = FindFileNode(child, guid);
                    if (result != null)
                        return result;
                }
            }

            return null;
        }

        private void CreateFolder(TreeViewItem item)
        {
            if (item is ConfigDataTreeFileNode && item.parent is ConfigDataTreeFolderNode)
                item = item.parent;

            if (item is ConfigDataTreeFolderNode folder)
            {
                var guid = AssetDatabase.CreateFolder(AssetDatabase.GUIDToAssetPath(folder.guid), "未命名文件夹");
                if (!string.IsNullOrEmpty(guid))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var node = new ConfigDataTreeFolderNode(guid)
                    {
                        id = GetFolderID(path),
                        depth = item.depth + 1,
                        displayName = Path.GetFileName(path),
                        icon = (Texture2D)AssetDatabase.GetCachedIcon(path),
                    };
                    item.AddChild(node);
                    mGuid2FolderNode[guid] = node;

                    Reload();
                }
                else
                {
                    EditorUtility.DisplayDialog("创建失败", "创建文件夹失败", "确定");
                }
            }
        }

        private void CloneAsset(ConfigDataTreeFileNode item)
        {
            var fromPath = AssetDatabase.GUIDToAssetPath(item.guid);
            var type = item.type ?? AssetDatabase.GetMainAssetTypeAtPath(fromPath);
            if (type == null)
                return;

            var typeAttr = type.GetCustomAttributes(typeof(CfgAssetAttribute), true).FirstOrDefault() as CfgAssetAttribute;
            if (typeAttr != null && typeAttr.Unique)
            {
                if (HasAnyAssetOfType(type))
                {
                    EditorUtility.DisplayDialog("错误", "此类资源只能有一个，不允许复制！", "确定");
                    return;
                }
            }

            var folder = Path.GetDirectoryName(fromPath);
            var name = Path.GetFileNameWithoutExtension(fromPath);

            var targetPath = fromPath;
            var index = 1;
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(folder, $"{name} {index}.asset");
                index++;
            }

            // 统一用 CopyAsset 复制整个资产（含全部字段）
            if (!AssetDatabase.CopyAsset(fromPath, targetPath))
            {
                EditorUtility.DisplayDialog("克隆失败", "克隆文件失败", "确定");
                return;
            }

            // 复制必须派发新的 ID/GUID（绝对不重复）
            var copy = AssetDatabase.LoadAssetAtPath<ScriptableObject>(targetPath);
            if (copy != null)
            {
                try
                {
                    ConfigIdService.TryWriteId(copy, ConfigIdService.RequestIdSync());
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ConfigId] 复制后派发 ID 失败: {e.Message}");
                }
            }

            AssetDatabase.SaveAssets();
            Reload();
        }

        private void DeleteAsset(ConfigDataTreeFileNode item)
        {
            var path = AssetDatabase.GUIDToAssetPath(item.guid);
            if (EditorUtility.DisplayDialog("删除配置", $"确定删除 {Path.GetFileName(path)} ？", "删除", "取消"))
            {
                ReleaseIdOfPath(path);
                AssetDatabase.DeleteAsset(path);
                Reload();
            }
        }

        /// <summary>删除前把资产 ID 归还给构造器（本地记录回收，局域网通知服务器）。</summary>
        private static void ReleaseIdOfPath(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null)
                return;
            if (ConfigIdService.TryReadId(asset, out var id, out _))
                ConfigIdService.ReleaseIdSync(id);
        }

        /// <summary>删除文件夹（递归删除其中所有资产），根目录不可删。</summary>
        private void DeleteFolder(ConfigDataTreeFolderNode folder)
        {
            var path = AssetDatabase.GUIDToAssetPath(folder.guid);

            // 根目录保护
            if (string.Equals(path, ConfigDataSettings.RootFolder, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("无法删除", "配置根目录不能删除", "确定");
                return;
            }

            var subAssets = AssetDatabase.FindAssets("", new[] { path }).Length;
            var msg = subAssets > 0
                ? $"确定删除文件夹 {Path.GetFileName(path)} ？\n其中包含 {subAssets} 项资源，将一并删除。"
                : $"确定删除文件夹 {Path.GetFileName(path)} ？";
            if (EditorUtility.DisplayDialog("删除文件夹", msg, "删除", "取消"))
            {
                // 先归还文件夹内所有配置资产的 ID
                foreach (var asset in ConfigIdService.CollectConfigAssets(path))
                {
                    if (ConfigIdService.TryReadId(asset, out var id, out _))
                        ConfigIdService.ReleaseIdSync(id);
                }

                if (AssetDatabase.DeleteAsset(path))
                {
                    Reload();
                }
                else
                {
                    EditorUtility.DisplayDialog("删除失败", $"无法删除 {path}", "确定");
                }
            }
        }

        private bool HasAnyAssetOfType(Type type)
        {
            var folder = ConfigDataSettings.RootFolder;
            ConfigDataSettings.EnsureRootFolder();
            var guids = AssetDatabase.FindAssets($"t:{type.Name}", new[] { folder });
            return guids.Any(g => type.IsAssignableFrom(AssetDatabase.GetMainAssetTypeAtPath(AssetDatabase.GUIDToAssetPath(g))));
        }

        private void PingGuid(string guid)
        {
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(guid)));
        }

        #endregion

        #region 重命名

        protected override bool CanRename(TreeViewItem item)
        {
            return item is ConfigDataTreeFolderNode || item is ConfigDataTreeFileNode;
        }

        protected override void RenameEnded(RenameEndedArgs args)
        {
            var item = FindItem(args.itemID, rootItem);
            if (item is ConfigDataTreeFileNode fileNode)
            {
                var path = AssetDatabase.GUIDToAssetPath(fileNode.guid);
                var result = AssetDatabase.RenameAsset(path, args.newName);
                if (!string.IsNullOrEmpty(result))
                    EditorUtility.DisplayDialog("重命名失败", result, "确定");
                else
                    item.displayName = args.newName;
            }
            else if (item is ConfigDataTreeFolderNode folder)
            {
                var path = AssetDatabase.GUIDToAssetPath(folder.guid);
                var result = AssetDatabase.RenameAsset(path, args.newName);
                if (!string.IsNullOrEmpty(result))
                    EditorUtility.DisplayDialog("重命名失败", result, "确定");
                else
                    item.displayName = args.newName;
            }
        }

        #endregion

        #region 拖拽移动

        protected override bool CanStartDrag(CanStartDragArgs args)
        {
            foreach (var id in args.draggedItemIDs)
            {
                var item = FindItem(id, rootItem);
                if (item is ConfigDataTreeFileNode || item is ConfigDataTreeFolderNode)
                    return true;
            }
            return false;
        }

        protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
        {
            var objects = new List<UnityEngine.Object>();
            var paths = new List<string>();

            foreach (var id in args.draggedItemIDs)
            {
                var item = FindItem(id, rootItem);
                if (item is ConfigDataTreeFolderNode folder)
                {
                    paths.Add(AssetDatabase.GUIDToAssetPath(folder.guid));
                    objects.Add(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(folder.guid)));
                }
                else if (item is ConfigDataTreeFileNode file)
                {
                    paths.Add(AssetDatabase.GUIDToAssetPath(file.guid));
                    objects.Add(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(file.guid)));
                }
            }

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.paths = paths.ToArray();
            DragAndDrop.objectReferences = objects.ToArray();
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            DragAndDrop.StartDrag("ConfigDataTree");
        }

        protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
        {
            var dropNode = args.parentItem;
            if (dropNode is ConfigDataTreeFileNode)
                dropNode = dropNode.parent;

            if (DragAndDrop.paths == null || DragAndDrop.paths.Length <= 0)
                return DragAndDropVisualMode.Rejected;

            if (dropNode is ConfigDataTreeFolderNode folder)
            {
                var dropPath = AssetDatabase.GUIDToAssetPath(folder.guid);

                var parent = dropPath;
                while (!string.IsNullOrEmpty(parent))
                {
                    if (DragAndDrop.paths.Contains(parent))
                        return DragAndDropVisualMode.Rejected;
                    parent = Path.GetDirectoryName(parent);
                }

                if (args.performDrop)
                {
                    foreach (var dragPath in DragAndDrop.paths)
                    {
                        var newPath = Path.Combine(dropPath, Path.GetFileName(dragPath));
                        var result = AssetDatabase.ValidateMoveAsset(dragPath, newPath);
                        if (string.IsNullOrEmpty(result))
                        {
                            AssetDatabase.MoveAsset(dragPath, newPath);
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("错误", result, "确定");
                            break;
                        }
                    }
                    Reload();
                }

                return DragAndDropVisualMode.Move;
            }

            return DragAndDropVisualMode.Rejected;
        }

        #endregion
    }
}
