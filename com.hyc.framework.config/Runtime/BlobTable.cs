using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace HYC.Framework.Config
{
    /// <summary>
    /// Generic, Blob-backed config table for one row type <typeparamref name="TRow"/>.
    /// Generated Cfg structs are blittable; the pipeline bakes a
    /// <see cref="BlobAssetReference{T}"/> whose root holds an array of rows plus a
    /// <see cref="NativeHashMap{long,int}"/> id→index index. Runtime lookups are
    /// allocation-free.
    /// </summary>
    public unsafe struct ConfigBlobTable<TRow> : System.IDisposable
        where TRow : unmanaged
    {
        private BlobAssetReference<BlobRoot<TRow>> _ref;
        private NativeHashMap<long, int> _index;
        private NativeHashMap<long, int> _secondary;

        public bool IsValid => _ref.IsCreated;

        public static ConfigBlobTable<TRow> Build(IEnumerable<TRow> rows, IEnumerable<KeyValuePair<long, int>> explicitIndex = null)
        {
            var alloc = new NativeArray<TRow>(0, Allocator.Temp);
            var list = new List<TRow>();
            foreach (var r in rows) list.Add(r);
            alloc.Dispose();

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BlobRoot<TRow>>();
            var arr = builder.Allocate(ref root.Rows, list.Count);
            for (int i = 0; i < list.Count; i++) arr[i] = list[i];
            var asset = builder.CreateBlobAssetReference<BlobRoot<TRow>>(Allocator.Persistent);
            builder.Dispose();

            var index = new NativeHashMap<long, int>(list.Count, Allocator.Persistent);
            for (int i = 0; i < list.Count; i++) index.TryAdd(i, i);
            if (explicitIndex != null)
            {
                foreach (var kv in explicitIndex) index[kv.Key] = kv.Value;
            }
            var secondary = new NativeHashMap<long, int>(0, Allocator.Persistent);
            return new ConfigBlobTable<TRow> { _ref = asset, _index = index, _secondary = secondary };
        }

        /// <summary>
        /// 从已构建/已读取的 BlobAssetReference 构造表（不重建 Blob，仅重建索引）。
        /// 运行时从 .blob 文件 TryRead 后使用。
        /// </summary>
        /// <param name="blobRef">已读取的 Blob 引用（根为 <see cref="BlobRoot{T}"/>）。</param>
        /// <param name="idSelector">从行提取 ID 的函数；为 null 时按行序 0..n-1 建索引。</param>
        public static ConfigBlobTable<TRow> FromBlob(BlobAssetReference<BlobRoot<TRow>> blobRef,
            System.Func<TRow, long> idSelector = null,
            IEnumerable<KeyValuePair<long, int>> explicitIndex = null)
        {
            var count = blobRef.IsCreated ? blobRef.Value.Rows.Length : 0;
            var index = new NativeHashMap<long, int>(count, Allocator.Persistent);
            for (var i = 0; i < count; i++)
            {
                var key = idSelector != null ? idSelector(blobRef.Value.Rows[i]) : i;
                if (key != 0)
                    index.TryAdd(key, i);
            }
            if (explicitIndex != null)
            {
                foreach (var kv in explicitIndex) index[kv.Key] = kv.Value;
            }
            var secondary = new NativeHashMap<long, int>(0, Allocator.Persistent);
            return new ConfigBlobTable<TRow> { _ref = blobRef, _index = index, _secondary = secondary };
        }

        public int Count => _ref.IsCreated ? _ref.Value.Rows.Length : 0;

        public bool TryGet(long id, out TRow value)
        {
            value = default;
            if (!_ref.IsCreated) return false;
            int idx;
            if (_index.TryGetValue(id, out idx) && idx >= 0 && idx < _ref.Value.Rows.Length)
            {
                value = _ref.Value.Rows[idx];
                return true;
            }
            return false;
        }

        public TRow Get(long id)
        {
            TryGet(id, out TRow value);
            return value;
        }

        public BlobArray<TRow> Rows => _ref.IsCreated ? _ref.Value.Rows : default;

        public void Dispose()
        {
            if (_ref.IsCreated) _ref.Dispose();
            if (_index.IsCreated) _index.Dispose();
            if (_secondary.IsCreated) _secondary.Dispose();
        }
    }

    public struct BlobRoot<TRow> where TRow : unmanaged
    {
        public BlobArray<TRow> Rows;
    }
}