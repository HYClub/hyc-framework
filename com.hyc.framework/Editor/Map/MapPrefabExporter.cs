using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Map.Editor
{
    /// <summary>
    /// 地图 → Prefab 导出。
    /// <para>
    /// 产出的 prefab 是<b>装载器</b>而不是「把几万格烘成 Tilemap」的静态物件：
    /// 只挂一个配好 mapId / 瓦片集 / 格边长的 <see cref="MapView"/>，
    /// 运行时 Start 读 Blob 建图层。这样做的理由——
    /// ① 烘成 Tilemap 需要给每张瓦片建 Tile 资产（瓦片池 × 图层数的量级），地图一改就得跟着重建；
    /// ② 烘进去的数据无法复用，两张只差几格的地图会各存一份完整副本。
    /// 需要「关卡里直接摆一张图」时，把这个 prefab 拖进场景即可，位置随意平移。
    /// </para>
    /// <para>
    /// prefab 落在<b>地图资产同目录</b>，名字与资产同名——不做集中目录，
    /// 因为集中目录最容易出现「改了资产忘了重导 prefab」的漂移。
    /// </para>
    /// </summary>
    public static class MapPrefabExporter
    {
        /// <summary>导出选中的地图为 prefab。</summary>
        [MenuItem("HYC Framework/Map/Export Prefab (Selected)", false, 77)]
        public static void ExportSelected()
        {
            int ok = 0;
            int total = 0;
            foreach (var o in Selection.objects)
            {
                if (!(o is MapAsset a)) continue;
                total++;
                if (Export(a)) ok++;
            }
            if (total == 0)
            {
                Debug.LogWarning("[Map] 没有选中任何 MapAsset。");
                return;
            }
            Debug.Log($"[Map] Prefab 导出：{ok}/{total} 张地图成功。");
        }

        /// <summary>导出单张地图的装载器 prefab，返回是否成功。</summary>
        public static bool Export(MapAsset asset)
        {
            if (asset == null) return false;
            if (asset.tileSet == null)
            {
                Debug.LogError($"[Map] 地图「{asset.name}」没有瓦片集，装载器跑起来会报错。");
                return false;
            }

            var assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError($"[Map] 地图「{asset.name}」不是工程内资产，无法确定 prefab 的输出目录。");
                return false;
            }
            var dir = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(dir)) dir = "Assets";
            var prefabPath = dir.Replace('\\', '/') + "/" + asset.name + ".prefab";

            var go = new GameObject(asset.name);
            try
            {
                var view = go.AddComponent<MapView>();
                view.mapId = asset.mapId;
                view.tileSet = asset.tileSet;
                view.cellSize = asset.cellSize;
                view.loadOnStart = true;

                var saved = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                if (saved == null)
                {
                    Debug.LogError($"[Map] 保存 prefab 失败：{prefabPath}");
                    return false;
                }
                Debug.Log($"[Map] Prefab 导出完成：{prefabPath}（mapId={asset.mapId}，运行期读 Blob 建图层）");
                Selection.activeObject = saved;
                EditorGUIUtility.PingObject(saved);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
