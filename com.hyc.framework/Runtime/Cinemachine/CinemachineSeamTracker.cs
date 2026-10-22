// ============================================================
// HYC Framework - Cinemachine 相机装配 · 接缝查询
// 文件: Runtime/Cinemachine/CinemachineSeamTracker.cs
// 说明: 玩法侧要知道「某个东西现在在接缝哪一侧」。
//       判定公式与合成 shader 共用同一套（SeamState.SignedDistance），
//       任何一边改了都要同步另一面，否则会出现「玩法说在 A、画面画在 B」。
// ============================================================

using System;
using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>
    /// 跟踪一个世界目标在接缝的哪一侧，跨缝时抛事件。
    /// <para>典型用法：接缝旋转变成玩法障碍时，判断角色是否落在安全侧。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineSeamTracker : MonoBehaviour
    {
        [Tooltip("接缝合成器。留空时在父级向上查找。")]
        [SerializeField] private CinemachineSeamCompositor m_Compositor;

        [Tooltip("做屏幕投影的相机。留空时用合成器的 A 侧相机。")]
        [SerializeField] private Camera m_Camera;

        [Tooltip("被跟踪的世界目标。")]
        [SerializeField] private Transform m_Target;

        private bool m_OnSideA = true;
        private bool m_HasSample;

        /// <summary>跨缝时触发：参数为「现在是否在 A 侧」。</summary>
        public event Action<bool> SideChanged;

        /// <summary>当前是否在 A 侧。还没采到样本时恒为 true。</summary>
        public bool IsOnSideA => m_OnSideA;

        /// <summary>是否已经采到过有效样本（目标在相机背后时不算）。</summary>
        public bool HasSample => m_HasSample;

        /// <summary>绑定依赖。三个参数都可以留空，留空的走自动查找。</summary>
        public void Bind(CinemachineSeamCompositor compositor, Camera projectionCamera, Transform target)
        {
            m_Compositor = compositor;
            m_Camera = projectionCamera;
            m_Target = target;
            m_HasSample = false;
        }

        /// <summary>
        /// 世界点到接缝的有符号距离（&gt; 0 = B 侧，&lt; 0 = A 侧）。
        /// 目标在相机背后时返回 <see cref="float.NaN"/>。
        /// </summary>
        public static float SignedDistanceOnScreen(in SeamState seam, Camera camera, Vector3 worldPosition)
        {
            if (camera == null)
                return float.NaN;

            var viewport = camera.WorldToViewportPoint(worldPosition);
            if (viewport.z <= 0f)
                return float.NaN;

            return seam.SignedDistance(new Vector2(viewport.x, viewport.y));
        }

        /// <summary>世界点是否落在 A 侧。无法判定时返回 false。</summary>
        public static bool IsWorldPointOnSideA(in SeamState seam, Camera camera, Vector3 worldPosition)
        {
            var distance = SignedDistanceOnScreen(seam, camera, worldPosition);
            return !float.IsNaN(distance) && distance < 0f;
        }

        private void LateUpdate()
        {
            var compositor = ResolveCompositor();
            var camera = ResolveCamera(compositor);
            if (compositor == null || camera == null || m_Target == null)
                return;

            var distance = SignedDistanceOnScreen(compositor.Seam, camera, m_Target.position);
            if (float.IsNaN(distance))
                return;

            var onSideA = distance < 0f;
            m_HasSample = true;

            if (onSideA == m_OnSideA)
                return;

            m_OnSideA = onSideA;
            SideChanged?.Invoke(onSideA);
        }

        private CinemachineSeamCompositor ResolveCompositor()
        {
            if (m_Compositor != null)
                return m_Compositor;

            m_Compositor = GetComponentInParent<CinemachineSeamCompositor>();
            return m_Compositor;
        }

        private Camera ResolveCamera(CinemachineSeamCompositor compositor)
        {
            if (m_Camera != null)
                return m_Camera;

            if (compositor != null && compositor.RigA != null)
                m_Camera = compositor.RigA.GetComponentInChildren<Camera>(true);

            return m_Camera;
        }

        private void OnDestroy()
        {
            SideChanged = null;
        }
    }
}
