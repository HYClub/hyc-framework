using System;
using System.Collections.Generic;

namespace HYC.Framework.Config
{
    /// <summary>
    /// Registry manager that holds all loaded config tables. Every generated
    /// table registers itself here during boot (from a
    /// <c>[UpdateInGroup(UpdateGroup_A1)]</c> system). Read access is
    /// world-independent so play/produce and UI layers can query config tables
    /// without an ECS world handle.
    /// </summary>
    public static class ConfigManager
    {
        private static readonly Dictionary<Type, object> _tables = new Dictionary<Type, object>();
        private static readonly Dictionary<(Type table, long id), object> _objectCache = new Dictionary<(Type, long), object>();

        /// <summary>Registers a <see cref="ConfigBlobTable{T}"/> under its row type.</summary>
        public static void Register<TRow>(ConfigBlobTable<TRow> table) where TRow : unmanaged
        {
            _tables[typeof(TRow)] = table;
            _objectCache.Clear();
        }

        public static bool Has<TRow>() where TRow : unmanaged => _tables.ContainsKey(typeof(TRow));

        public static IReadOnlyList<Type> AllTypes
        {
            get { var list = new List<Type>(_tables.Keys); return list.AsReadOnly(); }
        }

        public static bool TryGet<TRow>(long id, out TRow row) where TRow : unmanaged
        {
            if (_tables.TryGetValue(typeof(TRow), out var obj) && obj is ConfigBlobTable<TRow> table)
            {
                return table.TryGet(id, out row);
            }
            row = default;
            return false;
        }

        public static TRow Get<TRow>(long id) where TRow : unmanaged
        {
            TryGet(id, out TRow row);
            return row;
        }

        public static bool Contains<TRow>(long id) where TRow : unmanaged
            => TryGet(id, out TRow _);

        /// <summary>获取某张表的全部行（BlobArray 引用）。表未注册时返回空。</summary>
        public static Unity.Entities.BlobArray<TRow> GetAllRows<TRow>() where TRow : unmanaged
        {
            if (_tables.TryGetValue(typeof(TRow), out var obj) && obj is ConfigBlobTable<TRow> table)
                return table.Rows;
            return default;
        }

        /// <summary>
        /// Materializes and caches any managed wrapper built from a row.
        /// Overrides must only live in an Editor/`#if` path; the runtime
        /// default simply returns a boxed row.
        /// </summary>
        public static T Materialize<T, TRow>(long id, Func<TRow, T> factory) where TRow : unmanaged
        {
            var key = (typeof(T), id);
            if (_objectCache.TryGetValue(key, out var hit)) return (T)hit;
            TryGet(id, out TRow row);
            var value = factory(row);
            _objectCache[key] = value;
            return value;
        }

        public static void Clear()
        {
            foreach (var v in _tables.Values)
            {
                if (v is IDisposable disposable) disposable.Dispose();
            }
            _tables.Clear();
            _objectCache.Clear();
        }
    }
}