using HYC.Framework.Dots;
using HYC.Framework.Input;
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
    /// Decoupled QK port: the game-generated <c>HotkeyID</c> enum is replaced by
    /// action-name <see cref="string"/>s (see <see cref="HotkeyActionNames"/>), and
    /// the source <c>InputManager</c> static API is served by
    /// <see cref="HotkeyManager"/>.
    /// </summary>
    [DisableAutoCreation]
    public abstract partial class AbsUISystem : SystemBase
    {
        /// <summary>Optional UI scene key loaded additively before the prefab.</summary>
        public virtual string SceneKey => string.Empty;

        /// <summary>Addressable prefab key that instantiates the window view.</summary>
        public abstract string PrefabKey { get; }

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
            if (Focusable) Enable();
            _targetAlpha = 1;
        }

        protected override void OnUpdate()
        {
            if (View != null)
            {
                if (Focused)
                    UpdateByFocusManager();

                OnViewUpdate();
            }
        }

        public virtual void OnViewUpdate() { }

        public virtual void OnViewClose()
        {
            if (Focusable) Disable();
            UnregisterAllHotkey();
            CloseAllWindowPart();
        }

        public virtual void OnViewFocus()
        {
            SetHotkeySilence(false);
            foreach (var part in _partList)
                part.OnViewFocus();
        }

        public virtual void OnViewLost()
        {
            SetHotkeySilence(true);
            foreach (var part in _partList)
                part.OnViewLost();
        }

        public virtual void OnViewLayerChanged(int sortingLayer)
        {
            ViewSortingOrder = sortingLayer;
        }

        public virtual void Hide() { _targetAlpha = 0; }
        public virtual void Show() { _targetAlpha = 1; }

        #region 部件窗口

        private class PartState
        {
            public Transform Parent;
            public GameObject View;
            public AsyncOperationHandle<GameObject> LoadOperation;
        }

        private readonly List<BaseWindowPart> _partList = new List<BaseWindowPart>();
        private readonly Dictionary<BaseWindowPart, PartState> _partMap = new Dictionary<BaseWindowPart, PartState>();

        public T OpenWindowPart<T>() where T : BaseWindowPart
            => (T)OpenWindowPart(typeof(T), null, null);

        public T OpenWindowPart<T>(Transform parent) where T : BaseWindowPart
            => (T)OpenWindowPart(typeof(T), parent, null);

        public T OpenWindowPart<T>(GameObject windowPart) where T : BaseWindowPart
            => (T)OpenWindowPart(typeof(T), null, windowPart.transform);

        public T OpenWindowPart<T>(Transform parent, GameObject windowPart) where T : BaseWindowPart
            => (T)OpenWindowPart(typeof(T), parent, windowPart.transform);

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
                    if (partState.LoadOperation.IsValid())
                    {
                        if (obj.Status == AsyncOperationStatus.Succeeded)
                        {
                            partState.View = obj.Result;
                            partState.View.transform.SetParent(partState.Parent != null ? partState.Parent : View.transform, false);

                            partSystem.View = partState.View;
                            partSystem.Loaded = true;
                            partSystem.SetCloseHandler(() => CloseWindowPart(partSystem));
                            partSystem.OnViewOpen();
                        }
                        else
                        {
                            Debug.LogError($"{partSystem.GetType().Name} 预置体加载失败! ({partSystem.Key})");
                        }
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

        #endregion 部件窗口

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