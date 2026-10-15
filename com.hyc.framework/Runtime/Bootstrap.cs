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
    ///
    /// <para><b>跨平台引导：</b>打包后请优先用 <see cref="InitializeAsync"/>——
    /// 它会先 <c>await StreamingAssets.PreloadAsync()</c> 再建 World。WebGL / Android 上
    /// StreamingAssets 不能直接读文件，而配置 / 本地化 / 音频 / 行为树的加载都发生在建 World 时的
    /// <c>System.OnCreate</c>，所以预取必须在建 World 之前完成。PC / iOS 上预取是 no-op，
    /// 调 <see cref="InitializeAsync"/> 与调 <see cref="Initialize"/> 等价——
    /// 用哪个入口都不需要写平台判断代码。</para>
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

            // 应用项目级音频后端：编辑器在 HYC Settings 里设定（持久化到 FrameworkSettings 资产），
            // 运行时从这里读取并初始化门面；无资产则保持代码默认（框架默认后端）。
            HYC.Framework.Audio.Audio.SetBackend(FrameworkSettings.Instance.audioBackend);

            // 应用项目级音量/静音（与后端同源，出包后保留设定）。门面先记一份状态，未初始化后端也不丢；
            // 这里在 SetBackend 之后调用，确保落到正确后端。分组来自 audioGroups（数据驱动，id 0 = Master）。
            var audioSettings = FrameworkSettings.Instance;
            if (audioSettings.audioGroups != null)
            {
                foreach (var g in audioSettings.audioGroups)
                    HYC.Framework.Audio.Audio.SetVolume(g.id, g.defaultVolume); // g.id 隐式转 AudioChannelId，0 = Master
            }
            HYC.Framework.Audio.Audio.SetMuted(audioSettings.audioMuted);
            return true;
        }

        /// <summary>
        /// 跨平台引导入口（推荐）：先完成 StreamingAssets 预取，再建 World 并安装系统。
        ///
        /// <para><c>StreamingAssets.PreloadAsync()</c> 在 PC / macOS / Linux / iOS 上是 no-op，
        /// 在 WebGL / Android 上把配置 blob / 本地化 / 行为树等 HTTP 进内存缓存——
        /// 必须先于建 World 完成，因为读取都发生在 <c>System.OnCreate</c>。</para>
        ///
        /// <para>预取完成后还会补一次引导期数据加载（本地化在 BeforeSceneLoad 阶段若发现缓存未就绪会主动跳过，
        /// 由此处补上）。<b>游戏层全平台统一调这个方法即可，无需写任何平台分支。</b></para>
        /// </summary>
        public async System.Threading.Tasks.Task<bool> InitializeAsync(string defaultWorldName)
        {
            await HYC.Framework.IO.StreamingAssets.PreloadAsync();
            var ok = Initialize(defaultWorldName);
            HYC.Framework.FrameworkLifecycle.EnsureBootDataLoaded();
            return ok;
        }
    }
}