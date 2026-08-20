// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTBlackboard.cs
// 说明: 运行时黑板 - 类型化 key-value 存储
//       实例级数据, 与 Blob 定义分离(定义只读, 实例可写)
// ============================================================

using Unity.Collections;
using Unity.Mathematics;

namespace HYC.Framework.BT
{
    /// <summary>
    /// 运行时黑板实例。每个运行中的树绑定一个。
    /// 键用哈希(与 Blob 定义中的黑板键哈希一致), 类型化数组存储。
    /// </summary>
    public unsafe struct BTBlackboardRuntime : System.IDisposable
    {
        private NativeHashMap<ulong, int> _intMap;
        private NativeHashMap<ulong, int> _floatMap;
        private NativeHashMap<ulong, int> _longMap;
        private NativeHashMap<ulong, int> _stringMap;
        private NativeHashMap<ulong, int> _boolMap;

        private NativeList<int> _ints;
        private NativeList<float> _floats;
        private NativeList<long> _longs;
        private NativeList<FixedString128Bytes> _strings;
        private NativeList<bool> _bools;

        public bool IsCreated => _intMap.IsCreated;

        public static BTBlackboardRuntime Create(int capacity = 8)
        {
            return new BTBlackboardRuntime
            {
                _intMap = new NativeHashMap<ulong, int>(capacity, Allocator.Persistent),
                _floatMap = new NativeHashMap<ulong, int>(capacity, Allocator.Persistent),
                _longMap = new NativeHashMap<ulong, int>(capacity, Allocator.Persistent),
                _stringMap = new NativeHashMap<ulong, int>(capacity, Allocator.Persistent),
                _boolMap = new NativeHashMap<ulong, int>(capacity, Allocator.Persistent),
                _ints = new NativeList<int>(capacity, Allocator.Persistent),
                _floats = new NativeList<float>(capacity, Allocator.Persistent),
                _longs = new NativeList<long>(capacity, Allocator.Persistent),
                _strings = new NativeList<FixedString128Bytes>(capacity, Allocator.Persistent),
                _bools = new NativeList<bool>(capacity, Allocator.Persistent),
            };
        }

        public void Dispose()
        {
            if (!IsCreated) return;
            _intMap.Dispose(); _floatMap.Dispose(); _longMap.Dispose(); _stringMap.Dispose(); _boolMap.Dispose();
            _ints.Dispose(); _floats.Dispose(); _longs.Dispose(); _strings.Dispose(); _bools.Dispose();
        }

        // ---- 读写 ----

        public void SetInt(ulong key, int value)
        {
            if (_intMap.TryGetValue(key, out int idx)) { _ints[idx] = value; return; }
            _ints.Add(value);
            _intMap.TryAdd(key, _ints.Length - 1);
        }

        public int GetInt(ulong key, int def = 0)
            => _intMap.TryGetValue(key, out int idx) ? _ints[idx] : def;

        public void SetFloat(ulong key, float value)
        {
            if (_floatMap.TryGetValue(key, out int idx)) { _floats[idx] = value; return; }
            _floats.Add(value);
            _floatMap.TryAdd(key, _floats.Length - 1);
        }

        public float GetFloat(ulong key, float def = 0f)
            => _floatMap.TryGetValue(key, out int idx) ? _floats[idx] : def;

        public void SetLong(ulong key, long value)
        {
            if (_longMap.TryGetValue(key, out int idx)) { _longs[idx] = value; return; }
            _longs.Add(value);
            _longMap.TryAdd(key, _longs.Length - 1);
        }

        public long GetLong(ulong key, long def = 0)
            => _longMap.TryGetValue(key, out int idx) ? _longs[idx] : def;

        public void SetBool(ulong key, bool value)
        {
            if (_boolMap.TryGetValue(key, out int idx)) { _bools[idx] = value; return; }
            _bools.Add(value);
            _boolMap.TryAdd(key, _bools.Length - 1);
        }

        public bool GetBool(ulong key, bool def = false)
            => _boolMap.TryGetValue(key, out int idx) ? _bools[idx] : def;

        public void SetString(ulong key, FixedString128Bytes value)
        {
            if (_stringMap.TryGetValue(key, out int idx)) { _strings[idx] = value; return; }
            _strings.Add(value);
            _stringMap.TryAdd(key, _strings.Length - 1);
        }

        public FixedString128Bytes GetString(ulong key, FixedString128Bytes def = default)
            => _stringMap.TryGetValue(key, out int idx) ? _strings[idx] : def;

        /// <summary>键名 → 64 位哈希(FNV-1a)。</summary>
        public static ulong HashKey(FixedString128Bytes key)
        {
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
