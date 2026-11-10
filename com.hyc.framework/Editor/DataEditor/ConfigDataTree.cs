using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HYC.Framework.Editor;
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

        /// <summary>该文件夹的直接子节点是否已构建（懒加载标记）。</summary>
        public bool childrenBuilt;

        public ConfigDataTreeFolderNode(string guid)
        {
            this.guid = guid;
            // 自始持非空 children：Unity 的 GetVisibleItemsRecursive 对「hasChildren=true 且已展开」
            // 的节点直接 foreach children，null 会 NRE；空列表则两条路径都安全。
            children = new List<TreeViewItem>();
        }

        /// <summary>
        /// 未构建的文件夹恒报可展开：懒加载下直接子节点还没装，若沿用默认判定（children.Count &gt; 0）
        /// 则初始不画展开三角、点三角也不触发展开（IsExpandable=false 时点击仅改选中）。
        /// 已构建后回归真实判定——空文件夹自然失去三角。
        /// </summary>
        public override bool hasChildren => !childrenBuilt || base.hasChildren;
    }

    /// <summary>Config asset file node in the data editor tree.</summary>
    public sealed class ConfigDataTreeFileNode : ConfigDataTreeNode
    {
        public string guid;
        public Type type;

        /// <summary>构建期一次性算好的树标签（不含脏标记 *），避免 RowGUI 每帧 Load 资产。</summary>
        public string cachedBaseLabel;

        /// <summary>构建期缓存的资产引用，用于廉价计算脏标记（避免每帧 LoadAssetAtPath）。</summary>
        public UnityEngine.Object cachedAsset;

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

        /// <summary>构建期一次性计算并缓存 <see cref="cachedBaseLabel"/> 与 <see cref="cachedAsset"/>。</summary>
        internal void CacheLabel()
        {
            cachedAsset = GetAsset();
            cachedBaseLabel = BuildDisplayLabelCore(displayName);
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
                // 只读本体直接标注：配置类由生成器产出，本体必定有一条 [CfgAsset]
                var attr = type?.GetCustomAttributes(typeof(CfgAssetAttribute), false).FirstOrDefault() as CfgAssetAttribute;
                return attr != null ? attr.Name : "";
            }
        }
    }

    /// <summary>
    /// 「外部配置」虚拟文件夹节点：挂在配置树最底部，名下子节点是外部资产的**引用**。
    /// <para><b>虚拟</b>的含义：资产本体留在原路径，不移动也不复制；这里只登记
    /// <see cref="HYCSettings.IncludedAssetGuids"/> 里的 GUID 引用。由此推导出的三条约束：
    /// 子节点不能重命名（改名等于改真资产）、不能拖拽移动（资产不在配置根目录内，无路径可搬）、
    /// 「移除」只能删引用不能删文件。</para>
    /// </summary>
    public sealed class ConfigDataTreeVirtualFolderNode : ConfigDataTreeNode
    {
        /// <summary>固定节点 ID（独立负数段，不与文件（+）/ 文件夹（高位 -）/ 外部资产（-1000 起）冲突。</summary>
        public const int NodeId = -4;
    }

    /// <summary>外部纳入「数据配置」编辑器的 Unity 资产节点（如 .inputactions）。</summary>
    public sealed class ConfigDataTreeExternalNode : ConfigDataTreeNode
    {
        public string guid;
        public Type type;

        public ConfigDataTreeExternalNode(string guid, Type type)
        {
            this.guid = guid;
            this.type = type;
        }

        public override UnityEngine.Object GetAsset()
            => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(guid));

        /// <summary>树标签："[类型名] 文件名"，资产有未保存修改时追加 " *"。</summary>
        public string BuildDisplayLabel(string originalLabel)
        {
            var label = $"[{(type != null ? type.Name : "资产")}] {originalLabel}";
            var asset = GetAsset();
            if (asset != null && EditorUtility.IsDirty(asset))
                label += " *";
            return label;
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
                var asset = f.cachedAsset != null ? f.cachedAsset : f.GetAsset();
                if (asset != null && EditorUtility.IsDirty(asset))
                {
                    any = true;
                    return;
                }
            }
            else if (item is ConfigDataTreeExternalNode ext)
            {
                var asset = ext.GetAsset();
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

        #region 文件夹子节点查询（供 FolderConfigEditor 使用）

        /// <summary>返回指定文件夹节点下的直接子文件夹与配置文件节点（按树内存层级遍历，不重新扫磁盘）。</summary>
        public void GetFolderChildren(string folderGuid, out List<ConfigDataTreeFolderNode> subfolders, out List<ConfigDataTreeFileNode> files)
        {
            subfolders = new List<ConfigDataTreeFolderNode>();
            files = new List<ConfigDataTreeFileNode>();
            var node = FindFolderNodeByGuid(rootItem, folderGuid);
            if (node == null)
                return;
            if (!node.childrenBuilt)
                BuildChildren(node);
            if (node.children == null)
                return;
            foreach (var c in node.children)
            {
                if (c is ConfigDataTreeFolderNode fn) subfolders.Add(fn);
                else if (c is ConfigDataTreeFileNode filen) files.Add(filen);
            }
        }

        private static ConfigDataTreeFolderNode FindFolderNodeByGuid(TreeViewItem item, string guid)
        {
            if (item is ConfigDataTreeFolderNode fn && fn.guid == guid)
                return fn;
            if (item.children != null)
            {
                foreach (var c in item.children)
                {
                    var r = FindFolderNodeByGuid(c, guid);
                    if (r != null)
                        return r;
                }
            }
            return null;
        }

        #endregion

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

        // 外部资产（虚拟文件夹的引用子节点）用独立负数 ID 段，避开文件（+）与文件夹（高位 -）
        private static int mExternalID = -1000;

        private static int GetExternalID()
        {
            mExternalID--;
            return mExternalID;
        }

        #endregion

        #region 构建树

        protected override TreeViewItem BuildRoot()
        {
            var sw = EnableBuildProfiling ? System.Diagnostics.Stopwatch.StartNew() : null;
            mGuid2FolderNode.Clear();

            var folder = ConfigDataSettings.RootFolder;
            ConfigDataSettings.EnsureRootFolder();

            var guid = AssetDatabase.AssetPathToGUID(folder);
            var root = new ConfigDataTreeFolderNode(guid) { id = -1, depth = -1 };
            mGuid2FolderNode[guid] = root;

            if (string.IsNullOrEmpty(searchString))
            {
                // 懒加载：仅构建根的直接子节点；子文件夹在首次展开时由 BuildChildren 填充
                InstallOneLevelFolders(root, folder);
                InstallOneLevelFiles(root, folder);
                root.childrenBuilt = true;
            }
            else
            {
                // 搜索态：全量构建，保证未展开分支也能参与匹配
                InstallFolders(root, folder);
                InstallFiles(root, folder);
            }
            // 挂在最底部：真实配置层级之后，避免混进正常配置结构里
            InstallVirtualFolder(root);

            mGuid2FolderNode.Clear();

            // TreeView requires a non-null children list even when empty.
            if (root.children == null)
                root.children = new List<TreeViewItem>();

            rowHeight = EditorGUIUtility.singleLineHeight + 6;
            showAlternatingRowBackgrounds = true;

            SetupDepthsFromParentsAndChildren(root);

            if (sw != null)
            {
                sw.Stop();
                Debug.Log($"[ConfigDataTree] BuildRoot 耗时 {sw.ElapsedMilliseconds}ms（直接子节点 {root.children?.Count ?? 0}）");
            }
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
                node.childrenBuilt = true;
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

                node.CacheLabel();
                folderNode.AddChild(node);
            }
        }

        /// <summary>只枚举指定文件夹的<b>直接子目录</b>（一层，不递归），用于懒加载构建。</summary>
        private void InstallOneLevelFolders(TreeViewItem parent, string folder)
        {
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
                    // childrenBuilt 保持 false：子节点在首次展开时由 BuildChildren 填充
                };
                parent.AddChild(node);
            }
        }

        /// <summary>只枚举指定文件夹的<b>直接配置文件</b>（一层，不递归，文件系统级），用于懒加载构建。</summary>
        private void InstallOneLevelFiles(TreeViewItem parent, string folder)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath).Replace("\\", "/");
            var fullFolder = $"{projectRoot}/{folder}";
            if (!Directory.Exists(fullFolder))
                return;

            var cfgTypeNames = new HashSet<string>(
                GetCfgItems().Select(r => r.Type.Name), StringComparer.Ordinal);

            foreach (var filePath in Directory.GetFiles(fullFolder))
            {
                var fp = filePath.Replace("\\", "/");
                if (!fp.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    continue;

                var unityPath = fp.Substring(projectRoot.Length + 1);
                var guid = AssetDatabase.AssetPathToGUID(unityPath);
                if (string.IsNullOrEmpty(guid))
                    continue;

                var type = AssetDatabase.GetMainAssetTypeAtPath(unityPath);
                if (type == null)
                    continue;
                if (!(type == typeof(ConfigTemplate) || type == typeof(ConfigEnumDefinition) || cfgTypeNames.Contains(type.Name)))
                    continue;

                var node = new ConfigDataTreeFileNode(guid, type)
                {
                    id = GetFileID(unityPath),
                    depth = parent.depth + 1,
                    displayName = Path.GetFileNameWithoutExtension(unityPath),
                    icon = (Texture2D)AssetDatabase.GetCachedIcon(unityPath),
                };

                if (type == typeof(ConfigTemplate))
                {
                    var tpl = node.GetAsset() as ConfigTemplate;
                    var tplIcon = ConfigTemplateIcon.Resolve(tpl);
                    if (tplIcon != null)
                        node.icon = tplIcon as Texture2D;
                }

                node.CacheLabel();
                parent.AddChild(node);
            }
        }

        /// <summary>
        /// 底部「外部配置」虚拟文件夹：把 <see cref="HYCSettings.IncludedAssetGuids"/> 注册表里的
        /// 外部资产渲染成引用子节点。分组本身恒定显示（即便为空），作为拖入外部资产的可见落点说明。
        /// 资产已被删除（GUID 解析不到路径）的幽灵项跳过，由右键「移除 / 清空」清理。
        /// </summary>
        private void InstallVirtualFolder(TreeViewItem root)
        {
            var folder = new ConfigDataTreeVirtualFolderNode
            {
                id = ConfigDataTreeVirtualFolderNode.NodeId,
                depth = 0,
                displayName = "外部配置",
                icon = (Texture2D)EditorGUIUtility.IconContent("Folder Icon").image,
                children = new List<TreeViewItem>(),
            };
            root.AddChild(folder);

            var guids = HYCSettings.Instance.IncludedAssetGuids;
            if (guids == null || guids.Count == 0)
                return;

            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (string.IsNullOrEmpty(path))
                    continue;
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                folder.AddChild(new ConfigDataTreeExternalNode(g, type)
                {
                    id = GetExternalID(),
                    depth = 1,
                    displayName = Path.GetFileNameWithoutExtension(path),
                    icon = (Texture2D)AssetDatabase.GetCachedIcon(path),
                });
            }
        }

        /// <summary>All config asset types declared with <c>[CfgAsset]</c>, sorted by Order.</summary>
        private static CfgAssetItem[] GetCfgItems()
        {
            return TypeCache.GetTypesWithAttribute<CfgAssetAttribute>()
                .Where(r => typeof(ScriptableObject).IsAssignableFrom(r) && !r.IsAbstract && !r.IsGenericType)
                .SelectMany(r => r.GetCustomAttributes<CfgAssetAttribute>(false)
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

        #region 懒加载与手风琴

        /// <summary>开启后 BuildRoot 打印耗时日志，用于优化前后对比。默认关闭。</summary>
        public static bool EnableBuildProfiling;

        private bool mInExpansion;
        private HashSet<int> mLastExpanded; // 上一帧已展开分支根集合，用于检测展开态变化并驱动手风琴

        /// <summary>
        /// 懒加载：构建指定文件夹节点的直接子节点（一层，不递归）。已构建过直接返回；
        /// 调用方在折叠时剪枝（children=null + childrenBuilt=false）以释放内存与避免脏数据。
        /// </summary>
        private void BuildChildren(ConfigDataTreeFolderNode folderNode)
        {
            if (folderNode.childrenBuilt)
                return;
            var folderPath = AssetDatabase.GUIDToAssetPath(folderNode.guid);
            if (string.IsNullOrEmpty(folderPath))
            {
                folderNode.childrenBuilt = true;
                return;
            }
            InstallOneLevelFolders(folderNode, folderPath);
            InstallOneLevelFiles(folderNode, folderPath);
            folderNode.childrenBuilt = true;
            if (folderNode.children == null)
                folderNode.children = new List<TreeViewItem>();
        }

        private void BuildExpandedChain(TreeViewItem item)
        {
            // 构建「已展开」文件夹的子节点；递归必须无条件下沉——入口是根节点（id=-1，永不展开），
            // 若把递归嵌进 IsExpanded 分支，从根进来直接返回，懒加载一次都不会执行。
            if (item is ConfigDataTreeFolderNode fn && IsExpanded(fn.id) && !fn.childrenBuilt)
                BuildChildren(fn);
            if (item.children != null)
            {
                foreach (var c in item.children)
                    BuildExpandedChain(c);
            }
        }

        /// <summary>懒加载必须挂 BuildRows（渲染唯一取行路径：FetchData → BuildRows → AddExpandedRows），
        /// 不能挂 GetRows——渲染路径不经过它，点三角只会命中「已展开但子节点还没构建」的空文件夹。
        /// 先按展开链补齐子节点（只加行、不动展开态），再交给 base 走常规展开遍历。</summary>
        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            if (root != null && string.IsNullOrEmpty(searchString))
                BuildExpandedChain(root);
            return base.BuildRows(root);
        }

        /// <summary>
        /// 手风琴必须挂在 OnGUI 入口而非 GetRows：Unity 点三角时用点击瞬间的行下标回查 rows
        /// （UserInputChangedExpandedState → GetLastChildRowUnder），并假设展开只在其下方加行；
        /// 若在同一次 GetRows 里折叠兄弟分支，rows 收缩会令旧下标越界（ArgumentOutOfRangeException）。
        /// 每帧渲染前先收敛展开态，再交给 base 渲染，行号全程一致。
        /// </summary>
        public override void OnGUI(Rect rect)
        {
            EnsureExpansionAnimationDisabled();
            if (string.IsNullOrEmpty(searchString))
                ApplyAccordionIfChanged();
            base.OnGUI(rect);
        }

        private bool mExpansionAnimationDisabled;

        /// <summary>
        /// 关闭展开动画：动画分支在点击时用「点击瞬间的行下标」回查 rows
        /// （UserInputChangedExpandedState → GetLastChildRowUnder），而本树的懒加载/手风琴
        /// 会让 rows 在两次取行之间收缩，旧下标越界（ArgumentOutOfRangeException）。
        /// 该下标回查只存在于动画分支；关掉后走 data.SetExpanded 直通路径，崩溃点不可达。
        /// useExpansionAnimation 挂在 internal 的 TreeViewController 上且封装未暴露，反射一次性关闭。
        /// </summary>
        private void EnsureExpansionAnimationDisabled()
        {
            if (mExpansionAnimationDisabled)
                return;
            var controller = typeof(TreeView).GetField("m_TreeView",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(this);
            if (controller == null)
                return; // 控制器尚未初始化（首次 OnGUI 前），下一帧重试
            var prop = controller.GetType().GetProperty("useExpansionAnimation");
            prop?.SetValue(controller, false);
            mExpansionAnimationDisabled = true;
        }

        /// <summary>
        /// 强制单展开（手风琴）：同一时刻只保留一条展开分支（含其祖先链）。
        /// 检测已展开分支根集合相较上一帧的变化，展开新分支时折叠其余；折叠时剪枝释放子节点。
        /// 由 <see cref="OnGUI"/> 每帧入口驱动（不能挂 GetRows，见其重载注释）。
        /// </summary>
        private void ApplyAccordionIfChanged()
        {
            if (mInExpansion)
                return;

            var branches = new List<TreeViewItem>();
            CollectExpandedItems(rootItem, branches);
            var current = new HashSet<int>(branches.Select(b => b.id));

            var prev = mLastExpanded;
            if (prev != null && prev.SetEquals(current))
                return; // 展开态未变化，无需处理

            mInExpansion = true;
            try
            {
                if (branches.Count > 1)
                {
                    // 保留一条分支（含其祖先链），折叠其余：优先保留本帧新展开的分支
                    // （current-prev 的差集）；无新增（如恢复历史展开态）才退回取最深分支。
                    // 不能只按深度排序取首——同层兄弟深度相同，List.Sort 不稳定，
                    // 保留谁全凭运气，表现为「点新文件夹的三角永远打不开」。
                    TreeViewItem keepRoot = null;
                    if (prev != null)
                    {
                        foreach (var it in branches)
                        {
                            if (!prev.Contains(it.id)
                                && (keepRoot == null || it.depth > keepRoot.depth))
                                keepRoot = it;
                        }
                    }
                    if (keepRoot == null)
                    {
                        branches.Sort((a, b) => b.depth.CompareTo(a.depth));
                        keepRoot = branches[0];
                    }
                    var keep = new HashSet<int>();
                    var p = keepRoot;
                    while (p != null) { keep.Add(p.id); p = p.parent; }
                    // mLastExpanded 只允许收「真实展开的文件夹根」：keep 会带根节点(-1)等祖先 id，
                    // 而下一帧的 current（CollectExpandedItems 采样）不含它们 —— SetEquals 永远不等，
                    // 剪枝兜底循环会把 prev 里不在 current 的 id 逐个 PruneById，连根节点(-1)一起剪光。
                    keep.IntersectWith(current);
                    foreach (var it in branches)
                    {
                        if (!keep.Contains(it.id))
                        {
                            SetExpanded(it.id, false);
                            PruneById(it.id);
                        }
                    }
                    mLastExpanded = new HashSet<int>(keep);
                }
                else
                {
                    mLastExpanded = current;
                }

                // 用户主动折叠的节点（上一帧有、本帧无）也要剪枝释放
                if (prev != null)
                {
                    foreach (var id in prev)
                    {
                        if (!current.Contains(id))
                            PruneById(id);
                    }
                }
            }
            finally
            {
                mInExpansion = false;
            }
        }

        private void CollectExpandedItems(TreeViewItem item, List<TreeViewItem> outItems)
        {
            if ((item is ConfigDataTreeFolderNode fn && fn.id != -1 && IsExpanded(fn.id)) ||
                (item is ConfigDataTreeVirtualFolderNode vf && IsExpanded(vf.id)))
            {
                outItems.Add(item);
            }
            if (item.children != null)
            {
                foreach (var c in item.children)
                    CollectExpandedItems(c, outItems);
            }
        }

        private void PruneById(int id)
        {
            if (id == -1)
                return; // 根节点永不剪枝：children 被清空后没有任何路径会重建（BuildRoot 只在 Reload 跑）
            if (FolderContainsSelection(id))
                return; // 含当前选中文件的文件夹不剪枝，避免属性面板因 FindItem 失效而空白
            var it = FindItem(id, rootItem);
            if (it is ConfigDataTreeFolderNode fn)
            {
                // 置回空列表而非 null：节点仍需参与 hasChildren/递归遍历，null 会踩 Unity 内部 foreach
                fn.children = new List<TreeViewItem>();
                fn.childrenBuilt = false;
            }
        }

        /// <summary>判断指定文件夹是否为任一当前选中项的祖先（用于剪枝保护）。</summary>
        private bool FolderContainsSelection(int folderId)
        {
            foreach (var selId in GetSelection())
            {
                var it = FindItem(selId, rootItem);
                var p = it?.parent;
                while (p != null)
                {
                    if (p.id == folderId)
                        return true;
                    p = p.parent;
                }
            }
            return false;
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

        /// <summary>「外部配置」虚拟文件夹的标签样式：按 Pro/普通皮肤取两套青色，保证深浅底都能读清。</summary>
        private static GUIStyle _virtualFolderLabelStyle;

        private static GUIStyle VirtualFolderLabelStyle
        {
            get
            {
                if (_virtualFolderLabelStyle == null)
                {
                    _virtualFolderLabelStyle = new GUIStyle(EditorStyles.label);
                    _virtualFolderLabelStyle.alignment = TextAnchor.MiddleLeft; // 行高有留白，垂直居中
                    _virtualFolderLabelStyle.normal.textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.30f, 0.79f, 0.88f)
                        : new Color(0.04f, 0.42f, 0.50f);
                }
                return _virtualFolderLabelStyle;
            }
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var label = args.label;
            args.label = string.Empty;
            base.RowGUI(args);

            // 显示层动态加前缀，节点原名保持不动（重命名编辑的是原名）
            if (args.item is ConfigDataTreeFileNode file)
            {
                var lbl = file.cachedBaseLabel ?? label;
                if (file.cachedAsset != null && EditorUtility.IsDirty(file.cachedAsset))
                    lbl += " *";
                label = lbl;
            }

            // 「外部配置」虚拟文件夹用独立颜色区分于真实目录（子节点保持默认色，避免整棵花掉）
            var style = args.item is ConfigDataTreeVirtualFolderNode ? VirtualFolderLabelStyle : LabelStyle;

            var indent = GetContentIndent(args.item) + 16;
            var textRect = new Rect(args.rowRect.x + indent, args.rowRect.y, args.rowRect.width - indent, args.rowRect.height);
            GUI.Label(textRect, label, style);
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

        /// <summary>
        /// 构建「外部配置」虚拟文件夹的右键菜单。清空仅删引用不动文件，故文案里必须写明，
        /// 且因一次涉及全部条目，走一次二次确认。
        /// </summary>
        private GenericMenu BuildVirtualFolderContextMenu(ConfigDataTreeVirtualFolderNode node)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("清空外部配置（仅移除引用）"), false, () =>
            {
                if (EditorUtility.DisplayDialog("清空外部配置",
                        "将移除「外部配置」下的全部引用。\n\n资产文件本身不会被删除，仍保留在原位置。",
                        "清空", "取消"))
                    ClearExternalGuids();
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("展开全部"), false, () => SetExpanded(new List<int> { ConfigDataTreeVirtualFolderNode.NodeId }));
            menu.AddItem(new GUIContent("刷新"), false, () => Reload());
            return menu;
        }

        /// <summary>
        /// 构建外部资产引用节点的右键菜单。因这里是**引用语义**，
        /// 「移除」只删注册表条目，绝不触碰磁盘上的资产文件。
        /// </summary>
        private GenericMenu BuildExternalContextMenu(ConfigDataTreeExternalNode ext)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("从外部配置移除（不删除资产）"), false, () => RemoveExternalGuid(ext.guid));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("定位资源"), false, () => PingGuid(ext.guid));
            menu.AddItem(new GUIContent("在 Inspector 中打开"), false, () =>
            {
                var asset = ext.GetAsset();
                if (asset != null) Selection.activeObject = asset;
            });
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

            GenericMenu menu = null;

            if (item is ConfigDataTreeFolderNode folder)
            {
                menu = BuildFolderContextMenu(folder, false);
            }
            else if (item is ConfigDataTreeExternalNode ext)
            {
                menu = BuildExternalContextMenu(ext);
            }
            else if (item is ConfigDataTreeVirtualFolderNode vf)
            {
                menu = BuildVirtualFolderContextMenu(vf);
            }
            else if (item is ConfigDataTreeFileNode file)
            {
                menu = BuildFileContextMenu(file);
            }

            if (menu == null)
                return;

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
            else if (item is ConfigDataTreeExternalNode ext)
                PingGuid(ext.guid); // 引用节点双击 = 在 Project 里定位原资产
        }

        #region 外部配置（虚拟文件夹）

        /// <summary>把外部资产 GUID 加入注册表（去重，已存在返回 false）。写入自带落盘。</summary>
        public bool AddExternalGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return false;
            return HYCSettings.Instance.AddIncludedAssetGuid(guid);
        }

        /// <summary>从注册表移除一个外部资产 GUID 并刷新树（只删引用，不动磁盘上的资产）。</summary>
        public void RemoveExternalGuid(string guid)
        {
            if (HYCSettings.Instance.RemoveIncludedAssetGuid(guid))
                Reload();
        }

        /// <summary>清空注册表全部条目并刷新树（只删引用，不动磁盘上的资产）。</summary>
        public void ClearExternalGuids()
        {
            HYCSettings.Instance.ClearIncludedAssets();
            Reload();
        }

        /// <summary>选中并滚动到指定 GUID 对应的引用节点（树会先重建）。</summary>
        private void SelectExternalNode(string guid)
        {
            var node = FindExternalNode(rootItem, guid);
            if (node == null)
                return;
            SetExpanded(new List<int> { ConfigDataTreeVirtualFolderNode.NodeId });
            SetSelection(new List<int> { node.id });
            FrameItem(node.id);
            OnSelectionChanged?.Invoke();
        }

        private static ConfigDataTreeExternalNode FindExternalNode(TreeViewItem item, string guid)
        {
            if (item is ConfigDataTreeExternalNode ext && ext.guid == guid)
                return ext;

            if (item.children != null)
            {
                foreach (var child in item.children)
                {
                    var result = FindExternalNode(child, guid);
                    if (result != null)
                        return result;
                }
            }

            return null;
        }

        #endregion

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
            // 懒加载树：沿目录链逐级构建，定位目标文件节点
            var node = ResolveFileNodeByGuid(guid);
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

        /// <summary>
        /// 在懒加载树上定位文件节点：从根出发，按资产路径逐级 BuildChildren 直到其所在文件夹，
        /// 再在其中找到对应 GUID 的文件节点。避免全量重建即可揭示深层资产。
        /// </summary>
        private ConfigDataTreeFileNode ResolveFileNodeByGuid(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return null;

            var dir = Path.GetDirectoryName(path).Replace("\\", "/");
            var rootFolder = ConfigDataSettings.RootFolder.Replace("\\", "/").TrimEnd('/');
            if (!dir.StartsWith(rootFolder, StringComparison.OrdinalIgnoreCase))
                return null;

            var rel = dir.Substring(rootFolder.Length).TrimStart('/');
            var segs = rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            var current = (ConfigDataTreeFolderNode)rootItem;
            var currentPath = rootFolder;
            foreach (var seg in segs)
            {
                BuildChildren(current);
                currentPath += "/" + seg;
                var next = current.children?
                    .OfType<ConfigDataTreeFolderNode>()
                    .FirstOrDefault(f => string.Equals(AssetDatabase.GUIDToAssetPath(f.guid), currentPath, StringComparison.OrdinalIgnoreCase));
                if (next == null)
                    return null;
                current = next;
            }

            BuildChildren(current);
            return current.children?
                .OfType<ConfigDataTreeFileNode>()
                .FirstOrDefault(f => f.guid == guid);
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

        public void CloneAsset(ConfigDataTreeFileNode item)
        {
            var fromPath = AssetDatabase.GUIDToAssetPath(item.guid);
            var type = item.type ?? AssetDatabase.GetMainAssetTypeAtPath(fromPath);
            if (type == null)
                return;

            // 与 ConfigAssetFactory.IsUniqueType 同语义：只认本体直接标注
            var typeAttr = type.GetCustomAttributes(typeof(CfgAssetAttribute), false).FirstOrDefault() as CfgAssetAttribute;
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

        public void DeleteAsset(ConfigDataTreeFileNode item)
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
        public void DeleteFolder(ConfigDataTreeFolderNode folder)
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

        /// <summary>供 FolderConfigEditor 触发重命名，复用 TreeView 内置重命名流程与 RenameEnded 的 FileUtil 搬家逻辑（文件夹改名 GUID 不变）。</summary>
        public void BeginRenameNode(ConfigDataTreeNode node) => BeginRename(node);

        protected override void RenameEnded(RenameEndedArgs args)
        {
            // 此 Unity 版本（2022.3）的 RenameEndedArgs 无 accepted 字段：取消（Esc）时 newName 回退为原名，
            // 以此判定跳过；空名/未变也由 ApplyRenameCore 的 null/empty 守卫兜底。
            var item = FindItem(args.itemID, rootItem);
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

                string result = null;
                if (isFolder)
                {
                    // 文件夹：FileUtil 支持目录搬家，但 .meta 要手动一起搬；内容是「搬」不是「复制+删除」。
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
                    result = AssetDatabase.RenameAsset(path, newName);
                }

                // 诊断：重命名后 GUID 映射与磁盘状态
                var pathAfter = AssetDatabase.GUIDToAssetPath(guid);
                var absAfter = Path.Combine(projectRoot, pathAfter).Replace("\\", "/");

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
            if (DragAndDrop.paths == null || DragAndDrop.paths.Length <= 0)
                return DragAndDropVisualMode.Rejected;

            // 先按「是否在配置根目录内」把拖拽项分成两组，两组走完全不同的语义：
            //   外部组 → 只登记引用到「外部配置」虚拟文件夹（文件原地不动）
            //   内部组 → 沿用原有「树内移动」（真实搬家）
            // 两条路径彻底隔离，外部项绝不进入下面的 Move 分支。
            var root = ConfigDataSettings.RootFolder.Replace('\\', '/').TrimEnd('/');
            var external = new List<string>();
            var inner = new List<string>();
            foreach (var p in DragAndDrop.paths)
            {
                if (string.IsNullOrEmpty(p))
                    continue;
                var np = p.Replace('\\', '/');
                if (np.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(np, root, StringComparison.OrdinalIgnoreCase))
                    inner.Add(np);
                else
                    external.Add(np);
            }

            if (external.Count > 0)
            {
                // 混合拖放（既有内部配置又有外部资产）语义冲突，直接拒并说明
                if (inner.Count > 0)
                {
                    if (args.performDrop)
                        RejectDrop("一次只能处理外部资产或配置资产，请勿混合拖放。");
                    return DragAndDropVisualMode.Rejected;
                }

                // 落点必须是树的空白区：压在具体节点行上时保持老行为（返回 None = 本控件不处理）
                if (!IsBlankDropArea(args))
                    return DragAndDropVisualMode.None;

                string reason;
                List<string> accepted;
                if (!TryClassifyExternal(external, out accepted, out reason))
                {
                    if (args.performDrop)
                        RejectDrop(reason);
                    return DragAndDropVisualMode.Rejected;
                }

                if (args.performDrop)
                {
                    DragAndDrop.AcceptDrag();
                    if (Event.current != null)
                        Event.current.Use();
                    // 弹模态确认框与写注册表都推迟到下一个编辑器 tick：
                    // 拖拽事务内做 IO / 弹窗不稳（与 MoveFileOrDirectory 同因）。
                    EditorApplication.delayCall += () => ConfirmAndIncludeExternal(accepted);
                }

                // Link：明确告诉用户这是「建立引用」而不是 Move
                return DragAndDropVisualMode.Link;
            }

            var dropNode = args.parentItem;
            if (dropNode is ConfigDataTreeFileNode)
                dropNode = dropNode.parent;

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

                    // AssetDatabase.MoveAsset 在 TreeView 拖拽回调的事务内调用会返回成功但不落盘，
                    // 必须延迟到下一编辑器 tick 真正执行（与 RenameEnded/ApplyRenameCore 同因）。
                    EditorApplication.delayCall += () => ApplyMoveCore(moves);
                }

                return DragAndDropVisualMode.Move;
            }

            return DragAndDropVisualMode.Rejected;
        }

        /// <summary>
        /// 判断本次落点是否为树的空白区。Unity 在空白处悬停时 <c>parentItem</c> 可能为 null，
        /// 也可能给出隐藏的根节点（id/depth 均为 -1），两种都算空白。
        /// </summary>
        private static bool IsBlankDropArea(DragAndDropArgs args)
        {
            var item = args.parentItem;
            return item == null || item.depth < 0 || item.id == -1;
        }

        /// <summary>
        /// 筛选可纳入「外部配置」的外部路径：只收单个主资产，文件夹与黑名单一律拒绝并给出理由。
        /// </summary>
        private static bool TryClassifyExternal(List<string> paths, out List<string> accepted, out string reason)
        {
            accepted = new List<string>();
            reason = null;

            foreach (var np in paths)
            {
                if (Directory.Exists(np))
                {
                    reason = $"不支持文件夹：{Path.GetFileName(np)}\n「外部配置」只登记单个资产。";
                    return false;
                }
                if (!ExternalAssetFilter.IsIncludablePath(np))
                {
                    reason = $"不支持该文件类型：{Path.GetFileName(np)}\n（代码、着色器、UI 资源、程序集定义与预制体不可加入）";
                    return false;
                }
                var type = AssetDatabase.GetMainAssetTypeAtPath(np);
                if (ExternalAssetFilter.IsBlacklistedType(type))
                {
                    reason = $"「{Path.GetFileName(np)}」是本框架自身产出的资产，\n它本就应该显示在配置树里，无需加入「外部配置」。";
                    return false;
                }
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(np)))
                {
                    reason = $"无法解析资产 GUID（可能不在 Assets 目录下）：\n{np}";
                    return false;
                }
                accepted.Add(np);
            }

            return accepted.Count > 0;
        }

        /// <summary>拖拽拒绝时的统一出口：吞掉本次 Perform 并说明原因。</summary>
        private static void RejectDrop(string reason)
        {
            DragAndDrop.AcceptDrag();
            if (Event.current != null)
                Event.current.Use();
            EditorApplication.delayCall += () =>
                EditorUtility.DisplayDialog("无法加入「外部配置」", reason, "确定");
        }

        /// <summary>
        /// 外部资产落盘的确认 + 登记：先弹「确定/取消」列出资产名，确认后才写入注册表，
        /// 随后重建树、自动展开「外部配置」并选中首个新增项。全程只写引用，不碰磁盘上的原文件。
        /// </summary>
        private void ConfirmAndIncludeExternal(List<string> paths)
        {
            var pending = new List<string>();
            foreach (var p in paths)
            {
                var guid = AssetDatabase.AssetPathToGUID(p);
                if (string.IsNullOrEmpty(guid) || pending.Contains(guid))
                    continue;
                if (HYCSettings.Instance.IsIncluded(guid))
                    continue;
                pending.Add(guid);
            }

            if (pending.Count == 0)
            {
                EditorUtility.DisplayDialog("外部配置", "这些资产已经在「外部配置」里了。", "确定");
                return;
            }

            var names = pending.Select(g => Path.GetFileName(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            var shown = names.Length <= 6
                ? names
                : names.Take(6).Concat(new[] { $"…等共 {names.Length} 个" }).ToArray();

            var message = $"是否将以下 {pending.Count} 个资产加入「外部配置」？\n\n· "
                          + string.Join("\n· ", shown)
                          + "\n\n「外部配置」是一个虚拟文件夹：\n"
                          + "文件不会被移动或复制，仍保留在原位置；这里只登记引用，\n"
                          + "可在右键菜单中移除（移除只删引用，不会删除资产本身）。";

            if (!EditorUtility.DisplayDialog("加入「外部配置」", message, "确定", "取消"))
                return;

            foreach (var guid in pending)
                HYCSettings.Instance.AddIncludedAssetGuid(guid);

            Reload();
            SelectExternalNode(pending[0]);
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
