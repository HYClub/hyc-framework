using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.U2D;
using HYC.Framework.Input;

namespace HYC.Framework.Runtime
{
    /// <summary>
    /// Boot-time configuration asset for the framework world. Mirrors the source
    /// <c>StartupSetting</c> ScriptableObject minus game-only concerns (Wwise,
    /// per-game cameras/banks). The game creates one in the
    /// editor and either references it from an installer or re-creates it in code.
    /// </summary>
    [CreateAssetMenu(fileName = "StartupSetting", menuName = "HYC Framework/Startup Setting")]
    public sealed class StartupSetting : ScriptableObject
    {
        [Header("Input")]
        public InputActionAsset InputActionAsset;
        [Range(0f, 1f)] public float NavigationKeyDeathArea = 0.6f;
        // [Range] cannot be applied to a float literal in field util; keep plain
        public float NavigationKeyDelayTime = 0.5f;
        public int NavigationKeyRepeateCount = 10;

        [Header("UI")]
        public Vector2 ReferenceResolution = new Vector2(1920, 1080);
        public Vector2 MaximumResolution = new Vector2(3440, 1440);

        [Header("Hotkey")]
        public SpriteAtlas HotkeyIcons;
        public float HotkeyHoldThreshold = 0.2f;
        public BaseHotkeyElement HotkeyStyle1;
        public BaseHotkeyElement HotkeyStyle2;
        public BaseHotkeyElement HotkeyStyle3;
        public BaseHotkeyElement HotkeyStyle4;
        public BaseHotkeyElement HotkeyStyle5;
    }
}