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
        // 子树运行态缓存: 按 (实体, 子树ID) 持久化跨帧状态(SubTree 节点复用, 使 Wait/CooldownGate/Until* 等跨帧延续)
        private static readonly Dictionary<(Entity, long), BTRunState> _subStates = new Dictionary<(Entity, long), BTRunState>();
        // 节点级跨帧执行态: 按 (实体, 树ID) 持久化, 索引 = 节点在 Blob 数组的位置
        // 对应 NodeCanvas 每个节点实例的 Status + 装饰器字段 + outConnections.Reset()
        private static readonly Dictionary<(Entity, long), NativeArray<BTNodeRuntimeState>> _nodeStates = new Dictionary<(Entity, long), NativeArray<BTNodeRuntimeState>>();
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

        // ---- 子树运行态缓存(按 实体 + 子树ID 持久化跨帧状态) ----
        /// <summary>获取某实体某子树的持久化运行态(跨帧)。不存在返回 false。</summary>
        public static bool TryGetSubTreeState(Entity entity, long subTreeId, out BTRunState state)
            => _subStates.TryGetValue((entity, subTreeId), out state);

        /// <summary>写入某实体某子树的运行态(BTInterpreter 每帧 Tick 子树后调用)。</summary>
        public static void SetSubTreeState(Entity entity, long subTreeId, BTRunState state)
            => _subStates[(entity, subTreeId)] = state;

        /// <summary>清除某实体的全部子树运行态(树完成或实体销毁时调用, 防止状态泄漏)。</summary>
        public static void ClearSubTreeStates(Entity entity)
        {
            var toRemove = new List<(Entity, long)>();
            foreach (var key in _subStates.Keys)
                if (key.Item1 == entity) toRemove.Add(key);
            foreach (var key in toRemove) _subStates.Remove(key);
        }

        // ---- 节点级跨帧执行态(对应 NodeCanvas 节点实例 Status + 装饰器字段) ----

        /// <summary>获取/创建某实体某树的 per-node 执行态数组(长度 = 节点数, 零初始化)。</summary>
        public static NativeArray<BTNodeRuntimeState> GetNodeStates(Entity entity, long treeId, int nodeCount)
        {
            var key = (entity, treeId);
            if (_nodeStates.TryGetValue(key, out var arr)) return arr;
            arr = new NativeArray<BTNodeRuntimeState>(nodeCount, Allocator.Persistent);
            _nodeStates[key] = arr;
            return arr;
        }

        /// <summary>清零某节点及其全部子孙的执行态(对应 NodeCanvas outConnections.Reset())。</summary>
        public static void ResetNode(Entity entity, long treeId, BTRootBlob* tree, int nodeIndex)
        {
            if (!_nodeStates.TryGetValue((entity, treeId), out var arr)) return;
            if (nodeIndex < 0 || nodeIndex >= arr.Length) return;

            // G1: 中断清理钩子(对应 NodeCanvas ActionTask.OnStop)
            // 若该自定义节点仍处于 Running 就被复位, 回调其 OnStop(true) 释放占用资源
            var st = arr[nodeIndex];
            if (st.Status == BTNodeState.Running)
            {
                var nd = tree->Nodes[nodeIndex];
                if (nd.Type == BTNodeType.GameCustom && nd.LongCount > 0)
                {
                    var longs = (long*)tree->Longs.GetUnsafePtr();
                    var custom = BTNodeRuntimeRegistry.Find(longs[nd.LongStart]);
                    custom?.OnStop(true);
                }
            }

            arr[nodeIndex] = default;
            var node = tree->Nodes[nodeIndex];
            for (int i = 0; i < node.ChildCount; i++)
            {
                int c = tree->ChildNodes[node.ChildStart + i];
                ResetNode(entity, treeId, tree, c);
            }
        }

        /// <summary>清除某实体的全部 per-node 执行态(树完成或实体销毁时调用, 防状态泄漏)。</summary>
        public static void ClearNodeStates(Entity entity)
        {
            var toRemove = new List<(Entity, long)>();
            foreach (var key in _nodeStates.Keys)
                if (key.Item1.Equals(entity)) toRemove.Add(key);
            foreach (var key in toRemove) { _nodeStates[key].Dispose(); _nodeStates.Remove(key); }

            // 同时释放该实体的全部守卫锁, 防残留
            var guardKeys = new List<(Entity, ulong)>();
            foreach (var key in _guards.Keys)
                if (key.Item1.Equals(entity)) guardKeys.Add(key);
            foreach (var key in guardKeys) _guards.Remove(key);
        }

        // ---- 跨分支守卫(Guard 节点: 同一实体同一 token 互斥) ----
        private static readonly Dictionary<(Entity, ulong), int> _guards = new Dictionary<(Entity, ulong), int>();

        /// <summary>返回持有该 token 的节点索引; -1 表示无人持有。</summary>
        public static int GuardHolder(Entity entity, ulong token)
            => _guards.TryGetValue((entity, token), out var holder) ? holder : -1;

        /// <summary>占用 token(记录持有节点索引)。</summary>
        public static void SetGuard(Entity entity, ulong token, int nodeIndex)
            => _guards[(entity, token)] = nodeIndex;

        /// <summary>仅当持有者为指定节点时释放(避免误删他人锁)。</summary>
        public static void ClearGuard(Entity entity, ulong token, int nodeIndex)
        {
            if (_guards.TryGetValue((entity, token), out var holder) && holder == nodeIndex)
                _guards.Remove((entity, token));
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
            _subStates.Clear();
            foreach (var kv in _nodeStates) kv.Value.Dispose();
            _nodeStates.Clear();
            _initialized = false;
        }
    }
}
