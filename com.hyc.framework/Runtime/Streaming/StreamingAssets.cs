using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace HYC.Framework.IO
{
    /// <summary>
    /// 统一 StreamingAssets 读取入口。
    ///
    /// <para><b>设计目标：</b>上层（配置 blob / 本地化 / 音频总览 / 行为树）只调
    /// <see cref="ReadBytes"/> / <see cref="ReadText"/>，<b>不出现任何 #if 平台分支</b>——
    /// 平台差异全部收在本类里，游戏层无需为切平台写不同代码。</para>
    ///
    /// <para><b>为什么需要它：</b>StreamingAssets 并不是在所有平台都能当普通文件夹读：</para>
    /// <list type="bullet">
    ///   <item><description>PC / macOS / Linux / iOS：真实文件夹，<c>File.ReadAllBytes</c> 直接可读。</description></item>
    ///   <item><description>WebGL：浏览器沙箱没有文件系统，<c>System.IO.File</c> 整个被禁，只能当 HTTP 资源用 <c>UnityWebRequest</c> 异步拉取。</description></item>
    ///   <item><description>Android：StreamingAssets 被打进 APK 压缩包（<c>jar:file://…!/assets</c>），<c>File</c> 读不到，同样必须走 <c>UnityWebRequest</c>。</description></item>
    /// </list>
    ///
    /// <para><b>两类"不能直接读文件"的平台（见 <see cref="NeedsPreload"/>）走同一条路：</b>
    /// 构建时生成预取清单 → 启动时 <see cref="PreloadAsync"/> 逐文件 HTTP 进内存缓存 →
    /// 之后 <see cref="ReadBytes"/> 同步读缓存。PC / iOS 上 <see cref="PreloadAsync"/> 是 no-op。</para>
    ///
    /// <para><b>时序约束：</b>读取都发生在 System.OnCreate（建 World 时），所以
    /// <see cref="NeedsPreload"/> 为 true 的平台上，<b>创建游戏世界之前</b>必须先
    /// <c>await StreamingAssets.PreloadAsync()</c> 完成预取。框架的
    /// <c>FrameworkBootstrap.InitializeAsync</c> 已内置这一步，游戏层直接调它即可。</para>
    /// </summary>
    public static class StreamingAssets
    {
        /// <summary>预加载清单文件名，由构建后处理器（仅 WebGL / Android）在打包时生成。</summary>
        public const string ManifestFileName = "hyc_streaming_manifest.txt";

        public static bool IsWebGL => Application.platform == RuntimePlatform.WebGLPlayer;

        public static bool IsAndroid => Application.platform == RuntimePlatform.Android;

        /// <summary>
        /// 该平台是否<b>不能</b>直接用 <c>System.IO.File</c> 读 StreamingAssets，
        /// 必须先 <see cref="PreloadAsync"/> 把数据 HTTP 进内存缓存。
        /// WebGL（浏览器无文件系统）与 Android（文件在 APK 压缩包内）同属此类。
        /// </summary>
        public static bool NeedsPreload => IsWebGL || IsAndroid;

        /// <summary>
        /// <see cref="PreloadAsync"/> 是否已完成。
        /// PC / macOS / Linux / iOS 上恒为 true（无需预取，直接读文件）。
        /// </summary>
        public static bool IsPreloaded => !NeedsPreload || s_Preloaded;

        private static bool s_Preloaded;

        private static readonly Dictionary<string, byte[]> s_Cache = new Dictionary<string, byte[]>();

        /// <summary>
        /// 预取：下载清单 → 逐文件 HTTP 拉取进内存缓存。
        /// <para>仅在 <see cref="NeedsPreload"/> 为 true 的平台（WebGL / Android）有实际动作；
        /// PC / iOS 上是 no-op（缓存始终为空，读取走 File）。</para>
        /// <para>必须在创建世界前 await 完成；框架的 <c>FrameworkBootstrap.InitializeAsync</c> 已内置。</para>
        /// </summary>
        public static async Task PreloadAsync()
        {
            if (!NeedsPreload) return;

            var manifest = await GetTextAsync(ManifestFileName);
            if (string.IsNullOrEmpty(manifest))
            {
                Debug.LogError($"[{nameof(StreamingAssets)}] 预加载清单缺失（{ManifestFileName}）。" +
                               "请确认构建时生成了它（WebGL / Android 构建会写入 StreamingAssets 根目录）。");
                return;
            }

            var lines = manifest.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in lines)
            {
                var rel = raw.Trim();
                if (rel.Length == 0) continue;
                var bytes = await GetBytesAsync(rel);
                if (bytes != null)
                    s_Cache[Normalize(rel)] = bytes;
            }
            s_Preloaded = true;
        }

        /// <summary>按相对 StreamingAssets 根的路径读取字节；相对路径统一用 '/' 分隔。</summary>
        /// <remarks>
        /// <see cref="NeedsPreload"/> 平台（WebGL / Android）读内存缓存——必须先完成
        /// <see cref="PreloadAsync"/>；其余平台直接 <c>File.ReadAllBytes</c>。
        /// </remarks>
        public static byte[] ReadBytes(string relativePath)
        {
            var key = Normalize(relativePath);
            if (NeedsPreload)
            {
                if (s_Cache.TryGetValue(key, out var b)) return b;
                Debug.LogError($"[{nameof(StreamingAssets)}] 未预加载：{key}（检查构建清单是否包含该文件，或 PreloadAsync 是否在读取前 await 完成）");
                return null;
            }
            return File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>等价于 <see cref="ReadBytes"/> + UTF8 解码；空内容返回 null。</summary>
        public static string ReadText(string relativePath)
        {
            var b = ReadBytes(relativePath);
            return b == null ? null : Encoding.UTF8.GetString(b);
        }

        /// <summary>直接读文件的平台走 <c>File.Exists</c>；<see cref="NeedsPreload"/> 平台查缓存。</summary>
        public static bool Exists(string relativePath)
        {
            var key = Normalize(relativePath);
            if (NeedsPreload) return s_Cache.ContainsKey(key);
            return File.Exists(Path.Combine(Application.streamingAssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary><see cref="NeedsPreload"/> 平台：返回缓存里所有相对路径键；其余平台：空集合。</summary>
        public static IEnumerable<string> GetKeys() => s_Cache.Keys;

        /// <summary><see cref="NeedsPreload"/> 平台：返回缓存中位于 <paramref name="relativeFolder"/> 下的所有键；其余平台：空集合。</summary>
        public static IEnumerable<string> GetKeysUnder(string relativeFolder)
        {
            var prefix = Normalize(relativeFolder).TrimEnd('/') + "/";
            foreach (var k in s_Cache.Keys)
                if (k.StartsWith(prefix)) yield return k;
        }

        // ---- NeedsPreload 平台（WebGL / Android）HTTP 子程序 ----

        private static async Task<byte[]> GetBytesAsync(string relativePath)
        {
            var url = Application.streamingAssetsPath.TrimEnd('/') + "/" + relativePath.Replace('\\', '/');
            using var req = UnityWebRequest.Get(url);
            try
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
            }
            catch (Exception e)
            {
                Debug.LogError($"[{nameof(StreamingAssets)}] 下载失败 {url}：{e.Message}");
                return null;
            }
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[{nameof(StreamingAssets)}] 下载失败 {url}：{req.error}");
                return None();
            }
            return req.downloadHandler.data;
        }

        private static byte[] None() => null;

        private static async Task<string> GetTextAsync(string relativePath)
        {
            var b = await GetBytesAsync(relativePath);
            return b == null ? null : Encoding.UTF8.GetString(b);
        }

        private static string Normalize(string p) => p.Replace('\\', '/');
    }
}
