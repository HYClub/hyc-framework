namespace HYC.Framework.Input
{
    /// <summary>
    /// 动作相位（与 UnityEngine.InputSystem.InputActionPhase 对应，但用 byte 承载，
    /// 使输入组件保持「纯数据、不引用 InputSystem」——生成的组件可进任意宿主 asmdef）。
    /// </summary>
    public enum InputActionPhaseKind : byte
    {
        Disabled = 0,
        Waiting = 1,
        Started = 2,
        Performed = 3,
        Canceled = 4,
    }
}
