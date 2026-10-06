// =============================================================================
//  HYCSimple · 属性系统示例 · 生成访问器（类型安全 AttrBag）
//  向导生成，实现 IAttrBag。把 GameAttrKey 经 GameAttrKeyMap 映射到哈希后转调
//  AttributeStore，暴露 GetInt(GameAttrKey.HP) 这类「零手写 key、拼错编译期报错」的重载。
// =============================================================================
using Unity.Entities;
using HYC.Framework.Attr;

namespace HYC.Simple.Attr.Generated
{
    /// <summary>
    /// 枚举版属性袋。包 (EntityManager, Entity)，转调 AttributeStore。
    /// Job/Burst 内不要走这套（托管分派），直接操作 DynamicBuffer&lt;AttributeValue&gt; + AttributeStore 的 ulong 重载。
    /// </summary>
    public readonly struct GameAttrBag : IAttrBag
    {
        readonly EntityManager em;
        readonly Entity e;

        public GameAttrBag(EntityManager em, Entity e)
        {
            this.em = em;
            this.e = e;
        }

        // ---- IAttrBag（ulong key 版）：供框架接口分派，使用方一般不直接调 ----
        public int GetInt(ulong key) => AttributeStore.GetInt(em, e, key);
        public void SetInt(ulong key, int v) => AttributeStore.SetInt(em, e, key, v);
        public float GetFloat(ulong key) => AttributeStore.GetFloat(em, e, key);
        public void SetFloat(ulong key, float v) => AttributeStore.SetFloat(em, e, key, v);
        public long GetLong(ulong key) => AttributeStore.GetLong(em, e, key);
        public void SetLong(ulong key, long v) => AttributeStore.SetLong(em, e, key, v);
        public bool GetBool(ulong key) => AttributeStore.GetBool(em, e, key);
        public void SetBool(ulong key, bool v) => AttributeStore.SetBool(em, e, key, v);

        // ---- 枚举友好重载（使用方代码调这些）----
        public int GetInt(GameAttrKey key) => GetInt(GameAttrKeyMap.Hash[(int)key]);
        public void SetInt(GameAttrKey key, int v) => SetInt(GameAttrKeyMap.Hash[(int)key], v);
        public float GetFloat(GameAttrKey key) => GetFloat(GameAttrKeyMap.Hash[(int)key]);
        public void SetFloat(GameAttrKey key, float v) => SetFloat(GameAttrKeyMap.Hash[(int)key], v);
        public long GetLong(GameAttrKey key) => GetLong(GameAttrKeyMap.Hash[(int)key]);
        public void SetLong(GameAttrKey key, long v) => SetLong(GameAttrKeyMap.Hash[(int)key], v);
        public bool GetBool(GameAttrKey key) => GetBool(GameAttrKeyMap.Hash[(int)key]);
        public void SetBool(GameAttrKey key, bool v) => SetBool(GameAttrKeyMap.Hash[(int)key], v);
    }
}
