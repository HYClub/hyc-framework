using Unity.Entities;

namespace HYC.Framework.Map
{
    /// <summary>
    /// 单个地图道具/装饰的落点（blittable）。Blob 内禁止托管类型，故 prefab 只能收成 int id，
    /// 运行期由宿主的 <c>MapView.PropLoaded</c> 回调按 id 解析并实例化。
    /// <para>
    /// 占地大小不进 Blob：它是 <see cref="MapPropDef"/> 的属性、跨地图共用，
    /// 复制进每张地图只会制造「改了一处、另一处没改」的漂移。
    /// </para>
    /// </summary>
    public struct MapPropBlob
    {
        /// <summary>道具类型 id（由 <see cref="MapPropDef.id"/> 分配，跨地图稳定）。</summary>
        public int PropId;

        /// <summary>占地左下角落在哪一格（格坐标，不是世界坐标）。</summary>
        public int CellX;

        /// <summary>占地左下角落在哪一格。</summary>
        public int CellY;
    }

    /// <summary>
    /// 地图 Blob 根（blittable）。
    /// <para>
    /// <b>只存值索引，不存拼好的瓦片下标</b>：某一格铺哪张瓦片，是「地图数据 × 瓦片集里那套规则」的派生量，
    /// 一旦烘进 Blob，换一套瓦片集或改一条拼接规则就得重导全部地图。所以这里只保留最原始的若干层
    /// <see cref="Cells"/>（每格一个字节），运行期与编辑器各自用 <see cref="MapAutoRules"/> 现算。
    /// 代价是加载时多一次 O(W×H×层数) 的规则求值，收益是瓦片与规则可以随时替换。
    /// </para>
    /// <para>
    /// <b>多层用扁平存储而不是嵌套数组</b>：<see cref="Cells"/> 是「所有层首尾相接」的一段，
    /// 第 g 层占 <c>[g·W·H, (g+1)·W·H)</c>。这样只有一层 <c>BlobArray</c>，
    /// 既不用赌嵌套 BlobArray 是否被支持，也更利于顺序遍历。
    /// </para>
    /// <para>
    /// 用稠密网格而不是稀疏条目：一张 512×512 的地图 = 256KB（byte/格），
    /// 比同规模的稀疏条目（每条约 40+ 字节）小两个数量级。
    /// 这也是它<b>不</b>复用 <c>SceneContentBlob</c> 结构的原因——那边是稀疏摆放，这边是稠密栅格。
    /// </para>
    /// </summary>
    public struct MapBlob
    {
        /// <summary>
        /// Blob 格式版本。改动字段布局时 +1；运行期 <c>TryRead</c> 版本不符会直接拒绝加载。
        /// <list type="bullet">
        /// <item>v1：初版，单网格。</item>
        /// <item>v2：多网格（<see cref="GridCount"/> + 扁平 <see cref="Cells"/>）。<b>v1 文件不再可读</b>，
        /// 需用新版编辑器重导——地图数据本身没变，重导即可。</item>
        /// </list>
        /// </summary>
        public const int BlobVersion = 2;

        /// <summary>地图 id（= <see cref="MapAsset.mapId"/>）。</summary>
        public int MapId;

        /// <summary>宽（格数，&gt; 0）。</summary>
        public int Width;

        /// <summary>高（格数，&gt; 0）。</summary>
        public int Height;

        /// <summary>网格层数（≥ 1，第 0 层是地形）。</summary>
        public int GridCount;

        /// <summary>
        /// 生成这张地图时所用瓦片集的「值层 key 指纹」（FNV-1a，覆盖所有层名与值 key）。
        /// 运行期与当前 <see cref="MapTileSet" /> 比对不上时说明层或值的顺序变了：索引会整体错位，
        /// 必须报警而不是画出一张乱图。
        /// </summary>
        public uint PaletteHash;

        /// <summary>
        /// 所有层的稠密网格首尾相接，总长 = <see cref="GridCount"/> × <see cref="Width"/> × <see cref="Height"/>。
        /// </summary>
        /// <remarks>
        /// 取值 0 = 空；1..N = 该层的值下标 + 1。
        /// <b>+1 偏移是刻意的</b>：<c>byte[]</c> 默认全 0，若 0 表示「第一个值」，
        /// 新建地图会整体铺满第一种值；偏移一格后「全 0 = 全空」，新建即空白。
        /// </remarks>
        public BlobArray<byte> Cells;

        /// <summary>道具落点（稀疏）。</summary>
        public BlobArray<MapPropBlob> Props;
    }
}
