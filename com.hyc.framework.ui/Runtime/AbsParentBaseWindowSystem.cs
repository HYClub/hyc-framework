using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace HYC.Framework.UI
{
    /// <summary>
    /// Parent window able to open/close sub-windows via <see cref="UIGroup"/>.
    /// Decoupled QK port: the source <c>UpdateGroup_B9_UI_2_Window</c> group is
    /// replaced by <see cref="UIManager.GetSystemGroup"/>.
    /// </summary>
    public abstract partial class AbsParentBaseWindowSystem : BaseWindowSystem
    {
        private Dictionary<AbsUISystem, WindowState> viewStates;

        protected override void OnCreate()
        {
            base.OnCreate();
            viewStates = new Dictionary<AbsUISystem, WindowState>();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            viewStates = null;
        }

        public override void OnViewClose()
        {
            base.OnViewClose();
            if (viewStates?.Count > 0)
            {
                foreach (var item in viewStates)
                {
                    Close(item.Key);
                }
                viewStates.Clear();
            }
        }

        protected T Open<T>(RectTransform parent, params object[] args) where T : AbsUISystem
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var group = UIManager.GetSystemGroup(typeof(T));
            var window = world.GetOrCreateSystemManaged<T>() as AbsUISystem;
            var windowState = GetWindowState(window);

            group.AddSystemToUpdateList(window);
            group.SortSystems();

            if (windowState.View == null && !windowState.LoadOperation.IsValid())
            {
                windowState.LoadOperation = Addressables.InstantiateAsync(window.PrefabKey);
                windowState.LoadOperation.Completed += (obj) =>
                {
                    if (windowState.LoadOperation.IsValid())
                    {
                        if (obj.Status == AsyncOperationStatus.Succeeded)
                        {
                            if (!windowState.LoadOperation.IsValid() || windowState.LoadOperation.Status != AsyncOperationStatus.Succeeded)
                            {
                                // ignore
                            }
                            else
                            {
                                windowState.View = windowState.LoadOperation.Result;
                                window.View = windowState.View;
                                window.View.name = window.View.name.Replace("(Clone)", "(SubWin)");
                                var rect = window.View.GetComponent<RectTransform>();
                                window.View.transform.SetParent(parent);
                                rect.anchoredPosition3D = Vector3.zero;
                                rect.offsetMin = Vector2.zero;
                                rect.offsetMax = Vector2.zero;
                                rect.localScale = Vector3.one;

                                window.Enabled = true;
                                window.OnViewOpen(args);
                            }
                        }
                    }
                    else
                    {
                        Addressables.Release(obj);
                    }
                };
            }

            return window as T;
        }

        protected void Close<T>(T window) where T : AbsUISystem
        {
            var windowState = GetWindowState(window);
            if (windowState.LoadOperation.IsValid())
            {
                Addressables.Release(windowState.LoadOperation);
            }

            if (windowState.View)
            {
                window.OnViewClose();

                windowState.LoadOperation = default;
                windowState.View = null;

                window.View = null;
                window.Enabled = false;
            }
        }

        protected bool IsOpened(AbsUISystem window)
        {
            var winState = GetWindowState(window);

            if (winState == null) return false;
            if (winState.LoadOperation.IsDone && winState.View == null) return false;
            if (winState.Window.Enabled == false) return false;
            if (winState.View == null && winState.LoadOperation.IsValid()) return false;

            return true;
        }

        /// <summary>Get window state.</summary>
        private WindowState GetWindowState(AbsUISystem window)
        {
            if (viewStates.TryGetValue(window, out WindowState state)) return state;

            state = new WindowState
            {
                Window = window,
                LoadOperation = default,
                View = null,
            };

            viewStates.Add(window, state);
            return state;
        }

        /// <summary>Window state.</summary>
        private class WindowState
        {
            public AbsUISystem Window;
            public AsyncOperationHandle<GameObject> LoadOperation;
            public GameObject View;
        }
    }

    public abstract partial class AbsParentBaseWindowSystem<T> : AbsParentBaseWindowSystem where T : IComponentBinder, new()
    {
        private T mComponentBinder;

        public T Binder
        {
            get
            {
                if (mComponentBinder == null)
                {
                    mComponentBinder = new T();
                    mComponentBinder.Reset(View);
                }

                return mComponentBinder;
            }
        }

        public override void OnViewClose()
        {
            base.OnViewClose();

            mComponentBinder = default;
        }
    }
}