using System;
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

            var expected = FolderNameFor(AssetDatabase.GetMainAssetTypeAtPath(assetPath));
            if (expected == null) return false; // 非配置类资产（模板/枚举之外）不归我们管

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

        /// <summary>资产移动 / 重命名前回调。返回 FailedMove = 取消操作并提示；DidMove = 放行。</summary>
        static AssetMoveResult OnWillMoveAsset(string oldPath, string newPath)
        {
            GuardPrompted = false;
            var block = ConfigAssetLayout.ShouldBlockMove(oldPath, newPath);
            if (block != null)
            {
                GuardPrompted = true;
                // 两按钮：用户可「仍然移动」覆盖建议，或「取消」接受拦截
                if (EditorUtility.DisplayDialog("不建议移动", block, "仍然移动", "取消"))
                    return AssetMoveResult.DidMove;
                return AssetMoveResult.FailedMove;
            }
            return AssetMoveResult.DidMove;
        }
    }
}
