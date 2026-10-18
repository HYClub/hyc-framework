using HYC.Framework.Dots;
using HYC.Framework.Input;
using HYC.Framework.RedDot;
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HYC.Framework.UI
{
    /// <summary>
    /// Base class for every ECS UI window/HUD/dialog/loading system. A SystemBase
    /// whose view (a prefab instantiated via <see cref="PrefabKey"/>) is fed by
    /// <see cref="UIManager"/>: it loads an optional UI <see cref="SceneKey"/>,
    /// instantiates the prefab under a world-space Canvas, and manages focus,
    /// hotkey registrations and child window parts.
    ///
    /// Decoupled HYC port: the game-generated <c>HotkeyID</c> enum is replaced by
    /// action-name <see cref="string"/>s (see <see cref="HotkeyActionNames"/>), and
    /// the source <c>InputManager</c> static API is served by
    /// <see cref="HotkeyManager"/>.
    /// </summary>
    [DisableAutoCreation]
    public abstract partial class AbsUISystem : SystemBase
    {
        /// <summary>Optional UI scene key loaded additively before the prefab.</summary>
        public virtual string SceneKey => string.Empty;

        /// <summary>Addressable prefab key that instantiates the window view.
        /// 内嵌 UI 允许为 null —— 改由 <see cref="Open{T}(GameObject, Transform)"/> 传入已有 GameObject。</summary>
        public virtual string PrefabKey => null;

        /// <summary>TopLevel = 由 UIManager 打开（有层/Canvas/焦点）；
        /// Embedded = 由宿主 <see cref="Open{T}(Transform, object[])"/> 打开（无层/Canvas/焦点，可多实例）。</summary>
        public UIMode Mode { get; set; } = UIMode.TopLevel;

        /// <summary>视图是否已就绪（内嵌 UI 由 Open 流程置位）。</summary>
        public bool Loaded { get; set; }

        /// <summary>Sorting order within the window layer.</summary>
        public virtual int Order => 0;

        /// <summary>Whether the window participates in focus navigation.</summary>
        public virtual bool Focusable => true;

        /// <summary>Normal vs single-instance window type.</summary>
        public UISystemType UISystemType = UISystemType.Normal;

        public Canvas Canvas { get; set; }
        public Camera Camera { get; set; }
        public GameObject View { get; set; }
        public CanvasGroup CanvasGroup { get; set; }

        /// <summary>Last sorting order assigned by the UIManager for this view.</summary>
        public int ViewSortingOrder { get; private set; }

        private float _targetAlpha = 1;

        /// <summary>Whether this window currently holds UI focus.</summary>
        protected bool Focused => Focusable && UIManager.FocusedWindow == this;

        private List<HotkeyHandle> _hotkeys;

        public virtual void OnAnimationEvent(string key) { }

        public virtual void OnSceneOpen(Scene scene, params object[] args) => OnSceneOpen(scene);
        public virtual void OnSceneOpen(Scene scene) { }

        public virtual void OnViewOpen(params object[] args) => OnViewOpen();
        public virtual void OnViewOpen()
        {
            // 内嵌 UI 不进 UIManager 的层列表，永远拿不到焦点，故不注册导航热键。
            if (Focusable && Mode == UIMode.TopLevel) Enable();
            _targetAlpha = 1;
            EnsureRedDotsBound();
        }

        protected override void OnUpdate()
        {
            if (View != null)
            {
                // 兜底：宿主子类若 override 了 OnViewOpen 却没调 base，这里补一次绑定（只尝试一次）。
                if (!_redDotsBound)
                    EnsureRedDotsBound();

                if (Focused)
                    UpdateByFocusManager();

                OnViewUpdate();
            }
        }

        public virtual void OnViewUpdate() { }

        public virtual void OnViewClose()
        {
            ReleaseRedDots();
            if (Focusable && Mode == UIMode.TopLevel) Disable();
            UnregisterAllHotkey();
            CloseAllEmbedded();
        }

        public virtual void OnViewFocus()
        {
            SetHotkeySilence(false);
            foreach (var part in _embedList)
                part.OnViewFocus();
        }

        public virtual void OnViewLost()
        {
            SetHotkeySilence(true);
            foreach (var part in _embedList)
                part.OnViewLost();
        }

        public virtual void OnViewLayerChanged(int sortingLayer)
        {
            ViewSortingOrder = sortingLayer;
        }

        public virtual void Hide() { _targetAlpha = 0; }
        public virtual void Show() { _targetAlpha = 1; }

        #region 红点（融合在 UI 基类：打开自动绑、变化自动刷、关闭自动退）

        /// <summary>预制体上的一枚红点绑定（L1 自动收集 / L2 手动绑定共用）。</summary>
        private struct RedDotBinding
        {
            public RedDotBehaviour Behaviour;
            public RedDotKey Key;
        }

        private List<RedDotBinding> _redDotBindings;
        private Action<RedDotKey> _redDotHandler;
        private bool _redDotsBound;

        /// <summary>某红点节点当前是否亮（聚合结果）。</summary>
        public bool IsRedDotActive(RedDotKey key) => RedDotManager.IsActive(key);

        /// <summary>某红点节点当前的聚合计数。</summary>
        public int GetRedDotCount(RedDotKey key) => RedDotManager.GetCount(key);

        /// <summary>业务「已读」：只清该节点自身的亮与计数（不动子节点）。</summary>
        public void ConsumeRedDot(RedDotKey key) => RedDotManager.Consume(key);

        /// <summary>业务「全部已读」：清该节点及整棵子树的亮与计数。</summary>
        public void ConsumeRedDotWithChildren(RedDotKey key) => RedDotManager.ConsumeWithChildren(key);

        /// <summary>
        /// L2 绑定：把运行期实例化的红点组件绑到某个键（动态列表项用）。
        /// 同一组件重复绑定会覆盖键并立刻刷新。
        /// </summary>
        public void BindRedDot(RedDotKey key, RedDotBehaviour behaviour)
        {
            if (behaviour == null || !key.IsValid)
                return;

            EnsureRedDotSubscription();
            if (_redDotBindings == null)
                _redDotBindings = new List<RedDotBinding>();

            var binding = new RedDotBinding { Behaviour = behaviour, Key = key };
            for (var i = 0; i < _redDotBindings.Count; i++)
            {
                if (_redDotBindings[i].Behaviour != behaviour)
                    continue;
                _redDotBindings[i] = binding;
                ApplyRedDot(binding);
                return;
            }

            _redDotBindings.Add(binding);
            ApplyRedDot(binding);
        }

        /// <summary>解除某枚红点组件的绑定（列表项回收时调）。</summary>
        public void UnbindRedDot(RedDotBehaviour behaviour)
        {
            if (behaviour == null || _redDotBindings == null)
                return;

            for (var i = _redDotBindings.Count - 1; i >= 0; i--)
            {
                if (_redDotBindings[i].Behaviour != behaviour)
                    continue;
                _redDotBindings.RemoveAt(i);
                behaviour.ResetApplied();
            }
        }

        /// <summary>
        /// 收集 <see cref="View"/> 下全部 <see cref="RedDotBehaviour"/> 并绑定（L1 零代码）。
        /// 只执行一次；<see cref="View"/> 尚未就绪时直接返回，等下一次尝试。
        /// </summary>
        private void EnsureRedDotsBound()
        {
            if (_redDotsBound || View == null)
                return;

            _redDotsBound = true;

            var found = View.GetComponentsInChildren<RedDotBehaviour>(true);
            if (found.Length == 0)
                return;   // 没有红点：不订阅，零开销

            EnsureRedDotSubscription();
            if (_redDotBindings == null)
                _redDotBindings = new List<RedDotBinding>();

            for (var i = 0; i < found.Length; i++)
            {
                var behaviour = found[i];
                if (behaviour == null || !behaviour.Key.IsValid)
                    continue;
                _redDotBindings.Add(new RedDotBinding { Behaviour = behaviour, Key = behaviour.Key });
                ApplyRedDot(_redDotBindings[_redDotBindings.Count - 1]);
            }
        }

        private void EnsureRedDotSubscription()
        {
            if (_redDotHandler != null)
                return;
            _redDotHandler = OnRedDotChanged;
            RedDotManager.OnChanged += _redDotHandler;
        }

        /// <summary>增量刷新：只更新与变化键相关联的那几枚红点。</summary>
        private void OnRedDotChanged(RedDotKey key)
        {
            if (_redDotBindings == null)
                return;

            for (var i = 0; i < _redDotBindings.Count; i++)
            {
                if (_redDotBindings[i].Key != key)
                    continue;
                ApplyRedDot(_redDotBindings[i]);
            }
        }

        private static void ApplyRedDot(RedDotBinding binding)
        {
            if (binding.Behaviour == null)
                return;
            RedDotManager.TryGet(binding.Key, out var active, out var count);
            binding.Behaviour.Apply(active, count);
        }

        /// <summary>视图关闭：退订 + 复位所有已绑定的红点，避免下次打开残留亮态。</summary>
        private void ReleaseRedDots()
        {
            if (_redDotHandler != null)
            {
                RedDotManager.OnChanged -= _redDotHandler;
                _redDotHandler = null;
            }

            if (_redDotBindings != null)
            {
                for (var i = 0; i < _redDotBindings.Count; i++)
                {
                    if (_redDotBindings[i].Behaviour != null)
                        _redDotBindings[i].Behaviour.ResetApplied();
                }
                _redDotBindings.Clear();
            }

            _redDotsBound = false;
        }

        #endregion

        #region 嵌入 UI

        private class EmbedState
        {
            public Transform Parent;
            public GameObject View;
            public AsyncOperationHandle<GameObject> LoadOperation;
        }

        private readonly List<AbsUISystem> _embedList = new List<AbsUISystem>();
        private readonly Dictionary<AbsUISystem, EmbedState> _embedMap = new Dictionary<AbsUISystem, EmbedState>();

        /// <summary>由宿主打开的内嵌 UI 的关闭回调（由 <see cref="Open(Type, Transform, Transform, object[])"/> 装配）。</summary>
        internal Action CloseHandler;

        public void SetCloseHandler(Action handler) => CloseHandler = handler;

        /// <summary>内嵌 UI 自关闭（等价于宿主调用 <see cref="Close(AbsUISystem)"/>）。</summary>
        public void Close() => CloseHandler?.Invoke();

        /// <summary>
        /// 打开一个内嵌 UI —— 窗口与部件不再区分，任何 <see cref="AbsUISystem"/> 子类都能当父也能当子。
        /// 每次调用都创建新实例，故同类型可同时开多份、可递归嵌套。
        /// </summary>
        public T Open<T>(Transform parent = null, params object[] args) where T : AbsUISystem
            => (T)Open(typeof(T), parent, null, args);

        /// <summary>用已有 GameObject 打开内嵌 UI（不加载预制体）。</summary>
        public T Open<T>(GameObject view, Transform parent = null) where T : AbsUISystem
            => (T)Open(typeof(T), parent, view != null ? view.transform : null, null);

        /// <summary>内嵌 UI 的通用入口。</summary>
        public AbsUISystem Open(Type type, Transform parent, Transform view, params object[] args)
        {
            if (!typeof(AbsUISystem).IsAssignableFrom(type))
                throw new Exception($"{type} 不是一个AbsUISystem的子类!");

            var group = UIManager.GetSystemGroup(type);
            var child = (AbsUISystem)World.CreateSystemManaged(type);
            child.Mode = UIMode.Embedded;

            var state = new EmbedState
            {
                Parent = parent,
                View = view != null ? view.gameObject : null,
            };

            _embedList.Add(child);
            _embedMap.Add(child, state);
            group.AddSystemToUpdateList(child.SystemHandle);
            child.SetCloseHandler(() => Close(child));

            if (state.View != null)
            {
                state.View.transform.SetParent(state.Parent != null ? state.Parent : View.transform, false);
                child.View = state.View;
                child.Loaded = true;
                child.OnViewOpen(args ?? Array.Empty<object>());
            }
            else if (!string.IsNullOrEmpty(child.PrefabKey))
            {
                state.LoadOperation = Addressables.InstantiateAsync(child.PrefabKey);
                state.LoadOperation.Completed += (AsyncOperationHandle<GameObject> obj) =>
                {
                    if (!state.LoadOperation.IsValid())
                    {
                        Addressables.Release(obj);
                        return;
                    }

                    if (obj.Status != AsyncOperationStatus.Succeeded)
                    {
                        Debug.LogError($"{child.GetType().Name} 预置体加载失败! ({child.PrefabKey})");
                        return;
                    }

                    state.View = obj.Result;
                    state.View.transform.SetParent(state.Parent != null ? state.Parent : View.transform, false);
                    child.View = state.View;
                    child.Loaded = true;
                    child.OnViewOpen(args ?? Array.Empty<object>());
                };
            }
            else
            {
                Debug.LogError($"打开 {type.Name} 失败 : 既未设置 PrefabKey, 也未指定界面对象");
            }

            return child;
        }

        /// <summary>关闭并销毁一个内嵌 UI 实例。</summary>
        public void Close(AbsUISystem child)
        {
            if (child == null) return;

            child.OnViewClose();

            var group = UIManager.GetSystemGroup(child.GetType());
            group.RemoveSystemFromUpdateList(child.SystemHandle);

            if (_embedMap.TryGetValue(child, out var state))
            {
                if (state.LoadOperation.IsValid())
                    Addressables.Release(state.LoadOperation);

                state.View = null;
                state.Parent = null;

                child.View = null;
                child.Loaded = false;
                child.SetCloseHandler(null);
                _embedMap.Remove(child);
            }
            _embedList.Remove(child);

            if (child.SystemHandle != SystemHandle.Null)
            {
                try { World.DestroySystemManaged(child); }
                catch (Exception e) { Debug.LogWarning(e.Message); }
            }
        }

        /// <summary>该内嵌 UI 是否由自己打开且仍开着。</summary>
        public bool IsOpened(AbsUISystem child) => child != null && _embedMap.ContainsKey(child);

        private void CloseAllEmbedded()
        {
            var all = _embedList.ToArray();
            foreach (var item in all)
                Close(item);
        }

        #endregion 嵌入 UI

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

        #region 焦点处理

        private enum ButtonState { NONE, DOWN, PRESS, UP }

        private float _horizontalValue;
        private float _verticalValue;
        private ButtonState _horizontalButton = ButtonState.NONE;
        private ButtonState _verticalButton = ButtonState.NONE;
        private ButtonState _submitButton = ButtonState.NONE;
        private ButtonState _cancelButton = ButtonState.NONE;

        private GameObject _selection;
        private GameObject _selectionPrev;
        private GameObject _lastSelection;
        private Vector2 _lastMoveVector;
        private float _prevActionTime;
        private int _consecutiveMoveCount;

        private BaseEventData _baseEventData;

        private HotkeyHandle _uguiHorizontal;
        private HotkeyHandle _uguiVertical;
        private HotkeyHandle _uguiSubmit;
        private HotkeyHandle _uguiCancel;

        public void Enable()
        {
            _uguiHorizontal = RegisterHotkey(HotkeyActionNames.UI_UGUI_Horizontal, OnHorizontal);
            _uguiVertical = RegisterHotkey(HotkeyActionNames.UI_UGUI_Vertical, OnVertical);
            _uguiSubmit = RegisterHotkey(HotkeyActionNames.UI_UGUI_Submit, OnSubmit);
            _uguiCancel = RegisterHotkey(HotkeyActionNames.UI_UGUI_Cancel, OnCancel);

            HotkeyManager.OnNavigateModeChanged += OnNavigateModeChanged;
            HotkeyManager.OnInputDeviceChanged += OnInputDeviceChanged;

            RestoreSelection();
        }

        public void Disable()
        {
            _lastSelection = _selection;
            _selection = null;

            _horizontalValue = 0;
            _verticalValue = 0;

            _lastMoveVector = Vector2.zero;
            _prevActionTime = 0;
            _consecutiveMoveCount = 0;

            UnregisterHotkey(_uguiHorizontal);
            UnregisterHotkey(_uguiVertical);
            UnregisterHotkey(_uguiSubmit);
            UnregisterHotkey(_uguiCancel);

            HotkeyManager.OnNavigateModeChanged -= OnNavigateModeChanged;
            HotkeyManager.OnInputDeviceChanged -= OnInputDeviceChanged;
        }

        public GameObject GetSelection() => _selection;

        public void SetSelection(GameObject selection)
        {
            selection = selection != null && selection.activeSelf && selection.activeInHierarchy && View != null && IsChild(View, selection) ? selection : null;
            if (selection)
            {
                _selection = selection;
                _selectionPrev = selection;

                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != _selection)
                    EventSystem.current.SetSelectedGameObject(_selection);
            }
            else
            {
                _selection = null;
                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void OnNavigateModeChanged(bool isNavigate)
            => OnInputDeviceChanged(HotkeyManager.CurrentDevice);

        private void OnInputDeviceChanged(InputDevice device)
        {
            if (HotkeyManager.IsNavigateMode) return;

            if (HotkeyManager.IsKeyboardMouseDevice)
            {
                GameObject selection = GetCurrentSelection();
                if (selection == null)
                {
                    if (_selectionPrev != null && _selectionPrev.activeSelf && _selectionPrev.activeInHierarchy && View != null && IsChild(View, _selectionPrev))
                        selection = _selectionPrev;
                    else
                        selection = FindFirstSelection();
                }
                SetSelection(selection);
            }
            else
            {
                if (EventSystem.current != null
                    && !UIManager.HitTest(EventSystem.current.currentSelectedGameObject, HotkeyManager.CurrentCursorPosition))
                    SetSelection(null);
            }
        }

        public void RestoreSelection()
        {
            GameObject prevSelection = _lastSelection;

            if (HotkeyManager.IsKeyboardMouseDevice || HotkeyManager.IsNavigateMode)
            {
                if (prevSelection != null && prevSelection.activeInHierarchy && View != null && IsChild(View, prevSelection))
                    SetSelection(prevSelection);
                else
                    SetSelection(FindFirstSelection());
            }

            OnNavigateModeChanged(HotkeyManager.IsNavigateMode);
        }

        private void OnHorizontal(HotkeyInputContext callback)
        {
            float oldValue = _horizontalValue;
            float newValue = callback.ReadValue<float>();

            if (Mathf.Approximately(oldValue, 0.0f))
            {
                if (!Mathf.Approximately(newValue, 0.0f)) _horizontalButton = ButtonState.DOWN;
            }
            else if (Mathf.Approximately(newValue, 0.0f))
            {
                _horizontalButton = ButtonState.NONE;
            }
            _horizontalValue = newValue;
        }

        private void OnVertical(HotkeyInputContext callback)
        {
            float oldValue = _verticalValue;
            float newValue = callback.ReadValue<float>();

            if (Mathf.Approximately(oldValue, 0.0f))
            {
                if (!Mathf.Approximately(newValue, 0.0f)) _verticalButton = ButtonState.DOWN;
            }
            else if (Mathf.Approximately(newValue, 0.0f))
            {
                _verticalButton = ButtonState.NONE;
            }
            _verticalValue = newValue;
        }

        private void OnSubmit(HotkeyInputContext callback)
        {
            if (callback.started)
                _submitButton = _submitButton == ButtonState.DOWN ? ButtonState.PRESS : ButtonState.DOWN;
            else if (callback.performed)
                _submitButton = ButtonState.UP;
        }

        private void OnCancel(HotkeyInputContext callback)
        {
            if (callback.started)
                _cancelButton = _cancelButton == ButtonState.DOWN ? ButtonState.PRESS : ButtonState.DOWN;
            else if (callback.performed)
                _cancelButton = ButtonState.UP;
        }

        private void UpdateByFocusManager()
        {
            if (!HotkeyManager.IsNavigateMode)
            {
                _consecutiveMoveCount = 0;
                _submitButton = ButtonState.NONE;
                _cancelButton = ButtonState.NONE;
                return;
            }

            if (Mathf.Approximately(_horizontalValue, 0f) && Mathf.Approximately(_verticalValue, 0f))
            {
                _consecutiveMoveCount = 0;
            }
            else
            {
                var rawMoveVector = new Vector2(_horizontalValue, _verticalValue);
                var isCurrFramePress = _horizontalButton == ButtonState.DOWN || _verticalButton == ButtonState.DOWN;
                var sameDirection = Vector2.Dot(rawMoveVector, _lastMoveVector) > 0f;

                if (!isCurrFramePress)
                {
                    isCurrFramePress = sameDirection && _consecutiveMoveCount == 1
                        ? (UnityEngine.Time.unscaledTime > _prevActionTime + UIManager.Setting.NavigationKeyDelayTime)
                        : (UnityEngine.Time.unscaledTime > _prevActionTime + 1f / UIManager.Setting.NavigationKeyRepeateCount);
                }

                if (isCurrFramePress)
                {
                    var moveDir = MoveDirection.None;

                    if (rawMoveVector.sqrMagnitude >= UIManager.Setting.NavigationKeyDeathArea * UIManager.Setting.NavigationKeyDeathArea)
                    {
                        moveDir = Mathf.Abs(rawMoveVector.x) > Mathf.Abs(rawMoveVector.y)
                            ? (rawMoveVector.x > 0f ? MoveDirection.Right : MoveDirection.Left)
                            : (rawMoveVector.y > 0f ? MoveDirection.Up : MoveDirection.Down);
                    }

                    if (moveDir != MoveDirection.None)
                    {
                        GameObject oldSelection = GetCurrentSelection();
                        if (oldSelection == null && _selectionPrev != null && _selectionPrev.activeInHierarchy && _selectionPrev.activeSelf && View != null && IsChild(View, _selectionPrev))
                            oldSelection = _selectionPrev;

                        if (oldSelection != null)
                        {
                            GameObject newSelection = FindNextFocusBy(oldSelection, moveDir);
                            if (newSelection != null && _selection != newSelection)
                            {
                                SetSelection(newSelection);
                                var autoSelect = _selection != null ? _selection.GetComponent<AutoSelectOnGotFocus>() : null;
                                if (autoSelect && EventSystem.current != null)
                                {
                                    _baseEventData = _baseEventData ?? new BaseEventData(EventSystem.current);
                                    _baseEventData.Reset();
                                    ExecuteEvents.Execute<ISubmitHandler>(_selection, _baseEventData, ExecuteEvents.submitHandler);
                                }
                            }
                            if (newSelection == null)
                            {
                                SetSelection(oldSelection);
                            }
                        }
                        else
                        {
                            SetSelection(FindFirstSelection());
                        }

                        _consecutiveMoveCount = sameDirection ? _consecutiveMoveCount : 0;
                        _consecutiveMoveCount++;
                        _prevActionTime = UnityEngine.Time.unscaledTime;
                        _lastMoveVector = rawMoveVector;
                    }
                    else
                    {
                        _consecutiveMoveCount = 0;
                    }
                }
            }

            if (_selection != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _selection)
            {
                if (_submitButton == ButtonState.DOWN || _cancelButton == ButtonState.DOWN)
                {
                    _baseEventData = _baseEventData ?? new BaseEventData(EventSystem.current);
                    _baseEventData.Reset();

                    if (_submitButton == ButtonState.DOWN)
                        ExecuteEvents.Execute<ISubmitHandler>(_selection, _baseEventData, ExecuteEvents.submitHandler);
                    if (_cancelButton == ButtonState.DOWN)
                        ExecuteEvents.Execute<ICancelHandler>(_selection, _baseEventData, ExecuteEvents.cancelHandler);
                }
            }

            _submitButton = TransportButtonState(_submitButton);
            _cancelButton = TransportButtonState(_cancelButton);
            _horizontalButton = TransportButtonState(_horizontalButton);
            _verticalButton = TransportButtonState(_verticalButton);
        }

        private GameObject GetCurrentSelection()
        {
            GameObject curr = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (curr != null && curr.gameObject.activeInHierarchy && View != null && IsChild(View, curr))
                return curr;

            if (_selection != null && _selection.activeInHierarchy && View != null && IsChild(View, _selection))
                return _selection;
            return null;
        }

        private GameObject FindFirstSelection()
        {
            if (View != null)
            {
                var focusables = View.GetComponentsInChildren<FocusableCustom>();
                foreach (var focusable in focusables)
                {
                    if (focusable.isActiveAndEnabled)
                        return focusable.gameObject;
                }

                var selectables = View.GetComponentsInChildren<Selectable>();
                foreach (var selectable in selectables)
                {
                    if (selectable.isActiveAndEnabled && selectable.IsInteractable() && selectable.navigation.mode != Navigation.Mode.None)
                        return selectable.gameObject;
                }
            }
            return null;
        }

        private bool IsChild(GameObject parent, GameObject child)
        {
            if (child)
            {
                Transform curr = child.transform;
                while (curr != null)
                {
                    if (curr.gameObject == parent) return true;
                    curr = curr.parent;
                }
            }
            return false;
        }

        private ButtonState TransportButtonState(ButtonState state)
        {
            switch (state)
            {
                case ButtonState.DOWN: return ButtonState.PRESS;
                case ButtonState.UP: return ButtonState.NONE;
                default: return state;
            }
        }

        private GameObject FindNextFocusBy(GameObject currentFocus, MoveDirection moveDir)
        {
            if (currentFocus.GetComponent<TMPro.TMP_InputField>() != null)
                return currentFocus;
            if (currentFocus.GetComponent<InputField>() != null)
                return currentFocus;

            var focusable = currentFocus.GetComponent<FocusableCustom>();
            if (focusable && focusable.isActiveAndEnabled)
                return focusable.OnMove(moveDir) ?? currentFocus;

            var selectable = currentFocus.GetComponent<Selectable>();
            if (selectable)
            {
                if (selectable.navigation.mode == Navigation.Mode.Explicit)
                {
                    Selectable next = null;
                    switch (moveDir)
                    {
                        case MoveDirection.Left: next = selectable.navigation.selectOnLeft; break;
                        case MoveDirection.Up: next = selectable.navigation.selectOnUp; break;
                        case MoveDirection.Right: next = selectable.navigation.selectOnRight; break;
                        case MoveDirection.Down: next = selectable.navigation.selectOnDown; break;
                    }
                    if (next != null) return next.gameObject;
                }

                if (selectable.navigation.mode != Navigation.Mode.None)
                {
                    switch (moveDir)
                    {
                        case MoveDirection.Left: return FindNextFocusBy(selectable.gameObject, selectable.transform.rotation * Vector3.left);
                        case MoveDirection.Up: return FindNextFocusBy(selectable.gameObject, selectable.transform.rotation * Vector3.up);
                        case MoveDirection.Right: return FindNextFocusBy(selectable.gameObject, selectable.transform.rotation * Vector3.right);
                        case MoveDirection.Down: return FindNextFocusBy(selectable.gameObject, selectable.transform.rotation * Vector3.down);
                    }
                }
            }
            return null;
        }

        public GameObject FindNextFocusBy(GameObject currentFocus, Vector3 dir)
        {
            dir = dir.normalized;

            var moveDirection = Quaternion.Inverse(currentFocus.transform.rotation) * dir;
            var referencePoint = currentFocus.transform.TransformPoint(GetPointOnRectEdge(currentFocus.transform as RectTransform, moveDirection));

            GameObject nextFocus = null;
            float weight = float.NegativeInfinity;

            if (View == null) return null;

            var selectables = View.GetComponentsInChildren<Selectable>();
            var focusables = View.GetComponentsInChildren<FocusableCustom>();
            int count = selectables.Length + focusables.Length;
            for (int i = 0; i < count; i++)
            {
                GameObject curr = i < selectables.Length ? selectables[i].gameObject : focusables[i - selectables.Length].gameObject;
                if (curr == currentFocus) continue;

                bool active = i < selectables.Length
                    ? (selectables[i].isActiveAndEnabled && selectables[i].IsInteractable() && selectables[i].navigation.mode != Navigation.Mode.None)
                    : focusables[i - selectables.Length].isActiveAndEnabled;

                if (active)
                {
                    var rect = curr.GetComponent<RectTransform>();
                    var targetPoint = rect.TransformPoint(rect.rect.center) - referencePoint;
                    float targetDirection = Vector3.Dot(dir, targetPoint);
                    if (targetDirection > 0f)
                    {
                        float value = targetDirection / targetPoint.sqrMagnitude;
                        if (value > weight)
                        {
                            weight = value;
                            nextFocus = rect.gameObject;
                        }
                    }
                }
            }
            return nextFocus;
        }

        private static Vector3 GetPointOnRectEdge(RectTransform rect, Vector2 dir)
        {
            if (rect != null)
            {
                if (dir != Vector2.zero)
                    dir /= Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));
                return rect.rect.center + Vector2.Scale(rect.rect.size, dir * 0.5f);
            }
            return Vector3.zero;
        }

        public class FocusableCustom : MonoBehaviour
        {
            public virtual GameObject OnMove(MoveDirection dir) => null;
        }

        #endregion 焦点处理

        #region 引导弹窗

        protected void OpenGuide(int messageId, bool isRepeated = false)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            var beginSimulationEcbSystem = world.GetExistingSystemManaged<BeginSimulationEntityCommandBufferSystem>();
            if (beginSimulationEcbSystem == null) return;
            var ecb = beginSimulationEcbSystem.CreateCommandBuffer();
            var e = ecb.CreateEntity();
            ecb.AddComponent<MessageEntity>(e);
            ecb.AddComponent(e, new EventMessage
            {
                EventID = messageId,
                IsRepeated = isRepeated,
            });
        }

        #endregion 引导弹窗
    }

    /// <summary>
    /// UI 实例的挂载形态。统一链路后窗口与部件是同一条继承链，区别只在打开方式：
    /// TopLevel 由 UIManager 打开（有层/Canvas/焦点，按类型单例）；
    /// Embedded 由宿主 <c>Open</c> 打开（无层/Canvas/焦点，可多实例、可递归嵌套）。
    /// </summary>
    public enum UIMode
    {
        TopLevel,
        Embedded,
    }

    public enum WindowCanvasType
    {
        Normal,
        // Curved removed (game/CurvedUI-specific).
    }

    public enum UISystemType
    {
        Normal,
        Single, // 此类型UI只能同时存在一个
    }
}