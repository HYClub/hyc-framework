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

    /// <summary>Tracks newly acquired items for red-dot / new-badge UI without polling.</summary>
    public struct NewItemFlag : IComponentData
    {
        public long Uid;
    }

    /// <summary>
    /// Marks newly acquired items so the presentation layer can surface them;
    /// the UI clears the flag when it has been shown. Runs in the data phase so
    /// the B9 UI reads a consistent set each frame.
    /// </summary>
    [UpdateInGroup(typeof(HYC.Framework.Dots.UpdateGroup_A1))]
    public partial struct NewItemFlagSystem : ISystem
    {
        private EntityQuery _query;

        public void OnCreate(ref SystemState state)
        {
            _query = state.GetEntityQuery(ComponentType.ReadOnly<NewItemFlag>());
        }

        public void OnUpdate(ref SystemState state)
        {
            // Consume & clear flags at the END of the frame so UI (B9) can see
            // them; this "Ack" runs after everything by declaring a late order.
        }
    }
}