using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

namespace HYC.Framework.Input
{
    /// <summary>本地多人的设备布局。</summary>
    public enum LocalInputLayout
    {
        /// <summary>一块键盘分左右：P1 = WASD + 左手区，P2 = 方向键 + 小键盘。</summary>
        KeyboardSplit = 0,

        /// <summary>键盘 + 手柄：P1 键盘，P2 手柄。</summary>
        KeyboardPlusGamepad = 1,

        /// <summary>两只手柄各一人。</summary>
        TwoGamepads = 2,

        /// <summary>单只手柄双人共用：左摇杆 / 右摇杆各一人。</summary>
        SingleGamepadDualStick = 3,
    }

    /// <summary>
    /// 本地多人会话：把**设备**分配给**槽位**，并处理热插拔。
    ///
    /// 分工（与框架既有设施对齐）：
    ///   · 本类只管「哪台设备属于哪个槽位」以及布局策略
    ///   · <see cref="InputPlayerRegistry"/> 管「实体 ↔ InputActionAsset 实例」
    ///   · <see cref="LocalPlayerJoinDetector"/> 管「未配对设备被按了 → 想加入」
    ///
    /// 每玩家必须有**独立的 InputActionAsset 实例**（否则 action 状态会互相污染），
    /// 所以这里负责 Instantiate 并在销毁时 Destroy。
    /// </summary>
    public sealed class LocalPlayerSession : IDisposable
    {
        public const int MaxSlots = 2;

        /// <summary>控制方案名（必须在 InputActionAsset 里定义）。</summary>
        public const string SchemeKeyboardLeft  = "KeyboardLeft";
        public const string SchemeKeyboardRight = "KeyboardRight";
        public const string SchemeGamepad       = "Gamepad";
        public const string SchemeGamepadLeft   = "GamepadLeft";
        public const string SchemeGamepadRight  = "GamepadRight";

        readonly InputUser[] m_Users = new InputUser[MaxSlots];
        readonly InputActionAsset[] m_Assets = new InputActionAsset[MaxSlots];
        readonly bool[] m_Assigned = new bool[MaxSlots];
        readonly string[] m_Schemes = new string[MaxSlots];

        bool m_Running;
        bool m_ListeningDeviceChange;

        public LocalInputLayout Layout { get; private set; } = LocalInputLayout.KeyboardSplit;

        /// <summary>模板资源（不会被启用；每槽位实例化一份）。</summary>
        public InputActionAsset Template { get; private set; }

        public bool IsRunning => m_Running;
        public int  PlayerCount { get; private set; }

        public event Action<int> PlayerJoined;
        public event Action<int> PlayerLeft;
        public event Action<int> PlayerDevicesChanged;

        // ---------------- 生命周期 ----------------

        /// <summary>启动会话。template 必须已配置好各控制方案。</summary>
        public void Start(LocalInputLayout layout, InputActionAsset template, bool autoAssign = true)
        {
            if (m_Running)
            {
                Stop();
            }

            Layout = layout;
            Template = template;
            m_Running = true;
            PlayerCount = 0;

            if (!m_ListeningDeviceChange)
            {
                InputSystem.onDeviceChange += OnDeviceChange;
                m_ListeningDeviceChange = true;
            }

            if (autoAssign)
            {
                AutoAssign();
            }
        }

        public void Stop()
        {
            for (int i = 0; i < MaxSlots; i++)
            {
                ReleaseSlot(i);
            }

            m_Running = false;
            PlayerCount = 0;

            if (m_ListeningDeviceChange)
            {
                InputSystem.onDeviceChange -= OnDeviceChange;
                m_ListeningDeviceChange = false;
            }
        }

        public void Dispose() => Stop();

        // ---------------- 查询 ----------------

        public bool IsAssigned(int slot) => slot >= 0 && slot < MaxSlots && m_Assigned[slot];

        public InputUser GetUser(int slot) => slot >= 0 && slot < MaxSlots ? m_Users[slot] : default;

        public InputActionAsset GetAsset(int slot) => slot >= 0 && slot < MaxSlots ? m_Assets[slot] : null;

        public string GetScheme(int slot) => slot >= 0 && slot < MaxSlots ? m_Schemes[slot] : null;

        // ---------------- 分配 ----------------

        /// <summary>按当前布局把现有设备分配到槽位。</summary>
        public void AutoAssign()
        {
            switch (Layout)
            {
                case LocalInputLayout.KeyboardSplit:
                    AssignKeyboardSplit();
                    break;

                case LocalInputLayout.KeyboardPlusGamepad:
                    AssignKeyboardPlusGamepad();
                    break;

                case LocalInputLayout.TwoGamepads:
                    AssignTwoGamepads();
                    break;

                case LocalInputLayout.SingleGamepadDualStick:
                    AssignSingleGamepadDualStick();
                    break;
            }
        }

        void AssignKeyboardSplit()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            Assign(0, kb, SchemeKeyboardLeft);
            Assign(1, kb, SchemeKeyboardRight);
        }

        void AssignKeyboardPlusGamepad()
        {
            if (Keyboard.current != null)
            {
                Assign(0, Keyboard.current, SchemeKeyboardLeft);
            }

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                Assign(1, pad, SchemeGamepad);
            }
        }

        void AssignTwoGamepads()
        {
            var pads = Gamepad.all;
            if (pads.Count > 0)
            {
                Assign(0, pads[0], SchemeGamepad);
            }

            if (pads.Count > 1)
            {
                Assign(1, pads[1], SchemeGamepad);
            }
        }

        void AssignSingleGamepadDualStick()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null)
            {
                return;
            }

            Assign(0, pad, SchemeGamepadLeft);
            Assign(1, pad, SchemeGamepadRight);
        }

        /// <summary>把一台设备分配给槽位（会先清掉该槽位原有配对）。</summary>
        public bool Assign(int slot, UnityEngine.InputSystem.InputDevice device, string controlScheme)
        {
            if (!m_Running || slot < 0 || slot >= MaxSlots || device == null || Template == null)
            {
                return false;
            }

            ReleaseSlot(slot);

            InputUser user = InputUser.PerformPairingWithDevice(device, InputUser.CreateUserWithoutPairedDevices());
            if (!string.IsNullOrEmpty(controlScheme))
            {
                user.ActivateControlScheme(controlScheme);
            }

            m_Users[slot] = user;
            m_Assigned[slot] = true;
            m_Schemes[slot] = controlScheme;

            // 每槽位一份独立 asset 实例（action 状态互不污染）
            InputActionAsset asset = UnityEngine.Object.Instantiate(Template);
            asset.name = $"{Template.name}_P{slot + 1}";
            asset.Enable();
            m_Assets[slot] = asset;

            PlayerCount = CountAssigned();
            PlayerJoined?.Invoke(slot);
            return true;
        }

        void ReleaseSlot(int slot)
        {
            if (!m_Assigned[slot])
            {
                return;
            }

            if (m_Assets[slot] != null)
            {
                m_Assets[slot].Disable();
                UnityEngine.Object.Destroy(m_Assets[slot]);
                m_Assets[slot] = null;
            }

            if (m_Users[slot].valid)
            {
                m_Users[slot].UnpairDevicesAndRemoveUser();
            }

            m_Users[slot] = default;
            m_Assigned[slot] = false;
            m_Schemes[slot] = null;

            PlayerCount = CountAssigned();
            PlayerLeft?.Invoke(slot);
        }

        int CountAssigned()
        {
            int n = 0;
            for (int i = 0; i < MaxSlots; i++)
            {
                if (m_Assigned[i])
                {
                    n++;
                }
            }

            return n;
        }

        // ---------------- 热插拔 ----------------

        void OnDeviceChange(UnityEngine.InputSystem.InputDevice device, InputDeviceChange change)
        {
            if (!m_Running)
            {
                return;
            }

            switch (change)
            {
                case InputDeviceChange.Removed:
                case InputDeviceChange.Disconnected:
                {
                    // 谁的配对里含这台设备 → 释放该槽位（松手即离队，避免"幽灵输入"）
                    for (int i = 0; i < MaxSlots; i++)
                    {
                        if (!m_Assigned[i] || !m_Users[i].valid)
                        {
                            continue;
                        }

                        var paired = m_Users[i].pairedDevices;
                        for (int k = 0; k < paired.Count; k++)
                        {
                            if (paired[k] == device)
                            {
                                ReleaseSlot(i);
                                break;
                            }
                        }
                    }

                    // 断开后按布局重新补人（例如双手柄剩一只 → 自动降级）
                    AutoAssign();
                    break;
                }

                case InputDeviceChange.Added:
                case InputDeviceChange.Reconnected:
                    // 新设备插入：只在有空槽位时补（不抢占已在玩的人）
                    if (CountAssigned() < MaxSlots)
                    {
                        AutoAssign();
                    }
                    break;
            }

            PlayerDevicesChanged?.Invoke(-1);
        }
    }
}
