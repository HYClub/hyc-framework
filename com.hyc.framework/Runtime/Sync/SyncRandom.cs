namespace HYC.Framework.Sync
{
    /// <summary>
    /// 确定性随机数（帧同步纪律工具）：纯整数位运算，同 seed 全平台逐位一致。
    /// 帧同步逻辑内禁止 UnityEngine.Random；按帧演化用 <see cref="ForTick"/>，
    /// 跨帧连续演化用局部 <see cref="SyncRandom"/> 变量。
    /// </summary>
    public struct SyncRandom
    {
        private ulong _state;

        public SyncRandom(uint seed)
        {
            _state = seed;
        }

        /// <summary>由 (tick, 比赛种子) 派生本帧随机源：同帧两端必得同值。</summary>
        public static SyncRandom ForTick(int tick, uint matchSeed)
        {
            return new SyncRandom(MixSeed((uint)tick, matchSeed));
        }

        private static uint MixSeed(uint tick, uint matchSeed)
        {
            ulong h = (ulong)matchSeed * 0x9E3779B97F4A7C15UL + ((ulong)tick << 32);
            h ^= h >> 33;
            h *= 0xFF51AFD7ED558CCDUL;
            h ^= h >> 29;
            return (uint)h;
        }

        /// <summary>splitmix64 下一步：32 位无符号整数。</summary>
        public uint NextUInt()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (uint)(z ^ (z >> 31));
        }

        /// <summary>[0, int.MaxValue)。</summary>
        public int NextInt() => (int)(NextUInt() & 0x7FFFFFFF);

        /// <summary>[0, 1)。</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>[minInclusive, maxExclusive)。maxExclusive &lt;= minInclusive 时返回 minInclusive。</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }
    }
}
