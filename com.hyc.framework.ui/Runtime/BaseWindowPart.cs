using HYC.Framework.Input;
using HYC.Framework.UI;
using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace HYC.Framework.UI
{
    /// <summary>
    /// Reusable window "part" that can be opened/closed under a parent window.
    /// A SystemBase whose view loads from <see cref="Key"/> (or a supplied object)
    /// and updates while registered. Decoupled QK port: uses string action names
    /// for hotkeys instead of the game HotkeyID enum.
    /// </summary>
    public abstract partial class BaseWindowPart : SystemBase
    {
        /// <summary>Addressable prefab key.</summary>
        public virtual string Key => null;

        /// <summary>Instantiated view.</summary>
        public GameObject View { get; set; }

        /// <summary>Whether the view is loaded.</summary>
        public bool Loaded { get; set; }

        internal Action CloseHandler;

        internal Action mCloseHandler { get => CloseHandler; set => CloseHandler = value; }

        private List<HotkeyHandle> _hotkeys;

        public void SetCloseHandler(Action handler) => CloseHandler = handler;

        public virtual void OnViewOpen() { }

        protected override void OnUpdate()
        {
            if (View != null)
                OnViewUpdate();
        }

        public virtual void OnViewUpdate() { }

        public virtual void OnViewClose()
        {
            UnregisterAllHotkey();
            CloseAllWindowPart();
        }

        public virtual void OnViewFocus() => SetHotkeySilence(false);
        public virtual void OnViewLost() => SetHotkeySilence(true);

        public virtual void Hide() { }
        public virtual void Show() { }

        #region 窗口部件

        private class PartState
        {
            public Transform Parent;
            public GameObject View;
            public AsyncOperationHandle<GameObject> LoadOperation;
        }

        private readonly List<BaseWindowPart> _partList = new List<BaseWindowPart>();
        private readonly Dictionary<BaseWindowPart, PartState> _partMap = new Dictionary<BaseWindowPart, PartState>();

        public T OpenWindowPart<T>() where T : BaseWindowPart => (T)OpenWindowPart(typeof(T), null, null);
        public T OpenWindowPart<T>(Transform parent) where T : BaseWindowPart => (T)OpenWindowPart(typeof(T), parent, null);
        public T OpenWindowPart<T>(GameObject windowPart) where T : BaseWindowPart => (T)OpenWindowPart(typeof(T), null, windowPart.transform);
        public T OpenWindowPart<T>(Transform parent, GameObject windowPart) where T : BaseWindowPart => (T)OpenWindowPart(typeof(T), parent, windowPart.transform);

        public BaseWindowPart OpenWindowPart(Type type, Transform parent, Transform windowPart)
        {
            if (!typeof(BaseWindowPart).IsAssignableFrom(type))
                throw new Exception($"{type} 不是一个BaseWindowPart的子类!");

            var group = UIManager.GetSystemGroup(type);
            var partSystem = (BaseWindowPart)World.CreateSystemManaged(type);
            var partState = new PartState
            {
                Parent = parent,
                View = windowPart != null ? windowPart.gameObject : null,
            };

            _partList.Add(partSystem);
            _partMap.Add(partSystem, partState);
            group.AddSystemToUpdateList(partSystem.SystemHandle);

            if (windowPart != null)
            {
                if (partState.Parent != null)
                    partState.View.transform.SetParent(partState.Parent, false);
                else
                    partState.View.transform.SetParent(View.transform, false);

                partSystem.View = partState.View;
                partSystem.Loaded = true;
                partSystem.SetCloseHandler(() => CloseWindowPart(partSystem));
                partSystem.OnViewOpen();
            }
            else if (!string.IsNullOrEmpty(partSystem.Key))
            {
                partState.LoadOperation = Addressables.InstantiateAsync(partSystem.Key);
                partState.LoadOperation.Completed += (AsyncOperationHandle<GameObject> obj) =>
                {
                    if (partState.LoadOperation.IsValid() && obj.Status == AsyncOperationStatus.Succeeded)
                    {
                        partState.View = obj.Result;
                        partState.View.transform.SetParent(partState.Parent != null ? partState.Parent : View.transform, false);

                        partSystem.View = partState.View;
                        partSystem.Loaded = true;
                        partSystem.SetCloseHandler(() => CloseWindowPart(partSystem));
                        partSystem.OnViewOpen();
                    }
                    else if (partState.LoadOperation.IsValid())
                    {
                        Debug.LogError($"{partSystem.GetType().Name} 预置体加载失败! ({partSystem.Key})");
                    }
                    else
                    {
                        Addressables.Release(obj);
                    }
                };
            }
            else
            {
                Debug.LogError($"打开 {type.Name} 失败 : WindowPart 既未设置 Key, 也未指定界面对象");
            }

            return partSystem;
        }

        public void Close()
        {
            CloseHandler?.Invoke();
        }

        public void CloseWindowPart(BaseWindowPart part)
        {
            if (part == null) return;

            part.OnViewClose();

            var group = UIManager.GetSystemGroup(part.GetType());
            group.RemoveSystemFromUpdateList(part.SystemHandle);

            if (_partMap.TryGetValue(part, out var state))
            {
                if (state.LoadOperation.IsValid())
                    Addressables.Release(state.LoadOperation);
                state.View = null;
                state.Parent = null;

                part.View = null;
                part.Loaded = false;
                part.SetCloseHandler(null);
                _partMap.Remove(part);
            }
            _partList.Remove(part);

            if (part.SystemHandle != SystemHandle.Null)
            {
                try { World.DestroySystemManaged(part); }
                catch (Exception e) { Debug.LogWarning(e.Message); }
            }
        }

        private void CloseAllWindowPart()
        {
            var all = _partList.ToArray();
            foreach (var item in all)
                CloseWindowPart(item);
        }

        #endregion 窗口部件

        #region 组件查找

        protected T FindComponent<T>(string path) where T : Component
        {
            var result = View != null ? View.transform.Find(path) : null;
            return result != null ? result.GetComponent<T>() : null;
        }

        protected T FindComponent<T>(Component node, string path) where T : Component
        {
            Transform result = node != null ? node.transform.Find(path) : null;
            return result ? result.GetComponent<T>() : default;
        }

        #endregion 组件查找

        #region 热键功能处理

        protected HotkeyHandle RegisterHotkey(string actionName, Action<HotkeyInputContext> callback, int priority = 0)
            => RegisterHotkey(actionName, callback, null, string.Empty, HotkeyElementStyle.UI, priority);

        protected HotkeyHandle RegisterHotkey(string actionName, BaseHotkeyElement hotkeyElement, Action<HotkeyInputContext> callback, int priority = 0)
        {
            var handle = HotkeyManager.RegisterHotkey(actionName, callback, 0, hotkeyElement != null ? hotkeyElement.transform : null, null, HotkeyElementStyle.UI, priority);
            if (_hotkeys == null) _hotkeys = new List<HotkeyHandle>();
            _hotkeys.Add(handle);
            return handle;
        }

        protected HotkeyHandle RegisterHotkey(string actionName, Action<HotkeyInputContext> callback, Transform parent, HotkeyElementStyle style = HotkeyElementStyle.UI, int priority = 0)
            => RegisterHotkey(actionName, callback, parent, string.Empty, style, priority);

        protected HotkeyHandle RegisterHotkey(string actionName, Action<HotkeyInputContext> callback, Transform parent, string text, HotkeyElementStyle style = HotkeyElementStyle.UI, int priority = 0)
            => RegisterHotkeyInternal(actionName, callback, 0, parent, text, style, priority);

        protected HotkeyHandle RegisterHotkey(string actionName, Action<HotkeyInputContext> callback, float holdTime, int priority = 0)
            => RegisterHotkey(actionName, callback, holdTime, null, string.Empty, HotkeyElementStyle.UI, priority);

        protected HotkeyHandle RegisterHotkey(string actionName, Action<HotkeyInputContext> callback, float holdTime, Transform parent, HotkeyElementStyle style = HotkeyElementStyle.UI, int priority = 0)
            => RegisterHotkey(actionName, callback, holdTime, parent, string.Empty, style, priority);

        protected HotkeyHandle RegisterHotkey(string actionName, Action<HotkeyInputContext> callback, float holdTime, Transform parent, string text, HotkeyElementStyle style = HotkeyElementStyle.UI, int priority = 0)
        {
            if (float.IsNaN(holdTime)) holdTime = 0.1f;
            if (holdTime < 0.1f) holdTime = 0.1f;
            return RegisterHotkeyInternal(actionName, callback, holdTime, parent, text, style, priority);
        }

        private HotkeyHandle RegisterHotkeyInternal(string actionName, Action<HotkeyInputContext> callback, float holdTime, Transform parent, string text, HotkeyElementStyle style, int priority)
        {
            var handle = HotkeyManager.RegisterHotkey(actionName, callback, holdTime, parent, text, style, priority);
            if (_hotkeys == null) _hotkeys = new List<HotkeyHandle>();
            _hotkeys.Add(handle);
            return handle;
        }

        protected void UnregisterHotkey(HotkeyHandle handle)
        {
            if (_hotkeys == null) return;
            for (var i = 0; i < _hotkeys.Count; i++)
            {
                if (_hotkeys[i].ID == handle.ID)
                {
                    _hotkeys.RemoveAt(i);
                    break;
                }
            }
            HotkeyManager.UnregisterHotkey(handle);
        }

        private void UnregisterAllHotkey()
        {
            if (_hotkeys == null) return;
            for (var i = 0; i < _hotkeys.Count; i++)
                HotkeyManager.UnregisterHotkey(_hotkeys[i]);
            _hotkeys.Clear();
        }

        private void SetHotkeySilence(bool silence)
        {
            if (_hotkeys == null) return;
            for (var i = 0; i < _hotkeys.Count; i++)
            {
                var hotkey = _hotkeys[i];
                hotkey.Silence = silence;
                _hotkeys[i] = hotkey;
            }
        }

        #endregion 热键功能处理
    }

    public abstract partial class BaseWindowPart<T> : BaseWindowPart where T : IComponentBinder, new()
    {
        private T _componentBinder;

        public T Binder
        {
            get
            {
                if (_componentBinder == null)
                {
                    _componentBinder = new T();
                    _componentBinder.Reset(View);
                }
                return _componentBinder;
            }
        }

        public override void OnViewClose()
        {
            base.OnViewClose();
            _componentBinder = default;
        }
    }
}