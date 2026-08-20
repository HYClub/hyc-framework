using System.Collections.Generic;
using Unity.Entities;

namespace HYC.Framework.Runtime
{
    /// <summary>
    /// Host that composes the framework world at startup.
    ///
    /// The game defines an <see cref="ICustomBootstrap"/> that instantiates
    /// this host, which then installs base systems (input, camera, UI,
    /// localization, settings), loads config tables, and enters a mode
    /// (live vs simulated) — mirroring the source client's bootstrap wiring but
    /// without any network dependency.
    /// </summary>
    public sealed class FrameworkWorld
    {
        private readonly World _world;
        private HYC.Framework.Dots.UpdateGroup_Root _rootGroup;
        public World World => _world;

        public FrameworkWorld(string worldName = "Framework World")
        {
            _world = new World(worldName);
        }

        /// <summary>Discovers any <see cref="IFrameworkInstaller"/> and runs them, then flips to Sim/Live mode.</summary>
        public void Install(IEnumerable<IFrameworkInstaller> installers, int mode)
        {
            _rootGroup = _world.GetOrCreateSystemManaged<HYC.Framework.Dots.UpdateGroup_Root>();

            if (installers != null)
            {
                foreach (var installer in installers)
                {
                    installer.Install(_world);
                }
            }

            HYC.Framework.Dots.SimulationBootstrap.Mode = mode;
        }

        public void Update()
        {
            if (_rootGroup != null)
            {
                _rootGroup.Update();
                return;
            }
            _world.Update();
        }

        public void Dispose()
        {
            _world.Dispose();
        }
    }

    /// <summary>Implement to wire one aspect of the framework into the new World.</summary>
    public interface IFrameworkInstaller
    {
        void Install(World world);
    }

    /// <summary>
    /// Concrete default bootstrap host. The framework ships this as a plain
    /// utility (NOT auto-discovered by the Entities <c>ICustomBootstrap</c>
    /// scan, to avoid conflicting with game-specific bootstraps). Games either
    /// call it directly, use <see cref="FrameworkBootstrapRunner"/>, or define
    /// their own <see cref="ICustomBootstrap"/> that instantiates
    /// <see cref="FrameworkWorld"/>.
    /// </summary>
    public sealed class FrameworkBootstrap
    {
        /// <summary>List of installers contributed by the owning game.</summary>
        public static readonly List<IFrameworkInstaller> Installers = new List<IFrameworkInstaller>();

        public static int BootMode = HYC.Framework.Dots.SimulationBootstrap.Live;

        public bool Initialize(string defaultWorldName)
        {
            var host = new FrameworkWorld(defaultWorldName);
            World.DefaultGameObjectInjectionWorld = host.World;
            host.Install(Installers, BootMode);
            return true;
        }
    }
}