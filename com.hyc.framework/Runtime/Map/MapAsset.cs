using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Map
{
    /// <summary>
    /// 地图上的一个道具落点（编辑器侧的序列化形态；导出时压成 <see cref="MapPropBlob"/>）。
    /// <para>
    /// <b>(x, y) 是占地区域的左下角</b>，与网格 y 向上一致；占几格由
    /// <see cref="MapPropDef.width"/>/<see cref="MapPropDef.height"/> 决定（写在瓦片集里，跨地图共用）。
    /// </para>
    /// </summary>
    [Serializable]
    public class MapPropPlacement
    {
        /// <summary>道具类型 id（对应 <see cref="MapPropDef.id"/>）。</summary>
        public int propId;

        /// <summary>占地左下角的格坐标。</summary>
        public int x;

        /// <summary>占地左下角的格坐标。</summary>
        public int y;
    }

    /// <summary>
    /// 地图编辑资产：编辑器里改的就是它，导出时烘成 <see cref="MapBlob"/>。
    /// <para>
    /// <b>为什么用 byte[] 稠密网格而不是 Tilemap 组件</b>：Tilemap 是渲染对象的化身，
    /// 一格一格改它再回读属于「用表现反推数据」，容易丢信息、也难做矩形填充/撤销。
    /// 这里数据与表现彻底分开：数据是这些字节网格，Tilemap 只是它的一个视图（<see cref="MapView"/>）。
    /// </para>
    /// <para>
    /// <b>多个网格</b>：第 0 层（地形）用 <see cref="cells"/>，第 1 层起用 <see cref="extraGrids"/>。
    /// 名字与值表在 <see cref="MapTileSet"/>（跨地图共用、规则要引用），数据在这里（每张地图自己）。
    /// 第 0 层单独留一个字段是为了让初版资产继续可用，对外统一成 <c>GridXxx(int grid, …)</c> 访问器。
    /// </para>
    /// <para>
    /// <b>[PreferBinarySerialization]</b>：一张 512×512 的地图是 256KB 字节，
    /// 走 YAML 文本序列化会膨胀成几 MB 且编辑器卡顿；二进制序列化是这类大数组资产的正确形态。
    /// 代价是资产不可文本 diff —— 地图本来也不适合 code review。
    /// </para>
    /// </summary>
    [PreferBinarySerialization]
    [CreateAssetMenu(menuName = "HYC Framework/Map/Map", fileName = "Map")]
    public class MapAsset : ScriptableObject
    {
        /// <summary>地图 id（导出时写进 Blob 做自校验；同一工程内应唯一）。</summary>
        public int mapId = 1;

        /// <summary>宽（格）。</summary>
        public int width = 32;

        /// <summary>高（格）。</summary>
        public int height = 24;

        /// <summary>单格边长（世界单位）。只影响 <see cref="MapView"/> 建出来的 Grid，不影响 Blob 布局。</summary>
        public float cellSize = 1f;

        /// <summary>本地图使用的瓦片集（值层定义 + 瓦片池 + 规则集 + 道具类型）。</summary>
        public MapTileSet tileSet;

        /// <summary>第 0 层（地形）的稠密网格，长度 = <see cref="width"/> × <see cref="height"/>，行优先（y 向上）。</summary>
        /// <remarks>0 = 空；1..N = 值下标 + 1（见 <see cref="MapBlob.Cells"/> 的偏移说明）。</remarks>
        public byte[] cells = Array.Empty<byte>();

        /// <summary>第 1 层起的网格数据（对齐 <see cref="MapTileSet.extraGrids"/> 的下标）。</summary>
        public List<MapGridLayer> extraGrids = new List<MapGridLayer>();

        /// <summary>道具落点（稀疏）。</summary>
        public List<MapPropPlacement> props = new List<MapPropPlacement>();

        /// <summary>应有的格子数。</summary>
        public int CellCount => Mathf.Max(0, width) * Mathf.Max(0, height);

        // ---------------- 网格 ----------------

        /// <summary>本资产实际持有的网格数（恒 ≥ 1）。</summary>
        public int GridCount => 1 + (extraGrids != null ? extraGrids.Count : 0);

        /// <summary>导出/加载时应有的网格数：跟着瓦片集走（瓦片集是定义的真源）。</summary>
        public int RequiredGridCount
        {
            get
            {
                int fromSet = tileSet != null ? tileSet.GridCount : 0;
                return Mathf.Max(1, fromSet);
            }
        }

        /// <summary>取某层的网格数组；下标非法返回 null。</summary>
        public byte[] CellsOf(int grid)
        {
            if (grid == 0) return cells;
            if (extraGrids == null || grid < 0 || grid > extraGrids.Count) return null;
            var layer = extraGrids[grid - 1];
            return layer != null ? layer.cells : null;
        }

        /// <summary>
        /// 长度对不上就重分配（<b>不保留内容</b>——保留逻辑在 <see cref="Resize"/>）。
        /// 只处理第 0 层；要对齐所有网格用 <see cref="EnsureGrids"/>。
        /// </summary>
        public void EnsureCells()
        {
            int need = CellCount;
            if (cells != null && cells.Length == need) return;
            cells = new byte[need];
        }

        /// <summary>
        /// 让网格数量与长度对齐到「瓦片集要求的层数」（只增不减）。
        /// <para>
        /// <b>不删多余层</b>：删数据是破坏性的，而多出来的层在导出时会被忽略。
        /// 真要裁掉，由使用方显式把 <see cref="extraGrids"/> 缩短。
        /// </para>
        /// </summary>
        public void EnsureGrids()
        {
            EnsureCells();

            int need = CellCount;
            int want = RequiredGridCount;
            if (extraGrids == null) extraGrids = new List<MapGridLayer>();

            while (extraGrids.Count < want - 1)
                extraGrids.Add(new MapGridLayer { cells = new byte[need] });

            for (int i = 0; i < extraGrids.Count; i++)
            {
                var layer = extraGrids[i];
                if (layer == null)
                {
                    extraGrids[i] = layer = new MapGridLayer();
                }
                if (layer.cells == null || layer.cells.Length != need)
                    layer.cells = new byte[need];
            }
        }

        /// <summary>
        /// 改宽高并（可选）保留重叠区内容。
        /// <para>
        /// 复制用的是<b>当前</b> <see cref="width"/>/<see cref="height"/>（此刻还是旧值），
        /// 所以必须在改字段之前先算好拷贝范围——这是唯一正确的做法，
        /// 试图从 <c>cells.Length</c> 反推旧宽高会在「旧宽恰好整除」时给出错误答案。
        /// </para>
        /// </summary>
        public void Resize(int newWidth, int newHeight, bool preserve)
        {
            newWidth = Mathf.Max(1, newWidth);
            newHeight = Mathf.Max(1, newHeight);

            cells = ResizeGrid(cells, newWidth, newHeight, preserve);
            if (extraGrids != null)
            {
                for (int i = 0; i < extraGrids.Count; i++)
                {
                    var layer = extraGrids[i];
                    if (layer == null) continue;
                    layer.cells = ResizeGrid(layer.cells, newWidth, newHeight, preserve);
                }
            }

            width = newWidth;
            height = newHeight;
            EnsureGrids();

            // 放不下的道具跟着裁掉，否则导出时会写进一张装不下它们的图
            if (props != null)
            {
                props.RemoveAll(p =>
                {
                    if (p == null || p.x < 0 || p.y < 0 || p.x >= width || p.y >= height) return true;
                    PropSize(p.propId, out int pw, out int ph);
                    return p.x + pw > width || p.y + ph > height;
                });
            }
        }

        private byte[] ResizeGrid(byte[] source, int newWidth, int newHeight, bool preserve)
        {
            var next = new byte[newWidth * newHeight];
            if (preserve && source != null && source.Length > 0)
            {
                int copyW = Mathf.Min(width, newWidth);
                int copyH = Mathf.Min(height, newHeight);
                for (int y = 0; y < copyH; y++)
                {
                    int src = y * width;
                    int dst = y * newWidth;
                    if (src + copyW <= source.Length && dst + copyW <= next.Length)
                        Array.Copy(source, src, next, dst, copyW);
                }
            }
            return next;
        }

        private void OnValidate()
        {
            // 只夹数值，<b>不</b>在这里重分配网格：在 Inspector 里手滑把宽高清零就该停在这一步，
            // 而不是顺手把整张地图清成空白。分配交给 EnsureCells()/EnsureGrids()/Resize()。
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            cellSize = Mathf.Max(0.01f, cellSize);
        }

        /// <summary>读一格（越界返回 0）。</summary>
        public byte GetCell(int x, int y) => GetCell(0, x, y);

        /// <summary>读某层的一格（越界/该层不存在返回 0）。</summary>
        public byte GetCell(int grid, int x, int y)
        {
            var data = CellsOf(grid);
            if (data == null) return 0;
            if (x < 0 || y < 0 || x >= width || y >= height) return 0;
            int i = y * width + x;
            // ★ 冒号后必须 (byte)0：三元表达式会做类型提升（byte 与 int 常量 → int），
            //   直接写 0 会让整个表达式变成 int，返回 byte 时 CS0266。
            return (uint)i < (uint)data.Length ? data[i] : (byte)0;
        }

        /// <summary>写一格（越界忽略）。调用方负责 Undo / SetDirty。</summary>
        public void SetCell(int x, int y, byte value) => SetCell(0, x, y, value);

        /// <summary>写某层的一格（越界/该层不存在忽略）。调用方负责 Undo / SetDirty。</summary>
        public void SetCell(int grid, int x, int y, byte value)
        {
            var data = CellsOf(grid);
            if (data == null) return;
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int i = y * width + x;
            if ((uint)i < (uint)data.Length) data[i] = value;
        }

        /// <summary>整张清空（<b>所有层</b>）。</summary>
        public void ClearAll(byte value = 0)
        {
            EnsureGrids();
            ClearGrid(cells, value);
            if (extraGrids == null) return;
            for (int i = 0; i < extraGrids.Count; i++)
            {
                var layer = extraGrids[i];
                if (layer != null) ClearGrid(layer.cells, value);
            }
        }

        private static void ClearGrid(byte[] grid, byte value)
        {
            if (grid == null) return;
            if (value == 0) Array.Clear(grid, 0, grid.Length);
            else for (int i = 0; i < grid.Length; i++) grid[i] = value;
        }

        /// <summary>某格的地形值定义（空格返回 null）。</summary>
        public MapMaterial MaterialAt(int x, int y) => ValueAt(0, x, y);

        /// <summary>某层某格的值定义（空格/该层不存在返回 null）。</summary>
        public MapMaterial ValueAt(int grid, int x, int y)
        {
            return tileSet != null ? tileSet.ValueOf(grid, GetCell(grid, x, y)) : null;
        }

        // ---------------- 道具（稀疏，与网格互相独立） ----------------

        /// <summary>某个道具类型的占地大小（瓦片集里没登记则按 1×1）。</summary>
        public void PropSize(int propId, out int w, out int h)
        {
            w = 1;
            h = 1;
            var set = tileSet;
            if (set == null || set.props == null) return;
            for (int i = 0; i < set.props.Count; i++)
            {
                var def = set.props[i];
                if (def == null || def.id != propId) continue;
                w = Mathf.Max(1, def.width);
                h = Mathf.Max(1, def.height);
                return;
            }
        }

        /// <summary>某个道具占地的右上角（开区间）。</summary>
        public void PropBounds(int propId, int x, int y, out int xMax, out int yMax)
        {
            PropSize(propId, out int w, out int h);
            xMax = x + w;
            yMax = y + h;
        }

        /// <summary>
        /// 找<b>覆盖</b>某格的道具下标（多格物件下同一格可能被别的 anchor 覆盖），没有返回 -1。
        /// <para>道具是稀疏列表、数量远小于格数，线性扫描比维护「格 → 道具」索引更省心。</para>
        /// </summary>
        public int GetPropIndexAt(int x, int y)
        {
            if (props == null) return -1;
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p == null) continue;
                PropBounds(p.propId, p.x, p.y, out int pxMax, out int pyMax);
                if (x >= p.x && x < pxMax && y >= p.y && y < pyMax) return i;
            }
            return -1;
        }

        /// <summary>某格上的道具 id，没有返回 0（0 保留给「无」）。</summary>
        public int GetPropIdAt(int x, int y)
        {
            int i = GetPropIndexAt(x, y);
            return i >= 0 ? props[i].propId : 0;
        }

        /// <summary>
        /// 这个道具能不能放在这里（占地不越界）。
        /// <para>编辑器放置前先问它，好给出「图太小放不下」这种能看懂的反馈。</para>
        /// </summary>
        public bool CanPlaceProp(int propId, int x, int y)
        {
            if (propId <= 0) return false;
            PropBounds(propId, x, y, out int xMax, out int yMax);
            return x >= 0 && y >= 0 && xMax <= width && yMax <= height;
        }

        /// <summary>
        /// 在某格放一个道具。<paramref name="propId"/> ≤ 0 表示删除覆盖该格的道具。
        /// <para>
        /// <b>占地区域不可重叠</b>：放新的之前先把重叠的旧道具全删掉。稀疏列表本可以叠，
        /// 但叠了之后编辑器要处理「同格多个怎么选、怎么删其中一个」，而实际需求（摆出生点 / 宝箱 / 树）
        /// 都不需要叠。
        /// </para>
        /// <para>越界/放不下直接忽略；调用方负责 Undo / SetDirty。</para>
        /// </summary>
        public void SetProp(int x, int y, int propId)
        {
            if (props == null) props = new List<MapPropPlacement>();
            if (propId <= 0)
            {
                RemovePropsOverlapping(x, y, 1, 1);
                return;
            }
            if (!CanPlaceProp(propId, x, y)) return;

            PropSize(propId, out int w, out int h);
            RemovePropsOverlapping(x, y, w, h);
            props.Add(new MapPropPlacement { propId = propId, x = x, y = y });
        }

        /// <summary>删掉覆盖某格的道具（多格物件只要点到任意一格就能整个删掉）。</summary>
        public void RemovePropAt(int x, int y)
        {
            RemovePropsOverlapping(x, y, 1, 1);
        }

        private void RemovePropsOverlapping(int x, int y, int w, int h)
        {
            if (props == null) return;
            int xMax = x + w;
            int yMax = y + h;
            props.RemoveAll(p =>
            {
                if (p == null) return true;
                PropBounds(p.propId, p.x, p.y, out int pxMax, out int pyMax);
                return p.x < xMax && p.y < yMax && x < pxMax && y < pyMax;
            });
        }

        /// <summary>清空全部道具。</summary>
        public void ClearProps()
        {
            props?.Clear();
        }

        /// <summary>
        /// 取一个把第 0 层当格子源的回调，供 <see cref="MapAutoRules"/> 使用。
        /// 每帧每格都新建闭包会持续产生 GC，所以编辑器里应缓存它。
        /// </summary>
        public MapAutoRules.CellAt CreateCellReader() => GetCell;

        /// <summary>取某一层当格子源的回调。</summary>
        public MapAutoRules.CellAt CreateCellReader(int grid)
        {
            if (grid == 0) return GetCell;
            return (x, y) => GetCell(grid, x, y);
        }
    }
}
