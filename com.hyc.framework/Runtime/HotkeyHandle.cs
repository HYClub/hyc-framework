namespace HYC.Framework.Input
{
    /// <summary>
    /// Lightweight, persistent handle for a registered hotkey. Wraps the
    /// internal state id so callers never touch the registry directly.
    /// </summary>
    public struct HotkeyHandle
    {
        public int ID;

        public string Text
        {
            get => HotkeyManager.GetText(this);
            set => HotkeyManager.SetText(this, value);
        }

        public bool Silence
        {
            get => HotkeyManager.IsSilence(this);
            set => HotkeyManager.SetSilence(this, value);
        }

        public bool Visible
        {
            get => HotkeyManager.IsVisible(this);
            set => HotkeyManager.SetVisible(this, value);
        }

        public bool Enable
        {
            get => HotkeyManager.IsEnabled(this);
            set => HotkeyManager.SetEnabled(this, value);
        }

        public float DisableTime
        {
            get => HotkeyManager.GetDisableTime(this);
            set => HotkeyManager.SetDisableTime(this, value);
        }
    }
}
