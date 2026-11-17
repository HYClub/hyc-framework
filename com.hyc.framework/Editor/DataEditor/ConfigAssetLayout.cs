using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置资产「类型文件夹」布局规则——创建自动归置与移出守护的唯一真源：
    /// <list type="bullet">
    /// <item>生成的配置资产 → 根目录/<b>类型短名</b>（<see cref="ConfigTypeDisplay.GetShortName"/>，即数据树里 [角色] 那个名字）</item>
    /// <item>枚举定义（<see cref="ConfigEnumDefinition"/>）→ 根目录/枚举</item>
    /// <item>配置模板（<see cref="ConfigTemplate"/>）→ 根目录/配置模板</item>
    /// </list>
    /// 文件夹不存在则逐级创建。用户自建子文件夹、手动拖拽归置一律不干预；
    /// 只有「已归置资产被移出自己类型文件夹」才给不建议提示（见 <see cref="ConfigAssetMoveGuard"/>）。
    /// </summary>
    public static class ConfigAssetLayout
    {
        /// <summary>枚举定义的类型文件夹名（与数据树前缀一致）。</summary>
        public const string EnumFolderName = "枚举";

        /// <summary>配置模板的类型文件夹名（与数据树前缀一致）。</summary>
        public const string TemplateFolderName = "配置模板";

        /// <summary>
        /// 资产类型 → 类型文件夹名。非配置类资产（无 <see cref="CfgAssetAttribute"/> 的 ScriptableObject）返回 null（不归置）。
        /// </summary>
        public static string FolderNameFor(Type assetType)
        {
            if (assetType == null) return null;
            if (assetType == typeof(ConfigTemplate)) return TemplateFolderName;
            if (assetType == typeof(ConfigEnumDefinition)) return EnumFolderName;
            if (!typeof(ConfigBase).IsAssignableFrom(assetType)) return null;
            var shortName = ConfigTypeDisplay.GetShortName(assetType);
            return Sanitize(shortName);
        }

        /// <summary>类型文件夹路径（纯计算，不建目录）。非配置类型返回 null。</summary>
        public static string TypeFolderPath(Type assetType)
        {
            var name = FolderNameFor(assetType);
            return name == null ? null : ConfigDataSettings.RootFolder + "/" + name;
        }

        /// <summary>类型文件夹路径，不存在则逐级创建。非配置类型返回 null。</summary>
        public static string EnsureTypeFolder(Type assetType)
        {
            var path = TypeFolderPath(assetType);
            if (path == null) return null;
            EnsureFolder(path);
            return path;
        }

        /// <summary>
        /// 资产当前是否正处于它自己的类型文件夹内（根目录/类型名/…，含任意深度子文件夹）。
        /// 用于移出守护的判定：只有「已归置」资产受保护；根目录直下或用户自建目录里的存量资产不受限。
        /// </summary>
        public static bool IsInItsTypeFolder(string assetPath) => TryGetItsTypeFolder(assetPath, out _);

        /// <summary>取资产对应的类型文件夹路径；仅当资产正处在该文件夹内时返回 true。</summary>
        public static bool TryGetItsTypeFolder(string assetPath, out string typeFolder)
        {
            typeFolder = null;
            if (string.IsNullOrEmpty(assetPath)) return false;

            var root = Normalize(ConfigDataSettings.RootFolder);
            if (string.IsNullOrEmpty(root)) return false;
            var path = Normalize(assetPath);
            if (!path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) return false;

            var relative = path.Substring(root.Length + 1);
            var slash = relative.IndexOf('/');
            if (slash <= 0) return false; // 资产直接在根目录下 = 未归置

            // 回调安全：类型文件夹名一律从 ConfigAssetIndex 读（TryPeek 是纯内存查询，
            // 不碰 AssetDatabase，可安全用于 OnWillMoveAsset）。
            // 未命中 → expected 为 null → 下方比较不成立 → 按「未归置」放行。
            var expected = ConfigAssetIndex.TryPeek(path, out var entry) ? entry.TypeFolder : null;

            var firstSegment = relative.Substring(0, slash);
            if (!string.Equals(firstSegment, expected, StringComparison.OrdinalIgnoreCase))
                return false; // 在别的文件夹里（用户自建结构/历史遗留）= 用户自由

            typeFolder = root + "/" + firstSegment;
            return true;
        }

        /// <summary>
        /// 判定一次资产移动是否应被「移出类型文件夹」守护拦截：仅当「已归置的 .asset 被移出自身类型文件夹」
        /// 时返回提示语，否则返回 null（放行）。
        /// 同时供 <see cref="ConfigAssetMoveGuard"/>（Project 窗口拖拽）与 <see cref="ConfigDataTree"/>（自定义数据树拖拽）复用，
        /// 保证两条路径的提示与拦截语义完全一致、且不会因 Unity 回调签名差异而失效。
        /// </summary>
        public static string ShouldBlockMove(string oldPath, string newPath)
        {
            oldPath = StripMeta(oldPath);
            newPath = StripMeta(newPath);
            if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
                return null;

            // 只守 .asset（配置 / 枚举 / 模板都是 ScriptableObject .asset）；其余（.cs/.prefab…）与文件夹移动一律放行
            if (!oldPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                return null;

            // 只守「已归置」资产：不在自己类型文件夹里的（根目录直下 / 用户自建结构 / 历史遗留）移动是用户自由
            if (!TryGetItsTypeFolder(oldPath, out var typeFolder))
                return null;

            // 目标仍在类型文件夹内（含子文件夹，如同目录重命名）→ 放行
            var target = (newPath ?? "").Replace("\\", "/");
            if (target.StartsWith(typeFolder + "/", StringComparison.OrdinalIgnoreCase))
                return null;

            var name = Path.GetFileName(oldPath);
            return $"不建议将「{name}」移出「{typeFolder}」文件夹。\n" +
                   "配置资产按类型文件夹组织（创建时自动归置，没有则自动创建）；" +
                   "文件夹内可自由移动 / 建子文件夹 / 重命名，移出会破坏该结构。";
        }

        // ---------- 兼容壳：类型文件夹登记并入 ConfigAssetIndex ----------

        /// <summary>
        /// 登记一条资产（调用方已持有类型，省一次反查）。
        /// 旧实现维护独立的「路径 → 类型文件夹名」字典；现已并入
        /// <see cref="ConfigAssetIndex"/> 这条唯一真源，避免两套缓存各自失效。
        /// 保留公开签名是因为 <see cref="ConfigAssetFactory"/> 在创建资产后直接调用。
        /// </summary>
        public static void Register(string assetPath, Type assetType)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            ConfigAssetIndex.Upsert(assetPath, assetType);
        }

        /// <summary>
        /// 登记一条资产（由索引内部反查类型）。
        /// <b>只允许在 AssetModificationProcessor 回调之外调用</b>。
        /// </summary>
        public static void Register(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            ConfigAssetIndex.Upsert(assetPath);
        }

        /// <summary>移除登记（资产删除，或改名 / 移动前的旧路径）。</summary>
        public static void Unregister(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            ConfigAssetIndex.Remove(assetPath);
        }

        /// <summary>
        /// 冷启动补齐：域重载后静态索引会清空，而 Unity <b>不会</b>重导入未变动的资产
        /// （Import 钩子不再触发），故需在 AssetDatabase 就绪后补一次全量扫描。
        /// </summary>
        public static void WarmupTypeFolderCache()
        {
            ConfigAssetIndex.Rebuild();
        }

        // ---------- 内部 ----------

        private static string Normalize(string p) => (p ?? "").Replace("\\", "/").Trim('/');

        private static string StripMeta(string p)
            => p != null && p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? p.Substring(0, p.Length - ".meta".Length)
                : p;

        private static string Sanitize(string name)
        {
            var s = (name ?? "").Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c.ToString(), "_");
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>逐级创建目录（必须在 Assets 下）；已存在则跳过。</summary>
        private static void EnsureFolder(string folder)
        {
            folder = Normalize(folder);
            if (!folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"配置目录必须在 Assets 下：{folder}");

            if (AssetDatabase.IsValidFolder(folder))
                return;

            var parts = folder.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }

    /// <summary>
    /// 配置资产「移出类型文件夹」守护（需求：把已归置的 asset 移到别的文件夹给不建议提示，
    /// 类型文件夹内部移动/建子文件夹不管）。只拦 .asset 文件，不拦文件夹本身。
    /// 拦截方式 = <see cref="AssetModificationProcessor.OnWillMoveAsset"/> 返回 <see cref="AssetMoveResult.FailedMove"/>（Unity 取消移动），
    /// 并自绘「不建议移动」对话框（含「仍然移动」覆盖选项）。
    /// <para><b>关键修复</b>：旧实现返回 <see cref="string"/> 的签名在 Unity 2022.3 已不被回调分发器识别，
    /// 导致守护静默失效（既不拦截也不弹窗）。现改回官方 <see cref="AssetMoveResult"/> 枚举签名。</para>
    /// 判定逻辑统一收敛到 <see cref="ConfigAssetLayout.ShouldBlockMove"/>，Project 窗口与自定义数据树两条路径共用，语义一致。
    /// </summary>
    public sealed class ConfigAssetMoveGuard : AssetModificationProcessor
    {
        /// <summary>守护是否已在本次移动中向用户弹窗（供自定义数据树避免重复弹窗）。</summary>
        public static bool GuardPrompted { get; internal set; }

        /// <summary>
        /// 资产移动 / 重命名前回调。<see cref="AssetMoveResult.FailedMove"/> = 取消该次操作；
        /// <see cref="AssetMoveResult.DidNotMove"/> = 放行并交回 Unity 执行真实改名。
        /// <para><b>禁止返回 <see cref="AssetMoveResult.DidMove"/></b>：其官方语义是「本回调已在磁盘上完成移动」，
        /// 本守护从不自行搬文件，返回它会让 Unity 跳过真实改名，导致 Project 窗口所有改名静默失效。</para>
        /// </summary>
        static AssetMoveResult OnWillMoveAsset(string oldPath, string newPath)
        {
            GuardPrompted = false;
            var block = ConfigAssetLayout.ShouldBlockMove(oldPath, newPath);
            if (block != null)
            {
                GuardPrompted = true;
                // 两按钮：用户可「仍然移动」覆盖建议，或「取消」接受拦截
                if (EditorUtility.DisplayDialog("不建议移动", block, "仍然移动", "取消"))
                    return AssetMoveResult.DidNotMove;
                return AssetMoveResult.FailedMove;
            }

            // 放行：必须返回 DidNotMove 交回 Unity 执行真实改名（返回 DidMove 会让改名静默失效，见上）
            return AssetMoveResult.DidNotMove;
        }
    }

    /// <summary>
    /// 维护 <see cref="ConfigAssetIndex"/>：导入 / 删除 / 移动时做**增量**更新，不重建整表。
    /// <para><b>回调层级差异</b>：AssetPostprocessor 的钩子不在 AssetModificationProcessor 内部，
    /// 因此这里可以安全调用 AssetDatabase API——正是为了让 <see cref="ConfigAssetMoveGuard"/> 在
    /// 改名回调里<b>不必</b>调用它们而存在。</para>
    /// </summary>
    public sealed class ConfigAssetTypeCacheSync : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported ?? Array.Empty<string>())
                ConfigAssetIndex.Upsert(p);
            foreach (var p in deleted ?? Array.Empty<string>())
                ConfigAssetIndex.Remove(p);

            var moves = moved ?? Array.Empty<string>();
            var movesFrom = movedFrom ?? Array.Empty<string>();
            for (var i = 0; i < moves.Length && i < movesFrom.Length; i++)
                ConfigAssetIndex.Move(movesFrom[i], moves[i]);

            ConfigAssetIndex.NoteImportHandled();
        }
    }

    /// <summary>
    /// 索引生命周期：① 域重载 / 打开工程后补一次全量扫描（延到 AssetDatabase 就绪）；
    /// ② <c>projectChanged</c> 防抖兜底——外部 VCS 拉取 / 脚本改资产等未走导入钩子的情形，
    /// 用 <see cref="ConfigAssetIndex.Reconcile"/> 做差集对齐，不重建结构。
    /// </summary>
    [InitializeOnLoad]
    public static class ConfigAssetTypeCacheWarmup
    {
        private static double sPendingAt = -1;

        static ConfigAssetTypeCacheWarmup()
        {
            EditorApplication.delayCall += Warmup;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void Warmup()
        {
            // 首次查询可能已通过 EnsureReady 建好索引，这里不重复扫一次全工程
            if (!ConfigAssetIndex.Ready) ConfigAssetIndex.Rebuild();
        }

        private static void OnProjectChanged()
        {
            sPendingAt = EditorApplication.timeSinceStartup;
        }

        private static void OnEditorUpdate()
        {
            if (sPendingAt < 0) return;
            if (EditorApplication.timeSinceStartup - sPendingAt < 0.15) return;
            var since = sPendingAt;
            sPendingAt = -1;
            // 导入钩子已精确处理过的变更不再全工程扫描（日常 Ctrl+S 零额外开销）；
            // 只有外部 VCS 拉取等未触发导入钩子的变更才做差集兜底。
            if (!ConfigAssetIndex.HandledSince(since))
                ConfigAssetIndex.Reconcile();
        }
    }
}
