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

        private const string TreeTypeFilter = "t:HYC.Framework.BT.Editor.BTTreeAsset";

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
    }
}
