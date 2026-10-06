// =============================================================================
//  HYCSimple · 属性系统示例 · 生成访问器（映射表）
//  向导生成。Hash[i] 必须等于 AttributeKey.Hash(对应 key 字符串)。
// =============================================================================
using HYC.Framework.Attr;

namespace HYC.Simple.Attr.Generated
{
    /// <summary>
    /// 枚举 → FNV-1a 哈希 / 类型 的映射表。
    /// 运行时 AttrBag 把枚举经此表映射到哈希，再转调 AttributeStore 在实体的
    /// DynamicBuffer&lt;AttributeValue&gt; 里按 keyHash 线性查找。
    /// </summary>
    public static class GameAttrKeyMap
    {
        /// <summary>枚举序数 → 属性名 FNV-1a 哈希（与 AttributeCatalog 条目 key 对应）。</summary>
        public static readonly ulong[] Hash =
        {
            AttributeKey.Hash("HP"),
            AttributeKey.Hash("MP"),
            AttributeKey.Hash("MoveSpeed"),
            AttributeKey.Hash("IsInvincible"),
        };

        /// <summary>枚举序数 → 属性类型（决定 raw 的解释方式）。</summary>
        public static readonly AttributeType[] Type =
        {
            AttributeType.Int,
            AttributeType.Int,
            AttributeType.Float,
            AttributeType.Bool,
        };
    }
}
