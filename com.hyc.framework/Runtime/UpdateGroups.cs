using Unity.Entities;

namespace HYC.Framework.Dots
{
    /// <summary>
    /// Root ECS group for the whole framework update ladder.
    /// Everything the framework runs lives under here.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BeginSimulationEntityCommandBufferSystem))]
    public partial class UpdateGroup_Root : ComponentSystemGroup
    {
    }

    /// <summary>
    /// A0: Bootstrap phase. One-shot systems that install singletons,
    /// start pipelines and read command-line / boot config.
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_Root))]
    [UpdateBefore(typeof(UpdateGroup_A1))]
    public partial class UpdateGroup_A0 : ComponentSystemGroup
    {
    }

    /// <summary>
    /// A1: Data phase. Config loading, account/state data, statistics.
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_Root))]
    [UpdateAfter(typeof(UpdateGroup_A0))]
    [UpdateBefore(typeof(UpdateGroup_A2))]
    public partial class UpdateGroup_A1 : ComponentSystemGroup
    {
    }

    /// <summary>
    /// A2: Room / scene phase. Room enter, scene baking, environment systems.
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_Root))]
    [UpdateAfter(typeof(UpdateGroup_A1))]
    [UpdateBefore(typeof(UpdateGroup_A3))]
    public partial class UpdateGroup_A2 : ComponentSystemGroup
    {
    }

    /// <summary>
    /// A3: Gameplay phase. Simulation, skills, AI, combat — the bulk of a game.
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_Root))]
    [UpdateAfter(typeof(UpdateGroup_A2))]
    [UpdateBefore(typeof(UpdateGroup_B9))]
    public partial class UpdateGroup_A3 : ComponentSystemGroup
    {
    }

    /// <summary>
    /// B9: UI / presentation phase. UI updates, HUD refresh, localization refresh.
    /// Runs last so it reads the freshest game state.
    /// The former <c>[UpdateBefore(typeof(LateSimulationSystemGroup))]</c> was
    /// invalid (cross-level ordering — UpdateGroup_Root already lives inside
    /// SimulationSystemGroup, which always runs before LateSimulationSystemGroup)
    /// and made the Entities system sorter throw an NRE.
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_Root))]
    [UpdateAfter(typeof(UpdateGroup_A3))]
    public partial class UpdateGroup_B9 : ComponentSystemGroup
    {
    }
}
