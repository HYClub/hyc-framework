using Unity.Entities;

namespace HYC.Framework.Attr
{
    /// <summary>
    /// 挂在实体上的属性袋元素。每个实体一个 DynamicBuffer&lt;AttributeValue&gt;，
    /// 元素按 key 的 FNV-1a 哈希线性查找（几十个属性内足够；高频访问可改排序+二分或 NativeHashMap）。
    /// raw 为统一载体：Int/Long/Bool 直接存；Float 用 System.BitConverter.DoubleToInt64Bits 存 bits。
    /// </summary>
    public struct AttributeValue : IBufferElementData
    {
        /// <summary>属性 key 的 FNV-1a 哈希（见 <see cref="AttributeKey"/>）。用 name 哈希而非枚举序号，保证属性被删/重排时运行时数据不串。</summary>
        public ulong keyHash;
        /// <summary>值类型，决定 raw 的解释方式。</summary>
        public AttributeType type;
        /// <summary>数值/布尔载体；Float 存 IEEE754 bits。</summary>
        public long raw;
    }
}
