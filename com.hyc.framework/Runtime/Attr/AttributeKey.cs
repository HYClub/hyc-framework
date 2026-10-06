namespace HYC.Framework.Attr
{
    /// <summary>
    /// 属性 key 的哈希。FNV-1a 算法，与 BT 的 BTBlackboardRuntime.HashKey 完全一致
    /// （独立实现，不跨模块引用 BT）。用 name 哈希而非枚举序号，保证属性被删/重排时运行时袋数据不会错位。
    /// </summary>
    public static class AttributeKey
    {
        public static ulong Hash(string key)
        {
            if (key == null) return 14695981039346656037UL;
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < key.Length; i++)
            {
                hash ^= (byte)key[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
