using System;
using System.Collections.Generic;

namespace HYC.Framework.Config
{
    /// <summary>
    /// 运行期配置表注册表：持有一切已加载的配置表。每张生成的表在引导期
    /// （<c>[UpdateInGroup(UpdateGroup_A1)]</c> 的 system 里）把自己 Register 进来。
    /// 读访问世界无关，玩法层与 UI 层无需 ECS World 句柄即可查表。
    ///
    /// 命名约定：宿主日常查询请用配置管线生成的静态门面 <c>{ns}.ConfigManager.{Type}.GetByID(id)</c>
    /// （按 id 查询；遍历全表用 <c>{Type}.Count</c> + <c>{Type}.GetByIndex(i)</c>）；
    /// 本类型是门面背后的低层注册表，一般只有生成代码（XxxBlobSystem 的 Register）才会直接调。
    /// </summary>
    public static class ConfigRegistry
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

        /// <summary>
        /// 按 id 取一行，返回指向 Blob 内该行的 <c>ref</c>（零拷贝，不复制行结构体）。
        /// 未命中抛 <see cref="KeyNotFoundException"/>——需区分"没这条"时先调
        /// <see cref="Contains{TRow}(long)"/>。返回的 ref 在表注册期间（至 <see cref="Clear"/> 前）有效，
        /// 直接指向 Blob 资产内存（由 <see cref="ConfigBlobTable{TRow}"/> 持有生命周期）。
        ///
        /// 刻意返回<b>可写 ref</b> 而非 <c>ref readonly</c>：C# 禁止把 ref-readonly 返回值绑到
        /// <c>ref var</c> 局部变量（CS8329），而 Entities 分析器又要求 blob 行的局部变量必须是
        /// 非只读 ref（EA0001，<c>ref readonly var</c> 同样被拦）——两端夹逼下可写 ref 是唯一合法形态。
        /// 只读靠纪律保证：blob 是不可变数据，调用方不得通过该 ref 写行。
        /// </summary>
        public static ref TRow GetRef<TRow>(long id) where TRow : unmanaged
        {
            if (_tables.TryGetValue(typeof(TRow), out var obj) && obj is ConfigBlobTable<TRow> matched && matched.TryGetIndex(id, out var idx))
                return ref RowRef(matched, idx);
            throw new KeyNotFoundException($"[ConfigRegistry] 未找到 {typeof(TRow).Name} 的行 id={id}");
        }

        /// <summary>
        /// 按<b>行序</b>取一行，返回指向 Blob 内该行的 <c>ref</c>（零拷贝）。
        /// 表未注册或越界抛 <see cref="KeyNotFoundException"/>——与 <see cref="GetRef{TRow}(long)"/>
        /// 失败语义一致：配置数据错误应响亮失败。门面 <c>{ns}.ConfigManager.{Type}.GetByIndex</c> 的转调目标。
        /// 可写 ref 的理由同 <see cref="GetRef{TRow}(long)"/>（CS8329 与 EA0001 两端夹逼）。
        /// </summary>
        public static ref TRow GetRowRefAt<TRow>(int index) where TRow : unmanaged
        {
            if (_tables.TryGetValue(typeof(TRow), out var obj) && obj is ConfigBlobTable<TRow> matched
                && (uint)index < (uint)matched.Count)
                return ref RowRef(matched, index);
            throw new KeyNotFoundException($"[ConfigRegistry] 未找到 {typeof(TRow).Name} 的行 index={index}");
        }

        /// <summary>
        /// 按<b>行序</b>取该行的可写 ref（零拷贝，不拷行结构体）。越界行为未定义——调用方须先做边界检查
        /// （<see cref="GetRef{TRow}(long)"/> / <see cref="GetRowRefAt{TRow}(int)"/> 已代劳）。
        ///
        /// 为什么助手收在注册表内且<b>按值传表</b>：跨类型 <c>ref</c> 形参助手（原
        /// <c>ConfigBlobTable&lt;TRow&gt;.GetRowRef(ref table, index)</c>）无解——ref 形参派生的返回 ref
        /// 只能绑 ref local（CS8347 + CS8168「not a ref local」），而字典里取出的表连方法级局部都不是
        /// ref local，拷局部同样被拦；改为按值传表 + 从 <c>BlobAssetReference.Value</c> 指针派生链取行，
        /// 返回 ref 的安全性系于指针而非形参存储，与实参形态无关（<c>Value</c> 与 BlobArray 索引器均为
        /// <c>UnsafeUtility.AsRef/ArrayElementAsRef(void*)</c> 实现，Blobs.cs 源码实证）。
        /// 表按值传入即一次指针级拷贝，无害（偏移自相对 + 同一 safety handle，随用随弃）。
        /// </summary>
        private static ref TRow RowRef<TRow>(ConfigBlobTable<TRow> table, int index) where TRow : unmanaged
        {
            if (!table.IsValid)
                throw new InvalidOperationException($"[ConfigRegistry] 表未加载（IsValid=false），不能取行 ref");
            Unity.Entities.BlobAssetReference<BlobRoot<TRow>> blob = table.BlobRef;
            return ref blob.Value.Rows[index];
        }

        public static bool Contains<TRow>(long id) where TRow : unmanaged
        {
            if (_tables.TryGetValue(typeof(TRow), out var obj) && obj is ConfigBlobTable<TRow> table)
                return table.TryGetIndex(id, out _);
            return false;
        }

        /// <summary>获取某张表的全部行（BlobArray 引用）。表未注册时返回空。</summary>
        public static Unity.Entities.BlobArray<TRow> GetAllRows<TRow>() where TRow : unmanaged
        {
            if (_tables.TryGetValue(typeof(TRow), out var obj) && obj is ConfigBlobTable<TRow> table)
                return table.Rows;
            return default;
        }

        /// <summary>
        /// 按<b>行序</b>取一行（值拷贝）。
        ///
        /// 为什么不提供「整表拷进 List 再遍历」的 API：生成的 Cfg 结构里含 <c>BlobString</c>，
        /// Unity.Entities 分析器会把这类类型整体视为「可能位于 blob 存储」，
        /// 于是任何容器索引访问（<c>list[i]</c>）都会报 EA0001；
        /// 而 <c>List&lt;T&gt;</c> 的索引器并非 ref 返回，按提示写 <c>ref var r = ref list[i]</c> 无法编译。
        /// 逐行 out 取值只产生局部变量，不受该规则影响，调用方可安全读标量字段。
        /// </summary>
        public static bool TryGetRowAt<TRow>(int index, out TRow row) where TRow : unmanaged
        {
            row = default;

            if (!_tables.TryGetValue(typeof(TRow), out var obj))
                return false;
            if (!(obj is ConfigBlobTable<TRow> table))
                return false;

            return table.TryGetRowAt(index, out row);
        }

        /// <summary>某张表已加载的行数；表未注册时返回 0。</summary>
        public static int GetRowCount<TRow>() where TRow : unmanaged
        {
            if (_tables.TryGetValue(typeof(TRow), out var obj) && obj is ConfigBlobTable<TRow> table)
                return table.Count;
            return 0;
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