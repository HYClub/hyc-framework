using Unity.Entities;

namespace HYC.Framework.UI
{
    /// <summary>
    /// Default component system group for UI windows/parts that are not
    /// assigned their own <c>[UpdateInGroup]</c>. Runs late in simulation so UI
    /// reads settled game state.
    /// </summary>
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public partial class UIGroup : ComponentSystemGroup
    {
    }
}