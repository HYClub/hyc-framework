using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HYC.Framework.Map
{
    /// <summary>
    /// 瓦片集：一张地图用到的全部<b>命名值层 + 瓦片池 + 自动拼接规则</b>。
    /// <para>
    /// 与 Tiled 的 Tileset、LDtk 的 Tileset 同一定位：它是「一块地图的视觉字典」，由美术一次性配好，
    /// 之后所有地图共用。地图资产只引用它，不复制它。
    /// </para>
    /// <para>
    /// <b>三块内容各管一件事，不要混</b>：
    /// <list type="bullet">
    /// <item><b>值层</b>（<see cref="materials"/> 是第 0 层，<see cref="extraGrids"/> 是其后的层）：数据侧。
    /// 每层有名字和值表，下标即数据里的「格子值 - 1」。地形是第一层，通行/区域/密度各自再开一层。</item>
    /// <item><see cref="tiles"/>：美术侧。扁平瓦片池，只被规则按下标引用，跟值层没有一一对应关系。</item>
    /// <item><see cref="ruleSets"/>：粘连层。每条规则说「什么样的 3×3 邻域铺哪张瓦片」。</item>
    /// </list>
    /// 分开的理由：值是玩法语义（存档、寻路、伤害判定都只认它），瓦片是表现；
    /// 一旦让值自己持有一组固定槽位的瓦片，就退回到「一种材质只能有一种长相」，
    /// 想要「沙地贴水边变成湿沙」就只能再开一种材质，地图数据跟着膨胀。
    /// </para>
    /// <para>
    /// <b>为什么第 0 层是特例字段而不是数组第 0 项</b>：<see cref="materials"/> 与
    /// <see cref="MapAsset.cells"/> 是初版就存在的序列化字段，把它们挪进列表会让已配好的资产数据作废。
    /// 所以第 0 层保留原名，其余层走 <see cref="extraGrids"/>，访问器统一成 <c>grid == 0 ? 主 : extra[grid-1]</c>。
    /// 对外只有一套 <c>GridXxx(int grid, …)</c>，用起来没有特例感。
    /// </para>
    /// <para>
    /// <b>为什么带指纹</b>：值层下标直接写进了 Blob（每格 1 字节）。有人在中途插入/删除一个值或一层，
    /// 下标就会整体平移，旧地图会变成一张错位但「看起来正常」的乱图——这种错最难查。
    /// 故导出处写一份 FNV-1a 指纹（覆盖所有层的名字与值 key），加载时比对不上直接拒绝并报错。
    /// 瓦片与规则<b>不进指纹</b>：换美术、改规则都不该逼着重导地图，那正是本设计想避免的。
    /// </para>
    /// </summary>
    // 创建入口走 Assets/Create（与其他 SO 一致），不占 HYC Framework 功能树。
    [CreateAssetMenu(menuName = "HYC Framework/Map/Tile Set", fileName = "MapTileSet")]
    public class MapTileSet : ScriptableObject
    {
        /// <summary>第 0 层（地形）的值表。下标 = Blob 里 <c>cell - 1</c>。</summary>
        public List<MapMaterial> materials = new List<MapMaterial>();

        /// <summary>第 0 层（地形）的名字。数据层的命名权在这里，<see cref="MapAsset"/> 只管数据。</summary>
        public string primaryGridName = "地形";

        /// <summary>第 1 层起的值层定义（通行 / 区域 / 密度…）。</summary>
        public List<MapGridDef> extraGrids = new List<MapGridDef>();

        /// <summary>
        /// 哪一层负责「能不能踩」。
        /// <para>
        /// 默认 0（地形层的 <see cref="MapMaterial.walkable"/> 决定）。想让「河挡路」由独立的通行层控制时，
        /// 把它指到那一层，地形层的 walkable 就不再参与判断。
        /// </para>
        /// </summary>
        [Tooltip("哪一层决定能不能踩。默认 0 = 地形层自己的 walkable。")]
        public int walkableGridIndex;

        /// <summary>扁平瓦片池。规则用下标引用它；顺序本身没有语义，随便排。</summary>
        public List<Sprite> tiles = new List<Sprite>();

        /// <summary>规则集，<b>列表顺序即图层顺序</b>（index 0 在最下）。</summary>
        public List<MapAutoRuleSet> ruleSets = new List<MapAutoRuleSet>();

        /// <summary>道具类型表。</summary>
        public List<MapPropDef> props = new List<MapPropDef>();

        // ---------------- 值层 ----------------

        /// <summary>值层数量（含第 0 层，恒 ≥ 1）。</summary>
        public int GridCount => 1 + (extraGrids != null ? extraGrids.Count : 0);

        /// <summary>某层的名字。</summary>
        public string GridName(int grid)
        {
            if (grid == 0) return string.IsNullOrEmpty(primaryGridName) ? "地形" : primaryGridName;
            int i = grid - 1;
            if (extraGrids != null && (uint)i < (uint)extraGrids.Count && extraGrids[i] != null
                && !string.IsNullOrEmpty(extraGrids[i].name))
                return extraGrids[i].name;
            return $"网格{grid}";
        }

        /// <summary>某层的值表。<b>恒不为 null</b>（不存在就建），越界返回 null。</summary>
        public List<MapMaterial> ValuesOf(int grid)
        {
            if (grid == 0)
            {
                if (materials == null) materials = new List<MapMaterial>();
                return materials;
            }
            if (extraGrids == null || grid < 0 || grid > extraGrids.Count) return null;
            var def = extraGrids[grid - 1];
            if (def == null) return null;
            if (def.values == null) def.values = new List<MapMaterial>();
            return def.values;
        }

        /// <summary>某层的值数量（越界返回 0）。</summary>
        public int ValueCount(int grid)
        {
            var list = ValuesOf(grid);
            return list != null ? list.Count : 0;
        }

        /// <summary>把某层的格子值换成值定义；越界或为空返回 null。</summary>
        public MapMaterial ValueOf(int grid, byte cellValue)
        {
            var list = ValuesOf(grid);
            int i = cellValue - 1;
            return list != null && (uint)i < (uint)list.Count ? list[i] : null;
        }

        /// <summary>某层的值下标 → 写进数据的格子值（越界返回 0 = 空）。</summary>
        public byte CellValueOf(int grid, int valueIndex)
        {
            var list = ValuesOf(grid);
            int v = valueIndex + 1;
            return list != null && valueIndex >= 0 && valueIndex < list.Count && v <= 255
                ? (byte)v : (byte)0;
        }

        /// <summary>按 key 在某层里找值下标，找不到返回 -1。</summary>
        public int IndexOfKey(int grid, string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;
            var list = ValuesOf(grid);
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].key == key) return i;
            return -1;
        }

        // ---------------- 第 0 层的便捷入口（等价于 grid = 0） ----------------

        /// <summary>第 0 层的值数量。合法下标范围 0..<see cref="MaterialCount"/>-1。</summary>
        public int MaterialCount => materials != null ? materials.Count : 0;

        /// <summary>规则集数量（= 图层数）。</summary>
        public int RuleSetCount => ruleSets.Count;

        /// <summary>第 0 层的格子值 → 值定义；越界或为空返回 null。</summary>
        public MapMaterial MaterialOf(byte cellValue) => ValueOf(0, cellValue);

        /// <summary>第 0 层的值下标 → 格子值（越界返回 0=空）。</summary>
        public byte CellValueOf(int materialIndex) => CellValueOf(0, materialIndex);

        /// <summary>按 key 在第 0 层里找下标，找不到返回 -1。</summary>
        public int IndexOfKey(string key) => IndexOfKey(0, key);

        /// <summary>取瓦片池里的一张；下标非法返回 null。</summary>
        public Sprite TileAt(int index)
        {
            return tiles != null && (uint)index < (uint)tiles.Count ? tiles[index] : null;
        }

        // ---------------- 通行 ----------------

        /// <summary>某一层的某个格子值能不能踩。</summary>
        public bool IsWalkableIn(int grid, byte cellValue)
        {
            var m = ValueOf(grid, cellValue);
            return m != null && m.walkable;
        }

        /// <summary>
        /// 这一格能不能踩（按 <see cref="walkableGridIndex"/> 指定的那层）。
        /// <para><b>注意</b>：<paramref name="cellValue"/> 必须是<b>那一层</b>的格子值，不是地形层的。</para>
        /// </summary>
        public bool IsWalkable(byte cellValue) => IsWalkableIn(ClampedWalkableGrid, cellValue);

        /// <summary>实际参与的通行层（越界时回落到 0，避免配置手滑导致全程不可走）。</summary>
        public int WalkableGrid => ClampedWalkableGrid;

        private int ClampedWalkableGrid
        {
            get
            {
                int g = walkableGridIndex;
                return g > 0 && g < GridCount ? g : 0;
            }
        }

        // ---------------- 指纹 ----------------

        /// <summary>
        /// 当前值层结构的指纹（FNV-1a over 所有层名 + 所有值 key）。
        /// <para>层级或值顺序一变，旧地图索引就整体错位，必须靠它拦住。</para>
        /// </summary>
        public uint PaletteHash
        {
            get
            {
                uint h = 2166136261u;
                int count = GridCount;
                for (int g = 0; g < count; g++)
                {
                    // 0x10 / 0x01 是不同的分隔字节：避免「层1 的 a,b」与「层2 的 a 和 b」撞码
                    h = (h ^ 0x10u) * 16777619u;
                    Hash(ref h, GridName(g));

                    var list = ValuesOf(g);
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        Hash(ref h, list[i] != null ? list[i].key : string.Empty);
                        h = (h ^ 0x01u) * 16777619u;
                    }
                }
                return h;
            }
        }

        private static void Hash(ref uint h, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            var bytes = Encoding.UTF8.GetBytes(s);
            for (int i = 0; i < bytes.Length; i++)
                h = (h ^ bytes[i]) * 16777619u;
        }

        // ---------------- 体检 ----------------

        /// <summary>
        /// 配置体检（不抛异常，返回人话问题列表）。空列表 = 通过。
        /// </summary>
        public List<string> Validate()
        {
            var issues = new List<string>();

            // ---- 值层 ----
            int gridCount = GridCount;
            if (gridCount == 1 && MaterialCount == 0)
                issues.Add("瓦片集没有任何值：第 0 层（地形）的值表是空的。");

            for (int g = 0; g < gridCount; g++)
            {
                var list = ValuesOf(g);
                string label = g == 0 ? $"第 0 层「{GridName(g)}」" : $"第 {g} 层「{GridName(g)}」";
                if (list == null)
                {
                    issues.Add($"{label}的定义缺失。");
                    continue;
                }
                if (list.Count > MapRulePattern.MaxCellValue)
                    issues.Add($"{label}有 {list.Count} 个值，超过上限 {MapRulePattern.MaxCellValue} 个。");

                var keys = new Dictionary<string, int>(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    var m = list[i];
                    if (m == null)
                    {
                        issues.Add($"{label}的下标 {i} 是空槽。");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(m.key))
                        issues.Add($"{label}下标 {i}（{m.displayName}）的 key 为空。");
                    else if (keys.TryGetValue(m.key, out int prev))
                        issues.Add($"{label}的 key「{m.key}」重复（下标 {prev} 与 {i}）。");
                    else
                        keys[m.key] = i;
                }
            }
            if (walkableGridIndex < 0 || walkableGridIndex >= gridCount)
                issues.Add($"「通行层」指向第 {walkableGridIndex} 层，但只有 {gridCount} 层。");

            if (tiles == null || tiles.Count == 0)
                issues.Add("瓦片池是空的，规则没有瓦片可引用。");

            if (ruleSets == null || ruleSets.Count == 0)
                issues.Add("没有任何规则集，地图加载后会是空的。");

            // ---- 规则集 ----
            int colliderLayers = 0;
            for (int s = 0; s < (ruleSets != null ? ruleSets.Count : 0); s++)
            {
                var set = ruleSets[s];
                if (set == null)
                {
                    issues.Add($"规则集下标 {s} 是空槽。");
                    continue;
                }
                if (set.contributesCollider) colliderLayers++;
                if (set.sourceGrid < 0 || set.sourceGrid >= gridCount)
                    issues.Add($"规则集「{set.name}」读第 {set.sourceGrid} 层，但只有 {gridCount} 层。");

                if (set.groups == null || set.groups.Count == 0)
                {
                    issues.Add($"规则集「{set.name}」没有任何规则组。");
                    continue;
                }

                for (int g = 0; g < set.groups.Count; g++)
                {
                    var group = set.groups[g];
                    if (group == null)
                    {
                        issues.Add($"规则集「{set.name}」第 {g} 组是空槽。");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(group.name))
                        issues.Add($"规则集「{set.name}」第 {g} 组的名字为空。");
                    if (group.rules == null || group.rules.Count == 0)
                    {
                        issues.Add($"规则集「{set.name}」的组「{group.name}」里没有规则。");
                        continue;
                    }

                    for (int r = 0; r < group.rules.Count; r++)
                    {
                        var rule = group.rules[r];
                        string where = $"规则集「{set.name}」的组「{group.name}」第 {r} 条";
                        if (rule == null)
                        {
                            issues.Add($"{where}是空槽。");
                            continue;
                        }
                        if (!rule.IsPatternValid())
                            issues.Add($"{where}的 3×3 图案非法（长度须为 {MapRulePattern.Length}，取值须在 {MapRulePattern.Any}..{MapRulePattern.MaxCellValue}）。");
                        if (rule.tiles == null || rule.tiles.Length == 0)
                        {
                            issues.Add($"{where}没有指定瓦片。");
                            continue;
                        }
                        for (int t = 0; t < rule.tiles.Length; t++)
                        {
                            int idx = rule.tiles[t];
                            if (tiles == null || (uint)idx >= (uint)tiles.Count)
                                issues.Add($"{where}引用了越界瓦片下标 {idx}（瓦片池共 {(tiles != null ? tiles.Count : 0)} 张）。");
                            else if (tiles[idx] == null)
                                issues.Add($"{where}引用的瓦片 {idx} 是空槽。");
                        }
                    }
                }
            }

            if (colliderLayers > 1)
                issues.Add($"{colliderLayers} 个规则集都勾了「生成碰撞」。同一格会叠出多个碰撞体，只应保留一层。");

            // ---- 道具 ----
            var propIds = new Dictionary<int, int>(props != null ? props.Count : 0);
            for (int i = 0; i < (props != null ? props.Count : 0); i++)
            {
                var p = props[i];
                if (p == null) continue;
                if (p.id <= 0)
                    issues.Add($"道具下标 {i}（{p.displayName}）的 id 必须 > 0（0 保留给「无」）。");
                else if (propIds.TryGetValue(p.id, out int prev))
                    issues.Add($"道具 id {p.id} 重复（下标 {prev} 与 {i}）。");
                else
                    propIds[p.id] = i;

                if (p.width < 1 || p.height < 1)
                    issues.Add($"道具「{p.displayName}」的占地 {p.width}×{p.height} 非法（都须 ≥ 1）。");
            }
            return issues;
        }
    }
}
