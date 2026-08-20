using UnityEngine;

namespace HYC.Framework.Input
{
    /// <summary>
    /// Contract for any UI element that renders a hotkey binding (key icon,
    /// description text, hold progress, visibility). Implementations live in
    /// the owning game; the framework drives them through this interface.
    /// </summary>
    public interface IHotkeyElement
    {
        Sprite KeyIcon { get; set; }
        string DescText { get; set; }
        float Progress { get; set; }
        bool Visible { get; set; }
        bool Enabled { get; set; }
        bool Interactable { get; set; }

        void PlayStartAnim();
        void PlayFinishAnim();

        void Reset(HotkeyManager.HotkeyState hotkeyState);
        void Clear();
        void RefreshIcon();
    }
}
