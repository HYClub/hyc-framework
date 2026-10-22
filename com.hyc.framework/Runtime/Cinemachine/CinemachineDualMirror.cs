// ============================================================
// HYC Framework - Cinemachine 相机装配 · 镜像相机
// 文件: Runtime/Cinemachine/CinemachineDualMirror.cs
// 说明: 「同场景、两侧视觉不同」的前提条件 —— 让两台相机看的是同一张图。
//       只要两台相机有一丝位姿差，接缝两侧就对不齐（折痕），
//       而手摆两台相机在相机一运动时立刻崩，所以位姿必须由代码保证。
// ============================================================

using Unity.Cinemachine;
using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>
    /// 镜像相机：B 相机每帧复制 A 相机的位姿与镜头参数，
    /// 于是两台相机**看到的是同一张图**，差别只剩 cullingMask。
    /// <para><b>用在哪</b>：<see cref="CinemachineDualVariant"/> 那条路
    /// （同场景、左半看香蕉 / 右半看保龄球、一屏黑一屏白）。
    /// 这条路必须走合成（<c>Camera.rect</c> 下每台相机宽高比是半屏，水平视野被压窄，
    /// 两半就不是同一张图切开的了）。</para>
    /// <para><b>只复制位姿与镜头</b>：<c>targetTexture</c> / <c>rect</c> /
    /// <c>cullingMask</c> / <c>depth</c> 一律不碰 —— 那几项正是让两台相机不同的东西。
    /// 同理<b>不复制</b> <c>aspect</c>：合成模式下两台都是整屏、本来一致，
    /// 一旦复制反而会在过渡帧把半屏宽高比带过去。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineDualMirror : MonoBehaviour
    {
        [Tooltip("被镜像的相机（真相机）。留空时取合成器的 RigA 相机。")]
        [SerializeField] private Camera m_Source;

        [Tooltip("跟随镜像的相机。留空时取合成器的 RigB 相机。")]
        [SerializeField] private Camera m_Target;

        [Tooltip("合成器。留空时在自身与父级查找，用来自动取两侧 rig 的相机。")]
        [SerializeField] private CinemachineSeamCompositor m_Compositor;

        [Tooltip("同时复制 FOV / 正交尺寸 / 近远裁剪面。关掉就只同步位姿。")]
        [SerializeField] private bool m_SyncLens = true;

        private bool m_Subscribed;
        private bool m_WarnedBrain;

        /// <summary>被镜像的相机。</summary>
        public Camera Source => m_Source;

        /// <summary>镜像相机。</summary>
        public Camera Target => m_Target;

        /// <summary>是否已经在镜像（两台相机都解析到了）。</summary>
        public bool IsMirroring => m_Source != null && m_Target != null && m_Source != m_Target;

        private void OnEnable()
        {
            Resolve();
            Subscribe();
            Apply();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>手动重新解析并同步一次（rig 是运行期实例化的时候用）。</summary>
        public void Refresh()
        {
            Resolve();
            Apply();
        }

        /// <summary>立即同步一次位姿与镜头参数。</summary>
        public void Apply()
        {
            if (!IsMirroring)
                return;

            WarnIfBrainOnTarget();

            var src = m_Source.transform;
            m_Target.transform.SetPositionAndRotation(src.position, src.rotation);

            if (!m_SyncLens)
                return;

            m_Target.orthographic = m_Source.orthographic;
            m_Target.orthographicSize = m_Source.orthographicSize;
            m_Target.fieldOfView = m_Source.fieldOfView;
            m_Target.nearClipPlane = m_Source.nearClipPlane;
            m_Target.farClipPlane = m_Source.farClipPlane;
        }

        private void LateUpdate()
        {
            if (!IsMirroring)
                Resolve();

            // 单纯给「读相机位姿的非渲染系统」一个合理值；
            // 真正保证渲染一致的是下面 onPreCull 那次（LateUpdate 与 Cinemachine 的先后不确定）
            Apply();
        }

        // ---------- 内部 ----------

        private void Resolve()
        {
            if (m_Compositor == null)
            {
                m_Compositor = GetComponent<CinemachineSeamCompositor>();
                if (m_Compositor == null)
                    m_Compositor = GetComponentInParent<CinemachineSeamCompositor>();
            }

            if (m_Source == null && m_Compositor != null && m_Compositor.RigA != null)
                m_Source = m_Compositor.RigA.GetComponentInChildren<Camera>(true);

            if (m_Target == null && m_Compositor != null && m_Compositor.RigB != null)
                m_Target = m_Compositor.RigB.GetComponentInChildren<Camera>(true);
        }

        private void Subscribe()
        {
            if (m_Subscribed)
                return;

            Camera.onPreCull += OnAnyCameraPreCull;
            m_Subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!m_Subscribed)
                return;

            Camera.onPreCull -= OnAnyCameraPreCull;
            m_Subscribed = false;
        }

        /// <summary>
        /// 必须在镜像相机自己出图之前同步：Cinemachine 在 LateUpdate 摆完真相机，
        /// 而本组件与它的执行顺序不确定 —— 挂 <c>onPreCull</c> 才能保证当帧一致。
        /// </summary>
        private void OnAnyCameraPreCull(Camera camera)
        {
            if (camera != m_Target)
                return;

            Apply();
        }

        private void WarnIfBrainOnTarget()
        {
            if (m_WarnedBrain || m_Target == null)
                return;

            m_WarnedBrain = true;
            if (m_Target.GetComponent<CinemachineBrain>() == null)
                return;

            Debug.LogWarning("[HYC.Cinemachine] 镜像相机 " + m_Target.name + " 上挂着 CinemachineBrain。" +
                             "位姿已由本组件接管，Brain 每帧白算一次且可能与其他系统读到的一帧不一致，" +
                             "建议把镜像那一侧的 Brain 禁用。");
        }
    }
}
