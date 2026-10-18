// ============================================================
// HYC Framework - Cinemachine 相机装配 · 运行期目标绑定器
// 文件: Runtime/Cinemachine/CinemachineRigBinder.cs
// 说明: 挂在 rig prefab 根节点。实例化 rig 后由宿主把
//       FollowTarget / LookAtTarget 指向玩家等实体，
//       本组件在 Awake 统一下发给所有子级虚拟相机。
//       两个目标都为空时不做任何事（各相机保留编辑期占位目标）。
// ============================================================

using UnityEngine;
using Unity.Cinemachine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>
    /// Cinemachine 相机装配（rig prefab）的运行期目标绑定器，挂在 rig 根节点。
    /// <para>实例化 rig 后，由宿主把 <see cref="FollowTarget"/> / <see cref="LookAtTarget"/>
    /// 指向玩家等实体，本组件在 Awake 统一下发给所有子级虚拟相机（含未激活的）。</para>
    /// <para>两个目标都为空时不做任何事：rig 内各相机保留编辑期指向的 RigTarget 占位。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineRigBinder : MonoBehaviour
    {
        [Tooltip("跟随目标（位置跟踪）。为空表示不改写各相机的 Follow。")]
        [SerializeField] Transform m_FollowTarget;

        [Tooltip("朝向目标（LookAt）。为空表示不改写各相机的 LookAt。")]
        [SerializeField] Transform m_LookAtTarget;

        /// <summary>跟随目标（位置跟踪）。为空表示不改写各相机的 Follow。</summary>
        public Transform FollowTarget
        {
            get => m_FollowTarget;
            set => m_FollowTarget = value;
        }

        /// <summary>朝向目标（LookAt）。为空表示不改写各相机的 LookAt。</summary>
        public Transform LookAtTarget
        {
            get => m_LookAtTarget;
            set => m_LookAtTarget = value;
        }

        void Awake()
        {
            if (m_FollowTarget == null && m_LookAtTarget == null)
                return;

            var cameras = GetComponentsInChildren<CinemachineVirtualCameraBase>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                var cam = cameras[i];
                if (cam == null)
                    continue;
                if (m_FollowTarget != null)
                    cam.Follow = m_FollowTarget;
                if (m_LookAtTarget != null)
                    cam.LookAt = m_LookAtTarget;
            }
        }
    }
}
