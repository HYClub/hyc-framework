using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HYC.Framework.Input
{
    /// <summary>
    /// Uniform hotkey payload handed to callbacks. Wraps either a native
    /// <see cref="InputAction.CallbackContext"/> (from the input system) or a
    /// synthesized UI event, exposing the same started/performed/cancelled
    /// flags and progress helpers regardless of source.
    /// </summary>
    public struct HotkeyInputContext
    {
        public InputAction.CallbackContext native;
        public InputAction action;
        public InputControl control;
        public IInputInteraction interaction;

        public HotkeyInputPhase phase;
        public bool started;
        public bool performed;
        public bool cancelled;
        public bool ended;

        public double time;
        public double startTime;
        public double duration;

        public Type valueType;
        public int valueSizeInBytes;

        /// <summary>The event originated from a UI element press (_not_ the input system).</summary>
        public bool isFromUI;
        public bool isFromKeyboardMouse;
        public bool isFromNative;
        public bool isButtonSchema;

        public InputAction.CallbackContext Native => native;

        public TValue ReadValue<TValue>() where TValue : struct
            => isFromNative ? native.ReadValue<TValue>() : default;

        public object ReadValueAsObject()
            => isFromNative ? native.ReadValueAsObject() : null;

        /// <summary>Normalised 0..1 progress of a hold interaction.</summary>
        public float GetHoldProgress()
            => Mathf.Clamp01((float)((time - startTime) / duration));

        public static HotkeyInputContext CreateFrom(InputAction.CallbackContext context)
        {
            var info = new HotkeyInputContext
            {
                native = context,
                isFromNative = true,
                action = context.action,
                control = context.control,
                interaction = context.interaction,
                phase = GetHotkeyPhase(context.phase),
                time = context.time,
                startTime = context.startTime,
                duration = context.duration,
                valueType = context.valueType,
                valueSizeInBytes = context.valueSizeInBytes,
                isFromUI = false,
                isFromKeyboardMouse = HotkeyManager.CurrentDevice == InputDevice.KeyboardAndMouse,
                isButtonSchema = true,
            };
            info.started = info.phase == HotkeyInputPhase.Started;
            info.performed = info.phase == HotkeyInputPhase.Performed;
            info.cancelled = info.phase == HotkeyInputPhase.Canceled;
            info.ended = info.phase == HotkeyInputPhase.End;
            return info;
        }

        public static HotkeyInputContext CreateFrom(InputAction.CallbackContext context, bool isButtonSchema)
        {
            var info = CreateFrom(context);
            info.isButtonSchema = isButtonSchema;
            return info;
        }

        /// <summary>Synthesizes a hotkey event for a UI-simulated press (no native context).</summary>
        public static HotkeyInputContext CreateFrom(HotkeyInputPhase phase, float time, float startTime, float duration, bool isFromUI = false)
        {
            var info = new HotkeyInputContext
            {
                isFromNative = false,
                action = null,
                control = null,
                interaction = null,
                phase = phase,
                time = time,
                startTime = startTime,
                duration = duration,
                valueType = null,
                valueSizeInBytes = 0,
                isFromUI = isFromUI,
                isFromKeyboardMouse = HotkeyManager.CurrentDevice == InputDevice.KeyboardAndMouse,
                isButtonSchema = true,
            };
            info.started = info.phase == HotkeyInputPhase.Started;
            info.performed = info.phase == HotkeyInputPhase.Performed;
            info.cancelled = info.phase == HotkeyInputPhase.Canceled;
            info.ended = info.phase == HotkeyInputPhase.End;
            return info;
        }

        private static HotkeyInputPhase GetHotkeyPhase(InputActionPhase phase)
        {
            switch (phase)
            {
                case InputActionPhase.Started: return HotkeyInputPhase.Started;
                case InputActionPhase.Performed: return HotkeyInputPhase.Performed;
                case InputActionPhase.Canceled: return HotkeyInputPhase.Canceled;
                case InputActionPhase.Waiting: return HotkeyInputPhase.Waiting;
                case InputActionPhase.Disabled: return HotkeyInputPhase.Disabled;
            }
            return HotkeyInputPhase.Disabled;
        }
    }
}