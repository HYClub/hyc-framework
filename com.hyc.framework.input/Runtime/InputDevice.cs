namespace HYC.Framework.Input
{
    /// <summary>
    /// Categorises the active input device. Decoupled from any game account or
    /// navigation system; used to route UI focus and hotkey presentation.
    /// </summary>
    public enum InputDevice
    {
        None = 0,
        KeyboardAndMouse = 1,
        Gamepad = 2,
        Touch = 3,
    }
}