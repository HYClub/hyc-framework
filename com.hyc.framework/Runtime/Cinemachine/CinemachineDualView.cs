// ============================================================
// HYC Framework - Cinemachine 相机装配 · 双屏门面
// 文件: Runtime/Cinemachine/CinemachineDualView.cs
// 说明: 给游戏层的一句话接口。宿主只关心「分开 / 合成 / 谁那一侧变色 / 按谁分」，
//       不碰 RenderTexture、不碰 shader、不碰 Cinemachine 优先级。
//       Splitter / Compositor 由本组件自动装配。
// ============================================================

using System;
using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>分屏方式。</summary>
    public enum SplitStyle
    {
        /// <summary>竖着切（左右）。</summary>
        Vertical,

        /// <summary>横着切（上下）。</summary>
        Horizontal,

        /// <summary>斜着切（角度见 <see cref="CinemachineDualView.DiagonalAngle"/>）。</summary>
        Diagonal,

        /// <summary>跟着两个角色：接缝垂直于两人连线（Lego 式）。</summary>
        FollowPlayers,

        /// <summary>跟着单个角色：接缝保持当前角度，整体平移到该角色屏幕上。</summary>
        FollowTarget,
    }

    /// <summary>
    /// 双屏游戏层门面。
    /// <code>
    /// // boss 出场：竖着分开
    /// view.Separate(SplitStyle.Vertical);
    ///
    /// // boss 抬左手 → 左边玩家那半变黑；抬右手 → 右边那半变白
    /// view.Tint(0, Color.black);
    /// view.Tint(1, Color.white);
    ///
    /// // 招式结束：合成一个屏
    /// view.Combine(0.4f);
    ///
    /// // 跟着两个人切：接缝角度随两人连线自动从竖变横变斜，占比也随位置自动变化
    /// view.Separate(SplitStyle.FollowPlayers);
    /// view.SplitWeight = 0.35f;   // 顺手让 A 那半更窄一点
    /// </code>
    /// <para><b>关键性质</b>：染色绑的是「玩家编号」而不是屏幕左右，
    /// 所以横切、竖切、斜切、跟着角色切，变色的永远是那个人那一侧。</para>
    /// <para><b>性能</b>：合成路径两侧都渲整屏（约 2 倍像素）。用完想省回来，
    /// 调 <c>Combine(duration, keepSide, releaseComposite: true)</c>。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineDualView : MonoBehaviour
    {
        [Tooltip("A 侧 rig（第一个玩家）。")]
        [SerializeField] private CinemachineRigBinder m_RigA;

        [Tooltip("B 侧 rig（第二个玩家）。")]
        [SerializeField] private CinemachineRigBinder m_RigB;

        [Tooltip("A 侧角色。FollowPlayers / FollowTarget 用它算接缝位置。")]
        [SerializeField] private Transform m_PlayerA;

        [Tooltip("B 侧角色。")]
        [SerializeField] private Transform m_PlayerB;

        [Tooltip("SplitStyle.Diagonal 用的接缝法线角度（度）。0 = 竖线，90 = 横线。")]
        [SerializeField] private float m_DiagonalAngle = 28f;

        [Tooltip("FollowPlayers 下接缝在两人之间的取点位置。0.5 = 对半；越小 A 那半越窄。")]
        [SerializeField, Range(0f, 1f)] private float m_SplitWeight = 0.5f;

        [Tooltip("任一侧的最小占比，防止跟随把某一侧压到看不见。")]
        [SerializeField, Range(0f, 0.49f)] private float m_MinShare = 0.15f;

        [Tooltip("分屏时自动接管取景（把角色推到自己那半的中心）。关掉则完全不动 rig 的 Follow / LookAt。")]
        [SerializeField] private bool m_AutoFraming = true;

        [Tooltip("是否让门面统一管理 AudioListener（同时只留一个，避免 Unity 告警）。")]
        [SerializeField] private bool m_ManageAudioListener = true;

        private CinemachineViewSplitter m_Splitter;
        private CinemachineSeamCompositor m_Compositor;
        private CinemachineSeamFraming m_Framing;

        private SeamState m_From;
        private SeamState m_To;
        private float m_TweenTime;
        private float m_TweenDuration;
        private bool m_Tweening;
        private bool m_ReleaseAfterTween;
        private bool m_SeparatedAfterTween;
        private bool m_Separated;

        // 合屏时接缝装饰（光带 / 缝隙）的原值：并拢过程中按权重淡出，退回分屏时原样恢复
        private bool m_MergeDecorCaptured;
        private float m_MergeGapBase;
        private float m_MergeGlowBase;

        /// <summary>分开 / 合成切换完成时触发（true = 已分开）。</summary>
        public event Action<bool> SeparatedChanged;

        /// <summary>当前是否处于分开状态。</summary>
        public bool IsSeparated => m_Separated;

        /// <summary>合屏（两人并拢）的过渡进度 0..1：0 = 各跟各的角色，1 = 两台相机已是同一景。</summary>
        public float MergeBlend => m_Framing != null ? m_Framing.GroupBlendCurrent : 0f;

        /// <summary>当前分屏方式。</summary>
        public SplitStyle Style { get; private set; } = SplitStyle.Vertical;

        /// <summary>斜切角度。</summary>
        public float DiagonalAngle
        {
            get => m_DiagonalAngle;
            set => m_DiagonalAngle = value;
        }

        /// <summary>
        /// 跟随玩家时接缝在两人之间的取点位置：0.5 = 对半（垂直平分线）；
        /// 越靠近 0，A 那半越窄；越靠近 1，B 那半越窄。
        /// <para>两人靠近时接缝会自然被推偏，占比自动跟着变；再叠加 <see cref="MinShare"/> 兜底。</para>
        /// </summary>
        public float SplitWeight
        {
            get => m_SplitWeight;
            set
            {
                m_SplitWeight = Mathf.Clamp01(value);
                if (m_Compositor != null)
                    m_Compositor.SplitWeight = m_SplitWeight;
            }
        }

        /// <summary>任一侧的最小占屏比例，0 = 允许压到零宽。</summary>
        public float MinShare
        {
            get => m_MinShare;
            set
            {
                m_MinShare = Mathf.Clamp(value, 0f, 0.49f);
                if (m_Compositor != null)
                    m_Compositor.MinShare = m_MinShare;
            }
        }

        /// <summary>A 侧当前占屏比例（0..1），可直接拿来做 UI 或音效判断。</summary>
        public float SideAShare => m_Compositor != null ? m_Compositor.SideAShare : 0.5f;

        /// <summary>是否自动接管取景。</summary>
        public bool AutoFraming
        {
            get => m_AutoFraming;
            set
            {
                m_AutoFraming = value;
                if (!value)
                    ReleaseFraming();
            }
        }

        /// <summary>取景协调器（分屏且开启自动取景时才存在）。</summary>
        public CinemachineSeamFraming Framing => m_Framing;

        /// <summary>底层合成器（需要细调时才用）。</summary>
        public CinemachineSeamCompositor Compositor => m_Compositor;

        /// <summary>底层分屏调度器（需要同屏/画中画等布局时才用）。</summary>
        public CinemachineViewSplitter Splitter => m_Splitter;

        /// <summary>当前接缝参数。</summary>
        public SeamState CurrentSeam => m_Compositor != null ? m_Compositor.Seam : default;

        private bool Ready => m_Compositor != null;

        // ---------- 装配 ----------

        private void Awake()
        {
            EnsureWired();
        }

        /// <summary>运行期装配（rig 是 Addressables 实例化的，Awake 时可能还没赋值）。</summary>
        public void Bind(CinemachineRigBinder rigA, CinemachineRigBinder rigB,
            Transform playerA = null, Transform playerB = null)
        {
            m_RigA = rigA;
            m_RigB = rigB;
            m_PlayerA = playerA;
            m_PlayerB = playerB;
            EnsureWired();
        }

        private void EnsureWired()
        {
            if (m_Splitter == null)
                m_Splitter = GetComponent<CinemachineViewSplitter>();

            if (m_Splitter == null)
                m_Splitter = gameObject.AddComponent<CinemachineViewSplitter>();

            m_Splitter.ManageAudioListener = m_ManageAudioListener;

            if (m_RigA != null)
                m_Splitter.Register(m_RigA);
            if (m_RigB != null)
                m_Splitter.Register(m_RigB);

            if (m_RigA != null)
                m_Splitter.Focused = m_RigA;

            // 首次装配才做「先竖切唤醒合成器、再推回单屏」的引导；
            // 后续 Bind 不能重跑，否则会把正在分开的画面打回单屏
            var booted = false;
            if (m_Compositor == null && m_RigA != null && m_RigB != null)
            {
                m_Splitter.SetSeam(SeamState.Vertical);
                m_Compositor = m_Splitter.Compositor;
                booted = m_Compositor != null;
            }

            if (booted)
                m_Compositor.SetSeam(SeamState.SingleA);

            if (m_Compositor != null)
            {
                m_Compositor.SplitWeight = m_SplitWeight;
                m_Compositor.MinShare = m_MinShare;
            }
        }

        // ---------- 取景 ----------

        /// <summary>
        /// 分屏时接管两侧 rig 的 Follow / LookAt：合成模式下相机渲整屏再裁，
        /// 不接管的话角色会停在接缝上。
        /// </summary>
        private void EnsureFraming()
        {
            if (!m_AutoFraming || m_Compositor == null)
                return;

            if (m_RigA == null || m_RigB == null || m_PlayerA == null || m_PlayerB == null)
                return;

            if (m_Framing == null)
                m_Framing = GetComponent<CinemachineSeamFraming>();
            if (m_Framing == null)
                m_Framing = gameObject.AddComponent<CinemachineSeamFraming>();

            m_Framing.Bind(m_Compositor, m_RigA, m_RigB, m_PlayerA, m_PlayerB);
        }

        /// <summary>交还取景权（退回 Camera.rect 路径时必须调用，rect 模式下相机自带半屏画幅）。</summary>
        private void ReleaseFraming()
        {
            if (m_Framing == null)
                return;

            m_Framing.Mode = SeamFramingMode.None;
        }

        // ---------- 分开 / 合成 ----------

        /// <summary>开始分屏。duration = 0 表示立刻到位。</summary>
        public void Separate(SplitStyle style, float duration = 0f)
        {
            if (!RequireReady(nameof(Separate)))
                return;

            Style = style;
            ApplySource(style);
            EnsureFraming();

            // 从「合屏」状态回来时，两台相机要各自撤回自己角色的机位
            if (m_Framing != null)
                m_Framing.GroupBlend = 0f;

            var seam = BuildSeam(style);

            // 合屏期间光带 / 缝隙被淡出到 0，这里直接还原成基准值 —— 交给补间去做的话，
            // 补间的目标值里带的就是「0」，会和淡出恢复逐帧互相覆盖
            if (m_MergeDecorCaptured)
            {
                seam.GapWidth = m_MergeGapBase;
                seam.GlowWidth = m_MergeGlowBase;
                m_MergeDecorCaptured = false;
            }

            AnimateTo(seam, duration, false, true);
        }

        /// <summary>
        /// 合屏 —— **两个人并拢成一景**（不是把一侧挤出屏幕）。
        /// <para>做法：两台相机都收拢到「两人中点 + 拉远到两人都在画面内」，位置/朝向/距离最终完全一致，
        /// 于是左右两半在画面上慢慢并成一张连续的图；接缝的光带与缝隙同步淡出。再调
        /// <see cref="Separate"/> 就各自撤回自己角色的机位（反向过程）。</para>
        /// <para>要求 <see cref="AutoFraming"/> 为 true（默认就是）—— 这个动作靠取景协调器
        /// <c>CinemachineSeamFraming</c> 完成；关掉自动取景时退回 <see cref="Combine"/> 的溶化。</para>
        /// </summary>
        public void CombineAroundPlayers()
        {
            if (!RequireReady(nameof(CombineAroundPlayers)))
                return;

            EnsureFraming();
            if (m_Framing == null)
            {
                Debug.LogWarning("[HYC.Cinemachine] CombineAroundPlayers 需要 AutoFraming = true（且 Bind 时给了两个角色），" +
                                 "已退回 Combine 的溶化效果。");
                Combine(0.6f);
                return;
            }

            m_Framing.GroupBlend = 1f;
            SetSeparated(false);
        }

        /// <summary>
        /// 让某一侧相机绕自己的角色转视角（<paramref name="offset"/> 是世界空间偏移向量，
        /// 一般用 <c>Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right) * 基准偏移</c>）。
        /// <para>两侧可以各转各的 —— 合屏时会自动混到公共视角，不需要宿主先对齐。</para>
        /// </summary>
        public void SetOrbit(int side, Vector3 offset)
        {
            EnsureFraming();
            if (m_Framing != null)
                m_Framing.SetOrbit(side, offset);
        }

        /// <summary>读某一侧当前的环绕偏移（0 = A，1 = B）。未启用自动取景时返回 <c>Vector3.zero</c>。</summary>
        public Vector3 GetOrbit(int side) => m_Framing != null ? m_Framing.GetOrbit(side) : Vector3.zero;

        /// <summary>合成一个屏（把另一侧**溶化**掉，画面不移动）。</summary>
        /// <param name="duration">过渡时长，0 = 立刻。</param>
        /// <param name="keepSide">保留哪一侧：0 = A，1 = B。</param>
        /// <param name="releaseComposite">合成后是否退回 <c>Camera.rect</c> 省掉 2 倍像素（单向，之后不能再分屏）。</param>
        public void Combine(float duration = 0f, int keepSide = 0, bool releaseComposite = false)
        {
            if (!RequireReady(nameof(Combine)))
                return;

            // 从合屏状态转成溶化时，先把两台相机拉回各自机位，否则「同一景 + 溶化」看不出变化
            if (m_Framing != null)
                m_Framing.GroupBlend = 0f;

            var target = m_Compositor.Seam;
            target.Push = 0f;
            target.Feather = 0f;
            // Crossfade 是**全屏混合权重**而不是位移：从 0 补到 ±1 就是从接缝处柔化开、
            // 最后整屏都是某一侧。所以它是「溶化」，不是「把另一侧挤出屏幕」。
            target.Crossfade = keepSide == 0 ? -1f : 1f;

            AnimateTo(target, duration, releaseComposite, false);
        }

        private bool RequireReady(string caller)
        {
            if (Ready)
                return true;

            Debug.LogWarning("[HYC.Cinemachine] CinemachineDualView." + caller +
                             " 在装配完成前被调用（两侧 rig 未就绪）。");
            return false;
        }

        private void ApplySource(SplitStyle style)
        {
            switch (style)
            {
                case SplitStyle.FollowPlayers:
                    m_Compositor.SetFollowTargets(m_PlayerA, m_PlayerB, null);
                    break;
                case SplitStyle.FollowTarget:
                    m_Compositor.SetFollowTarget(m_PlayerA, null);
                    break;
                default:
                    m_Compositor.Source = SeamSource.Authored;
                    break;
            }
        }

        private SeamState BuildSeam(SplitStyle style)
        {
            var seam = m_Compositor.Seam;
            seam.Push = 0f;
            seam.Crossfade = 0f;

            switch (style)
            {
                case SplitStyle.Vertical:
                    seam.Angle = 0f;
                    break;
                case SplitStyle.Horizontal:
                    seam.Angle = 90f;
                    break;
                case SplitStyle.Diagonal:
                    seam.Angle = m_DiagonalAngle;
                    break;
                default:
                    // 跟随类：位置与角度由驱动源每帧解算，这里只负责把 Push / 叠化归零
                    return seam;
            }

            // 标准切法一律对中，避免沿用上一次跟随留下的偏置位置。
            // 想切在别处请改用 SetSeam。
            seam.Point = new Vector2(0.5f, 0.5f);
            return seam;
        }

        // ---------- 染色 ----------

        /// <summary>给两侧染色。alpha 为强度，alpha = 0 表示不染。</summary>
        public void Tint(Color sideA, Color sideB)
        {
            if (m_Compositor != null)
                m_Compositor.SetSideTint(sideA, sideB);
        }

        /// <summary>只染一个玩家那一侧。</summary>
        /// <param name="player">0 = A 侧（第一个玩家），1 = B 侧。</param>
        public void Tint(int player, Color color)
        {
            if (m_Compositor != null)
                m_Compositor.SetSideTint(player, color);
        }

        /// <summary>清掉染色。</summary>
        public void ClearTint()
        {
            if (m_Compositor != null)
                m_Compositor.ClearSideTint();
        }

        // ---------- 接缝光带 ----------

        /// <summary>
        /// 让接缝「看得见」：沿切线叠一条加法发光带。
        /// <para>玩家能拖的分屏线一定要开 —— 否则一条数学上存在的线在画面上是不可见的，
        /// 用户根本不知道该往哪儿按。</para>
        /// </summary>
        /// <param name="width">光带宽度（屏幕比例），建议 0.015~0.03。</param>
        /// <param name="color">颜色，alpha 当强度。</param>
        /// <param name="pulseHz">呼吸频率（Hz），0 = 不闪。</param>
        public void SetGlow(float width, Color color, float pulseHz = 0f)
        {
            if (m_Compositor == null)
                return;

            // 合屏过程中设光带：记成新的基准，否则并拢淡出结束时会被旧基准还原回去
            if (m_MergeDecorCaptured)
                m_MergeGlowBase = Mathf.Max(0f, width);

            var seam = m_Compositor.Seam;
            seam.GlowWidth = Mathf.Max(0f, width);
            seam.GlowColor = color;
            seam.GlowPulse = Mathf.Max(0f, pulseHz);
            m_Compositor.SetSeam(seam);
        }

        /// <summary>关掉接缝光带。</summary>
        public void ClearGlow()
        {
            if (m_Compositor == null)
                return;

            if (m_MergeDecorCaptured)
                m_MergeGlowBase = 0f;

            var seam = m_Compositor.Seam;
            seam.GlowWidth = 0f;
            seam.GlowColor = Color.clear;
            seam.GlowPulse = 0f;
            m_Compositor.SetSeam(seam);
        }

        // ---------- 细调 ----------

        /// <summary>改接缝角度（度）。水平 0 = 竖线，90 = 横线。</summary>
        public void SetAngle(float degrees)
        {
            if (!Ready)
                return;

            var seam = m_Compositor.Seam;
            seam.Angle = Mathf.Repeat(degrees, 360f);
            m_Compositor.SetSeam(seam);
            Style = SplitStyle.Vertical;
        }

        /// <summary>直接改接缝参数（写全部字段，不做补间）。</summary>
        public void SetSeam(in SeamState seam)
        {
            if (m_Compositor != null)
                m_Compositor.SetSeam(seam);
        }

        /// <summary>换接缝跟随的角色。</summary>
        public void Follow(Transform target)
        {
            m_PlayerA = target;
            if (m_Compositor != null && Style == SplitStyle.FollowTarget)
                m_Compositor.SetFollowTarget(target, null);
        }

        // ---------- 补间 ----------

        private void AnimateTo(SeamState target, float duration, bool releaseAfter, bool separatedAfter)
        {
            m_From = m_Compositor.Seam;
            m_To = target;
            m_TweenDuration = Mathf.Max(0f, duration);
            m_TweenTime = 0f;
            m_Tweening = true;
            m_ReleaseAfterTween = releaseAfter;
            m_SeparatedAfterTween = separatedAfter;

            if (m_TweenDuration <= 0f)
                StepTween(1f);
        }

        private void Update()
        {
            TickMergeDecor();

            if (!m_Tweening || m_Compositor == null)
                return;

            m_TweenTime += Time.unscaledDeltaTime;
            StepTween(Mathf.Clamp01(m_TweenTime / m_TweenDuration));
        }

        /// <summary>
        /// 合屏过程中把接缝的「装饰」（光带 / 缝隙）跟着淡出。
        /// <para>两台相机都收到同一景之后，屏幕正中还留一条发光带或一道黑缝就露馅了；
        /// 退回分屏时按进合屏前记下的原值恢复。</para>
        /// </summary>
        private void TickMergeDecor()
        {
            if (m_Compositor == null)
                return;

            var blend = m_Framing != null ? m_Framing.GroupBlendCurrent : 0f;

            if (blend > 1e-4f)
            {
                if (!m_MergeDecorCaptured)
                {
                    var current = m_Compositor.Seam;
                    m_MergeGapBase = current.GapWidth;
                    m_MergeGlowBase = current.GlowWidth;
                    m_MergeDecorCaptured = true;
                }

                var k = 1f - Mathf.Clamp01(blend);
                var seam = m_Compositor.Seam;
                var gap = m_MergeGapBase * k;
                var glow = m_MergeGlowBase * k;
                if (Mathf.Abs(seam.GapWidth - gap) < 1e-5f && Mathf.Abs(seam.GlowWidth - glow) < 1e-5f)
                    return;

                seam.GapWidth = gap;
                seam.GlowWidth = glow;
                m_Compositor.SetSeam(seam, true);
                return;
            }

            if (!m_MergeDecorCaptured)
                return;

            var restored = m_Compositor.Seam;
            restored.GapWidth = m_MergeGapBase;
            restored.GlowWidth = m_MergeGlowBase;
            m_Compositor.SetSeam(restored, true);
            m_MergeDecorCaptured = false;
        }

        private void StepTween(float t)
        {
            var eased = Mathf.SmoothStep(0f, 1f, t);
            // keepSource: 跟随类驱动源在补间期间不能被改回 Authored
            m_Compositor.SetSeam(LerpSeam(m_From, m_To, eased), true);

            if (t < 1f)
                return;

            m_Tweening = false;

            SetSeparated(m_SeparatedAfterTween);

            if (m_ReleaseAfterTween && m_Splitter != null)
            {
                ReleaseFraming();
                m_Splitter.DowngradeToViewport();
            }
        }

        private static SeamState LerpSeam(in SeamState from, in SeamState to, float t)
        {
            var result = to;
            result.Point = Vector2.Lerp(from.Point, to.Point, t);
            result.Angle = from.Angle + Mathf.DeltaAngle(from.Angle, to.Angle) * t;
            result.Curvature = Mathf.Lerp(from.Curvature, to.Curvature, t);
            result.GapWidth = Mathf.Lerp(from.GapWidth, to.GapWidth, t);
            result.Feather = Mathf.Lerp(from.Feather, to.Feather, t);
            result.Noise = Mathf.Lerp(from.Noise, to.Noise, t);
            result.NoiseSpeed = Mathf.Lerp(from.NoiseSpeed, to.NoiseSpeed, t);
            result.GapColor = Color.Lerp(from.GapColor, to.GapColor, t);
            result.GlowWidth = Mathf.Lerp(from.GlowWidth, to.GlowWidth, t);
            result.GlowColor = Color.Lerp(from.GlowColor, to.GlowColor, t);
            result.GlowPulse = Mathf.Lerp(from.GlowPulse, to.GlowPulse, t);
            result.Push = Mathf.Lerp(from.Push, to.Push, t);
            result.Crossfade = Mathf.Lerp(from.Crossfade, to.Crossfade, t);
            return result;
        }

        private void SetSeparated(bool separated)
        {
            if (m_Separated == separated)
                return;

            m_Separated = separated;
            SeparatedChanged?.Invoke(separated);
        }
    }
}
