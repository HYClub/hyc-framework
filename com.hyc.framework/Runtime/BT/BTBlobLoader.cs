// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTBlobLoader.cs
// 说明: 打包后的行为树加载器 - 从 StreamingAssets 读回 Blob 并注册到 BTManager
//       与配置管线(StreamingAssets/ConfigBlob + ConfigManager)完全同构:
//         编辑器: BTBlobExporter(导出)  ≈  DataEditor(导出)
//         运行时: BTBlobLoader(加载)    ≈  XxxBlobSystem.Load()
//       背景: BTTreeAsset / BTBlobBuilder 位于 Editor 程序集, 打包后不存在,
//       因此「进 Play 自动注册」(BTAutoRegisterOnPlay) 只在编辑器生效,
//       真机必须走「导出文件 → 读回」这条路。
// ============================================================

using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using UnityEngine;

namespace HYC.Framework.BT
{
    /// <summary>
    /// 运行时行为树加载器：把编辑器导出的 Blob 文件读回并 <see cref="BTManager.Register"/>。
    ///
    /// <para>典型用法（游戏 Bootstrap 的数据阶段，早于任何实体 Tick）：</para>
    /// <code>
    /// BTBlobLoader.LoadAll();   // 读 StreamingAssets/BTBlob/manifest.txt 逐棵注册
    /// </code>
    ///
    /// <para>编辑器里通常<b>不需要</b>调用它——<c>BTAutoRegisterOnPlay</c> 进 Play 时已自动注册。
    /// 它服务于<b>打包后的真机</b>。两者注册的是同一批 TreeId，重复注册会覆盖（Register 内部会释放旧引用），
    /// 因此在编辑器里调用也无害，便于「用真机同一条链路自测」。</para>
    /// </summary>
    public static class BTBlobLoader
    {
        /// <summary>
        /// 文件格式版本。<b>必须与导出端一致</b>（<c>BTBlobBuilder.BuildToFile</c> 的默认参数就是它）。
        /// 改动 Blob 结构（<see cref="BTRootBlob"/> / <see cref="BTNodeBlob"/>）时递增，
        /// 版本不一致 <c>TryRead</c> 会失败并给出明确报错，避免读到错位的字节。
        /// </summary>
        public const int FileVersion = 1;

        /// <summary>StreamingAssets 下的默认子目录（与 ConfigBlob 平级）。</summary>
        public const string DefaultSubFolder = "BTBlob";

        /// <summary>清单文件名：每行一棵树，格式 <c>treeId</c> 或 <c>treeId|备注</c>。</summary>
        public const string ManifestFileName = "manifest.txt";

        /// <summary>单个树文件的扩展名。</summary>
        public const string FileExtension = ".blob";

        /// <summary>默认加载目录：<c>{StreamingAssets}/BTBlob</c>。</summary>
        public static string DefaultFolder => Path.Combine(Application.streamingAssetsPath, DefaultSubFolder);

        /// <summary>某棵树的文件路径。</summary>
        public static string PathFor(string folder, long treeId)
            => Path.Combine(folder ?? DefaultFolder, treeId + FileExtension);

        /// <summary>
        /// 加载并注册一棵树。已存在同 TreeId 的注册会被覆盖（旧引用由 <see cref="BTManager"/> 释放）。
        /// </summary>
        /// <param name="treeId">树 ID（与 <c>BTTreeAsset.TreeId</c> 一致）。</param>
        /// <param name="folder">目录，null = <see cref="DefaultFolder"/>。</param>
        /// <param name="version">文件格式版本，默认 <see cref="FileVersion"/>。</param>
        public static bool Load(long treeId, string folder = null, int version = FileVersion)
        {
            var dir = folder ?? DefaultFolder;
            var path = PathFor(dir, treeId);

            if (!IsReadablePath(dir, out var reason))
            {
                Debug.LogError($"[BT] 无法加载树 {treeId}：{reason}\n  目录：{dir}");
                return false;
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[BT] Blob 文件不存在: {path}（菜单 Tools/HYC/BT/导出 Blob(打包用) 重新导出）");
                return false;
            }

            if (BlobAssetReference<BTRootBlob>.TryRead(path, version, out var blob))
            {
                BTManager.Register(treeId, blob);
                return true;
            }

            Debug.LogError($"[BT] Blob 读取失败: {path}（文件损坏，或格式版本不一致：期望 {version}。" +
                           "请重新执行 菜单 Tools/HYC/BT/导出 Blob(打包用)）");
            return false;
        }

        /// <summary>
        /// 按清单加载目录下全部树，返回成功注册的棵数。
        /// 清单缺失时回退为「扫描目录下所有 <c>*.blob</c> 且文件名即 TreeId」，保证只拷了文件也能跑。
        /// </summary>
        public static int LoadAll(string folder = null, int version = FileVersion)
        {
            var dir = folder ?? DefaultFolder;

            if (!IsReadablePath(dir, out var reason))
            {
                Debug.LogWarning($"[BT] 跳过行为树加载：{reason}\n  目录：{dir}" +
                                 "（编辑器里由 BTAutoRegisterOnPlay 自动注册，真机需先导出）");
                return 0;
            }

            if (!TryReadManifest(dir, out var ids))
                ScanBlobFiles(dir, out ids);

            var ok = 0;
            for (var i = 0; i < ids.Count; i++)
            {
                if (Load(ids[i], dir, version))
                    ok++;
            }

            Debug.Log($"[BT] 行为树加载完成: 成功 {ok}/{ids.Count} 棵，目录 {dir}");
            return ok;
        }

        /// <summary>
        /// 读清单文件。每行格式 <c>treeId</c> 或 <c>treeId|备注</c>；<c>#</c> 开头为注释。
        /// 清单本身是可选的（便于人工核对导出了哪些树），缺失时 <see cref="LoadAll"/> 会退回扫描目录。
        /// </summary>
        public static bool TryReadManifest(string folder, out List<long> ids)
        {
            ids = new List<long>();
            var path = Path.Combine(folder ?? DefaultFolder, ManifestFileName);
            if (!File.Exists(path))
                return false;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                var sep = line.IndexOf('|');
                var idPart = sep >= 0 ? line.Substring(0, sep) : line;
                if (long.TryParse(idPart.Trim(), out var id) && id != 0)
                    ids.Add(id);
            }
            return ids.Count > 0;
        }

        /// <summary>兜底：扫描目录下所有 <c>*.blob</c>，文件名（不含扩展名）即 TreeId。</summary>
        private static void ScanBlobFiles(string folder, out List<long> ids)
        {
            ids = new List<long>();
            if (!Directory.Exists(folder))
                return;

            foreach (var file in Directory.GetFiles(folder, "*" + FileExtension))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (long.TryParse(name, out var id) && id != 0)
                    ids.Add(id);
            }
        }

        /// <summary>
        /// 判断目录是否可直接用文件 IO 读取。
        /// Android 的 <c>streamingAssetsPath</c> 指向 APK 内部（<c>jar:file://</c>），
        /// <c>File.Exists</c> / <c>TryRead</c> 在那里读不到——配置管线同样受此限制。
        /// 真机上要么把文件先拷到 <c>persistentDataPath</c>，要么改用 UnityWebRequest/AA 读取后
        /// 再把字节交给 <c>BlobAssetReference&lt;BTRootBlob&gt;.Create(byte[])</c> 并 Register。
        /// 这里给出明确报错，避免静默变成「树全部加载失败但游戏照跑」。
        /// </summary>
        private static bool IsReadablePath(string folder, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(folder))
            {
                reason = "目录为空";
                return false;
            }
            if (folder.StartsWith("jar:") || folder.StartsWith("http"))
            {
                reason = "该路径不支持直接文件 IO（Android APK 内/远程路径）。" +
                         "请先把导出文件复制到 Application.persistentDataPath 再传入该目录，" +
                         "或用 UnityWebRequest 读字节后自行 Register";
                return false;
            }
            if (!Directory.Exists(folder))
            {
                reason = "目录不存在";
                return false;
            }
            return true;
        }
    }
}
