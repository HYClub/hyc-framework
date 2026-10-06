using Unity.Entities;

namespace HYC.Framework.Attr
{
    /// <summary>
    /// 框架提供的 IAttrBag 实现（ulong key 版）。使用方 codegen 的枚举版 AttrBag 通常在内部包一层
    /// AttrKeyMap（枚举→哈希）后转调 AttributeStore，或复用本结构再外包枚举重载。
    /// </summary>
    public readonly struct AttrBag : IAttrBag
    {
        readonly EntityManager em;
        readonly Entity e;

        public AttrBag(EntityManager em, Entity e)
        {
            this.em = em;
            this.e = e;
        }

        public int GetInt(ulong key) => AttributeStore.GetInt(em, e, key);
        public void SetInt(ulong key, int v) => AttributeStore.SetInt(em, e, key, v);
        public float GetFloat(ulong key) => AttributeStore.GetFloat(em, e, key);
        public void SetFloat(ulong key, float v) => AttributeStore.SetFloat(em, e, key, v);
        public long GetLong(ulong key) => AttributeStore.GetLong(em, e, key);
        public void SetLong(ulong key, long v) => AttributeStore.SetLong(em, e, key, v);
        public bool GetBool(ulong key) => AttributeStore.GetBool(em, e, key);
        public void SetBool(ulong key, bool v) => AttributeStore.SetBool(em, e, key, v);
    }
}
