using System;
using Unity.Collections;
using Unity.Entities;

namespace HYC.Framework.Runtime
{
    /// <summary>
    /// Boot-mode attributes used to gate which systems are installed per mode.
    /// Mirrors the source <c>SimulationRoam/Fly</c> marking, generalised so any
    /// custom bootstrap mode can include/exclude whole system families.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class FrameworkModeOnlyAttribute : Attribute
    {
        public int Mode { get; }
        public FrameworkModeOnlyAttribute(int mode) => Mode = mode;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class FrameworkModeAttribute : Attribute
    {
        public int Mode { get; }
        public FrameworkModeAttribute(int mode) => Mode = mode;
    }

    /// <summary>Marker components for each supported boot mode.</summary>
    public struct FrameworkMode_Live : IComponentData { }
    public struct FrameworkMode_Sim : IComponentData { }

    /// <summary>
    /// Source-style ECS bootstrap. Creates the game World, installs systems
    /// filtered by the active <see cref="BootMode"/>, appends the world to the
    /// player loop, and stamps a mode singleton so systems can query it.
    ///
    /// This is the HYC counterpart of the source <c>GameBootstrap : ICustomBootstrap</c>.
    /// Like <see cref="FrameworkBootstrap"/> it is a plain utility — it does NOT
    /// implement <c>ICustomBootstrap</c>, so it is never auto-discovered and can
    /// never conflict with a game's own bootstrap. Games that want this behaviour
    /// on play call it from their own <c>ICustomBootstrap.Initialize</c>.
    /// The editor toolbar branching is replaced by a plain <see cref="BootMode"/>
    /// switch (see <see cref="HYC.Framework.Dots.SimulationBootstrap"/>); the
    /// block of <c>FrameworkModeAttribute</c> / <c>FrameworkModeOnlyAttribute</c>
    /// on systems controls inclusion per mode. Games that need bare installer-based
    /// composition can keep using <see cref="FrameworkWorld"/> instead.
    /// </summary>
    public class FrameworkModeBootstrap
    {
        /// <summary>1 = live, 2 = simulated. See <see cref="HYC.Framework.Dots.SimulationBootstrap"/>.</summary>
        public static int BootMode = HYC.Framework.Dots.SimulationBootstrap.Live;

        public bool Initialize(string defaultWorldName)
        {
            // 「仅场景运行」闸门：跳过整套引导，让 Entities 退回自动建默认世界。
            // 由编辑器工具栏按钮置位（见 PlaySceneToolbarButton），宿主无需改动。
            if (HYC.Framework.Runtime.BootstrapGate.ShouldSkip)
            {
                UnityEngine.Debug.Log("[HYC.Framework] Play Scene Only：跳过框架引导（宿主 bootstrap 返回 false）。");
                return false;
            }

            World.DefaultGameObjectInjectionWorld = new World(defaultWorldName, WorldFlags.Game);
            var world = World.DefaultGameObjectInjectionWorld;

            InstallSystems(world, BootMode);

#if !UNITY_DOTSRUNTIME
            Unity.Entities.ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
#endif

            return true;
        }

        /// <summary>
        /// 跨平台引导入口（推荐）：先完成 StreamingAssets 预取，再建 World 并安装系统。
        ///
        /// <para>PC / macOS / Linux / iOS 上 <c>PreloadAsync</c> 是 no-op，与 <see cref="Initialize"/> 等价；
        /// WebGL / Android 上必须走这里——否则建 World 时（<c>System.OnCreate</c>）读不到配置数据。</para>
        /// <para><b>游戏层全平台统一调这个方法即可，无需写任何平台分支。</b></para>
        /// </summary>
        public async System.Threading.Tasks.Task<bool> InitializeAsync(string defaultWorldName)
        {
            await HYC.Framework.IO.StreamingAssets.PreloadAsync();
            var ok = Initialize(defaultWorldName);
            if (ok)
                HYC.Framework.FrameworkLifecycle.EnsureBootDataLoaded();
            return ok;
        }

        /// <summary>
        /// Installs all default systems that are not excluded by the current mode,
        /// then stamps the matching mode singleton.
        /// </summary>
        private static void InstallSystems(World world, int mode)
        {
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default, false);

            using var systemIndexs = new NativeList<SystemTypeIndex>(Allocator.Temp);
            foreach (var system in systems)
            {
                if (system.FullName.StartsWith("Unity."))
                {
                    systemIndexs.Add(TypeManager.GetSystemTypeIndex(system));
                    continue;
                }

                var only = system.GetCustomAttributes(typeof(FrameworkModeOnlyAttribute), true);
                if (only.Length > 0)
                {
                    if (((FrameworkModeOnlyAttribute)only[0]).Mode == mode)
                        systemIndexs.Add(TypeManager.GetSystemTypeIndex(system));
                    continue;
                }

                var any = system.GetCustomAttributes(typeof(FrameworkModeAttribute), true);
                if (any.Length > 0)
                {
                    if (((FrameworkModeAttribute)any[0]).Mode == mode)
                        systemIndexs.Add(TypeManager.GetSystemTypeIndex(system));
                    continue;
                }

                systemIndexs.Add(TypeManager.GetSystemTypeIndex(system));
            }

            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systemIndexs);

            var e = world.EntityManager.CreateEntity();
            if (mode == HYC.Framework.Dots.SimulationBootstrap.Sim)
                world.EntityManager.AddComponent<FrameworkMode_Sim>(e);
            else
                world.EntityManager.AddComponent<FrameworkMode_Live>(e);
        }
    }
}
