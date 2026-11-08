using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace HYC.Framework.Map
{
    /// <summary>
    /// 把一份地图 Blob 画成真实的 Unity Tilemap 场景对象（「加载时一套一套」的那个「一套」）。
    /// <para>
    /// <b>一个规则集一层 Tilemap</b>：规则集决定「这一层怎么拼」，所以图层数跟着规则集走，而不是跟着网格走。
    /// 这样「河流是画在草地上的地上物」不需要两层地图数据——同一份格子、河做成值（于是它挡路），
    /// 底层规则集对河那格铺草、上层规则集对同一格铺水面即可。遮挡交给 <c>sortingOrder</c>，
    /// 不会出现自算遮挡算错所导致的漏底黑缝。
    /// </para>
    /// <para>
    /// <b>网格可以有多个</b>：地形一层、通行/区域各一层。规则集用
    /// <see cref="MapAutoRuleSet.sourceGrid"/> 指定读哪一层，所以同一套求值逻辑既能铺地形也能铺区域标记。
    /// </para>
    /// <para>
    /// <b>瓦片是运行期造的</b>：<see cref="Tile"/> 是 ScriptableObject，从瓦片池里的 Sprite 现造一张即可，
    /// 不需要美术手工建「瓦片数 × 值数」个 Tile 资产（那才是真正的体力活）。
    /// 碰撞同一张 Sprite 要按「是否挡路」各造一份，因为 <c>colliderType</c> 是 Tile 资产的属性、不是放置实例的属性。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class MapView : MonoBehaviour
    {
        [Header("数据来源")]
        [Tooltip("地图 id，对应 StreamingAssets/Map/<id>.blob。")]
        public int mapId = 1;

        [Tooltip("瓦片集。必须与导出地图时用的是同一份（值层指纹会校验）。")]
        public MapTileSet tileSet;

        [Tooltip("Start 时自动加载。关掉则由宿主自己调 Load()。")]
        public bool loadOnStart = true;

        [Header("世界尺寸")]
        [Tooltip("单格边长（世界单位）。只影响 Grid，不影响 Blob 内容。")]
        public float cellSize = 1f;

        [Header("碰撞")]
        [Tooltip("给勾了「生成碰撞」的规则集挂 TilemapCollider2D，并用 CompositeCollider2D 合成一张静态碰撞体。")]
        public bool generateCollider = true;

        /// <summary>道具落点回调：propId, cellX, cellY（cellX/Y 是占地左下角）。宿主在这里按 id 实例化自己的 prefab。</summary>
        public event Action<int, int, int> PropLoaded;

        /// <summary>地图是否已成功加载。</summary>
        public bool IsLoaded { get; private set; }

        /// <summary>当前地图尺寸（未加载时为 0）。</summary>
        public int Width { get; private set; }

        /// <summary>当前地图尺寸（未加载时为 0）。</summary>
        public int Height { get; private set; }

        /// <summary>当前地图的网格层数（未加载时为 0）。</summary>
        public int GridCount { get; private set; }

        /// <summary>
        /// 已建出来的图层。<b>下标与 <see cref="MapTileSet.ruleSets"/> 一一对应</b>（index 0 在最下），
        /// 即使某一层这一张地图上没铺任何瓦片也会存在，方便宿主按下标取层。
        /// </summary>
        public IReadOnlyList<Tilemap> Layers => _layers;

        private const string LayerRootName = "MapGrid";

        private Transform _layerRoot;
        private readonly List<Tilemap> _layers = new List<Tilemap>();
        private readonly List<Tile> _spawnedTiles = new List<Tile>();
        private readonly Dictionary<int, Tile> _tileCache = new Dictionary<int, Tile>();

        // 加载后保留全部网格数据，供 IsWalkable / ValueAt 这类查询用（每格 1 字节）
        private byte[][] _grids;

        private void Start()
        {
            if (loadOnStart) Load(mapId);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _spawnedTiles.Count; i++)
                if (_spawnedTiles[i] != null) Destroy(_spawnedTiles[i]);
            _spawnedTiles.Clear();
            _tileCache.Clear();
            _grids = null;
        }

        /// <summary>按 <see cref="mapId"/> 重新加载（会先清空现有图层）。成功返回 true。</summary>
        public bool Load()
        {
            return Load(mapId);
        }

        /// <summary>加载指定地图。</summary>
        public bool Load(int id)
        {
            if (tileSet == null)
            {
                Debug.LogError("[Map] MapView 没有指定瓦片集，无法渲染。");
                return false;
            }
            if (!MapBlobLoader.TryLoad(id, out var blob))
                return false;

            byte[][] grids;
            int w, h;
            MapPropBlob[] props;
            try
            {
                if (!MapBlobLoader.Validate(blob, tileSet, out var error))
                {
                    Debug.LogError($"[Map] 地图 {id} 校验失败：{error}");
                    return false;
                }
                ref var root = ref blob.Value;
                w = root.Width;
                h = root.Height;
                int stride = w * h;

                ref var cellsRef = ref root.Cells;
                grids = new byte[root.GridCount][];
                for (int g = 0; g < root.GridCount; g++)
                {
                    var slice = new byte[stride];
                    int baseIndex = g * stride;
                    for (int i = 0; i < stride; i++) slice[i] = cellsRef[baseIndex + i];
                    grids[g] = slice;
                }

                ref var propsRef = ref root.Props;
                props = new MapPropBlob[propsRef.Length];
                for (int i = 0; i < propsRef.Length; i++) props[i] = propsRef[i];
            }
            finally
            {
                // 数据已拷进托管数组：Blob 立刻释放，之后不用管生命周期
                blob.Dispose();
            }

            mapId = id;
            Build(grids, w, h, props);
            // ★ 必须在 Build 之后赋值：Build 内部先调 Clear()，而 Clear 会把 Width/Height 归零
            Width = w;
            Height = h;
            GridCount = grids.Length;
            _grids = grids;
            IsLoaded = true;
            return true;
        }

        /// <summary>清空所有图层与道具。</summary>
        public void Clear()
        {
            if (_layerRoot != null)
            {
                if (Application.isPlaying) Destroy(_layerRoot.gameObject);
                else DestroyImmediate(_layerRoot.gameObject);
                _layerRoot = null;
            }
            _layers.Clear();
            _grids = null;
            Width = 0;
            Height = 0;
            GridCount = 0;
            IsLoaded = false;
        }

        // ---------------- 查询 ----------------

        /// <summary>某格的地形值（0 = 空 / 越界 / 未加载）。等价于 <c>GetCell(0, x, y)</c>。</summary>
        public byte GetCell(int x, int y) => GetCell(0, x, y);

        /// <summary>某层某格的值（0 = 空 / 越界 / 该层不存在）。</summary>
        public byte GetCell(int grid, int x, int y)
        {
            if (!IsLoaded || x < 0 || y < 0 || x >= Width || y >= Height) return 0;
            var data = CellsOf(grid);
            if (data == null) return 0;
            int i = y * Width + x;
            return (uint)i < (uint)data.Length ? data[i] : (byte)0;
        }

        /// <summary>某层某格的值定义（空格或未加载返回 null）。</summary>
        public MapMaterial ValueAt(int grid, int x, int y)
        {
            return tileSet != null ? tileSet.ValueOf(grid, GetCell(grid, x, y)) : null;
        }

        /// <summary>某格的地形值定义（空格或未加载返回 null）。</summary>
        public MapMaterial MaterialAt(int x, int y) => ValueAt(0, x, y);

        /// <summary>
        /// 这一格能不能踩。<b>地图外与空格一律返回 false</b>——地图边界不该被当成可走区域。
        /// <para>
        /// 依据是 <see cref="MapTileSet.walkableGridIndex"/> 指定的那一层的
        /// <see cref="MapMaterial.walkable"/>；默认就是地形层，派生自格子数据，不额外存一层。
        /// </para>
        /// </summary>
        public bool IsWalkable(int x, int y)
        {
            if (!IsLoaded || x < 0 || y < 0 || x >= Width || y >= Height) return false;
            if (tileSet == null) return false;
            int grid = tileSet.WalkableGrid;
            return tileSet.IsWalkableIn(grid, GetCell(grid, x, y));
        }

        /// <summary>取某层的网格数组（未加载/越界返回 null）。</summary>
        public byte[] CellsOf(int grid)
        {
            return _grids != null && (uint)grid < (uint)_grids.Length ? _grids[grid] : null;
        }

        // ---------------- 内部实现 ----------------

        private void Build(byte[][] grids, int w, int h, MapPropBlob[] props)
        {
            Clear();
            EnsureGrid();

            var set = tileSet;
            if (set.ruleSets == null || set.ruleSets.Count == 0)
            {
                Debug.LogWarning("[Map] 瓦片集里没有规则集，地图将渲染为空。");
                return;
            }

            // reader 每层只建一次：放进内层循环会让每格都分配一个闭包，512×512 就是 26 万次分配。
            var readers = new MapAutoRules.CellAt[grids.Length];
            for (int g = 0; g < grids.Length; g++)
                readers[g] = MapAutoRules.Reader(grids[g], w, h);

            int walkableGrid = set.WalkableGrid;
            byte[] walkableData = walkableGrid < grids.Length ? grids[walkableGrid] : null;

            var positions = new List<Vector3Int>();
            var tiles = new List<TileBase>();

            for (int rank = 0; rank < set.ruleSets.Count; rank++)
            {
                var ruleSet = set.ruleSets[rank];
                // 层先建好（即使这一张地图上没铺任何瓦片），保证 Layers[rank] 恒等于 ruleSets[rank]
                var layer = CreateLayer(ruleSet, rank);
                _layers.Add(layer);

                if (ruleSet == null || ruleSet.RuleCount() == 0) continue;

                int source = ruleSet.sourceGrid;
                if (source < 0 || source >= readers.Length)
                {
                    Debug.LogWarning($"[Map] 规则集「{ruleSet.name}」要读第 {source} 层，但地图只有 {readers.Length} 层，跳过。");
                    continue;
                }
                var reader = readers[source];

                positions.Clear();
                tiles.Clear();

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int tileIndex = MapAutoRules.Evaluate(ruleSet, reader, x, y);
                        if (tileIndex == MapAutoRules.NoTile) continue;

                        // 碰撞只由「勾了生成碰撞的规则集」负责，且只给不可走的格子挂
                        bool walkableHere = walkableData != null
                            ? set.IsWalkableIn(walkableGrid, walkableData[y * w + x])
                            : true;
                        bool needCollider = ruleSet.contributesCollider && generateCollider && !walkableHere;

                        var tile = GetTile(tileIndex, needCollider);
                        if (tile == null) continue;      // 瓦片池里这一格是空槽：这一格这层不画

                        positions.Add(new Vector3Int(x, y, 0));
                        tiles.Add(tile);
                    }
                }

                if (positions.Count > 0)
                    layer.SetTiles(positions.ToArray(), tiles.ToArray());
            }

            for (int i = 0; i < props.Length; i++)
                PropLoaded?.Invoke(props[i].PropId, props[i].CellX, props[i].CellY);
        }

        private void EnsureGrid()
        {
            var root = new GameObject(LayerRootName);
            root.transform.SetParent(transform, false);

            var grid = root.AddComponent<Grid>();
            grid.cellSize = new Vector3(cellSize, cellSize, 0f);

            if (generateCollider)
            {
                var rb = root.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Static;
                var comp = root.AddComponent<CompositeCollider2D>();
                comp.geometryType = CompositeCollider2D.GeometryType.Polygons;
            }
            _layerRoot = root.transform;
        }

        private Tilemap CreateLayer(MapAutoRuleSet ruleSet, int rank)
        {
            var name = ruleSet != null && !string.IsNullOrEmpty(ruleSet.name) ? ruleSet.name : "layer";
            var go = new GameObject($"L{rank:00}_{name}");
            go.transform.SetParent(_layerRoot, false);

            var map = go.AddComponent<Tilemap>();
            var tmr = go.AddComponent<TilemapRenderer>();
            tmr.sortingOrder = rank;

            if (generateCollider && ruleSet != null && ruleSet.contributesCollider)
            {
                var col = go.AddComponent<TilemapCollider2D>();
                col.usedByComposite = true;
            }
            return map;
        }

        /// <summary>
        /// 取（或现造）一张瓦片。
        /// <para>
        /// 缓存键含 <paramref name="collider"/>：同一张 Sprite 要按「是否挡路」造两份 Tile，
        /// 因为 <c>colliderType</c> 是 Tile 资产自己的属性，改它会影响所有用到这张 Tile 的格子。
        /// </para>
        /// </summary>
        private Tile GetTile(int tileIndex, bool collider)
        {
            int key = tileIndex * 2 + (collider ? 1 : 0);
            if (_tileCache.TryGetValue(key, out var cached)) return cached;

            var sprite = tileSet.TileAt(tileIndex);
            if (sprite == null) return null;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            // Sprite 模式：碰撞形状取 Sprite 自己的 Physics Shape，于是「半格是水」的过渡瓦片
            // 物理上也就只挡水那半边。若美术没画 Physics Shape，这一格等于没有碰撞（不是报错，是静默不挡）。
            tile.colliderType = collider ? Tile.ColliderType.Sprite : Tile.ColliderType.None;
            tile.name = $"t{tileIndex}{(collider ? "_blk" : string.Empty)}";

            _spawnedTiles.Add(tile);
            _tileCache[key] = tile;
            return tile;
        }
    }
}
