using HYC.Framework.Input;
using HYC.Framework.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HYC.Framework.UI
{
    /// <summary>
    /// ECS window manager. Owns the root node, per-layer canvas nodes, view
    /// states, focus and the additive UI-scene cache.
    ///
    /// Decoupled HYC port of the source <c>UIManager</c>: the game
    /// <c>CameraManager</c>/<c>InputManager</c> dependencies are replaced by
    /// <list type="bullet">
    /// <item><see cref="UICamera"/> — assign the UI camera here.</item>
    /// <item><see cref="OnSetCullingMask"/> / <see cref="OnRecoverCullingMask"/> —
    /// optional hooks the game binds to its CameraManager equivalent.</item>
    /// <item><see cref="HotkeyManager"/> — UI input map routing becomes
    /// navigate-mode toggling.</item>
    /// </list>
    /// The game <c>CurvedUI</c> canvas style is removed from
    /// <see cref="WindowCanvasType"/>.
    /// </summary>
    [DisableAutoCreation]
    public partial class UIManager : SystemBase
    {
        /// <summary>Last rendered screen size.</summary>
        private static Vector2Int mLastRenderScreenSize;

        /// <summary>One-shot diagnostic: whether OnUpdate ever ticks.</summary>
        private static bool mLoggedFirstUpdate;

        /// <summary>Root node.</summary>
        public static GameObject RootNode { get; private set; }

        /// <summary>Idle node (Canvas recycling pool).</summary>
        public static Transform IdleNode { get; private set; }

        /// <summary>Layer nodes.</summary>
        private static List<Transform> LayerNodes { get; set; }

        /// <summary>Layer views.</summary>
        private static List<List<AbsUISystem>> LayerViews { get; set; }

        /// <summary>View state table.</summary>
        private static readonly Dictionary<AbsUISystem, WindowState> ViewStates = new Dictionary<AbsUISystem, WindowState>();

        /// <summary>UI scene table (key_operation).</summary>
        private static readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> mSceneKey2Scene = new Dictionary<string, AsyncOperationHandle<SceneInstance>>();
        /// <summary>UI scene/window relation table (key_windows).</summary>
        private static readonly Dictionary<string, HashSet<AbsUISystem>> mSceneKey2Window = new Dictionary<string, HashSet<AbsUISystem>>();
        /// <summary>UI scene cleanup table (key_closeTime).</summary>
        private static readonly Dictionary<string, float> mSceneKey2ClearTime = new Dictionary<string, float>();
        /// <summary>UI scene cleanup delay.</summary>
        private static readonly float mSceneClearDelayTime = 0.1f;

        /// <summary>Single-instance UI, only one can exist.</summary>
        private static AbsUISystem SingleUISystem;

        /// <summary>Currently focused window.</summary>
        public static AbsUISystem FocusedWindow { get; private set; }

        /// <summary>Settings.</summary>
        public static UIManagerSetting Setting { get; private set; }

        /// <summary>
        /// UI camera used for world-space canvases. Assignable; falls back to
        /// <c>Camera.main</c>. Decoupled replacement for the game CameraManager.UICamera.
        /// </summary>
        public static Camera UICamera { get; set; }

        /// <summary>
        /// Optional hook invoked when the main camera culling mask should be
        /// switched to the UI-scene mask (game binds its CameraManager equivalent).
        /// </summary>
        public static Action<LayerMask> OnSetCullingMask;

        /// <summary>
        /// Optional hook invoked when the main camera culling mask should be
        /// restored after all UI scenes unload.
        /// </summary>
        public static Action OnRecoverCullingMask;

        public class UIManagerSetting
        {
            /// <summary>Reference resolution.</summary>
            public Vector2 ReferenceResolution = new Vector2(1920, 1080);

            /// <summary>Maximum resolution.</summary>
            public Vector2 MaximumResolution = new Vector2(3440, 1440);

            /// <summary>Navigation key input dead zone.</summary>
            public float NavigationKeyDeathArea = 0.6f;

            /// <summary>Navigation key repeat delay.</summary>
            public float NavigationKeyDelayTime = 0.5f;

            /// <summary>Navigation key repeats per second.</summary>
            public int NavigationKeyRepeateCount = 10;
        }

        /// <summary>Create.</summary>
        protected override void OnCreate()
        {
            // Setting 是 UI 适配 / 导航参数的唯一真源，默认值即下表默认。宿主持家在引导期
            // 定制（如 UI 非按 1920×1080 设计）——在 FrameworkBootstrapBase 子类的 Configure
            // 钩子里改字段即可（彼时 WireUI 已建好本系统，Setting 必非空）：
            //     UIManager.Setting.ReferenceResolution = new Vector2(2560, 1080);
            // Setting 是 static，拟合 / 导航代码每帧实时读，创建后赋值即时生效。
            Setting = new UIManagerSetting();

            InitUIManager();
        }

        /// <summary>Cleanup.</summary>
        protected override void OnDestroy()
        {
            if (RootNode != null)
                GameObject.Destroy(RootNode);

            LayerNodes?.Clear();
            LayerNodes = null;

            LayerViews?.Clear();
            LayerViews = null;

            // ViewStates 的 key 是窗口实例（引用类型），不清的话整张表连同所有
            // WindowState 会跨 UIManager 重建存活；域重载关闭时更是直接泄漏到下一次 Play。
            ViewStates.Clear();

            FocusedWindow = null;
        }

        /// <summary>Update.</summary>
        protected override void OnUpdate()
        {
            // 一次性诊断闸门：确保 UIManager 至少被 tick 过一次（宿主世界/组装配问题排查用）。
            if (!mLoggedFirstUpdate)
            {
                mLoggedFirstUpdate = true;
            }

            // ScaleFactor<=0 ⇒ m_UIFitData 还是 struct 全零（ResetUIFitData 从未成功跑过，
            // 典型原因：宿主在首帧后才赋 UICamera，导致首屏那次 bails out 且屏幕尺寸被缓存、永不重试）。
            // 加此条件让拟合初始化失败时逐帧重试，直到相机就绪算出有效值为止。
            if (mLastRenderScreenSize.x != Screen.width || mLastRenderScreenSize.y != Screen.height
                || m_UIFitData.ScaleFactor <= 0f)
            {
                mLastRenderScreenSize = new Vector2Int(Screen.width, Screen.height);
                ResetScreenData(mLastRenderScreenSize);
                ResetUIFitData();
                ResetWindowFit();
            }

            if (mSceneKey2ClearTime.Count > 0)
            {
                var changed = false;

                var now = UnityEngine.Time.time;
                foreach (var key in mSceneKey2ClearTime.Keys.ToArray())
                {
                    if (mSceneKey2Window.ContainsKey(key) && mSceneKey2Window[key].Count > 0)
                    {
                        mSceneKey2ClearTime.Remove(key);
                    }
                    else
                    {
                        var time = now - mSceneKey2ClearTime[key];
                        if (time > mSceneClearDelayTime)
                        {
                            mSceneKey2ClearTime.Remove(key);

                            if (mSceneKey2Scene.ContainsKey(key))
                            {
                                var operation = mSceneKey2Scene[key];
                                if (operation.IsValid())
                                    Addressables.UnloadSceneAsync(operation);

                                changed = true;
                                mSceneKey2Scene.Remove(key);
                            }
                        }
                    }
                }

                if (changed && mSceneKey2Scene.Count <= 0)
                {
                    OnRecoverCullingMask?.Invoke();
                    ResetHudVisible();
                }
            }
        }

        /// <summary>Build the root node hierarchy.</summary>
        private void InitUIManager()
        {
            // 命名用 UIRoot 而非 GetType().Name：与 ECS 系统 UIManager 同名会在 Hierarchy 里混淆。
            RootNode = new GameObject("UIRoot");
            GameObject.DontDestroyOnLoad(RootNode);

            IdleNode = new GameObject("IdleNode").transform;
            IdleNode.SetParent(RootNode.transform);
            IdleNode.gameObject.SetActive(false);

            // 层表是数据（FrameworkSettings.uiLayers，HYC Settings → UI/层级 可增删改名、拖动排序）：
            // 节点名 = 层名，列表下标顺序即层级。增减层 / 调序都不需要改本文件。
            var defs = UILayerTable.All;
            LayerNodes = new List<Transform>(defs.Count);
            LayerViews = new List<List<AbsUISystem>>(defs.Count);

            for (var i = 0; i < defs.Count; i++)
            {
                var node = new GameObject(defs[i].name);
                node.transform.SetParent(RootNode.transform);

                LayerNodes.Add(node.transform);
                LayerViews.Add(new List<AbsUISystem>());
            }

            InitLayerIndexCache();
        }

        /// <summary>
        /// 默认层名 → 下标缓存。框架里少数 API 与「主窗口层」精确绑定（<see cref="HasWindow"/> /
        /// ExistOtherWindow* / <see cref="OpenWindowAndCloseOther{T}"/> / HitTest / 11 个角色 Open 方法），
        /// 它们保持按默认层名查找：层名被改掉时缓存为 -1，相等判断自然不命中（行为退化为"没有该层"），
        /// 并在初始化时一次性报错指明缺哪些层。群体行为（HUD 自动隐藏 / 挡输入 / 全隐藏）不查名，
        /// 走 <see cref="UILayerTable"/> 的标记，改名 / 插层自动跟随。
        /// </summary>
        private static readonly int[] LayerIndexCache = Enumerable.Repeat(-1, 10).ToArray();
        private static bool _layerIndexReady;

        private static void InitLayerIndexCache()
        {
            foreach (WindowLayer layer in Enum.GetValues(typeof(WindowLayer)))
                LayerIndexCache[(int)layer] = UILayerTable.Index(layer.ToString());

            _layerIndexReady = true;

            var missing = UILayerTable.MissingDefaultNames();
            if (missing.Count > 0)
            {
                Debug.LogError(
                    "UI 层表缺少默认层：" + string.Join("、", missing) +
                    "。按层名查找的 API（HasWindow / ExistOtherWindow* / HitTest / OpenHud、OpenWindow 等角色方法）会失效。" +
                    "请在 HYC Settings → UI/层级 改回默认层名，或改用设置页「保存并生成」产出的 UILayer 常量与 Open 方法。");
            }
        }

        /// <summary>默认层名 → 当前层表下标；层名不存在时返回 -1（相等判断永不命中）。</summary>
        private static int LayerIndex(WindowLayer layer)
        {
            if (!_layerIndexReady)
                InitLayerIndexCache();
            return LayerIndexCache[(int)layer];
        }

        private static Camera ResolveUICamera()
        {
            return UICamera != null ? UICamera : Camera.main;
        }

        // ---- 角色 Open 方法：默认 10 层的快捷方式，层号按层名在层表里现查 ----
        // 层名被改过时 LayerIndex 返回 -1，这里统一退化为「打不开」并报一条错误（不静默开到底层）。
        // 自定义层 / 改过名的层请用 Open<T>(layer, …) 或设置页「保存并生成」产出的方法。

        /// <summary>打开 HUD 层 UI（默认层名 "HUD"）。</summary>
        public static T OpenHud<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(LayerIndex(WindowLayer.HUD), Type, uISystemType);
        }

        /// <summary>打开 HUDNotice 层 UI（默认层名 "HUDNotice"）。</summary>
        public static T OpenHudNotice<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(LayerIndex(WindowLayer.HUDNotice), Type, uISystemType);
        }

        /// <summary>打开 HUDNotice 层 UI（非泛型重载）。</summary>
        public static AbsUISystem OpenHudNotice(Type type, WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal)
        {
            return Open(type, LayerIndex(WindowLayer.HUDNotice), Type, uISystemType);
        }

        /// <summary>打开 HUDNotice 层单例 UI（<see cref="UISystemType.Single"/>，默认层名 "HUDNotice"）。</summary>
        public static T OpenHudSingleNotice<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Single) where T : AbsUISystem
        {
            return Open<T>(LayerIndex(WindowLayer.HUDNotice), Type, uISystemType);
        }

        /// <summary>打开 Window 层 UI（默认层名 "Window"）。</summary>
        public static T OpenWindow<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal, params object[] args) where T : AbsUISystem
        {
            return Open<T>(LayerIndex(WindowLayer.Window), Type, uISystemType, args);
        }

        /// <summary>打开 Window 层 UI，并关掉该层其它窗口（默认层名 "Window"）。</summary>
        public static T OpenWindowAndCloseOther<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal, params object[] args) where T : AbsUISystem
        {
            var layer = LayerIndex(WindowLayer.Window);
            if (layer >= 0)
            {
                var windows = LayerViews[layer];
                for (var i = windows.Count - 1; i >= 0; i--)
                {
                    var win = windows[i];
                    if (win.GetType() != typeof(T))
                        Close(win);
                }
            }

            return Open<T>(layer, Type, uISystemType, args);
        }

        /// <summary>打开 WindowNotice 层 UI（默认层名 "WindowNotice"）。</summary>
        public static T OpenWindowNotice<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(LayerIndex(WindowLayer.WindowNotice), Type, uISystemType);
        }

        /// <summary>打开 Dialog 层 UI（默认层名 "Dialog"）。</summary>
        public static T OpenDialog<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(LayerIndex(WindowLayer.Dialog), Type, uISystemType);
        }

        /// <summary>打开 TopWindow 层 UI（默认层名 "TopWindow"）。</summary>
        public static T OpenTopWindow<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(LayerIndex(WindowLayer.TopWindow), Type, uISystemType);
        }

        /// <summary>打开 Loading 层 UI（默认层名 "Loading"）。</summary>
        public static T OpenLoading<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(LayerIndex(WindowLayer.Loading), Type, uISystemType);
        }

        /// <summary>打开 Cursor 层 UI（默认层名 "Cursor"）。</summary>
        public static T OpenCursor<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(LayerIndex(WindowLayer.Cursor), Type, uISystemType);
        }

        /// <summary>打开 Mask 层 UI（默认层名 "Mask"）。</summary>
        public static T OpenMask<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(LayerIndex(WindowLayer.Mask), Type, uISystemType);
        }

        /// <summary>打开 ToolTip 层 UI（默认层名 "ToolTip"）。</summary>
        public static T OpenToolTip<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(LayerIndex(WindowLayer.ToolTip), Type, uISystemType);
        }

        /// <summary>打开 ToolTip 层 UI（非泛型重载）。</summary>
        public static AbsUISystem OpenToolTip(Type type, WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal)
        {
            return Open(type, LayerIndex(WindowLayer.ToolTip), Type, uISystemType);
        }

        /// <summary>Window / Dialog 层是否有开着的窗口。</summary>
        public static bool HasWindow()
        {
            var windowLayer = LayerIndex(WindowLayer.Window);
            var dialogLayer = LayerIndex(WindowLayer.Dialog);

            return (windowLayer >= 0 && LayerViews[windowLayer].Count > 0) ||
                   (dialogLayer >= 0 && LayerViews[dialogLayer].Count > 0);
        }

        /// <summary>在指定层打开 UI（通用入口，层号取自层表；生成产物 <c>UILayer</c> 提供具名常量）。</summary>
        /// <param name="layer">层表下标，0 在最底。越界或层表为空时记错误并返回 null。</param>
        public static T Open<T>(int layer, WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal, params object[] args) where T : AbsUISystem
        {
            return Open(typeof(T), layer, Type, uISystemType, args) as T;
        }

        /// <summary>在指定层打开 UI（非泛型重载）。</summary>
        /// <param name="layer">层表下标，0 在最底。越界或层表为空时记错误并返回 null。</param>
        public static AbsUISystem Open(Type type, int layer, WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal, params object[] args)
        {
            if (LayerViews == null || layer < 0 || layer >= LayerViews.Count)
            {
                Debug.LogError($"打开 UI 失败：层号 {layer} 不存在（当前层表共 {LayerViews?.Count ?? 0} 层）。" +
                               "层号请用设置页「保存并生成」产出的 UILayer 常量。");
                return null;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            var group = GetSystemGroup(type);
            var window = world.GetOrCreateSystemManaged(type) as AbsUISystem;
            var windowState = GetWindowState(window);

            window.UISystemType = uISystemType;

            group.AddSystemToUpdateList(window);
            group.SortSystems();

            //Remove from the previous layer.
            foreach (var LayerCurr in LayerViews)
            {
                if (LayerCurr.Contains(window))
                {
                    LayerCurr.Remove(window);
                    break;
                }
            }

            //Append to the target layer.
            LayerViews[layer].Add(window);

            //Move under the target layer node.
            if (windowState.Canvas != null)
                windowState.Canvas.transform.SetParent(LayerNodes[layer]);

            //Record the window layer.
            windowState.Layer = layer;

            //Load the prefab.
            if (windowState.View == null && !windowState.ViewLoadOperation.IsValid())
            {
                LoadSceenBegin(window, Type, args);
            }

            return window;
        }
        public static bool IsOpen<T>() where T : AbsUISystem
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var window = world.GetExistingSystemManaged<T>();
            if (window == null)
            {
                return false;
            }
            return window.Enabled;
        }

        public static bool IsOpen(Type type)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var window = world.GetExistingSystemManaged(type);
            if (window == null)
            {
                return false;
            }
            return window.Enabled;
        }

        public static bool IsOpenAsync(Type type)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var window = world.GetExistingSystemManaged(type);
            if (window == null)
            {
                return false;
            }
            var windowState = GetWindowState(window as AbsUISystem);
            if (!windowState.ViewLoadOperation.IsDone)//If still async loading, treat as open.
            {
                return true;
            }
            return window.Enabled;
        }

        public static ComponentSystemGroup GetSystemGroup(Type type)
        {
            var world = World.DefaultGameObjectInjectionWorld;

            var group = default(ComponentSystemGroup);

            var custom = type.GetCustomAttribute<UpdateInGroupAttribute>();
            if (custom != null)
                group = world.GetOrCreateSystemManaged(custom.GroupType) as ComponentSystemGroup;

            if (group == null)
                group = world.GetExistingSystemManaged<UIGroup>();

            return group;
        }

        private static void LoadSceenBegin(AbsUISystem window, WindowCanvasType Type = WindowCanvasType.Normal, params object[] args)
        {
            var windowState = GetWindowState(window);

            var key = window.SceneKey;

            if (string.IsNullOrEmpty(key))
            {
                windowState.SceneKey = null;

                LoadPrefabBegin(window, Type, args);
            }
            else
            {
                windowState.SceneKey = window.SceneKey;

                if (!mSceneKey2Scene.ContainsKey(key))
                    mSceneKey2Scene.Add(key, Addressables.LoadSceneAsync(key, LoadSceneMode.Additive));
                if (!mSceneKey2Window.ContainsKey(key))
                    mSceneKey2Window.Add(key, new HashSet<AbsUISystem>());

                if (!mSceneKey2Window[key].Contains(window))
                    mSceneKey2Window[key].Add(window);

                var operation = mSceneKey2Scene[key];
                if (!operation.IsValid())
                {
                    Debug.LogError($"{window.GetType().Name} UI场景加载被清理? ({key})");
                    LoadPrefabBegin(window, Type, args);
                }

                if (operation.IsDone)
                {
                    if (operation.Status == AsyncOperationStatus.Succeeded)
                    {
                        OnSetCullingMask?.Invoke(LayerMask.GetMask("UIScene"));
                        ResetHudVisible();

                        window.OnSceneOpen(operation.Result.Scene, args);

                        LoadPrefabBegin(window, Type, args);
                    }
                    else
                    {
                        Debug.LogError($"{window.GetType().Name} UI场景加载失败! ({key})");

                        LoadPrefabBegin(window, Type, args);
                    }
                }
                else
                {
                    operation.Completed += (operation) =>
                    {
                        if (operation.Status == AsyncOperationStatus.Succeeded)
                        {
                            if (mSceneKey2Window[key].Contains(window))
                            {
                                OnSetCullingMask?.Invoke(LayerMask.GetMask("UIScene"));
                                ResetHudVisible();

                                window.OnSceneOpen(operation.Result.Scene, args);

                                LoadPrefabBegin(window, Type, args);
                            }
                            else
                            {
                                Debug.LogError($"{window.GetType().Name} UI场景加载被中止? ({key})");
                            }
                        }
                        else
                        {
                            Debug.LogError($"{window.GetType().Name} UI场景加载失败! ({key})");

                            LoadPrefabBegin(window, Type, args);
                        }
                    };
                }
            }
        }

        private static void LoadPrefabBegin(AbsUISystem window, WindowCanvasType Type = WindowCanvasType.Normal, params object[] args)
        {
            var windowState = GetWindowState(window);

            windowState.ViewLoadOperation = Addressables.InstantiateAsync(window.PrefabKey);
            windowState.ViewLoadOperation.Completed += (AsyncOperationHandle<GameObject> obj) =>
            {
                if (windowState.ViewLoadOperation.IsValid())
                {
                    if (obj.Status == AsyncOperationStatus.Succeeded)
                    {
                        LoadWindowCompleted(window, Type, args);

                        if (window.UISystemType == UISystemType.Single)//Force close single-instance UIs.
                        {
                            if (SingleUISystem != null)
                            {
                                CloseHudNotice(SingleUISystem);
                            }
                            SingleUISystem = window;
                        }
                    }
                    else
                    {
                        Debug.LogError($"{window.GetType().Name} 预置体加载失败! ({window.PrefabKey})");
                    }
                }
                else
                {
                    Addressables.Release(obj);
                }
            };
        }

        /// <summary>Window prefab load completed.</summary>
        private static void LoadWindowCompleted(AbsUISystem window, WindowCanvasType Type = WindowCanvasType.Normal, params object[] args)
        {
            var state = GetWindowState(window);
            if (!state.ViewLoadOperation.IsValid() || state.ViewLoadOperation.Status != AsyncOperationStatus.Succeeded)
                return;

            var canvasNode = new GameObject(window.GetType().Name);
            canvasNode.AddComponent<Canvas>();
            canvasNode.AddComponent<GraphicRaycaster>();

            state.Canvas = canvasNode.GetComponent<Canvas>();
            state.Canvas.renderMode = RenderMode.WorldSpace;
            state.Canvas.worldCamera = ResolveUICamera();
            state.Canvas.transform.SetParent(LayerNodes[(int)state.Layer]);
            state.Canvas.gameObject.layer = LayerMask.NameToLayer("UI");

            state.CanvasRaycaster = canvasNode.GetComponent<GraphicRaycaster>();

            state.View = state.ViewLoadOperation.Result;
            state.View.name = state.View.name.Replace("(Clone)", "");
            state.View.transform.SetParent(state.Canvas.transform);
            // View 整棵子树设为 UI 层：UICamera 的 cullingMask 通常只含 UI 层，
            // 只设 Canvas 节点的话，预制体里 Default 层的 Graphic 会被相机整体剔除（相机有画面但 UI 不可见）。
            SetLayerRecursive(state.View, LayerMask.NameToLayer("UI"));

            var hook = state.View.GetComponent<UIAnimationHook>();
            if (hook != null)
                state.ViewAnimationHook = new ViewAnimationHook(window, hook);

            window.View = state.View;
            window.Canvas = state.Canvas;
            window.Camera = state.Canvas.worldCamera;
            window.Enabled = true;
            window.OnViewOpen(args);

            // m_UIFitData 全零 = OnUpdate 的屏幕尺寸检测还没成功跑过（宿主首帧后才设相机、或宿主世界尚未 tick）。
            // 开窗现场立即补算，避免窗口以 z=0/尺寸0/缩放0 的拟合数据打开（表现为 UI 全屏不可见）。
            if (m_UIFitData.ScaleFactor <= 0f)
            {
                ResetScreenData(new Vector2(Screen.width, Screen.height));
                ResetUIFitData();
            }

            ResetWindowOrder();
            ResetWindowFocus();
            ResetWindowFit();
            ResetGUIInput();
            ResetHudVisible();
        }

        /// <summary>Recursively set the layer of a transform subtree.</summary>
        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null || layer < 0) return;
            var t = root.transform;
            for (var i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i).gameObject, layer);
            root.layer = layer;
        }

        private static bool IsHideAllUI = false;

        public static void HideAllUI()
        {
            // 逐层问层表的 hideOnHideAll 标记：勾了的层一起隐藏，没勾的层（Loading / Cursor / Mask 这类
            // 常驻层）原样留着。原实现按「层号 >= Loading」跳过，且 continue 漏了 index++（靠 LayerViews
            // 恰好有序才没出错）；改成按标记判断后顺序无关，也不会再漏计数。
            for (var i = 0; i < LayerViews.Count; i++)
            {
                if (!UILayerTable.HiddenOnHideAll(i))
                    continue;

                foreach (var View in LayerViews[i])
                {
                    View.Hide();
                }
            }

            IsHideAllUI = true;
        }

        public static void ShowAllUI()
        {
            foreach (var Views in LayerViews)
            {
                foreach (var View in Views)
                {
                    View.Show();
                }
            }
            IsHideAllUI = false;
            ResetHudVisible();
        }

        private class ViewAnimationHook : IDisposable
        {
            private AbsUISystem mWindow;
            private UIAnimationHook mHook;

            public ViewAnimationHook(AbsUISystem window, UIAnimationHook hook)
            {
                mWindow = window;

                mHook = hook;
                mHook.OnAnimationEvent += OnAnimationEvent;
                mHook.OnAnimationEnterEvent += OnAnimationOpenEvent;
                mHook.OnAnimationExitEvent += OnAnimationCloseEvent;
            }

            public void OnAnimationEvent(string key)
            {
                mWindow.OnAnimationEvent(key);
            }

            public void OnAnimationOpenEvent()
            {
                OnAnimationEvent("Enter");
            }

            public void OnAnimationCloseEvent()
            {
                OnAnimationEvent("Exit");

                if (mWindow != null)
                    Close(mWindow, false);
            }

            public void Dispose()
            {
                mWindow = null;

                mHook.OnAnimationEvent -= OnAnimationEvent;
                mHook.OnAnimationEnterEvent -= OnAnimationOpenEvent;
                mHook.OnAnimationExitEvent -= OnAnimationCloseEvent;
                mHook = null;
            }

            public bool FireExitAnimation()
            {
                if (mHook != null)
                {
                    var animator = mHook.GetComponent<Animator>();
                    foreach (var parameter in animator.parameters)
                    {
                        if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == "Exit")
                        {
                            animator.SetTrigger("Exit");
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        /// <summary>Close window.</summary>
        public static void Close<T>(bool animation = true) where T : AbsUISystem
        {
            Close(typeof(T), animation);
        }

        public static void Close(Type type, bool animation = true)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var window = world.GetExistingSystemManaged(type) as AbsUISystem;
            if (window == null)
                return;

            if (animation)
            {
                var state = GetWindowState(window);
                if (state.ViewAnimationHook != null && state.ViewAnimationHook.FireExitAnimation())
                    return;
            }

            Close(window);

            var group = GetSystemGroup(type);
            group.RemoveSystemFromUpdateList(window);
            window.Enabled = false;
        }

        public static void CloseHudNotice<T>(T window) where T : AbsUISystem
        {
            if (window == null)
            {
                return;
            }

            Close(window);

            var world = World.DefaultGameObjectInjectionWorld;

            var group = GetSystemGroup(typeof(T));
            group.RemoveSystemFromUpdateList(window);
        }

        public static void Close<T>(T window, bool animation = true) where T : AbsUISystem
        {
            var state = GetWindowState(window);

            if (animation && state.ViewAnimationHook != null && state.ViewAnimationHook.FireExitAnimation())
                return;

            try
            {
                window.OnViewClose();
            }
            catch (Exception exceptoin)
            {
                Debug.LogError($"关闭窗口时出错 : {window.GetType().Name}\n{exceptoin}");
            }

            var sceneKey = state.SceneKey;
            if (!string.IsNullOrEmpty(sceneKey))
            {
                if (mSceneKey2Window.ContainsKey(sceneKey))
                {
                    mSceneKey2Window[sceneKey].Remove(window);
                    if (mSceneKey2Window[sceneKey].Count <= 0)
                    {
                        mSceneKey2Window.Remove(sceneKey);
                        mSceneKey2ClearTime[sceneKey] = UnityEngine.Time.time;
                    }
                }
            }

            if (window.UISystemType == UISystemType.Single)//Single-instance UI.
            {
                SingleUISystem = null;
            }

            if (state.ViewAnimationHook != null)
                state.ViewAnimationHook.Dispose();

            //Remove from the previous layer.
            foreach (var LayerCurr in LayerViews)
            {
                if (LayerCurr.Contains(window))
                {
                    LayerCurr.Remove(window);
                    break;
                }
            }

            //Recycle the Canvas.
            if (state.Canvas != null)
                state.Canvas.transform.SetParent(IdleNode);

            //Clean Addressable.
            if (state.ViewLoadOperation.IsValid())
                Addressables.Release(state.ViewLoadOperation);

            state.ViewLoadOperation = default;
            state.Canvas = null;
            state.CanvasRaycaster = null;
            state.View = null;
            state.ViewAnimationHook = null;

            window.View = null;
            window.Canvas = null;
            window.Camera = null;

            // 必须从 ViewStates 移除。key 是窗口实例本身（引用类型），不移除的话
            // 每开关一次窗口就永久残留一条：WindowState 连带整棵 View 资源一起被字典钉住，
            // 既是无界内存增长，也会让「已销毁的窗口」继续出现在状态表里。
            ForgetWindowState(window);

            ResetWindowFocus();
            ResetGUIInput();
            ResetHudVisible();
        }

        /// <summary>
        /// 把窗口从静态状态表里摘掉。正常关闭走 <see cref="Close{T}(T,bool)"/> 即可，它内部已调用。
        ///
        /// 只有在使用方绕过 <c>Close</c>、直接 <c>World.DestroySystemManaged(window)</c>
        /// 销毁窗口 System 时才需要手动调一次，否则该窗口的状态条目会永久留在表里。
        /// </summary>
        public static void ForgetWindowState(AbsUISystem window)
        {
            if (window == null)
                return;

            ViewStates.Remove(window);

            // FocusedWindow / SingleUISystem 仍可能指着这个已销毁的实例，一并摘掉。
            if (ReferenceEquals(FocusedWindow, window))
                FocusedWindow = null;
            if (ReferenceEquals(SingleUISystem, window))
                SingleUISystem = null;
        }

        /// <summary>Reorder windows.</summary>
        private static void ResetWindowOrder()
        {
            var i = 1;
            foreach (var layer in LayerViews)
            {
                var j = 1;
                layer.Sort((a, b) => a.Order.CompareTo(b.Order));
                foreach (var window in layer)
                {
                    var state = GetWindowState(window);
                    if (state.Canvas)
                    {
                        state.Canvas.sortingOrder = i * 1000 + j;
                        window.OnViewLayerChanged(state.Canvas.sortingOrder);
                    }
                    else
                    {
                        window.OnViewLayerChanged(0);
                    }

                    j++;
                }

                i++;
            }
        }

        /// <summary>Close UI matching a selector.</summary>
        public static void CloseBy(Func<AbsUISystem, bool> selector)
        {
            var closeList = new List<AbsUISystem>();
            foreach (var layer in LayerViews)
            {
                foreach (var window in layer)
                {
                    if (selector.Invoke(window))
                    {
                        closeList.Add(window);
                    }
                }
            }

            foreach (var closeItem in closeList)
            {
                Close(closeItem);
            }
        }

        /// <summary>Reset HUD visibility.</summary>
        private static void ResetHudVisible()
        {
            var hasWindow = false;

            //A UI scene exists.
            if (mSceneKey2Scene.Count > 0)
                hasWindow = true;

            //A window exists.
            var windowLayer = LayerIndex(WindowLayer.Window);
            var dialogLayer = LayerIndex(WindowLayer.Dialog);
            foreach (var layer in LayerViews)
            {
                foreach (var window in layer)
                {
                    var state = GetWindowState(window);
                    if (state.Layer == windowLayer || state.Layer == dialogLayer)
                    {
                        hasWindow = true;
                        break;
                    }
                }
            }

            //Force-hide all UI.
            if (IsHideAllUI)
            {
                hasWindow = true;
            }

            // HUD 组的层读层表的 hud 标记（不按名），改名 / 插层后自动跟随。
            for (var i = 0; i < LayerViews.Count; i++)
            {
                if (!UILayerTable.IsHud(i))
                    continue;

                foreach (var window in LayerViews[i])
                {
                    var state = GetWindowState(window);
                    if (state.Canvas != null)
                        state.Canvas.enabled = !hasWindow;
                    if (state.CanvasRaycaster != null)
                        state.CanvasRaycaster.enabled = !hasWindow;

                    if (hasWindow && state.View != null)
                    {
                        var particles = state.View.GetComponentsInChildren<ParticleSystem>();
                        foreach (var particle in particles)
                            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
            }
        }

        /// <summary>Reset window focus.</summary>
        private static void ResetWindowFocus()
        {
            AbsUISystem focusNew = null;
            foreach (var layer in LayerViews)
            {
                foreach (var window in layer)
                {
                    if (window.Focusable)
                        focusNew = window;
                }
            }

            if (focusNew != FocusedWindow)
            {
                if (FocusedWindow != null)
                    FocusedWindow.OnViewLost();

                FocusedWindow = focusNew;

                if (FocusedWindow != null)
                    FocusedWindow.OnViewFocus();
            }
        }

        /// <summary>Reset UI input (routed to navigate mode).</summary>
        private static void ResetGUIInput()
        {
            // 从最顶层往下找第一个可聚焦窗口：层数由层表决定，不再假定"最后一个是 Mask、第一个是 HUD"。
            for (var i = LayerViews.Count - 1; i >= 0; i--)
            {
                foreach (var view in LayerViews[i])
                {
                    if (view.Focusable)
                    {
                        HotkeyManager.SetNavigateMode(true);
                        return;
                    }
                }
            }

            HotkeyManager.SetNavigateMode(false);
        }

        /// <summary>Reset focus and input.</summary>
        public static void ResetFocus()
        {
            ResetWindowFocus();
            ResetGUIInput();
            ResetHudVisible();
        }

        /// <summary>Current aspect ratio.</summary>
        public static float Aspect { get; private set; }

        /// <summary>Current render area.</summary>
        public static Rect ViewportRect { get; private set; }

        /// <summary>Screen size.</summary>
        public static Vector2 ScreenSize { get; private set; }

        /// <summary>UI fit data.</summary>
        private static UIFitData m_UIFitData;

        /// <summary>UI fit info.</summary>
        private struct UIFitData
        {
            /// <summary>Z coordinate in the UI camera space.</summary>
            public float ZInCamera;

            /// <summary>Scale factor.</summary>
            public float ScaleFactor;

            /// <summary>Reference resolution.</summary>
            public Vector2 ReferenceResolution;

            /// <summary>Best resolution.</summary>
            public Vector2 BestResolution;
        }

        /// <summary>Reset screen info.</summary>
        private static void ResetScreenData(Vector2 screenSize)
        {
            var referenceResolution = Setting.ReferenceResolution;
            var maximumResolution = Vector2.Max(referenceResolution, Setting.MaximumResolution);

            var scaleFactor = Mathf.Min(screenSize.x / referenceResolution.x, screenSize.y / referenceResolution.y);

            var maximumSize = maximumResolution * scaleFactor;
            var bestSize = Vector2.Min(maximumSize, screenSize);

            var x = (screenSize.x - bestSize.x) * 0.5f / screenSize.x;
            var y = (screenSize.y - bestSize.y) * 0.5f / screenSize.y;
            var w = bestSize.x / screenSize.x;
            var h = bestSize.y / screenSize.y;

            Aspect = bestSize.x / bestSize.y;
            ViewportRect = new Rect(x, y, w, h);
            ScreenSize = bestSize;
        }

        /// <summary>Reset UI fit data.</summary>
        private static void ResetUIFitData()
        {
            var camera = ResolveUICamera();
            if (camera == null)
            {
                Debug.LogError("UIManager.ResetUIFitData : UICamera 未设置!");
                return;
            }

            var middleClipPlane = camera.farClipPlane / 4;
            var height = Mathf.Tan(camera.fieldOfView / 2.0f * Mathf.Deg2Rad) * middleClipPlane * 2.0f;
            var width = height * Aspect;

            var screenSize = new Vector2(width, height);
            var referenceResolution = Setting.ReferenceResolution;
            var maximumResolution = Vector2.Max(referenceResolution, Setting.MaximumResolution);

            var scaleFactor = Mathf.Min(screenSize.x / referenceResolution.x, screenSize.y / referenceResolution.y);

            var maximumSize = maximumResolution * scaleFactor;
            var bestSize = Vector2.Min(maximumSize, screenSize);
            var bestResolution = bestSize / scaleFactor;

            m_UIFitData.ZInCamera = middleClipPlane;
            m_UIFitData.ScaleFactor = scaleFactor;
            m_UIFitData.ReferenceResolution = referenceResolution;
            m_UIFitData.BestResolution = bestResolution;
        }

        /// <summary>Reset fit for all windows.</summary>
        private static void ResetWindowFit()
        {
            foreach (var layer in LayerViews)
            {
                foreach (var window in layer)
                {
                    ResetPanelFit(window);
                }
            }
        }

        /// <summary>Reset fit for a specific window.</summary>
        public static void ResetPanelFit(AbsUISystem window)
        {
            var windowState = GetWindowState(window);

            if (windowState.Canvas != null && windowState.View != null)
            {
                var holdAspect = false;

                var canvasRect = windowState.Canvas.GetComponent<RectTransform>();
                canvasRect.localPosition = new Vector3(0, 0, m_UIFitData.ZInCamera);
                canvasRect.localScale = Vector3.one * m_UIFitData.ScaleFactor;
                canvasRect.localRotation = Quaternion.identity;
                canvasRect.sizeDelta = m_UIFitData.BestResolution;

                var root = windowState.View.GetComponent<RectTransform>();
                root.anchorMin = root.anchorMax = Vector2.one * 0.5f;
                root.anchoredPosition = Vector2.zero;
                root.sizeDelta = holdAspect ? m_UIFitData.ReferenceResolution : m_UIFitData.BestResolution;
            }
        }

        /// <summary>Get window state.</summary>
        private static WindowState GetWindowState(AbsUISystem window)
        {
            if (ViewStates.TryGetValue(window, out WindowState state))
                return state;

            state = new WindowState
            {
                Window = window,
                ViewLoadOperation = default,
                Canvas = null,
                View = null,
            };

            ViewStates.Add(window, state);

            return state;
        }

        /// <summary>Whether another Window/Dialog/TopWindow exists.</summary>
        public static bool ExistOtherWindowOrDialog<T>() where T : AbsUISystem
        {
            var windowLayer = LayerIndex(WindowLayer.Window);
            var dialogLayer = LayerIndex(WindowLayer.Dialog);
            var topWindowLayer = LayerIndex(WindowLayer.TopWindow);

            foreach (var layer in LayerViews)
            {
                foreach (var window in layer)
                {
                    if (window is T)
                    {
                        continue;
                    }
                    var state = GetWindowState(window);
                    if (state.Layer == windowLayer || state.Layer == dialogLayer || state.Layer == topWindowLayer)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Whether another Window exists.</summary>
        public static bool ExistOtherWindow<T>() where T : AbsUISystem
        {
            var windowLayer = LayerIndex(WindowLayer.Window);

            foreach (var layer in LayerViews)
            {
                foreach (var window in layer)
                {
                    if (window is T)
                    {
                        continue;
                    }
                    var state = GetWindowState(window);
                    if (state.Layer == windowLayer)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Window state.</summary>
        private class WindowState
        {
            public AbsUISystem Window;

            /// <summary>所在层在层表里的下标（<see cref="UILayerTable"/> 的下标，-1 = 无）。</summary>
            public int Layer;
            public string SceneKey;
            public AsyncOperationHandle<GameObject> ViewLoadOperation;
            public Canvas Canvas;
            public GraphicRaycaster CanvasRaycaster;
            public GameObject View;
            public ViewAnimationHook ViewAnimationHook;
        }

        /// <summary>
        /// 默认层名表（顺序即默认层级）。仅用于把「按默认层名查找」的 API 解析到层表下标：
        /// 实际层集合是数据（<see cref="UILayerTable"/>），可增删改名与拖动排序，
        /// 名字对不上时 <see cref="LayerIndex"/> 返回 -1（相等判断不命中），并在初始化时一次性报错。
        /// </summary>
        private enum WindowLayer
        {
            HUD,
            HUDNotice,
            Window,
            WindowNotice,
            Dialog,
            TopWindow,
            ToolTip,
            Loading,
            Cursor,
            Mask,
        }

        #region 碰撞检测

        private static PointerEventData m_UIRaycasterEvent;

        private static readonly List<RaycastResult> m_UIRaycasterList = new List<RaycastResult>();

        /// <summary>
        /// Whether the given screen-space point hits any interactive object.
        /// 逐层问层表的 blockInput 标记（不按名）：默认在 Loading / Dialog / Window 三层判定，
        /// 增层或改名后在设置页勾「挡输入」即可纳入。
        /// </summary>
        public static bool HitTest(Vector2 point)
        {
            for (var i = LayerViews.Count - 1; i >= 0; i--)
            {
                if (!UILayerTable.BlocksInput(i))
                    continue;
                if (HitTestLayer(i, point))
                    return true;
            }

            return false;
        }

        private static bool HitTestLayer(int layer, Vector2 point)
        {
            var windowList = LayerViews[layer];

            for (int i = windowList.Count - 1; i >= 0; i--)
            {
                var window = windowList[i];
                if (window == FocusedWindow)
                {
                    var windowState = GetWindowState(windowList[i]);
                    if (windowState.CanvasRaycaster != null && HitTest(windowState.CanvasRaycaster, point))
                        return true;

                    if (windowState.View != null)
                    {
                        var subCanvasRaycaster = windowState.View.GetComponentsInChildren<GraphicRaycaster>();
                        if (subCanvasRaycaster.Length > 0)
                        {
                            foreach (var raycaster in subCanvasRaycaster)
                            {
                                if (HitTest(raycaster, point))
                                    return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static bool HitTest(GraphicRaycaster canvasCaster, Vector2 point)
        {
            m_UIRaycasterEvent = m_UIRaycasterEvent ?? new PointerEventData(EventSystem.current);
            m_UIRaycasterEvent.pressPosition = point;
            m_UIRaycasterEvent.position = point;

            m_UIRaycasterList.Clear();
            canvasCaster.Raycast(m_UIRaycasterEvent, m_UIRaycasterList);
            foreach (RaycastResult item in m_UIRaycasterList)
            {
                Selectable selectable = item.gameObject.GetComponentInParent<Selectable>();
                if (selectable)
                    return selectable && selectable.interactable;

                if (item.gameObject.GetComponentInParent<IPointerDownHandler>() != null)
                    return true;
                if (item.gameObject.GetComponentInParent<IPointerUpHandler>() != null)
                    return true;
                if (item.gameObject.GetComponentInParent<ISelectHandler>() != null)
                    return true;
            }
            m_UIRaycasterList.Clear();

            return false;
        }

        /// <summary>
        /// Whether the given object and screen-space point collide.
        /// </summary>
        public static bool HitTest(GameObject gameObject, Vector2 point)
        {
            bool hited = false;
            if (gameObject)
            {
                GraphicRaycaster canvasCaster = gameObject.GetComponentInParent<GraphicRaycaster>();
                if (canvasCaster)
                {
                    m_UIRaycasterEvent = m_UIRaycasterEvent ?? new PointerEventData(EventSystem.current);
                    m_UIRaycasterEvent.pressPosition = point;
                    m_UIRaycasterEvent.position = point;

                    m_UIRaycasterList.Clear();
                    canvasCaster.Raycast(m_UIRaycasterEvent, m_UIRaycasterList);
                    foreach (RaycastResult item in m_UIRaycasterList)
                    {
                        Transform curr = item.gameObject.transform;
                        while (curr != null)
                        {
                            if (curr.gameObject == gameObject)
                            {
                                hited = true;
                                break;
                            }
                            curr = curr.parent;
                        }

                        if (hited)
                            break;
                    }
                    m_UIRaycasterList.Clear();
                }
            }

            return hited;
        }

        #endregion 碰撞检测
    }
}