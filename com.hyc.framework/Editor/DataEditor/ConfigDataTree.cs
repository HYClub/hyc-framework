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
                    if (!ConfigTemplateCodeGen.IsBaseReady(tpl))
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
    /// Left-side TreeView of the HYC data editor: shows the config root folder's
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

        /// <summary>
        /// 标记：下一次 <see cref="EditorApplication.projectChanged"/> 触发的自动 <see cref="Reload"/> 是否跳过。
        /// 用于文件夹重命名时避免 <see cref="ConfigDataWindow.OnProjectChanged"/> 在 Refresh 生效前读到脏数据，
        /// 从而产生幽灵/重复节点。
        /// </summary>
        public bool SkipNextProjectChangedReload { get; set; }

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
            // 绕过 AssetDatabase.GetSubFolders 的文件夹缓存 bug：直接从文件系统枚举子目录，
            // 再映射回 Unity 相对路径。只保留有 .meta 的目录（与 Unity 资产视图一致）。
            var projectRoot = Path.GetDirectoryName(Application.dataPath).Replace("\\", "/");
            var fullFolder = $"{projectRoot}/{folder}";
            if (!Directory.Exists(fullFolder))
                return;

            foreach (var dirPath in Directory.GetDirectories(fullFolder))
            {
                var dirUnity = dirPath.Replace("\\", "/").Replace(projectRoot + "/", "");
                var name = Path.GetFileName(dirPath);
                var metaPath = Path.Combine(fullFolder, name + ".meta");
                if (!File.Exists(metaPath))
                    continue;

                var guid = AssetDatabase.AssetPathToGUID(dirUnity);
                var node = new ConfigDataTreeFolderNode(guid)
                {
                    id = GetFolderID(dirUnity),
                    depth = parent.depth + 1,
                    displayName = name,
                    icon = (Texture2D)AssetDatabase.GetCachedIcon(dirUnity),
                };
                parent.AddChild(node);
                mGuid2FolderNode[guid] = node;

                InstallFolders(node, dirUnity);
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
                BuildFolderContextMenu(folder, true).ShowAsContext();
            }
        }

        /// <summary>导出单个配置实例（客户端+服务器）。</summary>
        private void ExportSingleAsset(ConfigDataTreeFileNode file)
        {
            var asset = file.GetAsset();
            if (ConfigExportService.ExportSingle(asset, true, true))
                EditorUtility.DisplayDialog("导出", $"已导出 {asset.name} 到客户端/服务器目录", "确定");
        }

        /// <summary>保存并导出文件夹下所有配置（先保存整个文件夹，再按类型批量导出，含子文件夹）。</summary>
        private void SaveAndExportFolderAll(ConfigDataTreeFolderNode folder)
        {
            var path = AssetDatabase.GUIDToAssetPath(folder.guid);
            ConfigExportService.SaveFolder(path);   // 先保存，避免「改了没存就导出」
            var count = ConfigExportService.ExportFolder(path, true, true);
            if (count <= 0)
                EditorUtility.DisplayDialog("保存并导出", "未找到可导出的配置", "确定");
            else
                EditorUtility.DisplayDialog("保存并导出", $"已保存并导出 {count} 种配置到客户端/服务器目录", "确定");
        }

        /// <summary>
        /// 构建文件夹节点的右键菜单（根目录与普通文件夹共用；<paramref name="isRoot"/> 时
        /// 无重命名/删除、多一项「全量清理后全量导出」）。
        /// <para>导出项按文件夹内容动态列出，集中在「导出」子菜单里（GenericMenu 的路径按 "/"
        /// 拆子菜单，见 Unity 官方文档）：每种可导出配置类型一项（带该文件夹内资产数），
        /// 末尾分隔线 + 「导出全部」。枚举 / 模板不是「导出」动作（枚举走导出、模板走生成代码），
        /// 作为顶级动态项单列。</para>
        /// </summary>
        private GenericMenu BuildFolderContextMenu(ConfigDataTreeFolderNode folder, bool isRoot)
        {
            var path = AssetDatabase.GUIDToAssetPath(folder.guid);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("创建配置"), false, () => OpenCreateWindow(folder));
            menu.AddItem(new GUIContent("创建配置模板"), false, () => CreateTemplateAsset());
            menu.AddItem(new GUIContent("创建枚举"), false, () => CreateEnumAsset());
            menu.AddItem(new GUIContent("创建目录"), false, () => CreateFolder(folder));
            menu.AddSeparator("");
            AddDynamicExportItems(menu, folder, path);
            menu.AddSeparator("");
            if (isRoot)
                menu.AddItem(new GUIContent("全量清理后全量导出"), false, () => CleanAndExportAll());
            menu.AddItem(new GUIContent("刷新"), false, () => Reload());
            menu.AddSeparator("");
            if (!isRoot)
            {
                menu.AddItem(new GUIContent("重命名"), false, () => BeginRename(folder));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("删除"), false, () => DeleteFolder(folder));
                menu.AddSeparator("");
            }
            menu.AddItem(new GUIContent(isRoot ? "选择根目录资源" : "选择资源"), false, () => PingGuid(folder.guid));
            return menu;
        }

        /// <summary>
        /// 按文件夹内容动态追加导出/生成项：
        /// <list type="bullet">
        /// <item>「导出」子菜单：每种可导出配置类型一项「短名（N 个）」（N = 该文件夹内资产数），
        /// 末尾 <c>AddSeparator("导出/")</c> 子菜单内分隔线 + 「导出全部」；</item>
        /// <item>枚举 / 模板：顶级动态项「保存导出所有枚举（N 个）」「保存并生成所有模板代码（N 个）」——
        /// 模板是生成代码不是导出，不塞进导出子菜单；</item>
        /// <item>三类皆无：一条禁用提示。</item>
        /// </list>
        /// </summary>
        private void AddDynamicExportItems(GenericMenu menu, ConfigDataTreeFolderNode folder, string path)
        {
            var exportable = CountExportableAssetsByType(path);
            var enumCount = CountAssetsUnder(path, typeof(ConfigEnumDefinition));
            var templateCount = CountAssetsUnder(path, typeof(ConfigTemplate));

            if (exportable.Count > 0)
            {
                var seen = new HashSet<string>();
                foreach (var kv in exportable)
                {
                    // 循环体内声明的局部变量 → 每个 lambda 捕获各自的副本（不会全绑到最后一个类型上）
                    var type = kv.Key;
                    var label = ConfigTypeDisplay.GetShortName(type);
                    if (!seen.Add(label))
                        label += $"（{type.Name}）"; // 不同命名空间的同类短名撞车 → 加类名消歧

                    menu.AddItem(new GUIContent($"导出/{label}（{kv.Value} 个）"), false,
                        () => ExportFolderType(folder, type));
                }

                menu.AddSeparator("导出/"); // 尾斜杠 = 分隔线嵌在子菜单内（Unity 官方用法）
                menu.AddItem(new GUIContent("导出/导出全部"), false, () => SaveAndExportFolderAll(folder));
            }

            if (enumCount > 0)
                menu.AddItem(new GUIContent($"保存导出所有枚举（{enumCount} 个）"), false, () => SaveAndExportFolderEnums(folder));
            if (templateCount > 0)
                menu.AddItem(new GUIContent($"保存并生成所有模板代码（{templateCount} 个）"), false, () => GenerateFolderTemplateCode(folder));

            // ⚠️ 文案里不能出现半角 '/'：GenericMenu 会把它当子菜单分隔符
            //（曾写成「（没有可导出 / 可生成的内容）」，渲染成主项「（没有可导出」+ 子菜单「可生成的内容）」）
            if (exportable.Count == 0 && enumCount == 0 && templateCount == 0)
                menu.AddDisabledItem(new GUIContent("（没有可导出或可生成的内容）"));
        }

        /// <summary>
        /// 导出文件夹内某一配置类型（先保存整个文件夹，再按类型批量导出）。
        /// <para>必须走 <see cref="ConfigExportService.ExportAll"/> 整类型导出：Blob/JSON 产物
        /// 一类型一个文件，逐资产调 <c>ExportSingle</c> 会各写一遍、互相覆盖成只剩最后一个。</para>
        /// </summary>
        private void ExportFolderType(ConfigDataTreeFolderNode folder, Type configType)
        {
            var path = AssetDatabase.GUIDToAssetPath(folder.guid);
            ConfigExportService.SaveFolder(path); // 先保存，避免「改了没存就导出」

            if (ConfigExportService.ExportAll(configType, true, true))
                EditorUtility.DisplayDialog("导出",
                    $"已导出 {ConfigTypeDisplay.GetShortName(configType)}（{configType.Name}）到客户端/服务器目录", "确定");
            else
                EditorUtility.DisplayDialog("导出失败",
                    $"导出 {configType.Name} 失败（该类型是否还没生成导出类？）", "确定");
        }

        /// <summary>
        /// 统计文件夹（递归，含子文件夹）下各可导出配置类型的资产数，按类型短名排序。
        /// 过滤规则与 <see cref="ConfigExportService.ExportFolder"/> 完全一致（排除 <see cref="ConfigTemplate"/>、
        /// 排除命名空间不在 <see cref="ConfigDataSettings.Namespace"/> 内的资产），
        /// 保证菜单里写的「N 个」就是该文件夹内该类型的实际资产数。
        /// </summary>
        private static List<KeyValuePair<Type, int>> CountExportableAssetsByType(string folder)
        {
            var counts = new Dictionary<Type, int>();
            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { folder }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null)
                    continue;

                var type = asset.GetType();
                if (type == typeof(ConfigTemplate))
                    continue;
                if (type.Namespace == null || !type.Namespace.StartsWith(ConfigDataSettings.Namespace))
                    continue;

                counts.TryGetValue(type, out var n);
                counts[type] = n + 1;
            }

            var list = new List<KeyValuePair<Type, int>>(counts);
            list.Sort((a, b) => string.CompareOrdinal(
                ConfigTypeDisplay.GetShortName(a.Key), ConfigTypeDisplay.GetShortName(b.Key)));
            return list;
        }

        /// <summary>统计文件夹（递归，含子文件夹）下指定类型资产的数量。</summary>
        private static int CountAssetsUnder(string folder, Type type)
            => AssetDatabase.FindAssets($"t:{type.Name}", new[] { folder }).Length;

        /// <summary>加载文件夹（递归，含子文件夹）下所有指定类型资产。</summary>
        private static List<T> LoadAssetsUnder<T>(string folder) where T : UnityEngine.Object
        {
            var list = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null)
                    list.Add(asset);
            }
            return list;
        }

        /// <summary>保存并导出文件夹下所有枚举定义（递归，含子文件夹；逐个先落盘再导出）。</summary>
        private void SaveAndExportFolderEnums(ConfigDataTreeFolderNode folder)
        {
            var path = AssetDatabase.GUIDToAssetPath(folder.guid);
            var defs = LoadAssetsUnder<ConfigEnumDefinition>(path);
            if (defs.Count == 0)
            {
                EditorUtility.DisplayDialog("保存导出所有枚举", "未找到枚举定义", "确定");
                return;
            }

            var ok = 0;
            foreach (var def in defs)
            {
                EditorUtility.SetDirty(def);
                AssetDatabase.SaveAssetIfDirty(def);
                if (ConfigEnumCodeGen.Export(def, true, true))
                    ok++;
            }

            EditorUtility.DisplayDialog("保存导出所有枚举",
                $"已保存并导出 {ok}/{defs.Count} 个枚举到客户端/服务器目录", "确定");
        }

        /// <summary>保存并生成文件夹下所有模板代码（递归，含子文件夹；流程与单个生成一致：先落盘再生成，生成后再落盘消 * 标记）。</summary>
        private void GenerateFolderTemplateCode(ConfigDataTreeFolderNode folder)
        {
            var path = AssetDatabase.GUIDToAssetPath(folder.guid);
            var tpls = LoadAssetsUnder<ConfigTemplate>(path);
            if (tpls.Count == 0)
            {
                EditorUtility.DisplayDialog("生成模板代码", "未找到配置模板", "确定");
                return;
            }

            var ok = 0;
            string firstError = null;
            foreach (var tpl in tpls)
            {
                // 生成前先落盘：保证生成读到的字段值与磁盘一致（改了显示名没保存就生成的情况）
                EditorUtility.SetDirty(tpl);
                AssetDatabase.SaveAssetIfDirty(tpl);

                if (ConfigTemplateCodeGen.WriteFile(tpl, out var error))
                {
                    ok++;
                    // WriteFile 已 SetDirty（记录 lastGeneratedClassName），补落盘消除 * 标记
                    EditorUtility.SetDirty(tpl);
                    AssetDatabase.SaveAssetIfDirty(tpl);
                }
                else if (firstError == null)
                {
                    firstError = $"{tpl.className}: {error}";
                }
            }

            Reload();
            if (ok == tpls.Count)
                EditorUtility.DisplayDialog("生成模板代码", $"已生成 {ok} 个模板的代码", "确定");
            else
                EditorUtility.DisplayDialog("生成模板代码", $"成功 {ok}/{tpls.Count} 个。\n首个失败：{firstError}", "确定");
        }

        /// <summary>全量清理后全量导出（先确认，再调用 ConfigExportService 清空并重新导出所有配置类型）。</summary>
        public void CleanAndExportAll()
        {
            if (!EditorUtility.DisplayDialog("全量清理后全量导出",
                    "将删除客户端/服务器导出目录下所有旧 *.blob，再重新导出全部配置。\n（改名或删除的配置类型不会留下幽灵文件）", "执行", "取消"))
                return;
            var n = ConfigExportService.CleanAndExportAll(true, true);
            EditorUtility.DisplayDialog("全量清理后全量导出", $"已全量清理并重新导出 {n} 类配置", "确定");
            Reload();
        }

        /// <summary>保存并导出单个配置实例（先保存资产，再导出）。</summary>
        private void SaveAndExportSingleAsset(ConfigDataTreeFileNode file)
        {
            var asset = file.GetAsset();
            if (asset == null) return;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            if (ConfigExportService.ExportSingle(asset, true, true))
                EditorUtility.DisplayDialog("保存并导出", $"已保存并导出 {asset.name} 到客户端/服务器目录", "确定");
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
                menu.AddItem(new GUIContent("创建配置模板"), false, () => CreateTemplateAsset());
                menu.AddItem(new GUIContent("创建枚举"), false, () => CreateEnumAsset());
                menu.AddItem(new GUIContent("创建目录"), false, () => CreateFolder(file));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("导出"), false, () => ExportSingleAsset(file));
                menu.AddItem(new GUIContent("保存并导出"), false, () => SaveAndExportSingleAsset(file));
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
            if (tpl == null)
                return;

            // 生成前先落盘：WriteFile 内部为记录 lastGeneratedClassName 会 SetDirty，
            // 生成后必须再存一次，否则资产留着未保存标记（标题带 *）。
            // 同时保证生成读到的字段值与磁盘一致（改了显示名没保存就生成的情况）。
            EditorUtility.SetDirty(tpl);
            AssetDatabase.SaveAssetIfDirty(tpl);

            if (!ConfigTemplateCodeGen.WriteFile(tpl, out var error))
            {
                EditorUtility.DisplayDialog("生成失败", error, "确定");
                return;
            }

            // WriteFile 已 SetDirty（记录 lastGeneratedClassName），补落盘消除 * 标记
            EditorUtility.SetDirty(tpl);
            AssetDatabase.SaveAssetIfDirty(tpl);

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
                menu = BuildFolderContextMenu(folder, false);
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

        /// <summary>创建新模板资产（自动归置到「配置模板」类型文件夹）并在树中选中。</summary>
        private void CreateTemplateAsset()
        {
            var tpl = ConfigTemplateCodeGen.CreateTemplateAsset();
            Reload();
            SelectAsset(tpl);
        }

        /// <summary>创建新枚举定义资产（自动归置到「枚举」类型文件夹）并在树中选中。</summary>
        private void CreateEnumAsset()
        {
            var def = ConfigEnumCodeGen.CreateEnumAsset();
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
            // 此 Unity 版本（2022.3）的 RenameEndedArgs 无 accepted 字段：取消（Esc）时 newName 回退为原名，
            // 以此判定跳过；空名/未变也由 ApplyRenameCore 的 null/empty 守卫兜底。
            var item = FindItem(args.itemID, rootItem);
            Debug.Log($"[ConfigDataTree] RenameEnded itemId={args.itemID}, newName={args.newName}, item={(item == null ? "null" : item.displayName)}");
            if (item == null || args.newName == item.displayName)
                return;

            string path;
            bool isFolder;
            if (item is ConfigDataTreeFileNode fileNode)
            {
                path = AssetDatabase.GUIDToAssetPath(fileNode.guid);
                isFolder = false;
            }
            else if (item is ConfigDataTreeFolderNode folder)
            {
                path = AssetDatabase.GUIDToAssetPath(folder.guid);
                isFolder = true;
            }
            else
            {
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[ConfigDataTree] RenameEnded path empty, skip.");
                return;
            }

            var newName = args.newName;
            var itemId = args.itemID;
            Debug.Log($"[ConfigDataTree] Schedule delayCall rename: path={path}, newName={newName}, isFolder={isFolder}, itemId={itemId}");
            EditorApplication.delayCall += () => ApplyRenameCore(path, newName, isFolder, itemId);
        }

        /// <summary>
        /// 真正执行重命名，由 <see cref="EditorApplication.delayCall"/> 调度到 RenameEnded 回调外执行。
        /// <para><b>根因修复</b>：Unity 的 <see cref="AssetDatabase.RenameAsset"/> 文档明确只支持「asset file」，
        /// 对文件夹返回空成功却实际不做任何操作。文件夹改名必须走
        /// <see cref="FileUtil.MoveFileOrDirectory"/> —— Unity 原生的文件/目录搬家工具；但它不会自动搬目录的
        /// <c>.meta</c>，所以目录与 <c>.meta</c> 要各搬一次，GUID 才保持不变。这是「搬家/改名」不是「新建+删除」，
        /// 旧目录里的内容会一并搬到新目录。</para>
        /// </summary>
        private void ApplyRenameCore(string path, string newName, bool isFolder, int itemId)
        {
            Debug.Log($"[ConfigDataTree] ApplyRenameCore start: path={path}, newName={newName}, isFolder={isFolder}, itemId={itemId}");
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newName))
                return;

            // 跳过本次 projectChanged 自动 Reload：改名会触发 projectChanged，
            // 若 ConfigDataWindow.OnProjectChanged 在 Reload 前读到中间态会重影。
            SkipNextProjectChangedReload = true;
            try
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath).Replace("\\", "/");
                var absOld = Path.Combine(projectRoot, path).Replace("\\", "/");
                var newPath = Path.GetDirectoryName(path).Replace("\\", "/") + "/" + newName;
                var absNew = Path.Combine(projectRoot, newPath).Replace("\\", "/");
                var oldMetaPath = path + ".meta";
                var newMetaPath = newPath + ".meta";
                var absOldMeta = Path.Combine(projectRoot, oldMetaPath).Replace("\\", "/");
                var absNewMeta = Path.Combine(projectRoot, newMetaPath).Replace("\\", "/");

                // 诊断：重命名前 GUID 映射与磁盘状态
                var guid = AssetDatabase.AssetPathToGUID(path);
                Debug.Log($"[ConfigDataTree] Before: guid={guid}, oldDir={Directory.Exists(absOld)}, newDir={Directory.Exists(absNew)}, oldMeta={File.Exists(absOldMeta)}, newMeta={File.Exists(absNewMeta)}");

                string result = null;
                if (isFolder)
                {
                    // 文件夹：FileUtil 支持目录搬家，但 .meta 要手动一起搬；内容是「搬」不是「复制+删除」。
                    Debug.Log($"[ConfigDataTree] Folder move: {path} -> {newPath}");
                    try
                    {
                        FileUtil.MoveFileOrDirectory(path, newPath);
                        if (File.Exists(absOldMeta))
                            FileUtil.MoveFileOrDirectory(oldMetaPath, newMetaPath);
                    }
                    catch (Exception ex)
                    {
                        result = ex.Message;
                    }
                }
                else
                {
                    // 文件：AssetDatabase.RenameAsset 是官方支持的文件改名 API，自动处理 .meta。
                    Debug.Log($"[ConfigDataTree] File rename: {path} -> {newName}");
                    result = AssetDatabase.RenameAsset(path, newName);
                }

                // 诊断：重命名后 GUID 映射与磁盘状态
                var pathAfter = AssetDatabase.GUIDToAssetPath(guid);
                var absAfter = Path.Combine(projectRoot, pathAfter).Replace("\\", "/");
                Debug.Log($"[ConfigDataTree] After: result='{result ?? "<null>"}', guidPathAfter={pathAfter}, afterDir={Directory.Exists(absAfter)}, oldDir={Directory.Exists(absOld)}, oldMeta={File.Exists(absOldMeta)}");

                if (!string.IsNullOrEmpty(result))
                {
                    Debug.LogError($"[ConfigDataTree] Rename failed: {result}");
                    EditorUtility.DisplayDialog("重命名失败", result, "确定");
                    return;
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Reload();
            }
            finally
            {
                SkipNextProjectChangedReload = false;
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
                    parent = Path.GetDirectoryName(parent)?.Replace("\\", "/");
                }

                if (args.performDrop)
                {
                    var moves = new List<(string from, string to)>();
                    foreach (var dragPath in DragAndDrop.paths)
                    {
                        // Unity 路径必须全用正斜杠；Path.Combine 在 Windows 会产出反斜杠，导致 MoveAsset 失败。
                        var newPath = dropPath + "/" + Path.GetFileName(dragPath).Replace("\\", "/");
                        moves.Add((dragPath, newPath));
                    }

                    Debug.Log($"[ConfigDataTree] DragDrop scheduled {moves.Count} moves to {dropPath}");
                    // AssetDatabase.MoveAsset 在 TreeView 拖拽回调的事务内调用会返回成功但不落盘，
                    // 必须延迟到下一编辑器 tick 真正执行（与 RenameEnded/ApplyRenameCore 同因）。
                    EditorApplication.delayCall += () => ApplyMoveCore(moves);
                }

                return DragAndDropVisualMode.Move;
            }

            return DragAndDropVisualMode.Rejected;
        }

        /// <summary>
        /// 在拖拽回调外真正执行资产移动。
        /// <para>AssetDatabase.MoveAsset 在 TreeView 拖拽事务内调用会返回成功但不落盘；本方法改用
        /// <see cref="FileUtil.MoveFileOrDirectory"/> 做真文件系统搬家，并手动复现
        /// <see cref="ConfigAssetLayout.ShouldBlockMove"/> 的「不建议移动」弹窗（FileUtil 不会触发
        /// AssetModificationProcessor，必须自己弹）。</para>
        /// </summary>
        private void ApplyMoveCore(List<(string from, string to)> moves)
        {
            SkipNextProjectChangedReload = true;
            try
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath).Replace("\\", "/");
                foreach (var (dragPath, newPath) in moves)
                {
                    Debug.Log($"[ConfigDataTree] FileUtil move: {dragPath} -> {newPath}");

                    // 手动复现「移出类型文件夹守护」弹窗（FileUtil 不会触发 ConfigAssetMoveGuard）
                    var block = ConfigAssetLayout.ShouldBlockMove(dragPath, newPath);
                    if (block != null)
                    {
                        if (!EditorUtility.DisplayDialog("不建议移动", block, "仍然移动", "取消"))
                            break;
                    }

                    var absFrom = Path.Combine(projectRoot, dragPath).Replace("\\", "/");
                    var absTo = Path.Combine(projectRoot, newPath).Replace("\\", "/");
                    var absFromMeta = Path.Combine(projectRoot, dragPath + ".meta").Replace("\\", "/");
                    var absToMeta = Path.Combine(projectRoot, newPath + ".meta").Replace("\\", "/");
                    Debug.Log($"[ConfigDataTree] Before move: from={File.Exists(absFrom)}, to={File.Exists(absTo)}, fromMeta={File.Exists(absFromMeta)}, toMeta={File.Exists(absToMeta)}");

                    try
                    {
                        FileUtil.MoveFileOrDirectory(dragPath, newPath);
                        if (File.Exists(absFromMeta))
                            FileUtil.MoveFileOrDirectory(dragPath + ".meta", newPath + ".meta");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ConfigDataTree] Move failed: {ex.Message}");
                        EditorUtility.DisplayDialog("移动未执行", ex.Message, "确定");
                        break;
                    }

                    Debug.Log($"[ConfigDataTree] After move: from={File.Exists(absFrom)}, to={File.Exists(absTo)}, fromMeta={File.Exists(absFromMeta)}, toMeta={File.Exists(absToMeta)}");
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Reload();
            }
            finally
            {
                SkipNextProjectChangedReload = false;
            }
        }

        #endregion
    }
}
