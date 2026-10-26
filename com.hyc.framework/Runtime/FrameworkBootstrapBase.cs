using System;
using System.Collections;
using HYC.Framework.BT;
using HYC.Framework.Dots;
using HYC.Framework.Input;
using HYC.Framework.IO;
using HYC.Framework.UI;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace HYC.Framework.Runtime
{
    /// <summary>
    /// 框架级 ECS 引导基类。游戏写一个 <c>sealed class XxxBootstrap : FrameworkBootstrapBase</c>
    /// 并实现 <see cref="ICustomBootstrap"/>（由 Entities 播放时自动发现），即可获得一整套
    /// 「开箱即用」的框架世界，而无需自己拼组 / 挂 UIManager / 挂 player loop / 设步长。
    ///
    /// <para><b>本类负责（框架级初始化，子类勿重复）：</b></para>
    /// <list type="number">
    ///   <item>跨端预取：协程 <c>await StreamingAssets.PreloadAsync()</c>（WebGL/Android 必须先 HTTP 进内存，PC 为 no-op）；</item>
    ///   <item>建 World（<see cref="WorldName"/>）并设为默认注入世界；</item>
    ///   <item><b>标准自动装配</b> <c>AddSystemsToRootLevelSystemGroups(GetAllSystems(Default))</c>——
    ///     自动创建三大根组、Begin/EndSimulationECB、<c>UpdateWorldTimeSystem</c>（推进 World.Time 的命门）、
    ///     HYC 阶梯组、<b>配置 Blob 系统（XxxConfigBlobSystem 自动 OnCreate→Load，子类无需逐个建）</b>、
    ///     <c>BTInterpreterSystem</c> 及所有未标 <c>[DisableAutoCreation]</c> 的框架 / 玩法系统；</item>
    ///   <item>框架 <c>[DisableAutoCreation]</c> 系统接线：<see cref="UIManager"/> → <see cref="UIGroup"/>；</item>
    ///   <item>固定步长：<c>Root.RateManager = FixedRateCatchUpManager(FixedStep)</c>；</item>
    ///   <item>UI 前置：EventSystem + 透视 UI 相机（<see cref="UIManager.UICamera"/>）；</item>
    ///   <item>挂进 player loop（<c>AppendWorldToCurrentPlayerLoop</c>）。</item>
    /// </list>
    ///
    /// <para><b>子类只实现：</b><see cref="RegisterSystems"/>（游戏系统 / 需配置的系统 / 桥接）、
    /// <see cref="Configure"/>（一次性接线：音频 / 流程 / 存档语言等）、
    /// 以及可选的 <see cref="OnWorldCreated"/>（物理层等世界定制）、<see cref="BindHotkeyAsset"/>、
    /// <see cref="EnsureUICamera"/>、<see cref="EnsureEventSystem"/>。</para>
    ///
    /// <para><b>⚠ 自动装配语义</b>：所有<b>未标</b> <c>[DisableAutoCreation]</c> 的系统都会被自动创建并入组
    /// （这是标准 ECS 行为，也是「配置系统自动化」的来源）。需要手动掌控创建时机 / 初始状态的游戏系统，
    /// 请给它加 <c>[DisableAutoCreation]</c>，并在 <see cref="RegisterSystems"/> 里显式创建。</para>
    ///
    /// <para><b>⚠ abstract 安全</b>：本类是 abstract，不会被 Entities 的 <c>ICustomBootstrap</c> 自动发现
    /// （<c>DefaultWorldInitialization.CreateBootStrap</c> 跳过 abstract），只有游戏的具体子类会被发现，
    /// 因此不会与游戏自有的其它 bootstrap 冲突。</para>
    /// </summary>
    public abstract class FrameworkBootstrapBase : ICustomBootstrap
    {
        /// <summary>当前引导器实例（UI / 外部查询世界用）。</summary>
        public static FrameworkBootstrapBase Instance { get; private set; }

        /// <summary>框架世界（HUD / 外部查询用）。</summary>
        public World World { get; private set; }

        /// <summary>阶梯根组（固定步长挂在它上面）。自动装配后即可用。</summary>
        public UpdateGroup_Root Root { get; private set; }

        /// <summary>阶梯组 A0（输入 tick 采样等）。自动装配后即可用；<see cref="RegisterSystems"/> 里挂 [DisableAutoCreation] 系统用。</summary>
        public UpdateGroup_A0 A0 { get; private set; }

        /// <summary>阶梯组 A1（配置 Blob / 关卡加载等）。</summary>
        public UpdateGroup_A1 A1 { get; private set; }

        /// <summary>阶梯组 A2。</summary>
        public UpdateGroup_A2 A2 { get; private set; }

        /// <summary>阶梯组 A3（玩法主逻辑）。</summary>
        public UpdateGroup_A3 A3 { get; private set; }

        /// <summary>阶梯组 B9（表现层 / UI 末运行）。</summary>
        public UpdateGroup_B9 B9 { get; private set; }

        // ===== 可覆盖策略 =====

        /// <summary>世界名。默认 "Framework World"；游戏按需覆盖。</summary>
        protected virtual string WorldName => "Framework World";

        /// <summary>逻辑固定步长（秒）。默认 1/60。</summary>
        protected virtual float FixedStep => 1f / 60f;

        /// <summary>是否把 <c>Time.fixedDeltaTime</c> 对齐到 <see cref="FixedStep"/>（避免物理与逻辑错拍抖动）。默认 true。</summary>
        protected virtual bool AlignPhysicsToFixedStep => true;

        /// <summary>是否接线框架 UI（创建 <see cref="UIManager"/> + 确保 EventSystem / UI 相机）。默认 true。</summary>
        protected virtual bool EnableUI => true;

        /// <summary>是否加载行为树 Blob（<c>BTBlobLoader.LoadAll</c>）。<c>BTInterpreterSystem</c> 由自动装配创建。默认 true。</summary>
        protected virtual bool EnableBT => true;

        /// <summary>
        /// 是否对「所有未标 <c>[DisableAutoCreation]</c> 的系统」跑标准自动装配。默认 true（配置系统自动化来源）。
        /// 极少数需要完全手动拼世界的游戏可覆盖为 false——但此后 <c>UpdateWorldTimeSystem</c>、三大根组、
        /// Begin/EndECB、阶梯组都需游戏自行创建，否则 <see cref="Root"/> 的步长不会推进。
        /// </summary>
        protected virtual bool AutoAssembleDefaultSystems => true;

        // ===== ICustomBootstrap 入口（固定管线，sealed 语义：勿 override）=====

        /// <inheritdoc/>
        public bool Initialize(string defaultWorldName)
        {
            // 「仅场景运行」闸门：跳过整套引导，让 Entities 退回自动建默认世界。
            // 由编辑器工具栏按钮置位（见 PlaySceneToolbarButton），宿主无需改动。
            // 注意宿主常用入口正是本基类（宿主继承 FrameworkBootstrapBase），此闸门必须有。
            if (BootstrapGate.ShouldSkip)
            {
                Debug.Log("[HYC.Framework] Play Scene Only：跳过 FrameworkBootstrapBase 引导（返回 false，使用 Entities 默认世界）。");
                return false;
            }

            Instance = this;

            if (AlignPhysicsToFixedStep)
                Time.fixedDeltaTime = FixedStep;


            // ICustomBootstrap.Initialize 必须同步返回；WebGL/Android 预取是异步的，
            // 用一个临时 MonoBehaviour 驱动协程等预取完成后再装配世界。
            var runner = new GameObject("[HYC] FrameworkBootstrapRunner");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.hideFlags = HideFlags.HideAndDontSave;
            runner.AddComponent<BootstrapRunner>().Run(this);

            // 返回 true → 抑制 Entities 默认世界（单世界，不双开）。
            return true;
        }

        private IEnumerator BootstrapCoroutine()
        {
            var preload = StreamingAssets.PreloadAsync();
            while (!preload.IsCompleted)
                yield return null;
            if (preload.IsFaulted)
                Debug.LogError($"[HYC] StreamingAssets 预加载失败：{preload.Exception}");

            try
            {
                RunBootstrap();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HYC] 引导异常：{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void RunBootstrap()
        {
            // 1) 建世界
            World = new World(WorldName, WorldFlags.Game);
            World.DefaultGameObjectInjectionWorld = World;

            // 2) 游戏世界定制（物理层互穿、关卡运行时单例等）
            OnWorldCreated(World);

            // 3) UI 导航前置：必须在任何 AbsUISystem.Enable 注册热键之前绑定 InputActionAsset
            BindHotkeyAsset();

            // 4) 标准自动装配（配置系统 / 框架系统 / 未标记玩法系统全部自动创建入组）
            if (AutoAssembleDefaultSystems)
                AutoAssemble();

            Root = World.GetOrCreateSystemManaged<UpdateGroup_Root>();
            A0 = World.GetOrCreateSystemManaged<UpdateGroup_A0>();
            A1 = World.GetOrCreateSystemManaged<UpdateGroup_A1>();
            A2 = World.GetOrCreateSystemManaged<UpdateGroup_A2>();
            A3 = World.GetOrCreateSystemManaged<UpdateGroup_A3>();
            B9 = World.GetOrCreateSystemManaged<UpdateGroup_B9>();

            // 5) 框架 [DisableAutoCreation] 系统 + 行为树数据
            if (EnableUI)
                WireUI();
            if (EnableBT)
                BTBlobLoader.LoadAll();

            // 6) 游戏系统 / 需配置的系统 / 桥接
            RegisterSystems(World);

            // 7) 排序（ RegisterSystems 在自动装配后又加了系统，需重排保证 [UpdateBefore]/[UpdateAfter] 生效 ）
            SortGroups();

            // 8) 固定步长：整条阶梯树按 FixedStep 跑（CatchUp 追帧，受 World.MaximumDeltaTime 防死亡螺旋）
            Root.RateManager = new RateUtils.FixedRateCatchUpManager(FixedStep);

            // 9) UI 前置（相机 + EventSystem）——必须在挂 player loop（首 tick）之前
            if (EnableUI)
                SetupUIPrerequisites();

            // 10) 游戏一次性接线
            Configure(World);

            // 11) 挂进 player loop
#if !UNITY_DOTSRUNTIME
            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(World);
#endif
        }

        // ===== 自动装配 =====

        private void AutoAssemble()
        {
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default, false);
            using var indices = new NativeList<SystemTypeIndex>(Allocator.Temp);
            foreach (var system in systems)
                indices.Add(TypeManager.GetSystemTypeIndex(system));

            // 内部会自建 Initialization/Simulation/Presentation 三大根组（若缺），
            // 并按各系统的 [UpdateInGroup] 把系统挂进对应组；HYC 阶梯组、Begin/EndECB、
            // UpdateWorldTimeSystem、XxxConfigBlobSystem、BTInterpreterSystem 都在此自动创建。
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(World, indices);
        }

        /// <summary>接线框架 <c>[DisableAutoCreation]</c> 的 UIManager：创建并挂进 UIGroup。</summary>
        private void WireUI()
        {
            var uiManager = World.GetOrCreateSystemManaged<UIManager>();
            var uiGroup = World.GetOrCreateSystemManaged<UIGroup>(); // 自动装配已按其 [UpdateInGroup(LateSimulation)] 建好
            uiGroup.AddSystemToUpdateList(uiManager.SystemHandle);
        }

        private void SortGroups()
        {
            // 只排直接子级；覆盖已知层级。阶梯组内部顺序主要靠各系统自身 [UpdateBefore]/[UpdateAfter]。
            SortIfPresent<InitializationSystemGroup>();
            SortIfPresent<SimulationSystemGroup>();
            SortIfPresent<PresentationSystemGroup>();
            SortIfPresent<UpdateGroup_Root>();
            SortIfPresent<UpdateGroup_A0>();
            SortIfPresent<UpdateGroup_A1>();
            SortIfPresent<UpdateGroup_A2>();
            SortIfPresent<UpdateGroup_A3>();
            SortIfPresent<UpdateGroup_B9>();
            SortIfPresent<UIGroup>();
        }

        private void SortIfPresent<T>() where T : ComponentSystemGroup
        {
            var g = World.GetExistingSystemManaged<T>();
            g?.SortSystems();
        }

        private void SetupUIPrerequisites()
        {
            EnsureEventSystem();
            UIManager.UICamera = EnsureUICamera();
        }

        // ===== 子类钩子 =====

        /// <summary>
        /// 游戏系统装配：在此显式创建 <c>[DisableAutoCreation]</c> 的游戏系统、给自动创建的系统做初始配置
        /// （如关卡加载器 LevelId/AutoStart）、以及桥接（如把 World.EntityManager 交给游戏侧运行时）。
        /// 未标 <c>[DisableAutoCreation]</c> 的系统已由自动装配创建，无需在此重复 <c>GetOrCreateSystemManaged</c>。
        /// </summary>
        protected abstract void RegisterSystems(World world);

        /// <summary>一次性接线（音频 / 流程 / 存档语言 / 退出清理等）。默认空。</summary>
        protected virtual void Configure(World world) { }

        /// <summary>建世界后的游戏定制（如 <c>Physics2D.IgnoreLayerCollision</c>、关卡运行时单例）。默认空。</summary>
        protected virtual void OnWorldCreated(World world) { }

        /// <summary>
        /// 绑定 <see cref="HotkeyManager"/> 所需的 <see cref="InputActionAsset"/>。默认生成一个仅含 4 个 UI 导航
        /// action 的运行时资产（方向=WASD/方向键/左摇杆/十字键，确认=Enter/Space/手柄A，取消=Esc/手柄B）。
        /// 已有资产（<see cref="HotkeyManager.InputAsset"/> 非空）时幂等跳过；游戏可整体覆盖以绑定自有资产。
        /// </summary>
        protected virtual void BindHotkeyAsset()
        {
            if (HotkeyManager.InputAsset != null)
                return;

            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = asset.AddActionMap("UI");

            var h = map.AddAction(HotkeyActionNames.UI_UGUI_Horizontal);
            h.AddBinding("<Keyboard>/a");
            h.AddBinding("<Keyboard>/d");
            h.AddBinding("<Keyboard>/leftArrow");
            h.AddBinding("<Keyboard>/rightArrow");
            h.AddBinding("<Gamepad>/leftStick/x");
            h.AddBinding("<Gamepad>/dpad/x");

            var v = map.AddAction(HotkeyActionNames.UI_UGUI_Vertical);
            v.AddBinding("<Keyboard>/w");
            v.AddBinding("<Keyboard>/s");
            v.AddBinding("<Keyboard>/upArrow");
            v.AddBinding("<Keyboard>/downArrow");
            v.AddBinding("<Gamepad>/leftStick/y");
            v.AddBinding("<Gamepad>/dpad/y");

            // 确认 / 取消：必须用 InputActionType.Button（Value 类型绑按钮会同时抛 started+performed，判定永不成立）
            var submit = map.AddAction(HotkeyActionNames.UI_UGUI_Submit, InputActionType.Button);
            submit.AddBinding("<Keyboard>/enter");
            submit.AddBinding("<Keyboard>/space");
            submit.AddBinding("<Gamepad>/buttonSouth");

            var cancel = map.AddAction(HotkeyActionNames.UI_UGUI_Cancel, InputActionType.Button);
            cancel.AddBinding("<Keyboard>/escape");
            cancel.AddBinding("<Gamepad>/buttonEast");

            HotkeyManager.BindAsset(asset);
            asset.Enable(); // HotkeyManager 只持引用、从不 Enable，不调则静默失败

        }

        /// <summary>确保场景有 EventSystem（uGUI 点击前置）。已存在则跳过。</summary>
        protected virtual void EnsureEventSystem()
        {
            // EventSystem.current 仅在「有已启用实例」时非空，已覆盖「场景里已有 EventSystem」这一情况；
            // 此前的 FindObjectOfType<EventSystem> 是冗余的，且会把「挂在激活物体上但组件被关掉」的
            // 情况误判为已就绪（于是不创建、点击全失效）——去掉后该边界反而被修正。
            if (EventSystem.current != null)
                return;

            var go = new GameObject("EventSystem");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        /// <summary>
        /// 确保有一个透视 UI 相机并赋给 <see cref="UIManager.UICamera"/>。
        /// 透视是必须的：<c>UIManager.ResetUIFitData</c> 用 fieldOfView 反算 WorldSpace UI 尺寸。
        /// 游戏可覆盖以接入自有相机管理。
        /// </summary>
        protected virtual Camera EnsureUICamera()
        {
            // 采用场景内既有的 UICamera（历史约定名）。这里是引导期**单次**调用，
            // 全场景按名查找的成本可接受；但宿主若改了相机命名，应覆写本方法接入自有相机栈。
            var existing = GameObject.Find("UICamera");
            var cam = existing != null ? existing.GetComponent<Camera>() : null;
            if (cam == null)
            {
                var go = new GameObject("UICamera");
                UnityEngine.Object.DontDestroyOnLoad(go);
                cam = go.AddComponent<Camera>();
            }

            var uiLayer = LayerMask.NameToLayer("UI");
            cam.orthographic = false;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.Depth; // 不覆盖主相机画面，只叠加 UI
            cam.depth = 100;                          // 在主相机之后绘制
            cam.cullingMask = uiLayer >= 0 ? (1 << uiLayer) : ~0;

            // 相机观察方向是 transform.forward（本地 +Z）：identity 旋转即在原点朝 +Z 看，
            // 对着 HYC 放在 z=+ZInCamera(farClip/4) 的 WorldSpace UI 平面。
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity;

            if (Camera.main != null && uiLayer >= 0)
                Camera.main.cullingMask &= ~(1 << uiLayer);

            return cam;
        }

        /// <summary>辅助：显式创建并挂进指定组（供 <see cref="RegisterSystems"/> 里创建 [DisableAutoCreation] 系统用）。</summary>
        protected T Add<T>(ComponentSystemGroup group) where T : SystemBase
        {
            var system = World.GetOrCreateSystemManaged<T>();
            group.AddSystemToUpdateList(system.SystemHandle);
            return system;
        }

        // ===== 协程驱动器（ICustomBootstrap 不是 MonoBehaviour，协程需由它驱动）=====

        private sealed class BootstrapRunner : MonoBehaviour
        {
            public void Run(FrameworkBootstrapBase b) => StartCoroutine(b.BootstrapCoroutine());
        }
    }
}
