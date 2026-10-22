// ============================================================
// HYC Framework - Cinemachine 相机装配 · 机位类型
// 文件: Runtime/Cinemachine/RigKind.cs
// 说明: 机位类型枚举。放在 Runtime 是因为运行期要按类型切换机位
//       （binder.SwitchTo(RigKind.ThirdPerson)），生成的访问器类也用它。
//       每种类型对应的 CM 组件配方仍在编辑器侧
//       （Editor/Cinemachine/CinemachineRigKind.cs 的 RigKinds 表）。
// ============================================================

namespace HYC.Framework.Cinemachine
{
    /// <summary>
    /// 机位类型：决定该机位用哪一组 CM 管线组件。
    /// <para>由 <see cref="CinemachineRigCameraTag"/> 记录在机位节点上，
    /// 运行期 <see cref="CinemachineRigBinder"/> 按它做「按类型切换机位」。</para>
    /// </summary>
    public enum RigKind
    {
        /// <summary>环绕机位（CM 2 时代的 FreeLook）：OrbitalFollow + RotationComposer。</summary>
        FreeLook,

        /// <summary>第三人称跟随：ThirdPersonFollow + RotationComposer。</summary>
        ThirdPerson,

        /// <summary>构图跟随（取景框）：PositionComposer + RotationComposer。</summary>
        Framed,

        /// <summary>输入视角（CM 2 时代的 POV，CM 3 改名 PanTilt）：PanTilt。</summary>
        PanTilt,

        /// <summary>轨道机位：SplineDolly + RotationComposer（依赖 com.unity.splines）。</summary>
        Dolly,
    }
}
