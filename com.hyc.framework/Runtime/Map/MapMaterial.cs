using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Map
{
    /// <summary>
    /// 一个「值」的定义（草地 / 沙地 / 河流 / 石头…，也用于通行层这类非视觉网格）。
    /// <para>
    /// 它是网格数据的最小单位：每格存的就是「值下标 + 1」。它<b>不描述自己长什么样</b>——
    /// 长什么样由规则集（<see cref="MapAutoRuleSet"/>）决定，同一个值在不同规则集里可以是完全不同的瓦片
    /// （例如「河」在底层规则集里画河床、在上层画水面）。这个分工是刻意的：换美术只改规则集，不动地图数据。
    /// </para>
    /// </summary>
    [Serializable]
    public class MapMaterial
    {
        /// <summary>稳定 key（写进指纹、给存档/配置引用）。改它等于换了一种材质。</summary>
        public string key = "sand";

        /// <summary>界面上显示的名字。</summary>
        public string displayName = "沙地";

        /// <summary>调色板的标记色，也是「这一格没被任何规则铺到」时的兜底色。</summary>
        public Color color = new Color(0.85f, 0.78f, 0.55f, 1f);

        /// <summary>
        /// 能不能踩。只在<b>承担通行判断的那个网格</b>上被读取（见 <see cref="MapTileSet.walkableGridIndex"/>）。
        /// <para>
        /// 关闭后：<see cref="MapView.IsWalkable"/> 对这一格返回 false，且承载碰撞的规则集会给该格的
        /// 瓦片挂上 <c>Tile.ColliderType.Sprite</c>（靠 sprite 自带 Physics Shape 精确切分）。
        /// </para>
        /// </summary>
        [Tooltip("能不能踩。河流/岩浆关掉；草地、花草打开。只在「通行网格」上生效。")]
        public bool walkable = true;
    }

    /// <summary>
    /// 一个<b>命名值层</b>的定义（对应 LDtk 的 IntGrid）：名字 + 这张表里的值。
    /// <para>
    /// <b>为什么网格要能多于一个</b>：一格数据只能表达一种语义。地形（沙/水/草）是一种，
    /// 「能不能走」「是不是刷怪区」「光照遮蔽」都是<b>别的维度</b>。挤进同一个字节里就只能靠
    /// 材质枚举膨胀（沙地-可走 / 沙地-不可走 / 沙地-刷怪…），那是组合爆炸。
    /// 分开成多个网格后，各维度独立、互不干扰，规则集按 <see cref="MapAutoRuleSet.sourceGrid"/> 选读哪个。
    /// </para>
    /// <para>
    /// <b>定义在瓦片集、数据在地图</b>：定义（名字 + 值表）跨地图共用，所以规则能稳定引用；
    /// 数据（<c>byte[]</c>）属于每张地图自己，按这里的下标对齐。
    /// </para>
    /// </summary>
    [Serializable]
    public class MapGridDef
    {
        /// <summary>网格名（也用作编辑器下拉里的标签）。</summary>
        public string name = "通行";

        /// <summary>这一层的值表。下标即数据里的 <c>cell - 1</c>。</summary>
        public List<MapMaterial> values = new List<MapMaterial>();
    }

    /// <summary>
    /// 地图侧的一个网格数据层。<b>只存数据不存名字</b>——名字是定义的一部分，住在
    /// <see cref="MapTileSet"/> 里；两处都存会出现「改了名但只有一边生效」这种真源分裂。
    /// </summary>
    [Serializable]
    public class MapGridLayer
    {
        /// <summary>稠密网格，长度 = 地图 width × height，行优先（y 向上）。</summary>
        public byte[] cells = Array.Empty<byte>();
    }

    /// <summary>
    /// 地图道具/装饰的类型定义。只用 int id 进 Blob，运行期由宿主按 id 还原成 prefab。
    /// </summary>
    [Serializable]
    public class MapPropDef
    {
        /// <summary>稳定 id（跨地图唯一）。0 保留为「未定义」。</summary>
        public int id = 1;

        /// <summary>界面上显示的名字。</summary>
        public string displayName = "道具";

        /// <summary>调色板上画的颜色。</summary>
        public Color color = Color.white;

        /// <summary>
        /// 占几格宽。<b>锚点是左下角</b>（与网格 y 向上一致），向右上方铺开。
        /// <para>多格物件（2×2 的树、3×2 的门、1×4 的桥）靠它表达；1×1 是默认。</para>
        /// </summary>
        [Min(1)]
        [Tooltip("占几格宽。锚点在左下角，向右铺开。")]
        public int width = 1;

        /// <summary>占几格高。锚点在左下角，向上铺开。</summary>
        [Min(1)]
        [Tooltip("占几格高。锚点在左下角，向上铺开。")]
        public int height = 1;

        /// <summary>
        /// 可选的表现 prefab。<b>框架不自动实例化它</b>——摆放策略（对象池 / ECS / 层级）是宿主的事，
        /// 这里只是把「这个 id 对应哪个 prefab」存成一份跨地图共用的登记表，宿主在
        /// <see cref="MapView.PropLoaded"/> 回调里查它即可。
        /// </summary>
        [Tooltip("可选：这个道具对应的 prefab。框架不自动实例化，宿主在 PropLoaded 回调里查。")]
        public GameObject prefab;
    }
}
