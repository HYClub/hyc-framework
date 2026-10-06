namespace HYC.Framework.Attr
{
    /// <summary>
    /// 类型安全的属性读写契约（ulong key 版）。框架提供此契约与 ulong 版实现 <see cref="AttrBag"/>；
    /// 使用方 codegen 的枚举版 AttrBag 在内部把 AttrKey 经 AttrKeyMap 映射到哈希后转调 AttributeStore，
    /// 并额外暴露 <c>GetInt(AttrKey)</c> 这类枚举友好重载。
    /// 注意：接口分派是托管，Job/Burst 内请直接操作 DynamicBuffer&lt;AttributeValue&gt; + AttributeStore 的 ulong 重载。
    /// </summary>
    public interface IAttrBag
    {
        int GetInt(ulong key);
        void SetInt(ulong key, int v);
        float GetFloat(ulong key);
        void SetFloat(ulong key, float v);
        long GetLong(ulong key);
        void SetLong(ulong key, long v);
        bool GetBool(ulong key);
        void SetBool(ulong key, bool v);
    }
}
