using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Editor
{
    /// <summary>
    /// 把「导入 HYCSimple 样例」放进 HYC Framework 菜单，免去走 Package Manager 手动找 Samples 区。
    ///
    /// <para><b>为什么拷到 Assets/HYCSimple/ 而不是 Assets/Samples/&lt;包名&gt;/：</b>
    /// <c>Assets/Samples/&lt;包名&gt;/</c> 是 Package Manager 的<b>保留区</b> —— 只有当包被「解析」
    /// 且 package.json 声明了 samples 时，Unity 才会导入它的内容；否则 Unity 扫到 &lt;包名&gt; 一层就停，
    /// Project 窗口展开为空（官方文档点名的 "samples field in the package manifest is empty" 症状）。</para>
    ///
    /// <para>本菜单按钮不依赖包解析，故拷到<b>普通目录</b> <c>Assets/HYCSimple/</c> 保证「点了就有」，
    /// 且每次<b>全量重拷</b>（先删旧副本），避免「上次只导了 Attr、这次补了 Config/BT/Loc/Scene」时漏拷/残留。</para>
    ///
    /// <para>想要<b>官方落点</b>（<c>Assets/Samples/com.hyc.framework/&lt;版本&gt;/HYCSimple/</c>）请
    /// <b>重启 Unity</b> 后用 Package Manager 的 <b>Samples → HYCSimple → Import</b>。
    /// ⚠️ 两条路<b>二选一</b>，别同时用 —— 否则同一批类型被编译两次（CS0101 重复定义）。</para>
    /// </summary>
    public static class HYCSimpleSampleImporter
    {
        const string PackageName = "com.hyc.framework";
        const string SampleName = "HYCSimple";
        const string DestFolder = "HYCSimple";              // → Assets/HYCSimple
        const string ProbeRelFile = "/Attr/GameAttrKey.cs"; // 判断整棵树是否真被导入

        // priority=210：与 Settings 同属"项目级低频"档（差 10，故两项之间不画线）。
        // 上一档 Tools 是 100，差值 110 ≥ 11 ⇒ 与 Tools 之间有分隔线。
        // 导入是破坏性操作（每次全量重拷 Assets/HYCSimple），故置于该档末尾。
        [MenuItem("HYC Framework/Samples/Import HYCSimple", false, 210)]
        public static void Import()
        {
            // 1) 包在磁盘上的根目录
            string pkgRoot = ResolvePackageRoot();
            if (string.IsNullOrEmpty(pkgRoot) || !Directory.Exists(pkgRoot))
            {
                Debug.LogWarning("[HYC] 找不到包 " + PackageName + " 的安装路径，无法导入样例。" +
                                 "请确认包已正常解析（可重启 Unity 或重新加包）。");
                return;
            }

            // 2) 源：Samples~/HYCSimple（带 ~，Unity 不导入）
            string src = Path.Combine(pkgRoot, "Samples~", SampleName);
            if (!Directory.Exists(src))
            {
                Debug.LogWarning("[HYC] 未找到样例源目录：" + src);
                return;
            }

            // 3) 目标：Assets/HYCSimple（普通目录，一定被 Unity 导入）
            string dst = Path.Combine(Application.dataPath, DestFolder);

            // 全量刷新：先删旧副本，避免残留/漏拷（例如早先只有 Attr 的旧副本）
            if (Directory.Exists(dst))
            {
                try { Directory.Delete(dst, true); }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[HYC] 清理旧样例目录失败，请手动删除 " + dst + " 后重试：" + e.Message);
                    return;
                }
            }

            CopyDirectory(src, dst);
            EnsureImported(dst);
            WarnIfPmCopyExists();   // 旧官方 PM 副本仍在会触发 CS0101，提前点出

            if (IsImported(dst))
                Debug.Log("[HYC] 已导入 HYCSimple 样例（Attr / Config / BT / Loc / Scene）→ " + dst);
            else
                Debug.LogWarning("[HYC] 已拷贝样例到 " + dst + "，但 Unity 尚未导入其内容。" +
                                 "请菜单 Assets → Refresh，或重启 Unity 后再看。");
        }

        /// <summary>非破坏性检测：官方 PM 落点若仍有旧副本，会和 Assets/HYCSimple/ 重复编译同批类型（CS0101）。</summary>
        static void WarnIfPmCopyExists()
        {
            var samplesRoot = Path.Combine(Application.dataPath, "Samples", PackageName);
            if (!Directory.Exists(samplesRoot)) return;
            foreach (var verDir in Directory.GetDirectories(samplesRoot))
            {
                var pmCopy = Path.Combine(verDir, SampleName);
                if (Directory.Exists(pmCopy))
                    Debug.LogWarning("[HYC] 检测到旧的官方 PM 导入副本 " + pmCopy +
                        "，与菜单导入的 Assets/HYCSimple/ 会重复编译同一批类型（CS0101 重复定义）。" +
                        "请手动删除该目录后再编译：" + pmCopy);
            }
        }

        static string ResolvePackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + PackageName + "/package.json");
            if (info != null)
            {
                if (!string.IsNullOrEmpty(info.resolvedPath) && Directory.Exists(info.resolvedPath))
                    return info.resolvedPath;
                if (!string.IsNullOrEmpty(info.assetPath))
                {
                    var full = Path.GetFullPath(info.assetPath);
                    if (Directory.Exists(full)) return full;
                }
            }
            // 兜底：本地 file: 包在 Packages/ 下通常是真实路径的映射
            var fallback = Path.GetFullPath("Packages/" + PackageName);
            return Directory.Exists(fallback) ? fallback : null;
        }

        /// <summary>
        /// 绝对路径 → 工程内相对 assetPath（Assets/...），再对整棵树递归导入。
        /// 单独用 <c>AssetDatabase.Refresh()</c> 对新拷入的目录树不一定递归生效，
        /// <c>ImportAsset(ImportRecursive)</c> 更可靠。
        /// </summary>
        static void EnsureImported(string absDst)
        {
            string rel = ToAssetPath(absDst);
            if (!string.IsNullOrEmpty(rel))
                AssetDatabase.ImportAsset(rel, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
        }

        /// <summary>用具体文件判断整棵树是否真的进了 AssetDatabase（而非只创建了外层 .meta）。</summary>
        static bool IsImported(string absDst)
        {
            string rel = ToAssetPath(absDst);
            if (string.IsNullOrEmpty(rel)) return false;
            return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(rel + ProbeRelFile));
        }

        static string ToAssetPath(string absPath)
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            string abs = absPath.Replace('\\', '/');
            return abs.StartsWith(dataPath) ? "Assets" + abs.Substring(dataPath.Length) : null;
        }

        static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                if (f.EndsWith(".meta")) continue;   // 不拷源 .meta，交给 Unity 现生成
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            }
            foreach (var d in Directory.GetDirectories(src))
                CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }
}
