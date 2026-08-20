using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HYC.Framework.Input
{
    /// <summary>Whether a registered hotkey is a tap (Press) or a held (Hold) binding.</summary>
    public enum HotkeyMode
    {
        Press,
        Hold,
    }

    /// <summary>
    /// Registry + dispatcher for action-name to callback hotkey bindings. A
    /// decoupled re-implementation of the source HotkeyManager that drops the
    /// game InputManager/StartupSetting dependencies: the owning host assigns
    /// <see cref="InputAsset"/> once, and callbacks are resolved by action name
    /// instead of a generated enum.
    /// </summary>
    public static class HotkeyManager
    {
        private static int _autoID;
        private static readonly Dictionary<int, HotkeyState> _idToState = new Dictionary<int, HotkeyState>();
        private static readonly Dictionary<string, List<int>> _pathToIDs = new Dictionary<string, List<int>>();
        private static bool _needStopInvoke;
        private static readonly List<int> _sortedIDs = new List<int>();

        /// <summary>Active input asset resolved against when hotkeys are registered.</summary>
        public static InputActionAsset InputAsset { get; set; }

        /// <summary>Currently detected device; used for icon/presentation routing.</summary>
        public static InputDevice CurrentDevice { get; set; } = InputDevice.KeyboardAndMouse;

        /// <summary>Last known virtual cursor position (screen space).</summary>
        public static Vector2 CurrentCursorPosition { get; set; }

        /// <summary>Whether the last input came from the gamepad navigation layer.</summary>
        public static bool IsNavigateMode { get; set; }

        public static event Action<InputDevice> OnInputDeviceChanged;
        public static event Action<bool> OnNavigateModeChanged;

        /// <summary>Whether the active device is keyboard + mouse.</summary>
        public static bool IsKeyboardMouseDevice => CurrentDevice == InputDevice.KeyboardAndMouse;

        public static void SetCurrentDevice(InputDevice device)
        {
            if (CurrentDevice == device) return;
            CurrentDevice = device;
            OnInputDeviceChanged?.Invoke(device);
            UpdateAllHotkeyIcons();
        }

        public static void SetNavigateMode(bool enabled)
        {
            if (IsNavigateMode == enabled) return;
            IsNavigateMode = enabled;
            OnNavigateModeChanged?.Invoke(enabled);
        }

        /// <summary>Assigns a new input asset and clears prior registrations.</summary>
        public static void BindAsset(InputActionAsset asset)
        {
            InputAsset = asset;
            UnregisterAll();
            InputSystem.onActionChange += OnActionChange;
        }

        private static void OnActionChange(object obj, InputActionChange change)
        {
            if (change == InputActionChange.BoundControlsChanged)
                UpdateAllHotkeyIcons();
        }

        /// <summary>
        /// Registers a hotkey by action name. Returns ID=0 if the action cannot
        /// be resolved from <see cref="InputAsset"/>.
        /// </summary>
        public static HotkeyHandle RegisterHotkey(string actionName, Action<HotkeyInputContext> callback,
            float holdTime = 0, Transform parent = null, string text = null,
            HotkeyElementStyle style = HotkeyElementStyle.UI, int priority = 0)
        {
            var inputAction = ResolveAction(actionName);
            if (inputAction == null)
            {
                Debug.LogError($"RegisterHotkey failed, action not found: {actionName}");
                return new HotkeyHandle { ID = 0 };
            }

            _autoID++;
            var hotkey = new HotkeyState
            {
                ID = _autoID,
                ActionName = actionName,
                InputAction = inputAction,
                InputActionPath = $"{inputAction.actionMap.name}/{inputAction.name}",
                Mode = holdTime > 0 ? HotkeyMode.Hold : HotkeyMode.Press,
                HoldDuration = Mathf.Max(0, holdTime),
                Callback = callback,
                Parent = parent,
                Text = text,
                Visible = true,
                Enabled = true,
                Interactable = CurrentDevice == InputDevice.KeyboardAndMouse,
                Silence = false,
                Priority = priority,
            };

            _idToState.Add(hotkey.ID, hotkey);

            var path = hotkey.InputActionPath;
            if (!_pathToIDs.TryGetValue(path, out var ids))
            {
                ids = new List<int>();
                _pathToIDs.Add(path, ids);
            }
            if (!ids.Contains(hotkey.ID)) ids.Add(hotkey.ID);

            inputAction.started -= OnNativeCallback;
            inputAction.performed -= OnNativeCallback;
            inputAction.canceled -= OnNativeCallback;
            inputAction.started += OnNativeCallback;
            inputAction.performed += OnNativeCallback;
            inputAction.canceled += OnNativeCallback;

            return new HotkeyHandle { ID = hotkey.ID };
        }

        private static InputAction ResolveAction(string actionName)
        {
            if (InputAsset == null) return null;
            foreach (var map in InputAsset.actionMaps)
            {
                var action = map.FindAction(actionName);
                if (action != null) return action;
            }
            return null;
        }

        /// <summary>Looks up an action map by name from the bound asset.</summary>
        public static InputActionMap FindActionMap(string mapName)
            => InputAsset != null ? InputAsset.FindActionMap(mapName, false) : null;

        /// <summary>Looks up an action by name (any map) from the bound asset.</summary>
        public static InputAction FindAction(string actionName)
            => ResolveAction(actionName);

        private static void OnNativeCallback(InputAction.CallbackContext context)
        {
            var path = $"{context.action.actionMap.name}/{context.action.name}";
            if (!_pathToIDs.TryGetValue(path, out var ids)) return;

            var ctx = HotkeyInputContext.CreateFrom(context);
            for (var i = 0; i < ids.Count; i++)
            {
                if (_needStopInvoke) { _needStopInvoke = false; break; }
                if (_idToState.TryGetValue(ids[i], out var hotkey))
                    Dispatch(hotkey, ctx);
            }
        }

        private static void Dispatch(HotkeyState hotkey, HotkeyInputContext ctx)
        {
            if (!hotkey.Enabled || hotkey.Silence) return;
            if (hotkey.Mode == HotkeyMode.Hold)
            {
                if (ctx.started || ctx.performed)
                    hotkey.Callback?.Invoke(ctx);
                return;
            }
            if (ctx.started || ctx.performed || ctx.cancelled)
                hotkey.Callback?.Invoke(ctx);
        }

        public static void UnregisterHotkey(HotkeyHandle handle)
        {
            if (handle.ID == 0 || !_idToState.TryGetValue(handle.ID, out var hotkey)) return;

            if (hotkey.InputAction != null)
            {
                hotkey.InputAction.started -= OnNativeCallback;
                hotkey.InputAction.performed -= OnNativeCallback;
                hotkey.InputAction.canceled -= OnNativeCallback;
            }

            if (_pathToIDs.TryGetValue(hotkey.InputActionPath, out var ids))
            {
                ids.Remove(handle.ID);
                if (ids.Count == 0) _pathToIDs.Remove(hotkey.InputActionPath);
            }

            _idToState.Remove(handle.ID);
        }

        public static void UnregisterAll()
        {
            foreach (var state in _idToState.Values)
            {
                if (state.InputAction != null)
                {
                    state.InputAction.started -= OnNativeCallback;
                    state.InputAction.performed -= OnNativeCallback;
                    state.InputAction.canceled -= OnNativeCallback;
                }
            }
            _idToState.Clear();
            _pathToIDs.Clear();
        }

        public static void StopCurrentInvokeList() => _needStopInvoke = true;

        public static bool IsSilence(HotkeyHandle handle)
            => _idToState.TryGetValue(handle.ID, out var s) && s.Silence;

        public static void SetSilence(HotkeyHandle handle, bool value)
        {
            if (_idToState.TryGetValue(handle.ID, out var s)) s.Silence = value;
        }

        public static bool IsVisible(HotkeyHandle handle)
            => _idToState.TryGetValue(handle.ID, out var s) && s.Visible;

        public static void SetVisible(HotkeyHandle handle, bool value)
        {
            if (_idToState.TryGetValue(handle.ID, out var s)) s.Visible = value;
        }

        public static bool IsEnabled(HotkeyHandle handle)
            => _idToState.TryGetValue(handle.ID, out var s) && s.Enabled;

        public static void SetEnabled(HotkeyHandle handle, bool value)
        {
            if (_idToState.TryGetValue(handle.ID, out var s)) s.Enabled = value;
        }

        public static float GetDisableTime(HotkeyHandle handle)
            => _idToState.TryGetValue(handle.ID, out var s) ? s.DisableTime : 0f;

        public static void SetDisableTime(HotkeyHandle handle, float value)
        {
            if (_idToState.TryGetValue(handle.ID, out var s)) s.DisableTime = value;
        }

        public static string GetText(HotkeyHandle handle)
            => _idToState.TryGetValue(handle.ID, out var s) ? s.Text : null;

        public static void SetText(HotkeyHandle handle, string value)
        {
            if (_idToState.TryGetValue(handle.ID, out var s)) s.Text = value;
        }

        private static void UpdateAllHotkeyIcons()
        {
            foreach (var state in _idToState.Values)
                state.Element?.RefreshIcon();
        }

        /// <summary>Runtime state for one registered hotkey.</summary>
        public class HotkeyState
        {
            public int ID;
            public string ActionName;
            public InputAction InputAction;
            public string InputActionPath;
            public HotkeyMode Mode;
            public float HoldDuration;
            public Action<HotkeyInputContext> Callback;
            public Transform Parent;
            public IHotkeyElement Element;
            public string Text;
            public bool Visible;
            public bool Enabled;
            public bool Interactable;
            public bool Silence;
            public int Priority;
            public float DisableTime;
        }
    }
}
