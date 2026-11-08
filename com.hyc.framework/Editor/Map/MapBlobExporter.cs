using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Map.Editor
{
    /// <summary>
    /// 地图 → Blob 导出。
    /// <para>
    /// 只把「原始材质网格 + 道具落点」写盘：铺哪张瓦片、有几层、规则怎么配全是派生量，
    /// 烘进 Blob 就锁死了瓦片集，换套美术得把全部地图重导一遍。
    /// </para>
    /// <para>
    /// 写盘位置与 <c>SceneContent</c> 同构：<c>Assets/StreamingAssets/Map/&lt;mapId&gt;.blob</c>，
    /// 运行期由 <see cref="MapBlobLoader"/> 读。
    /// <b>注意</b>：WebGL/Android 上 <c>StreamingAssets.NeedsPreload</c> 为真，
    /// 会把清单里<b>所有</b>文件预取一遍；地图多到几十张时应改走 Addressables，
    /// 把字节直接交给 <see cref="MapBlobLoader.LoadBytes"/>。
    /// </para>
    /// </summary>
    public static class MapBlobExporter
    {
        /// <summary>Blob 输出目录（必须在 StreamingAssets 内，运行期才读得到）。</summary>
        public const string BlobDir = "Assets/StreamingAssets/Map";

        private const string Ext = ".blob";

        /// <summary>导出当前选中的地图（可多选）。</summary>
        [MenuItem("HYC Framework/Map/Export Blob (Selected)", false, 75)]
        public static void ExportSelected()
        {
            var targets = new List<MapAsset>();
            foreach (var o in Selection.objects)
                if (o is MapAsset a) targets.Add(a);

            if (targets.Count == 0)
            {
                Debug.LogWarning("[Map] 没有选中任何 MapAsset。");
                return;
            }
            int ok = 0;
            foreach (var a in targets)
                if (Export(a, false)) ok++;

            AssetDatabase.Refresh();
            Debug.Log($"[Map] Blob 导出：{ok}/{targets.Count} 张地图成功。");
        }

        /// <summary>导出工程内全部地图。</summary>
        [MenuItem("HYC Framework/Map/Export All Blobs", false, 76)]
        public static void ExportAll()
        {
            var guids = AssetDatabase.FindAssets("t:" + typeof(MapAsset).FullName);
            int ok = 0;
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var a = AssetDatabase.LoadAssetAtPath<MapAsset>(path);
                if (a == null) continue;
                if (Export(a, false)) ok++;
            }
            AssetDatabase.Refresh();
            Debug.Log($"[Map] Blob 全量导出：{ok}/{guids.Length} 张地图成功。");
        }

        /// <summary>导出单张地图，并读回自校验。</summary>
        public static bool Export(MapAsset asset, bool refresh = true)
        {
            if (asset == null) return false;
            if (asset.tileSet == null)
            {
                Debug.LogError($"[Map] 地图「{asset.name}」没有指定瓦片集，材质下标无从解释，拒绝导出。");
                return false;
            }
            int gridCount = asset.tileSet.GridCount;
            if (gridCount < 1)
            {
                Debug.LogError($"[Map] 地图「{asset.name}」的瓦片集一层网格都没有，拒绝导出。");
                return false;
            }
            asset.EnsureGrids();
            for (int g = 0; g < gridCount; g++)
            {
                var gridCells = asset.CellsOf(g);
                if (gridCells == null || gridCells.Length != asset.CellCount)
                {
                    Debug.LogError($"[Map] 地图「{asset.name}」第 {g} 层「{asset.tileSet.GridName(g)}」的格子数组与尺寸不一致，拒绝导出。");
                    return false;
                }
            }

            if (!Directory.Exists(BlobDir)) Directory.CreateDirectory(BlobDir);
            var path = Path.Combine(BlobDir, asset.mapId + Ext);

            WriteBlob(path, asset);

            // 读回自校验：版本不对 / 字段串位都会在这一步暴露，而不是等进游戏才发现
            if (!MapBlobLoader.LoadBytes(File.ReadAllBytes(path), out var check))
            {
                Debug.LogError($"[Map] 导出后自校验失败（无法读回）：{path}");
                return false;
            }
            int readW, readH, readMapId, readCells, readProps, readGrids;
            uint readHash;
            try
            {
                ref var root = ref check.Value;
                readW = root.Width;
                readH = root.Height;
                readMapId = root.MapId;
                readGrids = root.GridCount;
                readHash = root.PaletteHash;
                ref var cellsRef = ref root.Cells;
                readCells = cellsRef.Length;
                ref var propsRef = ref root.Props;
                readProps = propsRef.Length;
            }
            finally
            {
                check.Dispose();
            }

            bool ok = readW == asset.width && readH == asset.height
                      && readMapId == asset.mapId
                      && readGrids == gridCount
                      && readCells == asset.CellCount * gridCount
                      && readProps == (asset.props != null ? asset.props.Count : 0)
                      && readHash == asset.tileSet.PaletteHash;

            if (!ok)
            {
                Debug.LogError($"[Map] 导出自校验不一致：期望 {asset.width}×{asset.height} mapId={asset.mapId} " +
                               $"{gridCount} 层 {asset.CellCount} 格/层 {(asset.props != null ? asset.props.Count : 0)} 道具 " +
                               $"hash={asset.tileSet.PaletteHash}，" +
                               $"实际 {readW}×{readH} mapId={readMapId} {readGrids} 层 {readCells} 格 " +
                               $"{readProps} 道具 hash={readHash}。");
                return false;
            }

            Debug.Log($"[Map] 导出完成：{asset.name}（mapId={asset.mapId}，{asset.width}×{asset.height}" +
                      $"×{gridCount} 层，{(asset.props != null ? asset.props.Count : 0)} 道具）→ {path}");
            if (refresh) AssetDatabase.Refresh();
            return true;
        }

        private static void WriteBlob(string path, MapAsset asset)
        {
            int stride = asset.CellCount;
            int gridCount = asset.tileSet.GridCount;
            var builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref var root = ref builder.ConstructRoot<MapBlob>();
                root.MapId = asset.mapId;
                root.Width = asset.width;
                root.Height = asset.height;
                root.GridCount = gridCount;
                root.PaletteHash = asset.tileSet.PaletteHash;

                // 各层首尾相接：第 g 层占 [g·stride, (g+1)·stride)
                var cells = builder.Allocate(ref root.Cells, stride * gridCount);
                for (int g = 0; g < gridCount; g++)
                {
                    var source = asset.CellsOf(g);
                    int dst = g * stride;
                    for (int i = 0; i < stride; i++) cells[dst + i] = source[i];
                }

                int pc = asset.props != null ? asset.props.Count : 0;
                var props = builder.Allocate(ref root.Props, pc);
                for (int i = 0; i < pc; i++)
                {
                    var p = asset.props[i];
                    props[i] = new MapPropBlob
                    {
                        PropId = p != null ? p.propId : 0,
                        CellX = p != null ? p.x : 0,
                        CellY = p != null ? p.y : 0,
                    };
                }

                BlobAssetReference<MapBlob>.Write(builder, path, MapBlob.BlobVersion);
            }
            finally
            {
                builder.Dispose();
            }
        }
    }
}
