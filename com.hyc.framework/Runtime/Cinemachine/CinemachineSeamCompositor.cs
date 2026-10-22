// ============================================================
// HYC Framework - Cinemachine 相机装配 · 接缝合成器
// 文件: Runtime/Cinemachine/CinemachineSeamCompositor.cs
// 说明: 把两个 rig 的相机各渲一张整屏 RenderTexture，再由合成后端
//       按 SeamState 逐像素择一，输出到屏幕。
//       相机只负责「渲到 RT」，合成只负责「怎么拼」，两者都不碰对方的职责。
//       合成后端可替换（ISeamCompositeBackend），当前只提供 Overlay Canvas 实现，
//       这样框架不必绑定任何渲染管线。
// ============================================================

using System;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

namespace HYC.Framework.Cinemachine
{
    /// <summary>一帧合成所需的全部输入。</summary>
    public struct SeamPass
    {
        /// <summary>A 侧整屏画面。</summary>
        public RenderTexture A;

        /// <summary>B 侧整屏画面。</summary>
        public RenderTexture B;

        /// <summary>跨缝层画面（只含跨缝物体，背景透明）。无跨缝时为 null。</summary>
        public RenderTexture Cross;

        /// <summary>接缝带宽（屏幕比例），跨缝层只在该带内覆盖。</summary>
        public float CrossingWidth;

        /// <summary>接缝参数（已按驱动源解算过）。</summary>
        public SeamState Seam;

        /// <summary>A 侧染色（alpha = 强度，0 = 不染）。</summary>
        public Color TintA;

        /// <summary>B 侧染色（alpha = 强度，0 = 不染）。</summary>
        public Color TintB;

        /// <summary>
        /// 采样 RT 时是否翻转 Y。默认 0；只有「自己 <c>Graphics.Blit</c>」的自定义后端才需要 1。
        /// </summary>
        public float FlipY;
    }

    /// <summary>接缝合成的底层实现（换渲染管线只需换一个实现）。</summary>
    public interface ISeamCompositeBackend : IDisposable
    {
        /// <summary>把这一帧合成到屏幕。</summary>
        void Composite(in SeamPass pass);

        /// <summary>临时隐藏（例如降档回 <c>Camera.rect</c> 路径）。</summary>
        void SetVisible(bool visible);
    }

    /// <summary>
    /// 全屏 Overlay Canvas 实现：一个铺满屏幕的 RawImage + 自定义材质。
    /// <para>选它是因为它不依赖任何渲染管线（Built-in / URP 都能跑）；
    /// 也不用 GraphicRaycaster，所以不会吃掉 UI 输入。</para>
    /// </summary>
    public sealed class CanvasSeamCompositeBackend : ISeamCompositeBackend
    {
        private static readonly int k_TexA = Shader.PropertyToID("_TexA");
        private static readonly int k_TexB = Shader.PropertyToID("_TexB");
        private static readonly int k_TexCross = Shader.PropertyToID("_TexCross");
        private static readonly int k_SeamPoint = Shader.PropertyToID("_SeamPoint");
        private static readonly int k_SeamAngle = Shader.PropertyToID("_SeamAngle");
        private static readonly int k_Curvature = Shader.PropertyToID("_Curvature");
        private static readonly int k_GapWidth = Shader.PropertyToID("_GapWidth");
        private static readonly int k_GapColor = Shader.PropertyToID("_GapColor");
        private static readonly int k_Feather = Shader.PropertyToID("_Feather");
        private static readonly int k_Noise = Shader.PropertyToID("_Noise");
        private static readonly int k_NoiseSpeed = Shader.PropertyToID("_NoiseSpeed");
        private static readonly int k_Push = Shader.PropertyToID("_Push");
        private static readonly int k_Crossfade = Shader.PropertyToID("_Crossfade");
        private static readonly int k_CrossingWidth = Shader.PropertyToID("_CrossingWidth");
        private static readonly int k_CrossEnabled = Shader.PropertyToID("_CrossEnabled");
        private static readonly int k_TintA = Shader.PropertyToID("_TintA");
        private static readonly int k_TintB = Shader.PropertyToID("_TintB");
        private static readonly int k_FlipY = Shader.PropertyToID("_FlipY");
        private static readonly int k_GlowWidth = Shader.PropertyToID("_GlowWidth");
        private static readonly int k_GlowColor = Shader.PropertyToID("_GlowColor");
        private static readonly int k_GlowPulse = Shader.PropertyToID("_GlowPulse");

        private readonly GameObject m_Root;
        private readonly Material m_Material;
        private readonly RawImage m_Image;
        private bool m_Visible = true;
        private bool m_Disposed;

        public CanvasSeamCompositeBackend(Shader shader, int sortingOrder)
        {
            m_Material = new Material(shader) { hideFlags = HideFlags.DontSave };

            m_Root = new GameObject("[HYC Seam Composite]") { hideFlags = HideFlags.DontSave };
            var canvas = m_Root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var quad = new GameObject("Composite", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            quad.hideFlags = HideFlags.DontSave;
            quad.transform.SetParent(m_Root.transform, false);

            var rect = (RectTransform)quad.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            m_Image = quad.GetComponent<RawImage>();
            m_Image.raycastTarget = false;
            m_Image.material = m_Material;
        }

        /// <summary>材质实例（调试 / 外部改参数用）。</summary>
        public Material Material => m_Material;

        /// <inheritdoc />
        public void Composite(in SeamPass pass)
        {
            if (m_Disposed || !m_Visible)
                return;

            var seam = pass.Seam;
            m_Material.SetTexture(k_TexA, pass.A);
            m_Material.SetTexture(k_TexB, pass.B);
            m_Material.SetTexture(k_TexCross, pass.Cross);
            m_Material.SetVector(k_SeamPoint, new Vector4(seam.Point.x, seam.Point.y, 0f, 0f));
            m_Material.SetFloat(k_SeamAngle, seam.Angle * Mathf.Deg2Rad);
            m_Material.SetFloat(k_Curvature, seam.Curvature);
            m_Material.SetFloat(k_GapWidth, seam.GapWidth);
            m_Material.SetColor(k_GapColor, seam.GapColor);
            m_Material.SetFloat(k_Feather, seam.Feather);
            m_Material.SetFloat(k_Noise, seam.Noise);
            m_Material.SetFloat(k_NoiseSpeed, seam.NoiseSpeed);
            m_Material.SetFloat(k_Push, seam.Push);
            m_Material.SetFloat(k_Crossfade, seam.Crossfade);
            m_Material.SetFloat(k_CrossingWidth, pass.CrossingWidth);
            m_Material.SetFloat(k_CrossEnabled, pass.Cross != null ? 1f : 0f);
            m_Material.SetColor(k_TintA, pass.TintA);
            m_Material.SetColor(k_TintB, pass.TintB);
            m_Material.SetFloat(k_FlipY, pass.FlipY);
            m_Material.SetFloat(k_GlowWidth, seam.GlowWidth);
            m_Material.SetColor(k_GlowColor, seam.GlowColor);
            m_Material.SetFloat(k_GlowPulse, seam.GlowPulse);
        }

        /// <inheritdoc />
        public void SetVisible(bool visible)
        {
            if (m_Disposed)
                return;
            m_Visible = visible;
            m_Image.enabled = visible;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;

            SafeDestroy(m_Material);
            SafeDestroy(m_Root);
        }

        private static void SafeDestroy(UnityEngine.Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }
    }

    /// <summary>
    /// 双相机接缝合成器：两个 rig 的相机各渲一张整屏 RT，按 <see cref="SeamState"/> 合成。
    /// <para><b>所有权</b>：本组件独占参与相机的 <c>targetTexture</c>；接缝参数由本组件或
    /// <see cref="CinemachineViewSplitter"/> 写入。宿主不要再手改参与相机的 targetTexture。</para>
    /// <para><b>平台无关</b>：合成走 Overlay Canvas，不依赖 Built-in / URP / HDRP 的渲染管线 API。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineSeamCompositor : MonoBehaviour
    {
        [Tooltip("合成用的 Shader。留空时从 Resources/HYC/SeamComposite 加载。")]
        [SerializeField] private Shader m_Shader;

        [Tooltip("接缝由谁驱动。Authored = 只认 SetSeam；FollowPlayers = 跟随两个世界目标。")]
        [SerializeField] private SeamSource m_Source = SeamSource.Authored;

        [Tooltip("初始接缝（Authored 模式下的基准值）。")]
        [SerializeField] private SeamState m_Seam = SeamState.Vertical;

        [Tooltip("合成画面的 Canvas 排序号。要压在其他 UI 之下就取负值。")]
        [SerializeField] private int m_SortingOrder = -100;

        [Tooltip("RT 分辨率相对屏幕的缩放。1 = 一对一；低端机可降到 0.75 换性能（会轻微变糊）。")]
        [SerializeField, Range(0.25f, 1f)] private float m_RenderScale = 1f;

        [Tooltip("RT 的 MSAA 采样数。合成后没有全屏后处理，项目若未启用 post-AA 请设为 4。")]
        [SerializeField] private int m_MsaaSamples = 1;

        [Tooltip("FollowPlayers 模式下接缝角度的最大转速（度/秒），避免接缝甩动。")]
        [SerializeField] private float m_MaxRotationPerSecond = 150f;

        [Tooltip("FollowPlayers 模式下两个屏幕投影的合并死区（屏幕比例），低于它不更新角度与位置。")]
        [SerializeField] private float m_FollowDeadZone = 0.04f;

        [Tooltip("FollowPlayers 模式下接缝在两人之间的取点位置。0.5 = 垂直平分；越小 A 那半越窄，越大 B 那半越窄。")]
        [SerializeField, Range(0f, 1f)] private float m_SplitWeight = 0.5f;

        [Tooltip("任一侧的最小占比。跟随玩家时两人靠近会压扁一侧，低于它就把接缝推回来。")]
        [SerializeField, Range(0f, 0.49f)] private float m_MinShare = 0.15f;

        private CinemachineRigBinder m_RigA;
        private CinemachineRigBinder m_RigB;
        private Camera m_CameraA;
        private Camera m_CameraB;

        /// <summary>接缝解算用的投影相机（默认 A 相机）。合成用的相机不参与解算，两者不能混。</summary>
        private Camera m_ProjectionCamera;

        private Transform m_FollowA;
        private Transform m_FollowB;
        private Transform m_SeamTransform;

        private RenderTexture m_RtA;
        private RenderTexture m_RtB;
        private RenderTexture m_RtCross;
        private float m_CrossingWidth;
        private Color m_TintA = new Color(0f, 0f, 0f, 0f);
        private Color m_TintB = new Color(0f, 0f, 0f, 0f);

        private ISeamCompositeBackend m_Backend;
        private int m_Width;
        private int m_Height;
        private bool m_Active;
        private bool m_Suspended;

        /// <summary>接缝参数变化时触发（已解算的最终值）。</summary>
        public event Action<SeamState> SeamChanged;

        /// <summary>A 侧 rig。</summary>
        public CinemachineRigBinder RigA => m_RigA;

        /// <summary>B 侧 rig。</summary>
        public CinemachineRigBinder RigB => m_RigB;

        /// <summary>A 侧整屏画面。可直接当纹理贴到世界里的屏幕上做嵌套视口。</summary>
        public RenderTexture RenderTargetA => m_RtA;

        /// <summary>B 侧整屏画面。</summary>
        public RenderTexture RenderTargetB => m_RtB;

        /// <summary>接缝驱动源。切换后下一帧生效。</summary>
        public SeamSource Source
        {
            get => m_Source;
            set => m_Source = value;
        }

        /// <summary>当前解算出的接缝参数。</summary>
        public SeamState Seam => m_Seam;

        /// <summary>
        /// <see cref="SeamSource.FollowPlayers"/> 下接缝在两人之间的取点位置。
        /// 0.5 = 垂直平分（两边对半）；&lt; 0.5 接缝靠向 A，A 那半变窄；&gt; 0.5 反之。
        /// </summary>
        public float SplitWeight
        {
            get => m_SplitWeight;
            set => m_SplitWeight = Mathf.Clamp01(value);
        }

        /// <summary>任一侧的最小占屏比例（0..0.49），0 = 允许压到零宽。</summary>
        public float MinShare
        {
            get => m_MinShare;
            set => m_MinShare = Mathf.Clamp(value, 0f, 0.49f);
        }

        /// <summary>A 侧当前占屏比例（0..1，跟随解算后的实时值）。</summary>
        public float SideAShare => m_Seam.SideAShare();

        /// <summary>是否已就绪（有两侧 rig 与相机）。</summary>
        public bool IsReady => m_Active;

        /// <summary>是否正在合成。</summary>
        public bool IsCompositing => m_Active && !m_Suspended;

        /// <summary>合成后端（换管线时替换）。</summary>
        public ISeamCompositeBackend Backend => m_Backend;

        /// <summary>
        /// 采样两侧 RT 时是否上下翻转。<b>默认 0（不翻）</b>。
        /// <para>合成画在 <c>ScreenSpaceOverlay</c> Canvas 的 <c>RawImage</c> 上，UI 四边形的 UV 与
        /// RT 内容同向（RawImage 直接显示 RT 本来就是正立的），所以这里<b>不需要</b>按
        /// <c>SystemInfo.graphicsUVStartsAtTop</c> 再翻一次 —— 翻了就是整幅画面上下颠倒，
        /// 而接缝数学用的是视口空间没跟着翻，表现出来就是「画面倒了但切线位置对」。
        /// 若换到确实需要翻的自定义后端（自己 <c>Graphics.Blit</c> 的），把它设成 1。</para>
        /// </summary>
        public float FlipY { get; set; } = 0f;

        // ---------- 绑定 ----------

        /// <summary>绑定两侧 rig（顺序决定 A / B）。</summary>
        public void SetTargets(CinemachineRigBinder rigA, CinemachineRigBinder rigB)
        {
            if (m_RigA == rigA && m_RigB == rigB && m_Active)
                return;

            RestoreCameras();
            m_RigA = rigA;
            m_RigB = rigB;
            m_CameraA = rigA != null ? rigA.GetComponentInChildren<Camera>(true) : null;
            m_CameraB = rigB != null ? rigB.GetComponentInChildren<Camera>(true) : null;
            m_ProjectionCamera = m_CameraA;
            m_Active = m_CameraA != null && m_CameraB != null && m_CameraA != m_CameraB;

            if (!m_Active)
            {
                if (rigA != null && rigB != null)
                    Debug.LogWarning("[HYC.Cinemachine] 接缝合成需要两个各自带 Camera 的 rig。");
                return;
            }

            EnsureBackend();
            EnsureTextures(true);
            ApplyCameraTargets();
        }

        /// <summary>解绑并还原相机。</summary>
        public void ClearTargets()
        {
            RestoreCameras();
            m_RigA = null;
            m_RigB = null;
            m_CameraA = null;
            m_CameraB = null;
            m_Active = false;
        }

        /// <summary>设置接缝（Authored 模式下直接生效；其他模式只更新基准样式）。</summary>
        public void SetSeam(in SeamState seam)
        {
            SetSeam(seam, false);
        }

        /// <summary>
        /// 设置接缝。<paramref name="keepSource"/> = true 时不改驱动源
        /// （补间 / 外部驱动源自己写值时用，避免把 FollowPlayers 打回 Authored）。
        /// </summary>
        public void SetSeam(in SeamState seam, bool keepSource)
        {
            m_Seam = seam;
            if (!keepSource)
                m_Source = SeamSource.Authored;
            ApplySeam();
        }

        /// <summary>按布局枚举设置接缝（左右 / 上下 / 单屏；四宫格与画中画请走 Camera.rect 路径）。</summary>
        public void SetLayout(ViewLayout layout)
        {
            SetSeam(SeamPresets.FromLayout(layout));
        }

        /// <summary>FollowPlayers 模式的两个世界目标。参考相机只参与屏幕投影，不参与渲染。</summary>
        public void SetFollowTargets(Transform a, Transform b, Camera reference)
        {
            m_FollowA = a;
            m_FollowB = b;
            m_Source = SeamSource.FollowPlayers;
            if (reference != null)
                m_ProjectionCamera = reference;
        }

        /// <summary>Transform 模式：接缝位置与朝向取自该物体的世界位置与右方向。</summary>
        public void SetTransformSource(Transform seam)
        {
            m_SeamTransform = seam;
            m_Source = SeamSource.Transform;
        }

        /// <summary>
        /// 跟随单个目标：接缝保持当前角度，整体平移到该目标的屏幕位置上
        /// （「分屏线跟着这个角色走」）。
        /// </summary>
        public void SetFollowTarget(Transform target, Camera reference)
        {
            m_FollowA = target;
            m_Source = SeamSource.FollowTarget;
            if (reference != null)
                m_ProjectionCamera = reference;
        }

        /// <summary>挂上跨缝层。传 null 关闭。</summary>
        public void SetCrossing(RenderTexture cross, float width)
        {
            m_RtCross = cross;
            m_CrossingWidth = Mathf.Max(0f, width);
        }

        // ---------- 单侧染色 ----------

        /// <summary>给两侧染色。alpha 为强度，alpha = 0 表示不染。</summary>
        public void SetSideTint(Color sideA, Color sideB)
        {
            m_TintA = sideA;
            m_TintB = sideB;
        }

        /// <summary>只染一侧。</summary>
        /// <param name="player">0 = A 侧，1 = B 侧。</param>
        public void SetSideTint(int player, Color color)
        {
            if (player == 0)
                m_TintA = color;
            else
                m_TintB = color;
        }

        /// <summary>清掉两侧染色。</summary>
        public void ClearSideTint()
        {
            m_TintA = new Color(0f, 0f, 0f, 0f);
            m_TintB = new Color(0f, 0f, 0f, 0f);
        }

        /// <summary>取某一侧当前染色。</summary>
        public Color GetSideTint(int player)
        {
            return player == 0 ? m_TintA : m_TintB;
        }

        /// <summary>挂起：还原相机、隐藏合成层（降档或切回 Camera.rect 时用）。</summary>
        public void Suspend()
        {
            if (m_Suspended)
                return;
            m_Suspended = true;
            RestoreCameras();
            if (m_Backend != null)
                m_Backend.SetVisible(false);
        }

        /// <summary>恢复合成。</summary>
        public void Resume()
        {
            if (!m_Suspended)
                return;
            m_Suspended = false;
            if (m_Active)
            {
                EnsureTextures(true);
                ApplyCameraTargets();
            }
            if (m_Backend != null)
                m_Backend.SetVisible(true);
        }

        // ---------- 生命周期 ----------

        private void OnEnable()
        {
            if (m_Active)
                Resume();
        }

        private void OnDisable()
        {
            Suspend();
        }

        private void OnDestroy()
        {
            RestoreCameras();
            ReleaseTextures();
            m_Backend?.Dispose();
            m_Backend = null;
        }

        private void LateUpdate()
        {
            if (!m_Active || m_Suspended || m_Backend == null)
                return;

            var scaledWidth = Mathf.Max(1, Mathf.RoundToInt(Screen.width * m_RenderScale));
            var scaledHeight = Mathf.Max(1, Mathf.RoundToInt(Screen.height * m_RenderScale));
            if (scaledWidth != m_Width || scaledHeight != m_Height)
                EnsureTextures(true);

            ResolveSource();
            m_Backend.Composite(BuildPass());
        }

        // ---------- 内部：初始化 ----------

        private void EnsureBackend()
        {
            if (m_Backend != null)
                return;

            var shader = m_Shader != null ? m_Shader : Resources.Load<Shader>("HYC/SeamComposite");
            if (shader == null)
                shader = Shader.Find("HYC/Cinemachine/SeamComposite");

            if (shader == null)
            {
                Debug.LogError("[HYC.Cinemachine] 找不到接缝合成 Shader（HYC/Cinemachine/SeamComposite）。");
                return;
            }

            m_Backend = new CanvasSeamCompositeBackend(shader, m_SortingOrder);
        }

        private void EnsureTextures(bool force)
        {
            var width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * m_RenderScale));
            var height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * m_RenderScale));

            if (!force && m_RtA != null && width == m_Width && height == m_Height)
                return;

            ReleaseTextures();
            m_Width = width;
            m_Height = height;

            var samples = SnapMsaa(m_MsaaSamples);
            m_RtA = CreateTexture("HYC Seam A", width, height, samples);
            m_RtB = CreateTexture("HYC Seam B", width, height, samples);

            ApplyCameraTargets();
        }

        private RenderTexture CreateTexture(string name, int width, int height, int samples)
        {
            // LDR：每路相机的 tonemap 已经写进 RT，合成层只做像素搬运。
            // 用 HDR 会让 UI 采样后再做一次色彩变换，画面会发灰或过曝。
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.Default, RenderTextureReadWrite.Default)
            {
                name = name,
                antiAliasing = samples,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            rt.Create();
            return rt;
        }

        private static int SnapMsaa(int samples)
        {
            if (samples >= 8) return 8;
            if (samples >= 4) return 4;
            if (samples >= 2) return 2;
            return 1;
        }

        private void ApplyCameraTargets()
        {
            if (m_CameraA != null && m_RtA != null)
            {
                // 必须显式打开：Camera.rect 路径会把「没进槽位」的相机 enabled = false，
                // 切到合成后没有任何人再把它们打开，那一半就永远是黑的
                m_CameraA.enabled = true;
                m_CameraA.rect = new Rect(0f, 0f, 1f, 1f);
                m_CameraA.targetTexture = m_RtA;
            }
            if (m_CameraB != null && m_RtB != null)
            {
                m_CameraB.enabled = true;
                m_CameraB.rect = new Rect(0f, 0f, 1f, 1f);
                m_CameraB.targetTexture = m_RtB;
            }
        }

        private void RestoreCameras()
        {
            if (m_CameraA != null && m_CameraA.targetTexture == m_RtA)
                m_CameraA.targetTexture = null;
            if (m_CameraB != null && m_CameraB.targetTexture == m_RtB)
                m_CameraB.targetTexture = null;
        }

        private void ReleaseTextures()
        {
            ReleaseTexture(ref m_RtA);
            ReleaseTexture(ref m_RtB);

            // 跨缝层归外部（CinemachineSeamCrossing）所有，这里只解除引用
            m_RtCross = null;
        }

        private static void ReleaseTexture(ref RenderTexture rt)
        {
            if (rt == null)
                return;
            rt.Release();
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(rt);
            else
                UnityEngine.Object.DestroyImmediate(rt);
            rt = null;
        }

        // ---------- 内部：接缝解算 ----------

        private SeamPass BuildPass()
        {
            return new SeamPass
            {
                A = m_RtA,
                B = m_RtB,
                Cross = m_RtCross,
                CrossingWidth = m_CrossingWidth,
                Seam = m_Seam,
                TintA = m_TintA,
                TintB = m_TintB,
                // RawImage 上的 RT 与 UI 四边形 UV 同向，默认不翻（见 FlipY 属性注释）
                FlipY = this.FlipY,
            };
        }

        private void ResolveSource()
        {
            switch (m_Source)
            {
                case SeamSource.FollowPlayers:
                    ResolveFollowPlayers();
                    break;
                case SeamSource.Transform:
                    ResolveTransform();
                    break;
                case SeamSource.FollowTarget:
                    ResolveFollowTarget();
                    break;
            }
        }

        /// <summary>接缝保持当前角度整体平移，使接缝线穿过目标的屏幕位置。</summary>
        private void ResolveFollowTarget()
        {
            var camera = m_ProjectionCamera;
            if (camera == null || m_FollowA == null)
                return;

            var viewport = camera.WorldToViewportPoint(m_FollowA.position);
            if (viewport.z <= 0f)
                return;

            var seam = m_Seam;
            seam.GetFrame(out _, out var normal);

            var point = new Vector2(viewport.x, viewport.y);
            seam.Point += normal * Vector2.Dot(point - seam.Point, normal);
            CommitSeam(seam);
        }

        private void ResolveFollowPlayers()
        {
            var camera = m_ProjectionCamera;
            if (camera == null || m_FollowA == null || m_FollowB == null)
                return;

            var vpA = camera.WorldToViewportPoint(m_FollowA.position);
            var vpB = camera.WorldToViewportPoint(m_FollowB.position);

            // 任一方在相机背后时投影无意义，保持上一帧的接缝
            if (vpA.z <= 0f || vpB.z <= 0f)
                return;

            var pointA = new Vector2(vpA.x, vpA.y);
            var pointB = new Vector2(vpB.x, vpB.y);
            var delta = pointB - pointA;

            // 两人投影几乎重合时不更新角度，否则接缝会抖
            if (delta.sqrMagnitude <= m_FollowDeadZone * m_FollowDeadZone)
                return;

            // 接缝法线指向 A→B，所以角度直接取两点连线的屏幕方向
            var target = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var step = Mathf.Clamp(Mathf.DeltaAngle(m_Seam.Angle, target),
                -m_MaxRotationPerSecond * Time.unscaledDeltaTime,
                m_MaxRotationPerSecond * Time.unscaledDeltaTime);

            var seam = m_Seam;
            // 按权重在两人之间取点：0.5 是垂直平分线，偏离后两侧占比不再相等（一大一小）
            seam.Point = Vector2.Lerp(pointA, pointB, m_SplitWeight);
            seam.Angle = Mathf.Repeat(m_Seam.Angle + step, 360f);
            seam.ClampShare(m_MinShare);
            CommitSeam(seam);
        }

        /// <summary>
        /// Transform 驱动：接缝**线**沿物体的 <c>up</c> 方向，法线取物体的 <c>right</c>，
        /// 位置取物体在世界里的位置投影到屏幕。
        /// </summary>
        private void ResolveTransform()
        {
            var camera = m_ProjectionCamera;
            if (camera == null || m_SeamTransform == null)
                return;

            var origin = camera.WorldToViewportPoint(m_SeamTransform.position);
            var tip = camera.WorldToViewportPoint(m_SeamTransform.position + m_SeamTransform.right);
            if (origin.z <= 0f || tip.z <= 0f)
                return;

            var delta = new Vector2(tip.x - origin.x, tip.y - origin.y);
            if (delta.sqrMagnitude <= 1e-8f)
                return;

            var seam = m_Seam;
            seam.Point = new Vector2(origin.x, origin.y);
            seam.Angle = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, 360f);
            CommitSeam(seam);
        }

        private void ApplySeam()
        {
            SeamChanged?.Invoke(m_Seam);
        }

        /// <summary>驱动源每帧解算时用：只在真正变了的时候抛事件，避免订阅方被每帧刷。</summary>
        private void CommitSeam(SeamState seam)
        {
            const float epsilon = 1e-4f;
            var changed = (seam.Point - m_Seam.Point).sqrMagnitude > epsilon
                || Mathf.Abs(Mathf.DeltaAngle(seam.Angle, m_Seam.Angle)) > 0.01f;

            m_Seam = seam;
            if (changed)
                ApplySeam();
        }
    }
}
