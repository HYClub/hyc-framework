#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HYC.Framework.Editor.IO
{
    /// <summary>
    /// 仅 WebGL / Android 构建时，把 StreamingAssets 下<b>除 <c>aa/</c>（Addressables 自管）与 <c>.meta</c> 外</b>的所有文件，
    /// 以相对路径（'/' 分隔，相对 StreamingAssets 根）逐行写入 <c>hyc_streaming_manifest.txt</c>。
    ///
    /// <para>运行时的 <see cref="HYC.Framework.IO.StreamingAssets.PreloadAsync"/> 会先下载这份清单，
    /// 再逐个 HTTP 预取进内存缓存——从而让配置 blob / 本地化 / 音频总览 / 行为树等<b>无需感知平台</b>。</para>
    ///
    /// <para><b>为什么要覆盖 Android：</b>Android 的 StreamingAssets 被打进 APK 压缩包，
    /// <c>System.IO.File</c> 读不到（与 WebGL 同属 <see cref="HYC.Framework.IO.StreamingAssets.NeedsPreload"/>），
    /// 所以同样需要「构建时列清单 + 启动时预取」这条路。PC / macOS / Linux / iOS 直接读文件，不生成清单。</para>
    /// </summary>
    public class StreamingAssetsManifestBuilder : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var platform = report.summary.platform;
            if (platform != BuildTarget.WebGL && platform != BuildTarget.Android) return;

            var root = Application.streamingAssetsPath;
            if (!Directory.Exists(root)) return;

            var lines = new List<string>();
            Collect(root, root, lines);

            var manifestPath = Path.Combine(root, HYC.Framework.IO.StreamingAssets.ManifestFileName);
            File.WriteAllLines(manifestPath, lines);
            AssetDatabase.ImportAsset(manifestPath);
        }

        private static void Collect(string root, string dir, List<string> outLines)
        {
            foreach (var entry in Directory.GetFileSystemEntries(dir))
            {
                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name)) continue;

                if (Directory.Exists(entry))
                {
                    if (name == "aa") continue; // Addressables 自己会加载，不进预取清单
                    Collect(root, entry, outLines);
                }
                else if (File.Exists(entry))
                {
                    if (name == HYC.Framework.IO.StreamingAssets.ManifestFileName) continue;
                    if (name.EndsWith(".meta")) continue;
                    outLines.Add(ToRelative(root, entry));
                }
            }
        }

        private static string ToRelative(string root, string full)
        {
            var rel = full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rel.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
#endif
