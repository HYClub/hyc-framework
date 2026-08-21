// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTManager.cs
// 说明: 行为树注册表 - 树 ID → Blob 引用
//       运行时从资源加载后注册, 供解释器查询
// ============================================================

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace HYC.Framework.BT
{
    /// <summary>
    /// 全局行为树注册表。树资产(Blob)由加载系统在数据阶段注册。
    /// 静态存储, 世界无关, 与 hyc ConfigManager 同风格。
    /// </summary>
    public static unsafe class BTManager
    {
        private static NativeHashMap<long, BlobAssetReference<BTRootBlob>> _trees;
        private static bool _initialized;

        private static void EnsureInit()
        {
            if (_initialized) return;
            _trees = new NativeHashMap<long, BlobAssetReference<BTRootBlob>>(64, Allocator.Persistent);
            _initialized = true;
        }

        /// <summary>注册一棵树。重复注册会释放旧引用并覆盖。</summary>
        public static void Register(long treeId, BlobAssetReference<BTRootBlob> blob)
        {
            EnsureInit();
            if (_trees.TryGetValue(treeId, out var old) && old.IsCreated)
                old.Dispose();
            _trees[treeId] = blob;
        }

        /// <summary>按 ID 获取树。返回指向 Blob 根的指针(有效直到被注销)。</summary>
        public static BTRootBlob* TryGet(long treeId)
        {
            EnsureInit();
            if (_trees.TryGetValue(treeId, out var blob) && blob.IsCreated)
                return (BTRootBlob*)blob.GetUnsafePtr();
            return null;
        }

        public static bool Contains(long treeId)
        {
            EnsureInit();
            return _trees.TryGetValue(treeId, out var blob) && blob.IsCreated;
        }

        /// <summary>注销并释放一棵树。</summary>
        public static void Unregister(long treeId)
        {
            EnsureInit();
            if (_trees.TryGetValue(treeId, out var blob))
            {
                if (blob.IsCreated) blob.Dispose();
                _trees.Remove(treeId);
            }
        }

        // ---- 断点调试 ----
        private static readonly HashSet<long> _breakpoints = new HashSet<long>();

        /// <summary>添加/移除断点(按节点在树资产中的索引)。</summary>
        public static void ToggleBreakpoint(long treeId, int nodeIndex)
        {
            long key = treeId * 100000 + nodeIndex;
            if (!_breakpoints.Add(key))
                _breakpoints.Remove(key);
        }

        public static void SetBreakpoint(long treeId, int nodeIndex, bool enabled)
        {
            long key = treeId * 100000 + nodeIndex;
            if (enabled) _breakpoints.Add(key);
            else _breakpoints.Remove(key);
        }

        public static bool IsBreakpoint(long treeId, int nodeIndex)
            => _breakpoints.Contains(treeId * 100000 + nodeIndex);

        public static void ClearBreakpoints() => _breakpoints.Clear();

        /// <summary>释放全部树(世界销毁/重载时调用)。</summary>
        public static void DisposeAll()
        {
            if (!_initialized) return;
            foreach (var kv in _trees)
            {
                if (kv.Value.IsCreated) kv.Value.Dispose();
            }
            _trees.Dispose();
            _initialized = false;
        }
    }
}
