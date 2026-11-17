namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置资产索引：全包唯一的「配置数据库」。以资产路径为主键，保存路径 / GUID / 精确类型 /
    /// 所属文件夹 / 类型文件夹 / 资产对象（懒加载）等事实；左侧树、右键菜单、导出预检、
    /// 导出收集、类型文件夹归置都只读这里，不再各自 <c>AssetDatabase.FindAssets</c>。
    /// </summary>
    /// <remarks>
    /// 更新模型：AssetPostprocessor 的导入 / 删除 / 移动回调做**增量** Upsert / Remove / Move；
    /// 域重载后静态字典清空且 Unity 不会重导入未变动资产，故冷启动补一次 <see cref="Rebuild"/>
    /// （整个会话唯一一次全量扫描）。每次结构变更 <see cref="Revision"/> 自增，按文件夹的派生视图
    /// 靠 Revision 惰性失效，避免重建整表。
    /// <para><b>回调安全铁律</b>：<c>TryPeek</c> 是唯一纯内存查询，可在禁止调用 AssetDatabase 的
    /// <c>AssetModificationProcessor</c> 回调里使用；其余查询会 EnsureReady，只能用于普通编辑器上下文。</para>
    /// <para>索引只登记事实，不做业务过滤：是否可导出、是否枚举 / 模板等口径仍由各调用方按原有条件判断，
    /// 因此接入后行为与接入前一致，只是数据源统一。</para>
    /// </remarks>
    public static class ConfigAssetIndex
    {
        /// <summary>一条资产记录。字段是可直接读的事实，按需扩展（数量 / 位置 / 配置 / 类型…）。</summary>
        public sealed class Entry
        {
            public readonly string Path;        // 规范化资产路径（主键）
            public readonly string Guid;
            public readonly System.Type AssetType; // 精确 GetMainAssetTypeAtPath 结果
            public readonly string Folder;      // 直接父目录
            public readonly string FolderGuid;  // 父目录 GUID（左树按文件夹 GUID 找节点用）
            public readonly string TypeFolder;  // 该类型应归置的文件夹名，非配置类为 null

            private UnityEngine.ScriptableObject mAsset;

            internal Entry(string path, string guid, System.Type type, string folder,
                           string folderGuid, string typeFolder)
            {
                Path = path;
                Guid = guid;
                AssetType = type;
                Folder = folder;
                FolderGuid = folderGuid;
                TypeFolder = typeFolder;
            }

            /// <summary>按需加载资产对象（首次访问才反序列化）。字段值本身是实时的。</summary>
            public UnityEngine.ScriptableObject Asset
            {
                get
                {
                    if (mAsset == null)
                        mAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>(Path);
                    return mAsset;
                }
            }

            internal void DropAsset() { mAsset = null; }
        }

        private sealed class FolderView
        {
            public int Revision = -1;
            public System.Collections.Generic.List<Entry> Items;
        }

        // 主表 + 两个增量维护的视图
        private static readonly System.Collections.Generic.Dictionary<string, Entry> sByPath =
            new System.Collections.Generic.Dictionary<string, Entry>(System.StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Generic.List<Entry> sAll =
            new System.Collections.Generic.List<Entry>();
        private static readonly System.Collections.Generic.Dictionary<System.Type, System.Collections.Generic.List<Entry>> sByType =
            new System.Collections.Generic.Dictionary<System.Type, System.Collections.Generic.List<Entry>>();
        private static readonly System.Collections.Generic.Dictionary<string, FolderView> sByFolder =
            new System.Collections.Generic.Dictionary<string, FolderView>(System.StringComparer.OrdinalIgnoreCase);
        // 按「可赋值类型」的派生视图（含派生类型），与按文件夹的视图分开缓存
        private static readonly System.Collections.Generic.Dictionary<System.Type, FolderView> sByAssignable =
            new System.Collections.Generic.Dictionary<System.Type, FolderView>();
        // 文件夹路径 → GUID：整库同一个文件夹只查一次，避免每条资产都反查
        private static readonly System.Collections.Generic.Dictionary<string, string> sFolderGuids =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        private static readonly Entry[] sEmpty = new Entry[0];
        private static int sRevision;
        private static bool sReady;
        private static double sLastImportHandledAt;

        /// <summary>结构版本号。每次增删改 ++，调用方可据此判断自己的派生缓存是否过期。</summary>
        public static int Revision => sRevision;

        /// <summary>索引是否已建好（冷启动 Rebuild 完成）。</summary>
        public static bool Ready => sReady;

        /// <summary>
        /// 标记「本次资产变更已由导入钩子精确增量处理」。
        /// <see cref="Reconcile"/> 的调用方据此跳过日常保存触发的全工程扫描，
        /// 只在外部 VCS 拉取等未走导入钩子的变更时才真正对齐。
        /// </summary>
        public static void NoteImportHandled()
        {
            sLastImportHandledAt = UnityEditor.EditorApplication.timeSinceStartup;
        }

        /// <summary>自 <paramref name="since"/> 之后导入钩子是否已处理过变更。</summary>
        public static bool HandledSince(double since) => sLastImportHandledAt >= since;

        // ---------- 查询 ----------

        /// <summary>纯内存按路径取一条。未命中返回 false。<b>唯一允许在资产修改回调里调用的查询</b>。</summary>
        public static bool TryPeek(string path, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(path)) return false;
            return sByPath.TryGetValue(Normalize(path), out entry);
        }

        /// <summary>全部条目（O(1) 取快照，随 Upsert/Remove 增量维护）。</summary>
        public static System.Collections.Generic.IReadOnlyList<Entry> All()
        {
            EnsureReady();
            return sAll;
        }

        /// <summary>按**精确类型**取条目（O(1) 取视图）。派生类型不混入基类桶。</summary>
        public static System.Collections.Generic.IReadOnlyList<Entry> ByType(System.Type type)
        {
            EnsureReady();
            if (type != null && sByType.TryGetValue(type, out var list)) return list;
            return sEmpty;
        }

        /// <summary>按精确类型计数。</summary>
        public static int CountByType(System.Type type)
        {
            EnsureReady();
            if (type != null && sByType.TryGetValue(type, out var list)) return list.Count;
            return 0;
        }

        /// <summary>
        /// 按「可赋值」取条目（含派生类型），对齐 <c>AssetDatabase.FindAssets("t:Type")</c> 的语义。
        /// 纯内存遍历主表，不加载资产对象；结果按 (基类型, Revision) 缓存，结构不变时 O(1)。
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<Entry> ByTypeAssignable(System.Type baseType)
        {
            EnsureReady();
            if (baseType == null) return sEmpty;
            if (!sByAssignable.TryGetValue(baseType, out var view))
            {
                view = new FolderView();
                sByAssignable[baseType] = view;
            }
            if (view.Revision != sRevision || view.Items == null)
            {
                view.Items = new System.Collections.Generic.List<Entry>();
                for (var i = 0; i < sAll.Count; i++)
                    if (sAll[i].AssetType != null && baseType.IsAssignableFrom(sAll[i].AssetType))
                        view.Items.Add(sAll[i]);
                view.Revision = sRevision;
            }
            return view.Items;
        }

        /// <summary>取文件夹（含任意深度子文件夹）下的全部条目。结果按 Revision 缓存，结构不变时 O(1)。</summary>
        public static System.Collections.Generic.IReadOnlyList<Entry> ByFolder(string folder)
        {
            EnsureReady();
            var key = Normalize(folder ?? string.Empty);
            if (!sByFolder.TryGetValue(key, out var view))
            {
                view = new FolderView();
                sByFolder[key] = view;
            }
            if (view.Revision != sRevision || view.Items == null)
            {
                var prefix = key.Length > 0 ? key + "/" : string.Empty;
                view.Items = new System.Collections.Generic.List<Entry>();
                for (var i = 0; i < sAll.Count; i++)
                {
                    var e = sAll[i];
                    if (prefix.Length == 0 || e.Path.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                        view.Items.Add(e);
                }
                view.Revision = sRevision;
            }
            return view.Items;
        }

        // ---------- 增量维护 ----------

        /// <summary>新增 / 内容变更：重解析这一条，只动受影响的桶。已知类型时传 type 可省一次反查。</summary>
        public static void Upsert(string path, System.Type knownType = null)
        {
            var p = Normalize(path);
            if (p.Length == 0) return;
            // 只收 .asset：贴图 / 脚本 / 场景每次导入都进来会白付一次类型反查
            if (!p.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase)) return;

            var type = knownType ?? UnityEditor.AssetDatabase.GetMainAssetTypeAtPath(p);
            if (type == null)
            {
                Remove(p);
                return;
            }

            Remove(p); // 先摘旧桶，保证同一 path 不会在视图里留两份

            var slash = p.LastIndexOf('/');
            var folder = slash > 0 ? p.Substring(0, slash) : string.Empty;
            if (!sFolderGuids.TryGetValue(folder, out var folderGuid))
            {
                folderGuid = folder.Length > 0 ? UnityEditor.AssetDatabase.AssetPathToGUID(folder) : string.Empty;
                sFolderGuids[folder] = folderGuid;
            }
            var typeFolder = HYC.Framework.Config.Editor.ConfigAssetLayout.FolderNameFor(type);
            var entry = new Entry(p, UnityEditor.AssetDatabase.AssetPathToGUID(p), type, folder,
                                  folderGuid, typeFolder);

            sByPath[p] = entry;
            sAll.Add(entry);
            if (!sByType.TryGetValue(type, out var bucket))
            {
                bucket = new System.Collections.Generic.List<Entry>();
                sByType[type] = bucket;
            }
            bucket.Add(entry);
            sRevision++;
        }

        /// <summary>移除一条（删除 / 移动前的旧路径）。</summary>
        public static void Remove(string path)
        {
            var p = Normalize(path);
            if (p.Length == 0 || !sByPath.TryGetValue(p, out var entry)) return;

            sByPath.Remove(p);
            sAll.Remove(entry);
            if (sByType.TryGetValue(entry.AssetType, out var bucket))
            {
                bucket.Remove(entry);
                if (bucket.Count == 0) sByType.Remove(entry.AssetType);
            }
            sRevision++;
        }

        /// <summary>移动 / 改名：旧路径摘除 + 新路径登记，两步合并成一次对外变更。</summary>
        public static void Move(string from, string to)
        {
            Remove(from);
            Upsert(to);
        }

        /// <summary>内容变了但路径没变：只丢弃已缓存的对象引用，不动结构（类型 / 数量视图无需失效）。</summary>
        public static void Invalidate(string path)
        {
            var p = Normalize(path);
            if (p.Length > 0 && sByPath.TryGetValue(p, out var entry)) entry.DropAsset();
        }

        /// <summary>
        /// 全量重建。仅用于冷启动（域重载后静态字典清空）与显式「刷新」菜单；
        /// 日常变更一律走 Upsert / Remove / Move，不重建。
        /// </summary>
        public static void Rebuild()
        {
            sByPath.Clear();
            sAll.Clear();
            sByType.Clear();
            sByFolder.Clear();
            sByAssignable.Clear();
            sFolderGuids.Clear();
            sRevision++;

            // 不按 RootFolder 限定：ConfigBaseExport.CollectAssets 原本是全工程 t:类型名 扫描，
            // 且单资产导出允许资产不在根目录下，索引收全工程才不会漏。
            var guids = UnityEditor.AssetDatabase.FindAssets("t:ScriptableObject");
            for (var i = 0; i < guids.Length; i++)
                Upsert(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]));

            sReady = true;
        }

        /// <summary>
        /// 兜底对齐：projectChanged 防抖后调用。与资产库当前 GUID 集合做差集，只补增删的条目，
        /// 不重建结构 —— 覆盖外部 VCS 拉取 / 脚本改资产等未走导入钩子的情形。
        /// </summary>
        public static void Reconcile()
        {
            EnsureReady();
            var guids = UnityEditor.AssetDatabase.FindAssets("t:ScriptableObject");
            var current = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < guids.Length; i++)
            {
                var p = Normalize(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]));
                if (p.Length == 0 || !p.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase)) continue;
                current.Add(p);
                if (!sByPath.ContainsKey(p)) Upsert(p);
            }

            System.Collections.Generic.List<string> stale = null;
            foreach (var p in sByPath.Keys)
            {
                if (current.Contains(p)) continue;
                if (stale == null) stale = new System.Collections.Generic.List<string>();
                stale.Add(p);
            }
            if (stale != null)
                for (var i = 0; i < stale.Count; i++) Remove(stale[i]);
        }

        // ---------- 内部 ----------

        private static void EnsureReady()
        {
            if (!sReady) Rebuild();
        }

        private static string Normalize(string p)
            => (p ?? string.Empty).Replace("\\", "/").Trim('/');
    }
}
