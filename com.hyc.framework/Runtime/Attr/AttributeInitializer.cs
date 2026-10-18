using System.Collections.Generic;
using HYC.Framework.Config;
using Unity.Entities;

namespace HYC.Framework.Attr
{
    /// <summary>
    /// 把"实体引用了哪些属性"（一串 ConfigRowRef，由编辑器勾选得到）初始化成实体上的
    /// DynamicBuffer&lt;AttributeValue&gt;。默认值按 [MinRaw, MaxRaw] 钳制
    /// （Float 按 double 数值钳制后回存 bits）。运行时 Set 不再钳制——袋即为运行时真相，配置边界只约束默认值。
    /// </summary>
    public static class AttributeInitializer
    {
        /// <typeparam name="T">使用方由配置管线生成的 AttributeDef 行类型，需 unmanaged 且实现 IAttributeDef。</typeparam>
        public static void Init<T>(EntityManager em, Entity e, List<ConfigRowRef> refs)
            where T : unmanaged, IAttributeDef
        {
            if (refs == null) return;

            DynamicBuffer<AttributeValue> buf;
            if (em.HasBuffer<AttributeValue>(e)) { buf = em.GetBuffer<AttributeValue>(e); buf.Clear(); }
            else buf = em.AddBuffer<AttributeValue>(e);

            for (int i = 0; i < refs.Count; i++)
            {
                var r = refs[i];
                if (r == null || r.IsEmpty) continue;
                if (!ConfigRegistry.TryGet<T>(r.id, out T def)) continue;

                buf.Add(new AttributeValue
                {
                    keyHash = AttributeKey.Hash(def.Key),
                    type = def.ValueType,
                    raw = ClampDefault(def),
                });
            }
        }

        /// <summary>
        /// 从属性预设资产直接灌袋（优化方向 C 的手动入口）。
        /// 与 <see cref="Init{T}"/> 不同，这里不走 ConfigRowRef / ConfigRegistry，直接复制预设烘焙好的 values。
        /// </summary>
        public static void InitFromSet(EntityManager em, Entity e, AttributeSetAsset set)
        {
            if (set == null) return;
            DynamicBuffer<AttributeValue> buf;
            if (em.HasBuffer<AttributeValue>(e)) { buf = em.GetBuffer<AttributeValue>(e); buf.Clear(); }
            else buf = em.AddBuffer<AttributeValue>(e);
            for (int i = 0; i < set.values.Count; i++) buf.Add(set.values[i]);
        }

        private static long ClampDefault(IAttributeDef def)
        {
            if (def.ValueType == AttributeType.Float)
            {
                double v = System.BitConverter.Int64BitsToDouble(def.DefaultRaw);
                double mn = System.BitConverter.Int64BitsToDouble(def.MinRaw);
                double mx = System.BitConverter.Int64BitsToDouble(def.MaxRaw);
                double c = v < mn ? mn : (v > mx ? mx : v);
                return System.BitConverter.DoubleToInt64Bits(c);
            }
            long n = def.DefaultRaw;
            if (n < def.MinRaw) n = def.MinRaw;
            else if (n > def.MaxRaw) n = def.MaxRaw;
            return n;
        }
    }
}
