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
    /// Decoupled QK port of the source <c>UIManager</c>: the game
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
            Setting = new UIManagerSetting();

            try
            {
                var setting = World.EntityManager.CreateEntityQuery(typeof(StartupSetting)).GetSingleton<StartupSetting>();
                if (setting != null)
                {
                    Setting = new UIManagerSetting()
                    {
                        ReferenceResolution = setting.ReferenceResolution,
                        MaximumResolution = setting.MaximumResolution,
                        NavigationKeyDeathArea = setting.NavigationKeyDeathArea,
                        NavigationKeyDelayTime = setting.NavigationKeyDelayTime,
                        NavigationKeyRepeateCount = setting.NavigationKeyRepeateCount,
                    };
                }
            }
            catch
            {
                // No StartupSetting entity — keep the default settings.
            }

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

            FocusedWindow = null;
        }

        /// <summary>Update.</summary>
        protected override void OnUpdate()
        {
            if (mLastRenderScreenSize.x != Screen.width || mLastRenderScreenSize.y != Screen.height)
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
            RootNode = new GameObject(GetType().Name);
            GameObject.DontDestroyOnLoad(RootNode);

            IdleNode = new GameObject("IdleNode").transform;
            IdleNode.SetParent(RootNode.transform);
            IdleNode.gameObject.SetActive(false);

            LayerNodes = new List<Transform>();
            LayerViews = new List<List<AbsUISystem>>();

            foreach (var item in Enum.GetValues(typeof(WindowLayer)))
            {
                var node = new GameObject(item.ToString());
                node.transform.SetParent(RootNode.transform);

                LayerNodes.Add(node.transform);
                LayerViews.Add(new List<AbsUISystem>());
            }
        }

        private static Camera ResolveUICamera()
        {
            return UICamera != null ? UICamera : Camera.main;
        }

        public static T OpenHud<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(WindowLayer.HUD, Type, uISystemType) as T;
        }

        public static T OpenHudNotice<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(WindowLayer.HUDNotice, Type, uISystemType) as T;
        }

        public static AbsUISystem OpenHudNotice(Type type, WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal)
        {
            return Open(type, WindowLayer.HUDNotice, Type, uISystemType);
        }

        public static T OpenHudSingleNotice<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Single) where T : AbsUISystem
        {
            return Open<T>(WindowLayer.HUDNotice, Type, uISystemType) as T;
        }

        public static T OpenWindow<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal, params object[] args) where T : AbsUISystem
        {
            return Open<T>(WindowLayer.Window, Type, uISystemType, args) as T;
        }

        public static T OpenWindowAndCloseOther<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal, params object[] args) where T : AbsUISystem
        {
            var windows = LayerViews[(int)WindowLayer.Window];
            for (var i = windows.Count - 1; i >= 0; i--)
            {
                var win = windows[i];
                if (win.GetType() != typeof(T))
                    Close(win);
            }

            return Open<T>(WindowLayer.Window, Type, uISystemType, args) as T;
        }

        public static T OpenWindowNotice<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(WindowLayer.WindowNotice, Type, uISystemType) as T;
        }

        public static T OpenDialog<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(WindowLayer.Dialog, Type, uISystemType) as T;
        }

        public static T OpenTopWindow<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(WindowLayer.TopWindow, Type, uISystemType) as T;
        }

        public static T OpenLoading<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(WindowLayer.Loading, Type, uISystemType) as T;
        }

        public static T OpenCursor<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(WindowLayer.Cursor, Type, uISystemType) as T;
        }

        public static T OpenMask<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(WindowLayer.Mask, Type, uISystemType) as T;
        }

        public static T OpenToolTip<T>(WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal) where T : AbsUISystem
        {
            return Open<T>(WindowLayer.ToolTip, Type, uISystemType) as T;
        }

        public static AbsUISystem OpenToolTip(Type type, WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal)
        {
            return Open(type, WindowLayer.ToolTip, Type, uISystemType);
        }

        public static bool HasWindow()
        {
            var windowLayer = (int)WindowLayer.Window;
            var dialogLayer = (int)WindowLayer.Dialog;

            return LayerViews[windowLayer].Count > 0 || LayerViews[dialogLayer].Count > 0;
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

        /// <summary>Open. Generic overload.</summary>
        private static AbsUISystem Open<T>(WindowLayer layer, WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal, params object[] args) where T : AbsUISystem
        {
            return Open(typeof(T), layer, Type, uISystemType, args);
        }

        private static AbsUISystem Open(Type type, WindowLayer layer, WindowCanvasType Type = WindowCanvasType.Normal, UISystemType uISystemType = UISystemType.Normal, params object[] args)
        {
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
            LayerViews[(int)layer].Add(window);

            //Move under the target layer node.
            if (windowState.Canvas != null)
                windowState.Canvas.transform.SetParent(LayerNodes[(int)layer]);

            //Record the window layer.
            windowState.Layer = layer;

            //Load the prefab.
            if (windowState.View == null && !windowState.ViewLoadOperation.IsValid())
            {
                LoadSceenBegin(window, Type, args);
            }

            return window;
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

            var hook = state.View.GetComponent<UIAnimationHook>();
            if (hook != null)
                state.ViewAnimationHook = new ViewAnimationHook(window, hook);

            window.View = state.View;
            window.Canvas = state.Canvas;
            window.Camera = state.Canvas.worldCamera;
            window.Enabled = true;
            window.OnViewOpen(args);

            ResetWindowOrder();
            ResetWindowFocus();
            ResetWindowFit();
            ResetGUIInput();
            ResetHudVisible();
        }

        private static bool IsHideAllUI = false;

        public static void HideAllUI()
        {
            var index = 0;

            foreach (var Views in LayerViews)
            {
                var layer = (WindowLayer)index;
                if (layer >= WindowLayer.Loading)
                    continue;

                foreach (var View in Views)
                {
                    View.Hide();
                }

                index++;
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

            ResetWindowFocus();
            ResetGUIInput();
            ResetHudVisible();
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
            foreach (var layer in LayerViews)
            {
                foreach (var window in layer)
                {
                    var state = GetWindowState(window);
                    if (state.Layer == WindowLayer.Window || state.Layer == WindowLayer.Dialog)
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

            foreach (var layer in LayerViews)
            {
                foreach (var window in layer)
                {
                    var state = GetWindowState(window);
                    if (state.Layer == WindowLayer.HUD || state.Layer == WindowLayer.HUDNotice)
                    {
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
            var max = (int)WindowLayer.Mask;
            var min = (int)WindowLayer.HUD;

            for (var i = max; i >= min; i--)
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
            foreach (var layer in LayerViews)
            {
                foreach (var window in layer)
                {
                    if (window is T)
                    {
                        continue;
                    }
                    var state = GetWindowState(window);
                    if (state.Layer == WindowLayer.Window || state.Layer == WindowLayer.Dialog || state.Layer == WindowLayer.TopWindow)
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
            foreach (var layer in LayerViews)
            {
                foreach (var window in layer)
                {
                    if (window is T)
                    {
                        continue;
                    }
                    var state = GetWindowState(window);
                    if (state.Layer == WindowLayer.Window)
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
            public WindowLayer Layer;
            public string SceneKey;
            public AsyncOperationHandle<GameObject> ViewLoadOperation;
            public Canvas Canvas;
            public GraphicRaycaster CanvasRaycaster;
            public GameObject View;
            public ViewAnimationHook ViewAnimationHook;
        }

        /// <summary>Window layers.</summary>
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
        /// </summary>
        public static bool HitTest(Vector2 point)
        {
            if (HitTest(WindowLayer.Loading, point))
                return true;
            if (HitTest(WindowLayer.Dialog, point))
                return true;
            if (HitTest(WindowLayer.Window, point))
                return true;

            return false;
        }

        private static bool HitTest(WindowLayer layer, Vector2 point)
        {
            var windowList = LayerViews[(int)layer];

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