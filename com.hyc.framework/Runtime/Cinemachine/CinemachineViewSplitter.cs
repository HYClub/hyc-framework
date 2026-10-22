// ============================================================
// HYC Framework - Cinemachine 相机装配 · 同屏 / 分屏视口调度
// 文件: Runtime/Cinemachine/CinemachineViewSplitter.cs
// 说明: 场景里放一份，把若干 rig 实例登记进来，SetLayout 一键切换
//       同屏 / 左右 / 上下 / 2x2 / 画中画。
//       只写 Camera 的 rect / depth / clearFlags / enabled、机位 Lens 的 FOV、
//       机位的 OutputChannel（多 rig 分流，可关）、以及（可关的）AudioListener 独占
//       ——不改 rig 结构、不管实例化、不管输入与音频路由（那些归宿主）。
// ============================================================

using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>屏幕布局。</summary>
    public enum ViewLayout
    {
        /// <summary>同屏：只显示 Focused 那一份，其余 rig 的相机停渲染。</summary>
        Single,

        /// <summary>左右分屏（左 = 先登记的 / 顺序表里的第一个）。</summary>
        Vertical,

        /// <summary>上下分屏（上 = 先登记的 / 顺序表里的第一个）。</summary>
        Horizontal,

        /// <summary>2x2 四宫格（最多 4 份，按 Z 顺序填：左上→右上→左下→右下）。</summary>
        Quad,

        /// <summary>画中画：Focused 满屏，第二份缩成右下角小窗（小窗不清背景色）。</summary>
        PictureInPicture,
    }

    /// <summary>窄视口下的视野补偿策略。</summary>
    public enum FovPolicy
    {
        /// <summary>不动 FOV：左右分屏时两格看得更窄、上下分屏时看得更宽。</summary>
        None,

        /// <summary>保持横向视野：视口变窄就把 FOV 放大，两格看到的世界宽度与同屏一致。</summary>
        KeepHorizontal,

        /// <summary>只放大不缩小：取「不补偿」与「保持横向」中较大的 FOV（上下分屏时不会突然变窄）。</summary>
        Overscan,
    }

    /// <summary>分屏的渲染实现。</summary>
    public enum ViewRenderMode
    {
        /// <summary>
        /// 走 <c>Camera.rect</c>：零额外开销，但只能轴对齐矩形。
        /// 四宫格、画中画只能用它。
        /// </summary>
        Viewport,

        /// <summary>
        /// 双整屏 RenderTexture + 接缝合成：任意角度 / 弯曲 / 羽化 / 合并。
        /// 代价是两侧都渲整屏（像素量约 2 倍）。
        /// </summary>
        Composite,
    }

    /// <summary>
    /// 同屏 / 分屏调度器：把多个 rig 实例的 <see cref="Camera"/> 按布局切分屏幕。
    /// <para>用法：场景里挂一份 → 每实例化一份 rig 就 <see cref="Register"/> →
    /// <see cref="SetLayout"/> 一键切换。</para>
    /// <code>
    /// splitter.Register(rigA);
    /// splitter.Register(rigB);
    /// splitter.Focused = rigA;
    /// splitter.SetLayout(ViewLayout.Vertical);
    /// </code>
    /// <para><b>所有权</b>：本组件独占参与 rig 上 Camera 的 rect / depth / clearFlags / enabled，
    /// 以及各机位的 Lens.FOV（按 <see cref="FovPolicy"/> 从登记时的基准值重算）。
    /// 宿主不要再手改这些值；分屏期间改 FOV 请改 rig 后再 <see cref="Unregister"/> /
    /// <see cref="Register"/> 重拍基准。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineViewSplitter : MonoBehaviour
    {
        /// <summary>画中画小窗相对屏幕的边距与尺寸（宽高比例相同 ⇒ 小窗保持整屏宽高比）。</summary>
        private const float k_PipInset = 0.03f;
        private const float k_PipSize = 0.28f;

        private const float k_MinFov = 1f;
        private const float k_MaxFov = 160f;

        private sealed class RigEntry
        {
            public CinemachineRigBinder Binder;
            public Camera Camera;
            public AudioListener Listener;
            public CameraClearFlags BaseClearFlags;
            public Color BaseBackground;
            public float BaseDepth;
            public readonly List<CinemachineCamera> Cameras = new List<CinemachineCamera>();
            public readonly List<float> BaseFovs = new List<float>();
        }

        [Tooltip("初始布局。运行期用 SetLayout 切换。")]
        [SerializeField] ViewLayout m_Layout = ViewLayout.Single;

        [Tooltip("窄视口是否补偿 FOV。None = 不动；KeepHorizontal = 保持横向视野；Overscan = 只放大不缩小。")]
        [SerializeField] FovPolicy m_FovPolicy = FovPolicy.KeepHorizontal;

        [Tooltip("是否让 Focused 那份独占 AudioListener（Unity 同时只允许一个监听器，多余的会持续告警）。")]
        [SerializeField] bool m_ManageAudioListener = true;

        [Tooltip("登记多份 rig 时自动分配 Cinemachine 输出通道。CM3 的 Brain 是**全局**挑机位" +
                 "（只按 OutputChannel 过滤，不看层级），两份 rig 各带 Brain 而不分流 ⇒ 所有 Brain 抢同一台机位，两半画面一模一样。")]
        [SerializeField] bool m_ManageOutputChannels = true;

        [Tooltip("分屏渲染实现。Viewport = Camera.rect（省像素，只能轴对齐）；Composite = 双 RT + 接缝合成（任意接缝）。")]
        [SerializeField] ViewRenderMode m_RenderMode = ViewRenderMode.Viewport;

        [Tooltip("接缝合成器。留空且 RenderMode = Composite 时在运行期自动挂一份。")]
        [SerializeField] CinemachineSeamCompositor m_Compositor;

        private readonly List<RigEntry> m_Entries = new List<RigEntry>();
        private readonly List<RigEntry> m_Order = new List<RigEntry>();
        private CinemachineRigBinder m_Focused;
        private bool m_HasExplicitOrder;
        private bool m_CompositeActive;

        /// <summary>布局切换后触发（重复设置同一个布局不触发）。</summary>
        public event Action<ViewLayout> LayoutChanged;

        /// <summary>当前布局。</summary>
        public ViewLayout Layout => m_Layout;

        /// <summary>FOV 补偿策略。赋值立即应用。</summary>
        public FovPolicy FovPolicy
        {
            get => m_FovPolicy;
            set
            {
                m_FovPolicy = value;
                Apply();
            }
        }

        /// <summary>是否由本组件独占 AudioListener。赋值立即应用。</summary>
        public bool ManageAudioListener
        {
            get => m_ManageAudioListener;
            set
            {
                m_ManageAudioListener = value;
                Apply();
            }
        }

        /// <summary>
        /// 是否自动给每份 rig 分配 Cinemachine 输出通道（多 rig 同屏时必须开）。
        /// <para><b>为什么必须分流</b>：CM 3 的 <see cref="CinemachineBrain"/> 是从**全局**机位表里
        /// 挑优先级最高的那台，只看 <c>OutputChannel</c> 与自己的 <c>ChannelMask</c> 是否相交，
        /// 完全不看层级。两份 rig 各带 Brain 又都用默认通道时，两个 Brain 会挑到同一台机位
        /// —— 表现为「两半画面内容一模一样」，且不会报任何错。</para>
        /// <para>打开后第 1 份 rig 用 <c>Default</c>，第 2 份起依次是 <c>Channel01/02…</c>（最多 9 份）。</para>
        /// </summary>
        public bool ManageOutputChannels
        {
            get => m_ManageOutputChannels;
            set
            {
                if (m_ManageOutputChannels == value)
                    return;

                m_ManageOutputChannels = value;
                if (!value)
                    return;

                for (var i = 0; i < m_Entries.Count; i++)
                    AssignChannel(m_Entries[i], i);
            }
        }

        /// <summary>
        /// 当前渲染实现。只在 Inspector 里改，运行期不允许随意切换
        /// —— 两种模式的相机 aspect 不同，切了会跳变。要降档用 <see cref="DowngradeToViewport"/>。
        /// </summary>
        public ViewRenderMode RenderMode => m_RenderMode;

        /// <summary>接缝合成器（RenderMode = Composite 时才有）。</summary>
        public CinemachineSeamCompositor Compositor => m_Compositor;

        /// <summary>当前是否正走在合成路径上。</summary>
        public bool IsCompositeActive => m_CompositeActive;

        /// <summary>
        /// 单向降档：从合成模式退回 <c>Camera.rect</c>（低端机兜底用）。
        /// <para>降档那一下画面会跳（视野变了），之后不可逆。</para>
        /// </summary>
        /// <returns>确实发生了降档返回 true。</returns>
        public bool DowngradeToViewport()
        {
            if (m_RenderMode != ViewRenderMode.Composite)
                return false;

            m_RenderMode = ViewRenderMode.Viewport;
            m_CompositeActive = false;

            if (m_Compositor != null)
                m_Compositor.Suspend();

            Apply();
            return true;
        }

        /// <summary>
        /// 设置任意接缝（自动切到合成路径）。
        /// <para>只有登记了两份以上 rig 时才生效，否则退回 <c>Camera.rect</c>。</para>
        /// </summary>
        public void SetSeam(in SeamState seam)
        {
            m_RenderMode = ViewRenderMode.Composite;

            var compositor = EnsureCompositor();
            if (compositor == null || !TryBindComposite(compositor))
            {
                Apply();
                return;
            }

            compositor.SetSeam(seam);
            if (!SeamPresets.IsHalfPlane(m_Layout))
                m_Layout = ViewLayout.Vertical;
            m_CompositeActive = true;
            ApplyListeners();
        }

        /// <summary>已登记的 rig 数量（含当前布局没显示的）。</summary>
        public int RigCount => m_Entries.Count;

        /// <summary>主视角 rig：同屏时就是它占满屏，画中画时它满屏、其余进小窗。</summary>
        public CinemachineRigBinder Focused
        {
            get => m_Focused;
            set
            {
                m_Focused = value;
                Apply();
            }
        }

        // ---------- 登记 ----------

        /// <summary>登记一份 rig 实例（实例化 rig 后调用；重复登记会被忽略）。</summary>
        public void Register(CinemachineRigBinder rig)
        {
            if (rig == null || Find(rig) != null)
                return;

            var camera = rig.GetComponentInChildren<Camera>(true);
            if (camera == null)
            {
                Debug.LogWarning("[HYC.Cinemachine] rig 上没有 Camera，无法参与分屏：" + rig.name);
                return;
            }

            var entry = new RigEntry
            {
                Binder = rig,
                Camera = camera,
                Listener = rig.GetComponentInChildren<AudioListener>(true),
                BaseClearFlags = camera.clearFlags,
                BaseBackground = camera.backgroundColor,
                BaseDepth = camera.depth,
            };

            var cameras = rig.Cameras;
            for (int i = 0; i < cameras.Count; i++)
            {
                var vcam = cameras[i];
                if (vcam == null)
                    continue;
                entry.Cameras.Add(vcam);
                entry.BaseFovs.Add(vcam.Lens.FieldOfView);
            }

            m_Entries.Add(entry);

            // 每份 rig 一条输出通道：不分流的话第 2 份 rig 的机位永远抢不过第 1 份
            if (m_ManageOutputChannels)
                AssignChannel(entry, m_Entries.Count - 1);

            if (m_Focused == null)
                m_Focused = rig;
            Apply();
        }

        /// <summary>rig 序号 → 输出通道。0 = Default，之后依次 Channel01 / Channel02 …（上限 8）。</summary>
        private static OutputChannels ChannelForIndex(int index)
        {
            return index <= 0 ? OutputChannels.Default : (OutputChannels)(1 << Mathf.Clamp(index, 0, 8));
        }

        /// <summary>把 rig 里的全部机位与它的 Brain 一起收窄到同一条通道。</summary>
        private static void AssignChannel(RigEntry entry, int index)
        {
            if (entry.Binder == null)
                return;

            var channel = ChannelForIndex(index);

            var vcams = entry.Binder.GetComponentsInChildren<CinemachineVirtualCameraBase>(true);
            for (var i = 0; i < vcams.Length; i++)
            {
                if (vcams[i] != null)
                    vcams[i].OutputChannel = channel;
            }

            var brains = entry.Binder.GetComponentsInChildren<CinemachineBrain>(true);
            for (var i = 0; i < brains.Length; i++)
            {
                if (brains[i] != null)
                    brains[i].ChannelMask = channel;
            }
        }

        /// <summary>注销一份 rig（退场 / 销毁前调用），并把它还原成整屏、原始 FOV。</summary>
        public void Unregister(CinemachineRigBinder rig)
        {
            var entry = Find(rig);
            if (entry == null)
                return;

            Restore(entry);
            m_Entries.Remove(entry);
            if (m_Focused == rig)
                m_Focused = m_Entries.Count > 0 ? m_Entries[0].Binder : null;
            Apply();
        }

        // ---------- 布局 ----------

        /// <summary>一键切布局（按登记顺序排槽位，<see cref="Focused"/> 优先占第一个槽）。</summary>
        public void SetLayout(ViewLayout layout)
        {
            m_HasExplicitOrder = false;
            SetLayoutInternal(layout);
        }

        /// <summary>
        /// 一键切布局并指定槽位顺序（<paramref name="order"/>[0] 进第一个槽，
        /// 未列出的 rig 按登记顺序补在后面；单人/画中画布局下它就是主视角）。
        /// </summary>
        public void SetLayout(ViewLayout layout, params CinemachineRigBinder[] order)
        {
            m_Order.Clear();
            m_HasExplicitOrder = order != null && order.Length > 0;
            if (m_HasExplicitOrder)
            {
                for (int i = 0; i < order.Length; i++)
                {
                    var entry = Find(order[i]);
                    if (entry != null && !m_Order.Contains(entry))
                        m_Order.Add(entry);
                }
                for (int i = 0; i < m_Entries.Count; i++)
                {
                    if (!m_Order.Contains(m_Entries[i]))
                        m_Order.Add(m_Entries[i]);
                }
                if (order[0] != null)
                    m_Focused = order[0];
            }

            SetLayoutInternal(layout);
        }

        private void SetLayoutInternal(ViewLayout layout)
        {
            var changed = m_Layout != layout;
            m_Layout = layout;
            Apply();
            if (m_CompositeActive && m_Compositor != null)
                m_Compositor.SetLayout(m_Layout);
            if (changed)
                LayoutChanged?.Invoke(layout);
        }

        // ---------- 合成路径 ----------

        /// <summary>
        /// 尝试走接缝合成。四宫格 / 画中画不是半平面，强制回落 <c>Camera.rect</c>。
        /// </summary>
        private bool TryApplyComposite()
        {
            if (m_RenderMode != ViewRenderMode.Composite || !SeamPresets.IsHalfPlane(m_Layout))
            {
                m_CompositeActive = false;
                if (m_Compositor != null)
                    m_Compositor.Suspend();
                return false;
            }

            var compositor = EnsureCompositor();
            if (compositor == null || !TryBindComposite(compositor))
            {
                m_CompositeActive = false;
                return false;
            }

            // 宿主自己改成 FollowPlayers / Transform 驱动时不要被布局覆盖
            if (compositor.Source == SeamSource.Authored)
                compositor.SetLayout(m_Layout);

            m_CompositeActive = true;
            ApplyListeners();
            return true;
        }

        private bool TryBindComposite(CinemachineSeamCompositor compositor)
        {
            var order = BuildOrder();
            if (order.Count < 2)
                return false;

            for (var i = 2; i < order.Count; i++)
                Hide(order[i]);

            compositor.SetTargets(order[0].Binder, order[1].Binder);

            // 之前可能被 Suspend 过（切去 Quad / 画中画 / 同屏再切回来）。SetTargets 对
            // 「同一对 rig」是幂等的、不会自动恢复，所以这里必须显式 Resume，否则合成层
            // 一直隐藏、两侧相机也不渲染 → 整屏黑。
            var ready = compositor.IsReady;
            if (ready)
                compositor.Resume();
            return ready;
        }

        private CinemachineSeamCompositor EnsureCompositor()
        {
            if (m_Compositor != null)
                return m_Compositor;

            m_Compositor = GetComponent<CinemachineSeamCompositor>();
            if (m_Compositor != null)
                return m_Compositor;

            // 编辑期不自动加组件，避免把场景改脏
            if (!Application.isPlaying)
                return null;

            m_Compositor = gameObject.AddComponent<CinemachineSeamCompositor>();
            return m_Compositor;
        }

        /// <summary>合成路径下同时只有一个 AudioListener：Focused 那份。</summary>
        private void ApplyListeners()
        {
            if (!m_ManageAudioListener)
                return;

            var order = BuildOrder();
            if (order.Count == 0)
                return;

            var focusedIndex = 0;
            for (var i = 0; i < order.Count; i++)
            {
                if (m_Focused != null && order[i].Binder == m_Focused)
                {
                    focusedIndex = i;
                    break;
                }
            }

            for (var i = 0; i < order.Count; i++)
            {
                if (order[i].Listener != null)
                    order[i].Listener.enabled = i == focusedIndex;
            }
        }

        // ---------- 应用 ----------

        private void Apply()
        {
            if (m_Entries.Count == 0)
                return;

            if (TryApplyComposite())
                return;

            m_CompositeActive = false;

            var order = BuildOrder();
            var capacity = Capacity(m_Layout);
            var screenAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            var focused = m_Focused;

            for (int i = 0; i < order.Count; i++)
            {
                var entry = order[i];
                if (i >= capacity)
                {
                    Hide(entry);
                    continue;
                }

                var isFocused = focused == null ? i == 0 : entry.Binder == focused;
                Show(entry, SlotRect(m_Layout, i), i, screenAspect, isFocused);
            }
        }

        private void Show(RigEntry entry, Rect rect, int slot, float screenAspect, bool isFocused)
        {
            var camera = entry.Camera;
            if (camera == null)
                return;

            // 画中画的小窗用 Depth 清屏：小窗之外仍是主视角的画面
            var isPipInset = m_Layout == ViewLayout.PictureInPicture && slot > 0;

            camera.enabled = true;
            camera.rect = rect;
            camera.depth = entry.BaseDepth + slot;
            camera.clearFlags = isPipInset ? CameraClearFlags.Depth : CameraClearFlags.SolidColor;
            if (!isPipInset)
                camera.backgroundColor = entry.BaseBackground;

            if (entry.Listener != null && m_ManageAudioListener)
                entry.Listener.enabled = isFocused;

            ApplyFov(entry, rect, screenAspect);
        }

        private void Hide(RigEntry entry)
        {
            if (entry.Camera != null)
                entry.Camera.enabled = false;
            if (entry.Listener != null && m_ManageAudioListener)
                entry.Listener.enabled = false;
        }

        private void ApplyFov(RigEntry entry, Rect rect, float screenAspect)
        {
            if (rect.height <= 0f)
                return;

            var viewportAspect = (rect.width * screenAspect) / rect.height;
            for (int i = 0; i < entry.Cameras.Count; i++)
            {
                var vcam = entry.Cameras[i];
                if (vcam == null)
                    continue;

                var lens = vcam.Lens;
                lens.FieldOfView = CompensateFov(entry.BaseFovs[i], viewportAspect, screenAspect, m_FovPolicy);
                vcam.Lens = lens;
            }
        }

        /// <summary>
        /// 把整屏基准 FOV 换算到当前视口：保持横向视野时，
        /// tan(v/2) 随「整屏宽高比 / 视口宽高比」等比缩放。
        /// </summary>
        private static float CompensateFov(float baseFov, float viewportAspect, float screenAspect, FovPolicy policy)
        {
            if (policy == FovPolicy.None || viewportAspect <= 0.0001f || screenAspect <= 0.0001f)
                return baseFov;

            var halfTan = Mathf.Tan(baseFov * Mathf.Deg2Rad * 0.5f);
            var wanted = halfTan * (screenAspect / viewportAspect);
            var fov = Mathf.Atan(wanted) * Mathf.Rad2Deg * 2f;

            if (policy == FovPolicy.Overscan)
                fov = Mathf.Max(fov, baseFov);

            return Mathf.Clamp(fov, k_MinFov, k_MaxFov);
        }

        // ---------- 槽位 ----------

        private static int Capacity(ViewLayout layout)
        {
            switch (layout)
            {
                case ViewLayout.Vertical:
                case ViewLayout.Horizontal:
                case ViewLayout.PictureInPicture:
                    return 2;
                case ViewLayout.Quad:
                    return 4;
                default:
                    return 1;
            }
        }

        /// <summary>槽位 → 归一化视口。画中画小窗宽高取同一比例 ⇒ 宽高比与整屏一致，无需补偿 FOV。</summary>
        private static Rect SlotRect(ViewLayout layout, int slot)
        {
            switch (layout)
            {
                case ViewLayout.Vertical:
                    return slot == 0
                        ? new Rect(0f, 0f, 0.5f, 1f)
                        : new Rect(0.5f, 0f, 0.5f, 1f);
                case ViewLayout.Horizontal:
                    return slot == 0
                        ? new Rect(0f, 0.5f, 1f, 0.5f)
                        : new Rect(0f, 0f, 1f, 0.5f);
                case ViewLayout.Quad:
                {
                    var column = slot % 2;
                    var row = slot / 2;
                    return new Rect(column * 0.5f, 0.5f - row * 0.5f, 0.5f, 0.5f);
                }
                case ViewLayout.PictureInPicture:
                    return slot == 0
                        ? new Rect(0f, 0f, 1f, 1f)
                        : new Rect(1f - k_PipSize - k_PipInset, k_PipInset, k_PipSize, k_PipSize);
                default:
                    return new Rect(0f, 0f, 1f, 1f);
            }
        }

        /// <summary>槽位顺序表：显式顺序优先；单人/画中画把 Focused 提到第一位，其余按登记顺序。</summary>
        private List<RigEntry> BuildOrder()
        {
            if (m_HasExplicitOrder && m_Order.Count > 0)
                return m_Order;

            m_Order.Clear();
            if ((m_Layout == ViewLayout.Single || m_Layout == ViewLayout.PictureInPicture)
                && m_Focused != null)
            {
                var focused = Find(m_Focused);
                if (focused != null)
                    m_Order.Add(focused);
            }

            for (int i = 0; i < m_Entries.Count; i++)
            {
                if (!m_Order.Contains(m_Entries[i]))
                    m_Order.Add(m_Entries[i]);
            }
            return m_Order;
        }

        // ---------- 还原 / 生命周期 ----------

        private static void Restore(RigEntry entry)
        {
            if (entry.Camera != null)
            {
                entry.Camera.rect = new Rect(0f, 0f, 1f, 1f);
                entry.Camera.depth = entry.BaseDepth;
                entry.Camera.clearFlags = entry.BaseClearFlags;
                entry.Camera.backgroundColor = entry.BaseBackground;
                entry.Camera.enabled = true;
            }
            if (entry.Listener != null)
                entry.Listener.enabled = true;

            for (int i = 0; i < entry.Cameras.Count; i++)
            {
                var vcam = entry.Cameras[i];
                if (vcam == null)
                    continue;
                var lens = vcam.Lens;
                lens.FieldOfView = entry.BaseFovs[i];
                vcam.Lens = lens;
            }
        }

        private RigEntry Find(CinemachineRigBinder binder)
        {
            for (int i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].Binder == binder)
                    return m_Entries[i];
            }
            return null;
        }

        void OnDestroy()
        {
            if (m_Compositor != null)
                m_Compositor.Suspend();

            for (int i = 0; i < m_Entries.Count; i++)
                Restore(m_Entries[i]);
            m_Entries.Clear();
            m_Order.Clear();
        }
    }
}
