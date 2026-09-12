// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTEventBus.cs
// 说明: 事件总线(G3) - 对应 NodeCanvas 的 EventRouter / SendEvent / CheckEvent
//
//       设计: 事件用 key 哈希(与黑板同一套 FNV-1a 命名)表示, 分"实体事件"与"全局事件"。
//       查询时实体事件优先, 未命中再查全局(类似 NodeCanvas 的黑板上溯)。
//       事件被消费(Consume)后移除; 未被消费则保留到下次 —— 与 NodeCanvas 行为一致。
// ============================================================

using System.Collections.Generic;
using Unity.Entities;

namespace HYC.Framework.BT
{
    public static class BTEventBus
    {
        private static readonly Dictionary<Entity, HashSet<ulong>> _entityEvents = new Dictionary<Entity, HashSet<ulong>>();
        private static readonly HashSet<ulong> _globalEvents = new HashSet<ulong>();

        /// <summary>向某实体抛事件(树叶/游戏层调用)。</summary>
        public static void Raise(Entity entity, ulong eventKey)
        {
            if (!_entityEvents.TryGetValue(entity, out var set))
            {
                set = new HashSet<ulong>();
                _entityEvents[entity] = set;
            }
            set.Add(eventKey);
        }

        /// <summary>抛全局事件(所有实体可见)。</summary>
        public static void RaiseGlobal(ulong eventKey) => _globalEvents.Add(eventKey);

        /// <summary>事件是否已抛出(实体优先, 再查全局)。</summary>
        public static bool IsRaised(Entity entity, ulong eventKey)
        {
            if (_entityEvents.TryGetValue(entity, out var set) && set.Contains(eventKey)) return true;
            return _globalEvents.Contains(eventKey);
        }

        /// <summary>消费事件(命中则移除并返回 true)。</summary>
        public static bool Consume(Entity entity, ulong eventKey)
        {
            if (_entityEvents.TryGetValue(entity, out var set) && set.Remove(eventKey)) return true;
            return _globalEvents.Remove(eventKey);
        }

        /// <summary>清除某实体的全部事件(实体销毁时调用)。</summary>
        public static void Clear(Entity entity) => _entityEvents.Remove(entity);

        /// <summary>清除全部事件(世界销毁/重载时调用)。</summary>
        public static void ClearAll()
        {
            _entityEvents.Clear();
            _globalEvents.Clear();
        }
    }
}
