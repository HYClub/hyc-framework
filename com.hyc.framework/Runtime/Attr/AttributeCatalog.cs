using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Attr
{
    /// <summary>
    /// 属性目录：属性配置系统的**单一事实源**（优化方向 B）。
    /// 你只在此处列出属性（key + 类型 + 默认值 + 上下限），其余一切——
    /// 配置行、enum AttrKey、哈希映射、类型安全 AttrBag、预设——都从它派生。
    /// 创建：Project 右键 / 菜单 Assets ▸ Create ▸ HYC Framework/Attributes/Attribute Catalog。
    /// 之后用 Inspector 上的「生成属性访问器」按钮（或菜单 HYC Framework/Attributes/Generate Accessor）一键出代码。
    /// </summary>
    [CreateAssetMenu(menuName = "HYC Framework/Attributes/Attribute Catalog", fileName = "AttributeCatalog")]
    public class AttributeCatalog : ScriptableObject
    {
        public List<AttributeCatalogEntry> entries = new List<AttributeCatalogEntry>();

        /// <summary>按 key 取条目（线性查找，目录规模很小）。</summary>
        public AttributeCatalogEntry Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].key == key) return entries[i];
            return null;
        }

        /// <summary>key 在目录中的序号（= 生成 enum 时的枚举序数）。</summary>
        public int IndexOf(string key)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].key == key) return i;
            return -1;
        }
    }

    /// <summary>
    /// 目录里的一个属性定义。valueType 决定 raw 的解释方式；Float 的 default/min/max 需预存 IEEE754 bits。
    /// </summary>
    [System.Serializable]
    public class AttributeCatalogEntry
    {
        public string key;
        public AttributeType type = AttributeType.Int;
        public long defaultRaw;
        public long minRaw;
        public long maxRaw;

        /// <summary>把本条烘焙成运行期属性袋元素（key 用 FNV-1a 哈希，raw 按 [min,max] 钳制）。</summary>
        public AttributeValue ToValue()
        {
            long raw = defaultRaw;
            if (type == AttributeType.Float)
            {
                double v = System.BitConverter.Int64BitsToDouble(defaultRaw);
                double mn = System.BitConverter.Int64BitsToDouble(minRaw);
                double mx = System.BitConverter.Int64BitsToDouble(maxRaw);
                double c = v < mn ? mn : (v > mx ? mx : v);
                raw = System.BitConverter.DoubleToInt64Bits(c);
            }
            else
            {
                if (raw < minRaw) raw = minRaw;
                else if (raw > maxRaw) raw = maxRaw;
            }
            return new AttributeValue
            {
                keyHash = AttributeKey.Hash(key),
                type = type,
                raw = raw,
            };
        }
    }
}
