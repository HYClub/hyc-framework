using UnityEngine;

namespace HYC.Framework.Map
{
    /// <summary>
    /// 规则求值器：把「3×3 邻域图案」套到某格上，得出该铺哪张瓦片。
    /// <para>
    /// 编辑器与运行期共用这一份实现，保证「编辑器里看到的」和「进游戏看到的」不可能不一致。
    /// </para>
    /// <para>
    /// <b>为什么不做预烘表</b>：16 张过渡用固定查表当然更快，但规则是任意 3×3 图案，
    /// 签名空间是 128⁹，预烘没有意义。实际开销是「每格 × 每规则 × 最多 9 次比较」，
    /// 一张 512×512 的地图在加载时算一次，量级在几十毫秒——比它换来的灵活性值。
    /// 真成瓶颈时的正解是「按中心格材质分桶，只求值本桶内的规则」，而不是放弃规则模型。
    /// </para>
    /// </summary>
    public static class MapAutoRules
    {
        /// <summary>格子源。约定：<b>越界必须返回 0</b>（地图外当空地，规则才能统一处理边界）。</summary>
        public delegate byte CellAt(int x, int y);

        /// <summary>没有规则命中时 <see cref="Evaluate"/> 的返回值（= 这一格这层不画）。</summary>
        public const int NoTile = -1;

        /// <summary>
        /// 求某一格在某规则集下该铺的瓦片（返回 <see cref="MapTileSet.tiles"/> 的下标，<see cref="NoTile"/> = 不铺）。
        /// <para>组按列表顺序求值，停用的组跳过；组内规则同样按顺序；<see cref="MapAutoRule.breakOnMatch"/> 为 true 时命中即返回。</para>
        /// </summary>
        public static int Evaluate(MapAutoRuleSet set, CellAt cellAt, int x, int y)
        {
            if (set == null || set.groups == null) return NoTile;

            int result = NoTile;
            for (int gi = 0; gi < set.groups.Count; gi++)
            {
                var group = set.groups[gi];
                if (group == null || !group.active || group.rules == null) continue;

                for (int ri = 0; ri < group.rules.Count; ri++)
                {
                    var rule = group.rules[ri];
                    if (rule == null || rule.tiles == null || rule.tiles.Length == 0) continue;
                    if (!Matches(rule, cellAt, x, y)) continue;
                    if (!RollChance(rule, x, y)) continue;

                    result = PickTile(rule, x, y);
                    if (rule.breakOnMatch) return result;
                }
            }
            return result;
        }

        /// <summary>某条规则的图案是否匹配该格的 3×3 邻域。</summary>
        public static bool Matches(MapAutoRule rule, CellAt cellAt, int x, int y)
        {
            if (rule == null || cellAt == null) return false;
            var p = rule.pattern;
            if (p == null || p.Length != MapRulePattern.Length) return false;

            // 先比中心：绝大多数规则中心是有约束的，这一步能挡掉大部分不匹配。
            if (!MatchAt(p[MapRulePattern.Center], cellAt, x, y)) return false;

            for (int i = 0; i < MapRulePattern.Length; i++)
            {
                if (i == MapRulePattern.Center) continue;
                sbyte want = p[i];
                if (want == MapRulePattern.Any) continue;

                MapRulePattern.Offset(i, out int dx, out int dy);
                if (!MatchAt(want, cellAt, x + dx, y + dy)) return false;
            }
            return true;
        }

        private static bool MatchAt(sbyte want, CellAt cellAt, int x, int y)
        {
            if (want == MapRulePattern.Any) return true;
            return cellAt(x, y) == (byte)want;
        }

        /// <summary>
        /// <see cref="MapAutoRule.chance"/> 的判定。
        /// <para>
        /// 随机源是 <b>(格坐标, 规则图案) 的散列</b>，不是 <c>Random</c>：同一格每次加载、每台机器、
        /// 每次回放都得到同一结论。种子取自图案内容而非规则下标，所以调换规则顺序不会让整张地图的花纹重排。
        /// </para>
        /// </summary>
        public static bool RollChance(MapAutoRule rule, int x, int y)
        {
            if (rule == null) return false;
            if (rule.chance >= 1f) return true;
            if (rule.chance <= 0f) return false;

            uint h = Hash(x, y, rule.ChanceSeed());
            // 取高 24 位映射到 [0,1)，够细且不用浮点除法的额外分支。
            float t = (h >> 8) * (1f / 16777216f);
            return t < rule.chance;
        }

        private static uint Hash(int x, int y, uint seed)
        {
            uint h = seed * 0x9E3779B1u;
            h ^= (uint)x * 73856093u;
            h ^= (uint)y * 19349663u;
            h ^= h >> 13;
            h *= 0x5BD1E995u;
            h ^= h >> 15;
            return h;
        }

        /// <summary>
        /// 在候选瓦片里稳定地取一张。
        /// <para>
        /// <b>用坐标散列而不是 <c>Random</c></b>：同一张地图每次加载、每台机器、每次回放都得到同一结果。
        /// 抖动用随机数会在联机/回放里表现为「两边地面花纹不一样」，那种 bug 极难查。
        /// </para>
        /// </summary>
        public static int PickTile(MapAutoRule rule, int x, int y)
        {
            var tiles = rule.tiles;
            if (tiles == null || tiles.Length == 0) return NoTile;
            if (tiles.Length == 1) return tiles[0];

            uint h = (uint)x * 73856093u ^ (uint)y * 19349663u;
            h ^= h >> 13;
            h *= 0x5BD1E995u;
            h ^= h >> 15;
            return tiles[(int)(h % (uint)tiles.Length)];
        }

        /// <summary>把稠密网格包成 <see cref="CellAt"/>（越界返回 0）。</summary>
        public static CellAt Reader(byte[] cells, int width, int height)
        {
            return (x, y) =>
            {
                if (cells == null) return 0;
                if (x < 0 || y < 0 || x >= width || y >= height) return 0;
                int i = y * width + x;
                return (uint)i < (uint)cells.Length ? cells[i] : (byte)0;
            };
        }

        /// <summary>
        /// 把一组规则按「约束位数」降序重排（<b>稳定</b>：同分保持原相对顺序）。
        /// <para>
        /// 求值仍然是「顺序 + 命中即停」，这里只是把顺序<b>排成直觉上该有的样子</b>：
        /// 越具体的规则越靠前，通配越多的兜底越靠后。省掉手工拖拽，也避免
        /// 「兜底规则被随手加在中间，把后面所有规则全遮住」这种最难查的配置错误。
        /// </para>
        /// <para>
        /// 用插入排序而不是 <c>List.Sort</c>：后者是不稳定排序，同分规则的相对顺序会随机变，
        /// 而那意味着「同一份配置在不同机器上画面可能不同」。
        /// </para>
        /// </summary>
        /// <returns>实际移动过顺序的规则条数（0 = 本来就有序）。</returns>
        public static int SortBySpecificity(System.Collections.Generic.List<MapAutoRule> rules)
        {
            if (rules == null || rules.Count < 2) return 0;

            int moved = 0;
            for (int i = 1; i < rules.Count; i++)
            {
                var current = rules[i];
                int key = current != null ? current.ConstraintCount() : -1;

                int j = i - 1;
                bool shifted = false;
                while (j >= 0)
                {
                    var prev = rules[j];
                    int prevKey = prev != null ? prev.ConstraintCount() : -1;
                    if (prevKey >= key) break;      // >= 保证稳定
                    rules[j + 1] = prev;
                    j--;
                    shifted = true;
                }
                rules[j + 1] = current;
                if (shifted) moved++;
            }
            return moved;
        }
    }
}
