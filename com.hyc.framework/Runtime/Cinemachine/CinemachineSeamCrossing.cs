// ============================================================
// HYC Framework - Cinemachine 相机装配 · 跨缝穿越
// 文件: Runtime/Cinemachine/CinemachineSeamCrossing.cs
// 说明: 让横跨接缝的物体「接得上」而不是被切成两半。
//       做法：跨缝物体单独一层 → 一台只渲这层的相机 → 透明底 RT →
//       合成 shader 在接缝带内按 alpha 覆盖。
//       主相机的渲染路径一点没改，所以关掉本组件就完全回到原样。
// ============================================================

using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>
    /// 接缝带重投影：给横跨接缝的物体一个「独立渲染层 + 专用相机」，
    /// 只在接缝附近覆盖合成画面，于是物体在接缝两侧都能完整看到。
    /// <para><b>前提</b>：物体必须整层放到 <see cref="LayerName"/> 指定的层，
    /// 并且主相机不要渲染这一层（否则会出现重影）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineSeamCrossing : MonoBehaviour
    {
        [Tooltip("接缝合成器。留空时在同一物体上查找。")]
        [SerializeField] private CinemachineSeamCompositor m_Compositor;

        [Tooltip("跨缝物体所在的层（约定名）。手配了 LayerOverride 时忽略。")]
        [SerializeField] private string m_LayerName = "HYC.SeamCrossing";

        [Tooltip("手配跨缝层。为空时用约定名。")]
        [SerializeField] private LayerMask m_LayerOverride;

        [Tooltip("接缝带半宽（屏幕比例）。覆盖范围 = 接缝两侧各这么宽。")]
        [SerializeField, Range(0.01f, 0.5f)] private float m_BandWidth = 0.12f;

        [Tooltip("启动时把跨缝层从两台主相机的 cullingMask 剔掉，避免同一物体被画两遍出现重影。")]
        [SerializeField] private bool m_ExcludeFromMainCameras = true;

        private RenderTexture m_Target;
        private Camera m_Camera;
        private Camera m_MainA;
        private Camera m_MainB;
        private int m_MainMaskA;
        private int m_MainMaskB;
        private int m_Width;
        private int m_Height;
        private bool m_Running;

        /// <summary>跨缝层画面（透明底）。给合成器用的。</summary>
        public RenderTexture Target => m_Target;

        /// <summary>当前覆盖带宽度。</summary>
        public float BandWidth
        {
            get => m_BandWidth;
            set
            {
                m_BandWidth = Mathf.Clamp(value, 0.01f, 0.5f);
                if (m_Compositor != null)
                    m_Compositor.SetCrossing(m_Target, m_BandWidth);
            }
        }

        /// <summary>开始工作（解析层、建相机与 RT）。</summary>
        public void StartCrossing()
        {
            if (m_Running)
                return;

            var compositor = ResolveCompositor();
            var main = compositor != null && compositor.RigA != null
                ? compositor.RigA.GetComponentInChildren<Camera>(true)
                : null;

            if (compositor == null || main == null)
            {
                Debug.LogWarning("[HYC.Cinemachine] 跨缝穿越需要合成器与 A 侧相机。");
                return;
            }

            var mask = ResolveMask();
            if (mask == 0)
            {
                Debug.LogError("[HYC.Cinemachine] 跨缝层 \"" + m_LayerName + "\" 解析失败。" +
                               "请在 Project Settings > Tags and Layers 添加该层，或在 Inspector 手配 LayerMask。");
                return;
            }

            EnsureCamera(main, mask);
            EnsureTarget();
            m_Running = true;

            ExcludeFromMainCameras(main, mask);

            Camera.onPreCull += OnAnyCameraPreCull;
            Push();
        }

        /// <summary>停止工作并释放相机与 RT。</summary>
        public void StopCrossing()
        {
            if (!m_Running)
                return;

            Camera.onPreCull -= OnAnyCameraPreCull;
            m_Running = false;

            if (m_Compositor != null)
                m_Compositor.SetCrossing(null, 0f);

            RestoreMainCameras();
            ReleaseTarget();

            if (m_Camera != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(m_Camera.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(m_Camera.gameObject);
                m_Camera = null;
            }
        }

        private void LateUpdate()
        {
            if (!m_Running)
                return;

            if (Screen.width != m_Width || Screen.height != m_Height)
                EnsureTarget();

            Push();
        }

        private void Push()
        {
            if (m_Compositor != null)
                m_Compositor.SetCrossing(m_Target, m_BandWidth);
        }

        private void ExcludeFromMainCameras(Camera main, int mask)
        {
            if (!m_ExcludeFromMainCameras)
                return;

            m_MainA = main;
            m_MainMaskA = main.cullingMask;
            main.cullingMask &= ~mask;

            m_MainB = m_Compositor != null && m_Compositor.RigB != null
                ? m_Compositor.RigB.GetComponentInChildren<Camera>(true)
                : null;

            if (m_MainB == null)
                return;

            m_MainMaskB = m_MainB.cullingMask;
            m_MainB.cullingMask &= ~mask;
        }

        private void RestoreMainCameras()
        {
            if (m_MainA != null)
                m_MainA.cullingMask = m_MainMaskA;
            if (m_MainB != null)
                m_MainB.cullingMask = m_MainMaskB;

            m_MainA = null;
            m_MainB = null;
        }

        private void EnsureCamera(Camera main, int mask)
        {
            if (m_Camera == null)
            {
                var go = new GameObject("[HYC Seam Crossing Camera]") { hideFlags = HideFlags.DontSave };
                m_Camera = go.AddComponent<Camera>();
            }

            m_Camera.enabled = true;
            m_Camera.cullingMask = mask;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            m_Camera.fieldOfView = main.fieldOfView;
            m_Camera.nearClipPlane = main.nearClipPlane;
            m_Camera.farClipPlane = main.farClipPlane;
            m_Camera.allowHDR = false;
            m_Camera.allowMSAA = false;
            m_Camera.targetTexture = m_Target;
            m_Camera.depth = main.depth + 1;

            SyncTransform(main);
        }

        private void EnsureTarget()
        {
            m_Width = Screen.width;
            m_Height = Screen.height;

            ReleaseTarget();

            m_Target = new RenderTexture(
                Mathf.Max(1, m_Width), Mathf.Max(1, m_Height), 24,
                RenderTextureFormat.Default, RenderTextureReadWrite.Default)
            {
                name = "HYC Seam Crossing",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            m_Target.Create();

            if (m_Camera != null)
                m_Camera.targetTexture = m_Target;

            Push();
        }

        private void ReleaseTarget()
        {
            if (m_Target == null)
                return;

            if (m_Camera != null)
                m_Camera.targetTexture = null;

            m_Target.Release();
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(m_Target);
            else
                UnityEngine.Object.DestroyImmediate(m_Target);
            m_Target = null;
        }

        /// <summary>
        /// 跨缝相机必须在主相机定位完成之后、自己出图之前同步位姿，
        /// 否则接缝两侧的物体在运动时会有 1 帧视差抖动。
        /// </summary>
        private void OnAnyCameraPreCull(Camera camera)
        {
            if (!m_Running || m_Camera == null || camera != m_Camera)
                return;

            var compositor = m_Compositor;
            var main = compositor != null && compositor.RigA != null
                ? compositor.RigA.GetComponentInChildren<Camera>(true)
                : null;
            if (main == null)
                return;

            m_Camera.fieldOfView = main.fieldOfView;
            m_Camera.nearClipPlane = main.nearClipPlane;
            m_Camera.farClipPlane = main.farClipPlane;
            m_Camera.depth = main.depth + 1;
            SyncTransform(main);
        }

        private void SyncTransform(Camera main)
        {
            if (m_Camera == null || main == null)
                return;

            var source = main.transform;
            var target = m_Camera.transform;
            target.SetPositionAndRotation(source.position, source.rotation);
        }

        private CinemachineSeamCompositor ResolveCompositor()
        {
            if (m_Compositor == null)
                m_Compositor = GetComponent<CinemachineSeamCompositor>();
            if (m_Compositor == null)
                m_Compositor = GetComponentInParent<CinemachineSeamCompositor>();
            return m_Compositor;
        }

        private int ResolveMask()
        {
            if (m_LayerOverride.value != 0)
                return m_LayerOverride.value;

            var layer = LayerMask.NameToLayer(m_LayerName);
            if (layer < 0)
                return 0;

            return 1 << layer;
        }

        private void OnDestroy()
        {
            StopCrossing();
        }
    }
}
