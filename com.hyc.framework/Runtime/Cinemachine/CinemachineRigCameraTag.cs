// ============================================================
// HYC Framework - Cinemachine 相机装配 · 机位类型标记
// 文件: Runtime/Cinemachine/CinemachineRigCameraTag.cs
// 说明: 挂在每个机位节点上，声明这台相机属于哪种 RigKind。
//       相机装配窗口在新建机位 / 切换类型时自动写入，也可在 Inspector 手改。
// ============================================================

using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>
    /// 机位类型标记：让运行期知道「这台相机算哪种机位」。
    /// <para>由 <see cref="CinemachineRigBinder"/> 按类型切换时读取。
    /// 之所以不靠节点名解析，是因为节点名可随意改；不靠组件配方反查，
    /// 是因为用户可能手工给机位加减组件，配方反查会把类型判错。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineRigCameraTag : MonoBehaviour
    {
        [Tooltip("该机位的类型（决定运行期按类型切换时是否命中它）。")]
        [SerializeField] RigKind m_Kind = RigKind.ThirdPerson;

        /// <summary>该机位的类型。</summary>
        public RigKind Kind
        {
            get => m_Kind;
            set => m_Kind = value;
        }
    }
}
