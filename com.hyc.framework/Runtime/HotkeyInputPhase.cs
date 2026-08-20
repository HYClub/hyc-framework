namespace HYC.Framework.Input
{
    /// <summary>
    /// Lifecycle phase of a hotkey input within one interaction.
    /// Mirrored from the source <c>HotkeyInputPhase</c>.
    /// </summary>
    public enum HotkeyInputPhase
    {
        Disabled = 0,
        Waiting = 1,
        Started = 2,
        Performed = 3,
        Canceled = 4,
        End = 5,
    }
}