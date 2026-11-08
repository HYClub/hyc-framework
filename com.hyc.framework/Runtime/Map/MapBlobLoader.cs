using Unity.Entities;
using UnityEngine;
using HYC.Framework.IO;

namespace HYC.Framework.Map
{
    /// <summary>
    /// 地图 Blob 的运行期读取入口。
    /// <para>
    /// 复用框架既有的 <see cref="BlobReader"/> + <see cref="StreamingAssets"/>（版本校验、预取平台只查缓存）
    /// 与 <c>SceneContent</c> 完全一致，区别只在目录名：<c>StreamingAssets/Map/&lt;mapId&gt;.blob</c>。
    /// 【【注意】】<see cref="StreamingAssets.NeedsPreload"/> 在 WebGL/Android 上会<b>整包预取</b>清单内所有文件，
    /// 地图多了以后启动会明显变慢；量大的工程应改用 Addressables 直接把字节交给
    /// <see cref="LoadBytes"/>（本类刻意把这两条路都留出来了）。
    /// </para>
    /// </summary>
    public static class MapBlobLoader
    {
        /// <summary>Blob 相对 StreamingAssets 的目录（末尾带斜杠）。</summary>
        public const string BlobRelativeDir = "Map/";

        /// <summary>扩展名。</summary>
        public const string Ext = ".blob";

        /// <summary>拼出某张地图在 StreamingAssets 下的相对路径。</summary>
        public static string RelativePath(int mapId) => BlobRelativeDir + mapId + Ext;

        /// <summary>
        /// 读原始字节（不解析）。失败返回 null 并已 LogError。
        /// 走 Addressables 的宿主可以只用它，或直接把 <c>TextAsset.bytes</c> 交给 <see cref="LoadBytes"/>。
        /// </summary>
        public static byte[] ReadBytes(int mapId)
        {
            var bytes = StreamingAssets.ReadBytes(RelativePath(mapId));
            if (bytes == null)
                Debug.LogError($"[Map] 读取地图 {mapId} 失败：{RelativePath(mapId)} 不存在或未预取" +
                               "（WebGL/Android 需先走异步引导做 StreamingAssets 预取）。");
            return bytes;
        }

        /// <summary>从 StreamingAssets 读取并解析。<b>调用方负责 Dispose</b>。</summary>
        public static bool TryLoad(int mapId, out BlobAssetReference<MapBlob> blob)
        {
            blob = default;
            var bytes = ReadBytes(mapId);
            if (bytes == null) return false;
            if (!LoadBytes(bytes, out blob))
            {
                Debug.LogError($"[Map] 地图 {mapId} 的 Blob 解析失败（版本 {MapBlob.BlobVersion} 不符或文件损坏）。");
                return false;
            }
            // 文件里记的 mapId 与请求的不一致 ⇒ 十有八九是导出时 mapId 没改/两个地图撞了号
            if (blob.Value.MapId != mapId)
            {
                Debug.LogError($"[Map] 地图 id 不匹配：请求 {mapId}，文件里是 {blob.Value.MapId}（{RelativePath(mapId)}）。");
                blob.Dispose();
                blob = default;
                return false;
            }
            return true;
        }

        /// <summary>从已有字节解析（Addressables / 测试注入）。<b>调用方负责 Dispose</b>。</summary>
        public static bool LoadBytes(byte[] bytes, out BlobAssetReference<MapBlob> blob)
        {
            blob = default;
            if (bytes == null || bytes.Length == 0) return false;
            return BlobReader.TryRead<MapBlob>(bytes, MapBlob.BlobVersion, out blob);
        }

        /// <summary>
        /// 把 Blob 与一份瓦片集对齐检查：尺寸自洽、层数一致、格子值不越界、指纹一致。
        /// 运行期应在建图层之前调一次——错位的值索引画出来是一张「看起来正常」的乱图，必须主动拦住。
        /// </summary>
        /// <param name="blob">已解析的 Blob。</param>
        /// <param name="set">运行时实际使用的瓦片集（可为 null，表示只查尺寸与自洽性）。</param>
        /// <param name="error">不通过时的人话原因（通过时为 null）。</param>
        public static bool Validate(BlobAssetReference<MapBlob> blob, MapTileSet set, out string error)
        {
            error = null;
            if (!blob.IsCreated)
            {
                error = "Blob 未创建。";
                return false;
            }

            ref var root = ref blob.Value;
            if (root.Width <= 0 || root.Height <= 0)
            {
                error = $"地图尺寸非法：{root.Width}×{root.Height}。";
                return false;
            }
            if (root.GridCount < 1)
            {
                error = $"网格层数非法：{root.GridCount}（至少 1 层）。";
                return false;
            }

            ref var cells = ref root.Cells;
            long expected = (long)root.GridCount * root.Width * root.Height;
            if (cells.Length != expected)
            {
                error = $"格子数 {cells.Length} 与「{root.GridCount} 层 × {root.Width}×{root.Height}={expected}」不符。";
                return false;
            }
            if (set == null) return true;

            if (set.GridCount != root.GridCount)
            {
                error = $"层数不一致：Blob 是 {root.GridCount} 层，瓦片集现在有 {set.GridCount} 层。请重导地图。";
                return false;
            }
            if (set.PaletteHash != root.PaletteHash)
            {
                error = "瓦片集指纹不一致：值层/值的顺序或数量在导出后改过。请重导地图，或把瓦片集恢复成导出时的样子。";
                return false;
            }

            int stride = root.Width * root.Height;
            for (int g = 0; g < root.GridCount; g++)
            {
                int max = set.ValueCount(g);
                int baseIndex = g * stride;
                for (int i = 0; i < stride; i++)
                {
                    byte v = cells[baseIndex + i];
                    if (v > max)
                    {
                        error = $"第 {g} 层「{set.GridName(g)}」的格子 {i} 值 {v} 超出该层值数 {max}。";
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
