using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;

namespace HYC.Framework.Input
{
    /// <summary>
    /// 本地多人「未配对设备被使用即加入」的便捷监听器（基于 UnityEngine.InputSystem.Users.InputUser）。
    /// <para>只负责发出「有未配对设备被按了」的事件（回调给游戏层一个 <see cref="UnityEngine.InputSystem.InputDevice"/>）。
    /// 具体的配对 / 建玩家实体 / 调 <see cref="InputPlayerRegistry.Register"/> 由游戏层决定——
    /// 策略（最多几人、P1 默认设备、键鼠/手柄能否混编）永远留在游戏层。</para>
    /// <para>用法：游戏层在想要接收加入的时机调 <see cref="Start"/>，订阅 <see cref="OnJoinRequested"/>；
    /// 不想收了调 <see cref="Stop"/>。开销来自 listenForUnpairedDeviceActivity（计数器，按需开关）。</para>
    /// <para>注意：onUnpairedDeviceUsed 在输入真正并入设备之前触发，所以回调里拿到的 device 是「刚被按的那台」，
    /// 游戏层应把它配对给新玩家。</para>
    /// </summary>
    public static class LocalPlayerJoinDetector
    {
        // 全限定：本命名空间已有同名的 HYC.Framework.Input.InputDevice（热键 UI 设备枚举），
        // 裸写会解析到它并引发 CS1503。
        public static event System.Action<UnityEngine.InputSystem.InputDevice> OnJoinRequested;

        private static int _refCount;
        private static bool _subscribed;

        public static void Start()
        {
            ++_refCount;
            if (!_subscribed)
            {
                InputUser.onUnpairedDeviceUsed += OnUnpaired;
                _subscribed = true;
            }
            // 开启未配对设备活动监听（计数器，>0 即开启）
            ++InputUser.listenForUnpairedDeviceActivity;
        }

        public static void Stop()
        {
            if (_refCount > 0) --_refCount;
            if (_refCount <= 0 && _subscribed)
            {
                InputUser.onUnpairedDeviceUsed -= OnUnpaired;
                _subscribed = false;
            }
            if (InputUser.listenForUnpairedDeviceActivity > 0)
                --InputUser.listenForUnpairedDeviceActivity;
        }

        private static void OnUnpaired(InputControl control, InputEventPtr eventPtr)
        {
            if (control == null) return;
            var device = control.device;
            if (device == null) return;
            OnJoinRequested?.Invoke(device);
        }
    }
}
