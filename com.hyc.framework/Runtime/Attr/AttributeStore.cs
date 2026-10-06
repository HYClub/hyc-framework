using Unity.Entities;

namespace HYC.Framework.Attr
{
    /// <summary>
    /// 属性袋的主线程读写 API（基于 EntityManager）。
    /// Job/Burst 内请直接操作实体的 DynamicBuffer&lt;AttributeValue&gt;（按 keyHash 查找）。
    /// 所有读写都在实体的 DynamicBuffer 中按 key 的 FNV-1a 哈希线性查找。
    /// 找不到 key 时 Get 返回调用方给定的默认值；Set 为 no-op（袋即声明过的属性集合，不自动扩容）。
    /// </summary>
    public static class AttributeStore
    {
        public static int GetInt(EntityManager em, Entity e, ulong key, int def = 0)
        {
            if (!em.HasBuffer<AttributeValue>(e)) return def;
            var buf = em.GetBuffer<AttributeValue>(e);
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].keyHash == key) return (int)buf[i].raw;
            return def;
        }

        public static void SetInt(EntityManager em, Entity e, ulong key, int v)
        {
            if (!em.HasBuffer<AttributeValue>(e)) return;
            var buf = em.GetBuffer<AttributeValue>(e);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].keyHash == key) { var t = buf[i]; t.raw = v; buf[i] = t; return; }
            }
        }

        public static float GetFloat(EntityManager em, Entity e, ulong key, float def = 0f)
        {
            if (!em.HasBuffer<AttributeValue>(e)) return def;
            var buf = em.GetBuffer<AttributeValue>(e);
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].keyHash == key) return (float)System.BitConverter.Int64BitsToDouble(buf[i].raw);
            return def;
        }

        public static void SetFloat(EntityManager em, Entity e, ulong key, float v)
        {
            if (!em.HasBuffer<AttributeValue>(e)) return;
            long raw = System.BitConverter.DoubleToInt64Bits((double)v);
            var buf = em.GetBuffer<AttributeValue>(e);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].keyHash == key) { var t = buf[i]; t.raw = raw; buf[i] = t; return; }
            }
        }

        public static long GetLong(EntityManager em, Entity e, ulong key, long def = 0L)
        {
            if (!em.HasBuffer<AttributeValue>(e)) return def;
            var buf = em.GetBuffer<AttributeValue>(e);
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].keyHash == key) return buf[i].raw;
            return def;
        }

        public static void SetLong(EntityManager em, Entity e, ulong key, long v)
        {
            if (!em.HasBuffer<AttributeValue>(e)) return;
            var buf = em.GetBuffer<AttributeValue>(e);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].keyHash == key) { var t = buf[i]; t.raw = v; buf[i] = t; return; }
            }
        }

        public static bool GetBool(EntityManager em, Entity e, ulong key, bool def = false)
        {
            if (!em.HasBuffer<AttributeValue>(e)) return def;
            var buf = em.GetBuffer<AttributeValue>(e);
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].keyHash == key) return buf[i].raw != 0;
            return def;
        }

        public static void SetBool(EntityManager em, Entity e, ulong key, bool v)
        {
            if (!em.HasBuffer<AttributeValue>(e)) return;
            var buf = em.GetBuffer<AttributeValue>(e);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].keyHash == key) { var t = buf[i]; t.raw = v ? 1 : 0; buf[i] = t; return; }
            }
        }
    }
}
