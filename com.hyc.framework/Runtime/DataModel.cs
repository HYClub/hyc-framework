using System.Collections.Generic;
using Unity.Entities;

namespace HYC.Framework.Data
{
    /// <summary>An item instance in the player's data model (client-authoritative, non-network).</summary>
    public struct DataItem : IComponentData
    {
        public long Uid;
        public long CfgId;
        public int Count;
        public long Flags;      // bitmask of transient flags
    }

    /// <summary>Runtime value snapshot used by UI for displaying stats/attributes.</summary>
    public struct DataStatValue : IComponentData
    {
        public long Key;    // e.g. a PropertyID
        public float Base;
        public float Bonus;
    }

    /// <summary>Summary statistics for a player session, accumulated by gameplay systems.</summary>
    public struct DataStatistics : IComponentData
    {
        public ulong TotalGain;
        public ulong TotalSpend;
    }

    /// <summary>Marks a single item instance as "new" (for red-dot / new-badge UI).</summary>
    public struct NewItemFlag : IComponentData
    {
        public long Uid;
    }

    /// <summary>Buffer element used by the <see cref="NewItemFlagStore"/> singleton.</summary>
    public struct NewItemFlagBuffer : IBufferElementData
    {
        public long Uid;
    }

    /// <summary>Marker for the singleton entity that owns the new-item buffer.</summary>
    public struct NewItemFlagStore : IComponentData { }

    /// <summary>
    /// Collects <see cref="NewItemFlag"/> components (added by the game's
    /// item-grant systems) into a singleton <see cref="DynamicBuffer{T}"/> of
    /// <see cref="NewItemFlagBuffer"/> during the data phase (A1). The B9
    /// presentation layer reads that buffer to show red dots and calls
    /// <see cref="NewItemFlags.Ack"/> once a badge has been shown.
    /// </summary>
    [UpdateInGroup(typeof(HYC.Framework.Dots.UpdateGroup_A1))]
    public partial struct NewItemFlagSystem : ISystem
    {
        private EntityQuery _query;
        private Entity _store;

        public void OnCreate(ref SystemState state)
        {
            _query = state.GetEntityQuery(ComponentType.ReadOnly<NewItemFlag>());
            EnsureStore(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!state.EntityManager.Exists(_store)
                || !state.EntityManager.HasComponent<NewItemFlagStore>(_store))
            {
                EnsureStore(ref state);
            }

            var entities = _query.ToEntityArray(Unity.Collections.Allocator.Temp);
            if (entities.Length == 0) return;

            var buffer = state.EntityManager.GetBuffer<NewItemFlagBuffer>(_store);
            foreach (var e in entities)
            {
                var uid = state.EntityManager.GetComponentData<NewItemFlag>(e).Uid;
                bool exists = false;
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].Uid == uid) { exists = true; break; }
                }
                if (!exists) buffer.Add(new NewItemFlagBuffer { Uid = uid });
                state.EntityManager.RemoveComponent<NewItemFlag>(e);
            }
            entities.Dispose();
        }

        private void EnsureStore(ref SystemState state)
        {
            if (_store != Entity.Null
                && state.EntityManager.Exists(_store)
                && state.EntityManager.HasComponent<NewItemFlagStore>(_store))
            {
                return;
            }
            var e = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(e, new NewItemFlagStore());
            state.EntityManager.AddBuffer<NewItemFlagBuffer>(e);
            _store = e;
        }
    }

    /// <summary>
    /// Static helpers for the new-item (red-dot) buffer. Producers call
    /// <see cref="Mark"/>, the B9 UI reads <see cref="GetAll"/>/<see cref="Has"/>
    /// and calls <see cref="Ack"/> once a badge has been shown. Operates on the
    /// default world's <see cref="NewItemFlagStore"/> singleton.
    /// </summary>
    public static class NewItemFlags
    {
        public static void Mark(long uid)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            var em = world.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<NewItemFlagStore>());
            if (q.IsEmpty) return;
            var store = q.GetSingletonEntity();
            var buffer = em.GetBuffer<NewItemFlagBuffer>(store);
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].Uid == uid) return;
            buffer.Add(new NewItemFlagBuffer { Uid = uid });
        }

        public static void Ack(long uid)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            var em = world.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<NewItemFlagStore>());
            if (q.IsEmpty) return;
            var store = q.GetSingletonEntity();
            var buffer = em.GetBuffer<NewItemFlagBuffer>(store);
            for (int i = buffer.Length - 1; i >= 0; i--)
                if (buffer[i].Uid == uid) buffer.RemoveAt(i);
        }

        public static bool Has(long uid)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return false;
            var em = world.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<NewItemFlagStore>());
            if (q.IsEmpty) return false;
            var store = q.GetSingletonEntity();
            var buffer = em.GetBuffer<NewItemFlagBuffer>(store);
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].Uid == uid) return true;
            return false;
        }

        public static List<long> GetAll()
        {
            var result = new List<long>();
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return result;
            var em = world.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<NewItemFlagStore>());
            if (q.IsEmpty) return result;
            var store = q.GetSingletonEntity();
            var buffer = em.GetBuffer<NewItemFlagBuffer>(store);
            for (int i = 0; i < buffer.Length; i++)
                result.Add(buffer[i].Uid);
            return result;
        }
    }
}
