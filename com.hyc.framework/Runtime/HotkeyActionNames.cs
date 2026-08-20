namespace HYC.Framework.Input
{
    /// <summary>
    /// Action names used by the framework UI stack, expressed as strings so the
    /// framework never depends on a game-generated enum. The owning game's
    /// InputManager must expose actions with exactly these names on the "UI"
    /// action map, or hosts may register these with <see cref="HotkeyManager"/>.
    ///
    /// Mirrors the source <c>HotkeyID</c> values consumed by the ECS UI stack
    /// (UGUI focus navigation + generic function buttons).
    /// </summary>
    public static class HotkeyActionNames
    {
        public const string None = "None";

        // --- UI map ---
        public const string UI_FuncA = "UI_FuncA";
        public const string UI_FuncB = "UI_FuncB";
        public const string UI_FuncX = "UI_FuncX";
        public const string UI_FuncY = "UI_FuncY";
        public const string UI_FuncLT = "UI_FuncLT";
        public const string UI_FuncRT = "UI_FuncRT";
        public const string UI_FuncL3 = "UI_FuncL3";
        public const string UI_FuncR3 = "UI_FuncR3";
        public const string UI_NavUp = "UI_NavUp";
        public const string UI_NavDown = "UI_NavDown";
        public const string UI_NavLeft = "UI_NavLeft";
        public const string UI_NavRight = "UI_NavRight";
        public const string UI_NavNegative = "UI_NavNegative";
        public const string UI_NavPositive = "UI_NavPositive";
        public const string UI_ESC = "UI_ESC";
        public const string UI_Enter = "UI_Enter";
        public const string UI_ReleaseCursor = "UI_ReleaseCursor";
        public const string UI_MouseLeft = "UI_MouseLeft";
        public const string UI_MouseMiddle = "UI_MouseMiddle";
        public const string UI_MouseRight = "UI_MouseRight";

        // --- UGUI focus navigation (virtual cursor) ---
        public const string UI_UGUI_Stick1 = "UI_UGUI_Stick1";
        public const string UI_UGUI_Stick2 = "UI_UGUI_Stick2";
        public const string UI_UGUI_Button1 = "UI_UGUI_Button1";
        public const string UI_UGUI_Button2 = "UI_UGUI_Button2";
        public const string UI_UGUI_Horizontal = "UI_UGUI_Horizontal";
        public const string UI_UGUI_Vertical = "UI_UGUI_Vertical";
        public const string UI_UGUI_Submit = "UI_UGUI_Submit";
        public const string UI_UGUI_Cancel = "UI_UGUI_Cancel";

        // --- Human / Ship maps (framework does not require them; listed for hosts) ---
        public const string Human_ESC = "Human_ESC";
        public const string Human_MoveAxis = "Human_MoveAxis";
        public const string Human_Interactive = "Human_Interactive";
        public const string Ship_ESC = "Ship_ESC";
        public const string Ship_MoveAxis = "Ship_MoveAxis";
        public const string Ship_CameraAxis = "Ship_CameraAxis";
        public const string Ship_Interactive = "Ship_Interactive";
    }
}