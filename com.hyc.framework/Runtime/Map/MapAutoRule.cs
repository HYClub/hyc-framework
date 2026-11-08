using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Map
{
    /// <summary>
    /// <see cref="MapAutoRule.pattern"/> 的常量与下标换算。
    /// <para>
    /// 图案是 <b>3×3、行优先</b>，以「上排 → 中排 → 下排」排列，<b>y 向上为正</b>：
    /// <code>
    ///   (-1,+1) ( 0,+1) (+1,+1)   → 下标 0 1 2
    ///   (-1, 0) ( 0, 0) (+1, 0)   → 下标 3 4 5
    ///   (-1,-1) ( 0,-1) (+1,-1)   → 下标 6 7 8
    /// </code>
    /// 中排中间（下标 <see cref="Center"/>）就是「被求值的这一格自己」。
    /// </para>
    /// <para>
    /// 每个位置的值是<b>格子值</b>（0 = 必须为空，n = 必须是材质 n-1），
    /// 或 <see cref="Any"/> 表示这一位不参与匹配。空值用 <see cref="Any"/> 而不是省略，
    /// 这样图案永远是定长 9 项、可以肉眼对齐。
    /// </para>
    /// </summary>
    public static class MapRulePattern
    {
        /// <summary>该位置不参与匹配（通配）。</summary>
        public const sbyte Any = -1;

        /// <summary>图案格子数。</summary>
        public const int Length = 9;

        /// <summary>「自己这一格」在图案里的下标。</summary>
        public const int Center = 4;

        /// <summary>格子值上限（材质最多 127 种，留足余量且仍在 sbyte 正区间）。</summary>
        public const int MaxCellValue = 127;

        /// <summary>四个对角格的下标（左上 / 右上 / 右下 / 左下）。</summary>
        public static readonly int[] Diagonals = { 0, 2, 8, 6 };

        /// <summary>四个正交邻格的下标（上 / 右 / 下 / 左）。</summary>
        public static readonly int[] Edges = { 1, 5, 7, 3 };

        /// <summary>由相对偏移（dx, dy，各自 -1..1）换算图案下标。</summary>
        public static int Index(int dx, int dy)
        {
            int col = dx + 1;          // -1..1 → 0..2
            int row = 1 - dy;          // +1 → 0（上排），-1 → 2（下排）
            return row * 3 + col;
        }

        /// <summary>图案下标 → 相对偏移（调试/编辑器画方向标记用）。</summary>
        public static void Offset(int index, out int dx, out int dy)
        {
            dx = index % 3 - 1;
            dy = 1 - index / 3;
        }

        /// <summary>造一个全通配的图案（之后逐格填）。</summary>
        public static sbyte[] NewPattern()
        {
            var p = new sbyte[Length];
            for (int i = 0; i < Length; i++) p[i] = Any;
            return p;
        }
    }

    /// <summary>
    /// 一条自动拼接规则：<b>3×3 邻域图案 → 一组候选瓦片</b>。
    /// <para>
    /// 这就是「A 上面、B 下面 ⇒ 铺 A|B 过渡瓦片」这类规则的载体（与 LDtk 的 Auto-Layer Rule 同构）。
    /// 相比固定 16/47 张查表，它多出来的能力是：一条规则可以同时约束多个不同材质、
    /// 因此三种材质挤在同一格角上时也能各写一条规则解决，不必把整张表换成 256 张的混合集。
    /// </para>
    /// </summary>
    [Serializable]
    public class MapAutoRule
    {
        /// <summary>
        /// 3×3 图案，长度固定 <see cref="MapRulePattern.Length"/>。
        /// 取值见 <see cref="MapRulePattern"/>（<see cref="MapRulePattern.Any"/> = 通配）。
        /// </summary>
        [Tooltip("3×3 图案：-1 通配 / 0 必须空 / n 必须是材质 n-1。行优先，y 向上。")]
        public sbyte[] pattern = MapRulePattern.NewPattern();

        /// <summary>
        /// 命中的瓦片<b>候选池</b>（下标指向 <see cref="MapTileSet.tiles"/>）。
        /// 多于一个时按格坐标散列稳定取一张——用散列而不是 <c>Random</c>，
        /// 保证同一张地图每次加载、每台机器上长得完全一样（帧同步/回放不能容忍随机差异）。
        /// </summary>
        [Tooltip("命中的瓦片（瓦片池下标）。多个时按格坐标散列稳定挑一张，不会随机漂移。")]
        public int[] tiles = new int[0];

        /// <summary>
        /// 命中后是否立刻停止求值。<b>默认 true</b>：规则按列表顺序求值，
        /// 通用的「兜底」规则放最后，前面的具体规则先命中。
        /// </summary>
        [Tooltip("命中即停。关掉则继续往后匹配（后一条会覆盖前一条的结果）。")]
        public bool breakOnMatch = true;

        /// <summary>
        /// 命中概率（0..1）。1 = 必定生效。
        /// <para>
        /// 用途是「同一图案随机铺几种变体」——注意判定同样走<b>坐标散列</b>，不是 <c>Random</c>，
        /// 所以同一格每次加载的结果一致。判定失败时按「未命中」继续试后面的规则。
        /// </para>
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("命中概率。1 = 必定生效；小于 1 时按格坐标散列判定，每次加载结果一致。")]
        public float chance = 1f;

        /// <summary>该图案是否合法（长度对、取值在范围内）。</summary>
        public bool IsPatternValid()
        {
            if (pattern == null || pattern.Length != MapRulePattern.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
            {
                sbyte v = pattern[i];
                if (v < MapRulePattern.Any || v > MapRulePattern.MaxCellValue) return false;
            }
            return true;
        }

        /// <summary>
        /// <see cref="chance"/> 判定用的种子：由图案内容散列而来。
        /// <para>
        /// 用图案而不是规则下标当种子，调换规则顺序不会让整张地图的花纹重排。
        /// 不缓存——图案可在编辑器里随时改，缓存会悄悄过期；9 字节的散列比一次缓存失效检查还便宜。
        /// </para>
        /// </summary>
        public uint ChanceSeed()
        {
            uint h = 2166136261u;                       // FNV-1a 偏移基
            var p = pattern;
            if (p != null)
            {
                for (int i = 0; i < p.Length; i++)
                {
                    h ^= (byte)(p[i] & 0xFF);
                    h *= 16777619u;                     // FNV-1a 质数
                }
            }
            return h;
        }

        /// <summary>图案里有几个位置带约束（不是通配）。0 表示这条规则匹配一切。</summary>
        public int ConstraintCount()
        {
            if (pattern == null) return 0;
            int n = 0;
            for (int i = 0; i < pattern.Length; i++)
                if (pattern[i] != MapRulePattern.Any) n++;
            return n;
        }
    }

    /// <summary>
    /// 一组规则。<b>整组可一键启停</b>（对应 LDtk 的 rule group）：调一套过渡时只留这一组，
    /// 其余全关，画布上就能干净地看清这一套规则到底管了哪些格子。
    /// </summary>
    [Serializable]
    public class MapAutoRuleGroup
    {
        /// <summary>组名（编辑器里用来区分「草地↔水」「草地↔沙」这类不同过渡）。</summary>
        public string name = "默认组";

        /// <summary>整组是否生效。关掉时组内所有规则都不参与求值。</summary>
        [Tooltip("整组启停。调某一套过渡时关掉其余组，画布上就只剩这一套的效果。")]
        public bool active = true;

        /// <summary>组内规则。<b>顺序即优先级</b>，靠前的先求值。</summary>
        public List<MapAutoRule> rules = new List<MapAutoRule>();
    }

    /// <summary>
    /// 一个规则集 = <b>一层 Tilemap</b>。多个规则集读<b>同一份</b>格子数据、各铺各的层。
    /// <para>
    /// <b>为什么这样分层</b>：一格只能属于一种材质，但视觉上常常要叠——最典型的是「河流是画在草地上的地上物」。
    /// 把河做成材质（于是它挡路），再配两个规则集：底层规则集对「河」这一格铺草、上层规则集对同一格铺水面。
    /// 数据只有一份、材质只有一种，视觉却是叠的。这比再加一层数据便宜得多。
    /// </para>
    /// </summary>
    [Serializable]
    public class MapAutoRuleSet
    {
        /// <summary>层名（也用作生成的 GameObject 名，方便在 Hierarchy 里认）。</summary>
        public string name = "地面";

        /// <summary>
        /// 这一层是否负责生成碰撞。<b>只应有一层为 true</b>（通常是画地形的那层）。
        /// 多层都开会让同一格叠出多个碰撞体，物理上互相打架。
        /// </summary>
        [Tooltip("这一层是否生成碰撞。只应有一层为 true，通常是画地形的那层。")]
        public bool contributesCollider;

        /// <summary>
        /// <b>读哪一层值</b>（<see cref="MapTileSet.GridCount"/> 的下标）。
        /// <para>
        /// 规则本身不知道「网格」这个概念：它只是拿一个 <c>CellAt</c> 求值。
        /// 由这个字段决定传给它哪一层的 reader——所以同一条规则写法可以作用在地形层，也可以作用在通行层。
        /// </para>
        /// <para>
        /// 多个规则集读同一层是常规用法（一个网格铺好几层视觉），见类型注释。
        /// </para>
        /// </summary>
        [Tooltip("读哪一层值（0 = 地形层）。多个规则集可以读同一层，各铺各的视觉。")]
        public int sourceGrid;

        /// <summary>规则组列表。组顺序决定求值顺序。</summary>
        public List<MapAutoRuleGroup> groups = new List<MapAutoRuleGroup>();

        /// <summary>组内所有规则总数（校验/统计用）。</summary>
        public int RuleCount()
        {
            if (groups == null) return 0;
            int n = 0;
            for (int i = 0; i < groups.Count; i++)
                if (groups[i] != null) n += groups[i].rules != null ? groups[i].rules.Count : 0;
            return n;
        }
    }
}
