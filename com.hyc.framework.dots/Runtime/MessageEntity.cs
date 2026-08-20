using Unity.Entities;

namespace HYC.Framework.Dots
{
    /// <summary>
    /// Marks an entity as a single-frame message. Message entities are created,
    /// read by one or more systems during the frame, then destroyed at the end
    /// of the same frame by <see cref="MessageExpirySystem"/>.
    ///
    /// Convention: add shared components carrying payload/context data on the
    /// same entity, plus <c>[Message]</c> to declare intent (fire-and-forget).
    /// Read via <c>SystemAPI.Query</c>.
    /// </summary>
    public struct Message : IComponentData
    {
    }

    /// <summary>Convenience tag to register the entity as deletable at frame end.</summary>
    public struct DestroyEndOfFrame : IComponentData
    {
    }

    /// <summary>
    /// Marker tag carried by single-frame message entities. The ECS equivalent
    /// of the source <c>com.qianking.game.MessageEntity</c>; combined with
    /// <see cref="Message"/> it tells <see cref="MessageClearSystem"/> to drop
    /// the whole entity at the end of the frame.
    /// </summary>
    public struct MessageEntity : IComponentData
    {
    }

    /// <summary>
    /// Payload for UI guide/event messages. Created on an entity tagged
    /// <see cref="MessageEntity"/> and consumed by guide systems within the
    /// frame, then cleared. Decoupled QK port of the source <c>EventMessage</c>.
    /// </summary>
    [System.Serializable]
    public struct EventMessage : IComponentData
    {
        /// <summary>Event/guide id.</summary>
        public int EventID;

        /// <summary>Whether the guide may be re-opened.</summary>
        public bool IsRepeated;
    }

    /// <summary>
    /// Destroys every entity tagged <see cref="DestroyEndOfFrame"/> once per
    /// frame, after all consumers (gameplay in A3, UI in B9) ran. Ordered last
    /// within <see cref="UpdateGroup_B9"/> — the original
    /// <c>[UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]</c> was
    /// invalid (that system lives in LateSimulationSystemGroup, which always
    /// runs after the whole UpdateGroup_B9) and made the Entities system sorter
    /// throw an NRE.
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_B9), OrderLast = true)]
    public partial struct MessageExpirySystem : ISystem
    {
        private EntityQuery _query;

        public void OnCreate(ref SystemState state)
        {
            _query = state.GetEntityQuery(ComponentType.ReadOnly<DestroyEndOfFrame>());
        }

        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.DestroyEntity(_query);
        }
    }

    /// <summary>
    /// Destroys every entity carrying <see cref="MessageEntity"/> at the end of
    /// the simulation group (OrderLast). Mirrors the source
    /// <c>MessageClearSystem</c> so transient messages never survive a frame.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct MessageClearSystem : ISystem
    {
        private EntityQuery _query;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MessageEntity>();
            _query = state.GetEntityQuery(ComponentType.ReadOnly<MessageEntity>());
        }

        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.DestroyEntity(_query);
        }
    }
}