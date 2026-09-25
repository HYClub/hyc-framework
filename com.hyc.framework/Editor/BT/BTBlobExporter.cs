// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTBlobExporter.cs
// 说明: 把工程内全部 BTTreeAsset 导出为 Blob 文件(打包用) + 生成清单
//       对应运行时的 Runtime/BT/BTBlobLoader.cs
//       与配置管线(StreamingAssets/ConfigBlob)同构: 产物必须落在 StreamingAssets 内才能随包发布
// ============================================================

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    /// <summary>
    /// 行为树 Blob 导出器。
    ///
    /// <para><b>为什么需要它</b>：<c>BTTreeAsset</c> / <c>BTBlobBuilder</c> 都在 Editor 程序集，
    /// 打包后不存在；<c>BTAutoRegisterOnPlay</c> 是编辑器钩子，真机不执行。
    /// 所以<b>真机必须先把树导出成文件</b>，运行时再由 <c>BTBlobLoader</c> 读回。</para>
    ///
    /// <para>编辑器里<b>无需</b>导出也能跑（进 Play 会自动注册），导出只服务于打包；
    /// 但也正因如此，<b>改完树一定要重新导出</b>，否则真机跑的是旧版本。</para>
    /// </summary>
    public static class BTBlobExporter
    {
        /// <summary>默认输出目录（StreamingAssets 内，随包发布）。与 ConfigDataSettings.ConfigBlob 平级。</summary>
        public const string DefaultOutputFolder = "Assets/StreamingAssets/BTBlob";

        public const string TreeTypeFilter = "t:HYC.Framework.BT.Editor.BTTreeAsset";

        [MenuItem("Tools/HYC/BT/导出 Blob(打包用)", false, 30)]
        public static void ExportAllMenu()
        {
            var ok = ExportAll();
            Debug.Log($"[BT] 导出完成: {ok} 棵树 → {DefaultOutputFolder}");
        }

        /// <summary>
        /// 导出全部树资产。返回成功棵数。
        /// 打包流程可直接调用（例如在构建前自动跑一次，避免忘记导出）。
        /// </summary>
        /// <param name="outputDir">输出目录；null 用 <see cref="DefaultOutputFolder"/>。</param>
        public static int ExportAll(string outputDir = null)
        {
            var dir = string.IsNullOrEmpty(outputDir) ? DefaultOutputFolder : outputDir;

            // 先清空旧产物：改名/删树后不会残留幽灵文件被运行时误加载
            ClearFolder(dir);

            var ok = 0;
            var manifest = new List<string>
            {
                "# HYC BT Blob manifest",
                "# 格式: treeId|资产名   (由 菜单 Tools/HYC/BT/导出 Blob(打包用) 自动生成, 请勿手改)",
                $"# version={BTBlobLoader.FileVersion}",
            };

            foreach (var guid in AssetDatabase.FindAssets(TreeTypeFilter))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(assetPath);
                if (asset == null) continue;

                if (asset.TreeId == 0)
                {
                    Debug.LogWarning($"[BT] 跳过未设置 TreeId 的树资产: {assetPath}");
                    continue;
                }

                var outPath = Path.Combine(dir, asset.TreeId + BTBlobLoader.FileExtension);
                if (BTBlobBuilder.BuildToFile(asset, outPath))
                {
                    ok++;
                    manifest.Add($"{asset.TreeId}|{asset.name}");
                }
                else
                {
                    Debug.LogWarning($"[BT] 导出失败: {assetPath} treeId={asset.TreeId}（树结构无效，见上方错误）");
                }
            }

            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, BTBlobLoader.ManifestFileName), manifest);

            AssetDatabase.Refresh();
            Debug.Log($"[BT] 已导出 {ok} 棵树到 {dir}（运行时由 BTBlobLoader.LoadAll() 读回）");
            return ok;
        }

        /// <summary>清空目录内的旧 .blob 与 manifest（不删子目录，避免误伤）。</summary>
        private static void ClearFolder(string dir)
        {
            if (!Directory.Exists(dir)) return;

            foreach (var f in Directory.GetFiles(dir, "*" + BTBlobLoader.FileExtension))
                File.Delete(f);

            var manifest = Path.Combine(dir, BTBlobLoader.ManifestFileName);
            if (File.Exists(manifest))
                File.Delete(manifest);
        }

        /// <summary>
        /// 导出指定文件夹下的全部树资产（含子文件夹，递归）。会<b>先保存</b>其中每个树资产，再导出。
        /// 用于面板右键「保存并导出文件夹下全部」。返回成功棵数。
        /// </summary>
        /// <param name="folder">Unity 工程相对路径（如 Assets/BT/Trees/Level1）。</param>
        /// <param name="outputDir">输出目录；null 用 <see cref="DefaultOutputFolder"/>。</param>
        public static int ExportFolder(string folder, string outputDir = null)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning($"[BT] 文件夹不存在或路径无效: {folder}");
                return 0;
            }

            var assets = new List<BTTreeAsset>();
            foreach (var guid in AssetDatabase.FindAssets(TreeTypeFilter, new[] { folder }))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(assetPath);
                if (asset == null) continue;
                EditorUtility.SetDirty(asset);   // 先标记脏：确保「改了没保存」也会落盘
                assets.Add(asset);
            }

            if (assets.Count == 0)
            {
                Debug.Log($"[BT] 文件夹下没有行为树: {folder}");
                return 0;
            }

            AssetDatabase.SaveAssets();
            return ExportTrees(assets, outputDir);
        }

        /// <summary>
        /// 导出给定的一批树资产。<b>不会清空</b>目录里其它已存在的 blob，仅覆盖这批对应的文件。
        /// 导出后同步刷新 manifest（扫描目录内全部 <c>.blob</c>，保证清单与实际文件一致）。
        /// 返回成功棵数。
        /// </summary>
        /// <param name="trees">要导出的树资产集合。</param>
        /// <param name="outputDir">输出目录；null 用 <see cref="DefaultOutputFolder"/>。</param>
        public static int ExportTrees(IEnumerable<BTTreeAsset> trees, string outputDir = null)
        {
            var dir = string.IsNullOrEmpty(outputDir) ? DefaultOutputFolder : outputDir;
            Directory.CreateDirectory(dir);

            var ok = 0;
            foreach (var asset in trees)
            {
                if (asset == null) continue;
                if (asset.TreeId == 0)
                {
                    Debug.LogWarning($"[BT] 跳过未设置 TreeId 的树资产: {asset.name}");
                    continue;
                }

                var outPath = Path.Combine(dir, asset.TreeId + BTBlobLoader.FileExtension);
                if (BTBlobBuilder.BuildToFile(asset, outPath))
                {
                    ok++;
                    Debug.Log($"[BT] 已导出: {asset.name} (treeId={asset.TreeId}) → {outPath}");
                }
                else
                {
                    Debug.LogWarning($"[BT] 导出失败: {asset.name} treeId={asset.TreeId}（树结构无效，见上方错误）");
                }
            }

            SyncManifest(dir);
            AssetDatabase.Refresh();
            Debug.Log($"[BT] 已导出 {ok} 棵树到 {dir}（运行时由 BTBlobLoader.LoadAll() 读回）");
            return ok;
        }

        /// <summary>扫描目录内全部 <c>.blob</c>，按 treeId 重建 manifest（名字从工程内树资产反查）。</summary>
        private static void SyncManifest(string dir)
        {
            // treeId -> 资产名：清单备注用
            var nameByTreeId = new Dictionary<long, string>();
            foreach (var guid in AssetDatabase.FindAssets(TreeTypeFilter))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(assetPath);
                if (asset != null && asset.TreeId != 0) nameByTreeId[asset.TreeId] = asset.name;
            }

            var lines = new List<string>
            {
                "# HYC BT Blob manifest",
                $"# version={BTBlobLoader.FileVersion}",
            };
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*" + BTBlobLoader.FileExtension))
                {
                    var s = Path.GetFileNameWithoutExtension(f);
                    if (long.TryParse(s, out var tid))
                        lines.Add($"{tid}|{(nameByTreeId.TryGetValue(tid, out var n) ? n : s)}");
                }
            }
            File.WriteAllLines(Path.Combine(dir, BTBlobLoader.ManifestFileName), lines);
        }
    }
}
