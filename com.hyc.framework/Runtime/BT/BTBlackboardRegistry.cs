// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTBlackboardRegistry.cs
// 说明: 黑板注册表(G2) - 解决"跨实体共享数据 / 全局黑板"需求
//       对应 NodeCanvas 的 GlobalBlackboard + parentBlackboard 继承
//
//       设计: BTBlackboardRuntime 内部是 NativeHashMap 句柄(结构体拷贝即共享底层数据),
//       因此"共享黑板"= 多处持有同一份结构体拷贝。本注册表负责集中登记与生命周期。
//       实体私有数据仍放实体黑板; 需要跨实体共享的放全局黑板(ctx.SharedBlackboard)。
// ============================================================

using System.Collections.Generic;
using Unity.Entities;

namespace HYC.Framework.BT
{
    public static class BTBlackboardRegistry
    {
        // 全局共享黑板(整个游戏一份或多份, 按 key 区分)
        private static readonly Dictionary<ulong, BTBlackboardRuntime> _globals = new Dictionary<ulong, BTBlackboardRuntime>();
        // 实体私有黑板(由游戏层在实体创建时登记, 避免每帧重新构造)
        private static readonly Dictionary<Entity, BTBlackboardRuntime> _perEntity = new Dictionary<Entity, BTBlackboardRuntime>();

        /// <summary>登记/替换一份全局共享黑板。key=0 表示默认全局黑板。</summary>
        public static void SetGlobal(ulong key, BTBlackboardRuntime bb)
        {
            if (_globals.TryGetValue(key, out var old) && old.IsCreated && !old.Equals(bb)) old.Dispose();
            _globals[key] = bb;
        }

        /// <summary>取全局共享黑板。未登记返回 false。</summary>
        public static bool TryGetGlobal(ulong key, out BTBlackboardRuntime bb)
            => _globals.TryGetValue(key, out bb);

        /// <summary>取默认全局黑板(key=0)。</summary>
        public static bool TryGetGlobal(out BTBlackboardRuntime bb)
            => TryGetGlobal(0, out bb);

        /// <summary>登记某实体的私有黑板。</summary>
        public static void SetForEntity(Entity entity, BTBlackboardRuntime bb)
        {
            if (_perEntity.TryGetValue(entity, out var old) && old.IsCreated) old.Dispose();
            _perEntity[entity] = bb;
        }

        /// <summary>取某实体的私有黑板。未登记返回 false。</summary>
        public static bool TryGetForEntity(Entity entity, out BTBlackboardRuntime bb)
            => _perEntity.TryGetValue(entity, out bb);

        /// <summary>释放某实体的私有黑板(实体销毁时调用)。</summary>
        public static void Clear(Entity entity)
        {
            if (_perEntity.TryGetValue(entity, out var bb))
            {
                if (bb.IsCreated) bb.Dispose();
                _perEntity.Remove(entity);
            }
        }

        /// <summary>释放全部(世界销毁/重载时调用)。</summary>
        public static void DisposeAll()
        {
            foreach (var kv in _globals)
                if (kv.Value.IsCreated) kv.Value.Dispose();
            _globals.Clear();
            foreach (var kv in _perEntity)
                if (kv.Value.IsCreated) kv.Value.Dispose();
            _perEntity.Clear();
        }
    }
}
