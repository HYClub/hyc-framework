// ============================================================
// HYC Framework - Cinemachine 相机装配 · 接缝取景
// 文件: Runtime/Cinemachine/CinemachineSeamFraming.cs
// 说明: 合成模式下相机渲的是整屏再被裁，角色会落在屏幕正中（也就是接缝上）。
//       本组件按「玩家应落在自己那半的中心」反算瞄准偏移，
//       给每台相机一个运行时瞄准锚点，视觉上把角色推回各自半屏。
//       不开（Mode = None）时完全不动 rig。
// ============================================================

using Unity.Cinemachine;
using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>接缝取景模式。</summary>
    public enum SeamFramingMode
    {
        /// <summary>不动 rig，相机按原本的跟随方式取景。</summary>
        None,

        /// <summary>按角色到自己那半中心的位置反算瞄准偏移。</summary>
        LateralOffset,
    }

    /// <summary>
    /// 合成模式下的取景协调器：给两台相机各提供一个沿**接缝法向**偏移的瞄准锚点，
    /// 把角色从「屏幕正中」推到「自己那半的中心」。
    /// <para><b>为什么需要它</b>：<c>Camera.rect</c> 模式下相机 aspect 是半屏，
    /// 角色天然在半屏中心；合成模式下相机 aspect 是整屏再裁切，
    /// 角色会停在整屏中心 —— 也就是接缝上。不做偏移画面观感完全不对。</para>
    /// <para><b>偏移量怎么来</b>：读合成器的**实时接缝**，取屏幕四角在法向上的跨度，
    /// 反解出「自己那半的中心」。所以接缝被加权推偏、上下切、斜切都成立；
    /// 接缝不穿屏（合并 / 单屏）时偏移归零。</para>
    /// <para><b>所有权</b>：启用后本组件独占两侧 rig 的 Follow / LookAt，
    /// 由它每帧写入锚点；宿主不要再同时调用 <c>binder.SetTargets</c>。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineSeamFraming : MonoBehaviour
    {
        [Tooltip("取景模式。None = 不动 rig；LateralOffset = 按角色反算瞄准偏移。")]
        [SerializeField] private SeamFramingMode m_Mode = SeamFramingMode.None;

        [Tooltip("横向偏移强度。0 = 角色留在画面正中；1 = 推到整屏 1/4 与 3/4 处。")]
        [SerializeField, Range(0f, 1f)] private float m_LateralBias = 0.5f;

        [Tooltip("瞄准锚点的世界高度偏移，用来把角色压到画面偏下 / 偏上。")]
        [SerializeField] private float m_HeightOffset;

        [Tooltip("锚点跟随平滑速度，0 = 立即。")]
        [SerializeField] private float m_Smooth = 12f;

        [Tooltip("合屏（两人并拢）时两台相机向中点收拢的速度（1/秒）。0 = 立即。")]
        [SerializeField] private float m_GroupSmooth = 3.5f;

        [Tooltip("合屏时把两人都收进画面所需的拉远系数。1 = 两人刚好贴边，1.3 更宽松。")]
        [SerializeField] private float m_GroupPadding = 1.3f;

        [Tooltip("合屏时横向只用到画面中间这么大比例，给两人之间留边。")]
        [SerializeField, Range(0.3f, 1f)] private float m_GroupSafeWidth = 0.8f;

        private CinemachineSeamCompositor m_Compositor;
        private CinemachineRigBinder m_RigA;
        private CinemachineRigBinder m_RigB;
        private Transform m_TargetA;
        private Transform m_TargetB;
        private Transform m_AimA;
        private Transform m_AimB;

        /// <summary>跟随锚点：分屏时钉在各自角色身上，合屏时一起挪到两人中点 ——
        /// 这样两台相机会**走到同一个位置**，两侧画面才会真的并成一张。</summary>
        private Transform m_FollowA;
        private Transform m_FollowB;

        /// <summary>rig 里的跟随组件。跟随偏移（世界空间，绕角色的环绕）由本组件统一写：
        /// 分屏 = 各自环绕角，合屏 = 收敛到两侧的公共视角。</summary>
        private CinemachineFollow m_FollowCompA;
        private CinemachineFollow m_FollowCompB;

        /// <summary>两侧各自的环绕偏移（相对锚点的世界空间向量）。宿主可经 <see cref="SetOrbit"/> 改它。</summary>
        private Vector3 m_OrbitA = new Vector3(0f, 8f, -12f);
        private Vector3 m_OrbitB = new Vector3(0f, 8f, -12f);

        private float m_GroupBlend;
        private float m_GroupBlendTarget;
        private bool m_Bound;

        /// <summary>没有接合成器时的兜底接缝：竖切、正中对半。</summary>
        private static readonly SeamState DefaultSeam = SeamState.Vertical;

        /// <summary>取景模式。赋值立即重新下发。</summary>
        public SeamFramingMode Mode
        {
            get => m_Mode;
            set
            {
                m_Mode = value;
                if (value == SeamFramingMode.None)
                    Unbind();
                else if (m_Bound)
                    Apply();
            }
        }

        /// <summary>横向偏移强度，0..1。</summary>
        public float LateralBias
        {
            get => m_LateralBias;
            set => m_LateralBias = Mathf.Clamp01(value);
        }

        /// <summary>
        /// 合屏取景权重：0 = 两台相机各跟各的角色（分屏），1 = 都收拢到「两人中点 + 拉远到两人都在画面内」。
        /// <para>赋值只是定目标，实际过渡由 <see cref="GroupSpeed"/> 平滑；两台相机因此会**慢慢并拢**成同一景。</para>
        /// <para>0 与 1 之间可以停在任意值（半合并），不做强制取整。</para>
        /// </summary>
        public float GroupBlend
        {
            get => m_GroupBlendTarget;
            set
            {
                m_GroupBlendTarget = Mathf.Clamp01(value);
                if (m_GroupSmooth <= 0f)
                    m_GroupBlend = m_GroupBlendTarget;
            }
        }

        /// <summary>当前（平滑后）的合屏权重，0..1。</summary>
        public float GroupBlendCurrent => m_GroupBlend;

        /// <summary>合屏收拢速度（1/秒）。0 = 立即到位。</summary>
        public float GroupSpeed
        {
            get => m_GroupSmooth;
            set => m_GroupSmooth = Mathf.Max(0f, value);
        }

        /// <summary>是否已经基本合屏（用来决定接缝装饰要不要继续画）。</summary>
        public bool IsMerged => m_GroupBlend > 0.5f;

        /// <summary>读某一侧的环绕偏移（0 = A，1 = B）。</summary>
        public Vector3 GetOrbit(int side) => side == 0 ? m_OrbitA : m_OrbitB;

        /// <summary>
        /// 设置某一侧相机绕自己角色的环绕偏移（世界空间向量，通常来自
        /// <c>Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right) * 基准偏移</c>）。
        /// <para>分屏时该侧相机就绕到新角度；合屏时不受影响 —— 两侧会被混合到**公共视角**，
        /// 所以「两人视角不同也能并拢」是这里的保证，不需要宿主自己先对齐角度。</para>
        /// </summary>
        public void SetOrbit(int side, Vector3 offset)
        {
            if (side == 0)
                m_OrbitA = offset;
            else
                m_OrbitB = offset;

            if (m_Bound)
                Apply();
        }

        /// <summary>接缝来源（为 null 时按竖切对半计算）。换它不需要重新 Bind。</summary>
        public CinemachineSeamCompositor Compositor
        {
            get => m_Compositor;
            set => m_Compositor = value;
        }

        /// <summary>
        /// 绑定两侧 rig 与各自角色。A / B 的顺序必须与
        /// <see cref="CinemachineSeamCompositor.SetTargets"/> 一致。
        /// </summary>
        public void Bind(CinemachineRigBinder rigA, CinemachineRigBinder rigB, Transform targetA, Transform targetB)
        {
            Bind(null, rigA, rigB, targetA, targetB);
        }

        /// <summary>
        /// 绑定合成器与两侧 rig。接缝来自 <paramref name="compositor"/> 的实时值，
        /// 这样接缝被加权推偏（一大一小）或斜切时，角色依然落在自己那半的中心。
        /// <para>不传合成器时按「竖切、正中对半」计算，等价于老行为。</para>
        /// </summary>
        public void Bind(CinemachineSeamCompositor compositor, CinemachineRigBinder rigA, CinemachineRigBinder rigB,
            Transform targetA, Transform targetB)
        {
            m_Compositor = compositor;
            m_RigA = rigA;
            m_RigB = rigB;
            m_TargetA = targetA;
            m_TargetB = targetB;

            m_AimA = EnsureAnchor(m_AimA, "[HYC Seam Aim A]");
            m_AimB = EnsureAnchor(m_AimB, "[HYC Seam Aim B]");

            // 跟随锚点：分屏时与角色同位（等价于直接 Follow 角色），合屏时挪到中点
            m_FollowA = EnsureAnchor(m_FollowA, "[HYC Seam Follow A]");
            m_FollowB = EnsureAnchor(m_FollowB, "[HYC Seam Follow B]");
            if (targetA != null) m_FollowA.position = targetA.position;
            if (targetB != null) m_FollowB.position = targetB.position;

            m_Bound = rigA != null && rigB != null && targetA != null && targetB != null;
            if (!m_Bound)
            {
                Debug.LogWarning("[HYC.Cinemachine] 接缝取景需要两侧 rig 与两个角色目标。");
                return;
            }

            // 合屏拉远要改跟随偏移，先把两侧的组件与原始偏移缓存下来（退分屏时原样还原）
            m_FollowCompA = FindFollow(rigA);
            m_FollowCompB = FindFollow(rigB);
            if (m_FollowCompA != null) m_OrbitA = m_FollowCompA.FollowOffset;
            if (m_FollowCompB != null) m_OrbitB = m_FollowCompB.FollowOffset;

            if (m_Mode == SeamFramingMode.None)
                m_Mode = SeamFramingMode.LateralOffset;

            // 锚点 Transform 的引用不变，之后每帧只移动锚点即可，不需要重复下发
            m_RigA.SetTargets(m_FollowA, m_AimA);
            m_RigB.SetTargets(m_FollowB, m_AimB);
            Apply();
        }

        /// <summary>取 rig 里第一个带跟随组件的机位（固定机位返回 null）。</summary>
        private static CinemachineFollow FindFollow(CinemachineRigBinder rig)
        {
            if (rig == null)
                return null;

            var vcams = rig.GetComponentsInChildren<CinemachineCamera>(true);
            for (var i = 0; i < vcams.Length; i++)
            {
                if (vcams[i] == null)
                    continue;

                var follow = vcams[i].GetComponent<CinemachineFollow>();
                if (follow != null)
                    return follow;
            }

            return null;
        }

        /// <summary>解绑并停止写入锚点（rig 的 Follow / LookAt 保持最后一次的值）。</summary>
        public void Unbind()
        {
            m_Bound = false;
            m_Compositor = null;
            m_RigA = null;
            m_RigB = null;
            m_TargetA = null;
            m_TargetB = null;
            m_FollowCompA = null;
            m_FollowCompB = null;
            m_GroupBlend = 0f;
            m_GroupBlendTarget = 0f;
            DestroyAnchor(ref m_AimA);
            DestroyAnchor(ref m_AimB);
            DestroyAnchor(ref m_FollowA);
            DestroyAnchor(ref m_FollowB);
        }

        private Transform EnsureAnchor(Transform anchor, string name)
        {
            if (anchor != null)
                return anchor;

            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private static void DestroyAnchor(ref Transform anchor)
        {
            if (anchor == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(anchor.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(anchor.gameObject);
            anchor = null;
        }

        private void LateUpdate()
        {
            if (!m_Bound || m_Mode == SeamFramingMode.None)
                return;

            // 合屏 / 分屏之间的过渡在这里平滑：两台相机「慢慢并拢」而不是瞬移
            if (m_GroupSmooth > 0f)
            {
                var t = 1f - Mathf.Exp(-m_GroupSmooth * Time.unscaledDeltaTime);
                m_GroupBlend = Mathf.Lerp(m_GroupBlend, m_GroupBlendTarget, t);
                if (Mathf.Abs(m_GroupBlend - m_GroupBlendTarget) < 1e-3f)
                    m_GroupBlend = m_GroupBlendTarget;
            }
            else
            {
                m_GroupBlend = m_GroupBlendTarget;
            }

            Apply();
        }

        private void OnDestroy()
        {
            DestroyAnchor(ref m_AimA);
            DestroyAnchor(ref m_AimB);
            DestroyAnchor(ref m_FollowA);
            DestroyAnchor(ref m_FollowB);
        }

        private void Apply()
        {
            var camA = m_RigA != null ? m_RigA.GetComponentInChildren<Camera>(true) : null;
            var camB = m_RigB != null ? m_RigB.GetComponentInChildren<Camera>(true) : null;
            if (camA == null || camB == null || m_AimA == null || m_AimB == null)
                return;

            var aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            var seam = m_Compositor != null ? m_Compositor.Seam : DefaultSeam;
            seam.GetNormalSpan(out var min, out var max);

            // 接缝没穿过屏幕（合并 / 单屏）时两侧占满全屏，中心就是屏幕中心，不做偏移。
            // 否则把目标反算到「自己那半的中心」：目标当前落在屏幕中心（相对接缝 q = offset），
            // 期望落在 min/2（A）或 max/2（B），瞄准点朝反方向移动同样的量。
            var crosses = min < 0f && max > 0f;
            var offset = seam.NormalOffset();
            var shiftA = crosses ? -(offset + min * 0.5f) : 0f;
            var shiftB = crosses ? offset + max * 0.5f : 0f;

            seam.GetFrame(out _, out var normal);
            var dirA = NormalToWorld(camA, normal, aspect, m_TargetA.position);
            var dirB = NormalToWorld(camB, normal, aspect, m_TargetB.position);
            var height = Vector3.up * m_HeightOffset;

            var aimA = m_TargetA.position + dirA * (shiftA * m_LateralBias) + height;
            var aimB = m_TargetB.position - dirB * (shiftB * m_LateralBias) + height;

            var followA = m_TargetA.position;
            var followB = m_TargetB.position;

            var blend = Mathf.Clamp01(m_GroupBlend);
            if (blend > 1e-4f)
            {
                // 两人中点：两台相机都收到这里 ⇒ 位置、朝向、距离全部一致，
                // 两侧画面才会真的「并成一张」，而不是各看各的再被一刀切开
                var mid = (m_TargetA.position + m_TargetB.position) * 0.5f;
                followA = Vector3.Lerp(followA, mid, blend);
                followB = Vector3.Lerp(followB, mid, blend);

                var midAim = mid + height;
                aimA = Vector3.Lerp(aimA, midAim, blend);
                aimB = Vector3.Lerp(aimB, midAim, blend);
            }

            // 跟随锚点**不做二次平滑**：并拢过程由 GroupBlend 自己平滑，再叠一层只会变糊；
            // 分屏态则必须与角色同帧，否则跟随机位整体发软（玩家会感觉有拖影）
            if (m_FollowA != null) m_FollowA.position = followA;
            if (m_FollowB != null) m_FollowB.position = followB;

            MoveAnchor(m_AimA, aimA);
            MoveAnchor(m_AimB, aimB);

            // 跟随偏移每帧整份写死（不读上一帧的值）：分屏 = 各自环绕角，合屏 = 混到公共视角。
            // 这样 blend 回到 0 时偏移天然就是原值，不会留插值残留
            ApplyFollowOffsets(camA, camB, blend);
        }

        /// <summary>
        /// 把两侧的跟随偏移写下去。
        /// <para>分屏（blend = 0）：各写各的环绕角 —— 两台相机各自绕着自己的角色转，
        /// 所以「两个角色看到的是不同角度」是常态，不是错误。</para>
        /// <para>合屏（blend = 1）：两侧都写同一个 <see cref="SharedOrbit"/> —— 方向取两侧平均、
        /// 距离按两人间距放大 ⇒ 相机位姿完全一致，视角不同也能并成一张。</para>
        /// </summary>
        private void ApplyFollowOffsets(Camera camA, Camera camB, float blend)
        {
            if (m_FollowCompA == null && m_FollowCompB == null)
                return;

            var shared = SharedOrbit(camA, camB);

            if (m_FollowCompA != null)
                m_FollowCompA.FollowOffset = Vector3.Lerp(m_OrbitA, shared, blend);
            if (m_FollowCompB != null)
                m_FollowCompB.FollowOffset = Vector3.Lerp(m_OrbitB, shared, blend);
        }

        /// <summary>
        /// 合屏时两侧共用的偏移：方向取两个环绕角的平均（正好反向时退回 A 的方向），
        /// 长度取「两侧原长度的较大者」与「装得下两人的最小长度」的较大者 ——
        /// 只放大不缩小，且不动 FOV（FOV 归分屏调度器的 FOV 补偿管，两边都写会互相打架）。
        /// </summary>
        private Vector3 SharedOrbit(Camera camA, Camera camB)
        {
            var dirA = m_OrbitA.sqrMagnitude > 1e-6f ? m_OrbitA.normalized : Vector3.back;
            var dirB = m_OrbitB.sqrMagnitude > 1e-6f ? m_OrbitB.normalized : Vector3.back;

            var dir = dirA + dirB;
            var flat = new Vector3(dir.x, 0f, dir.z);
            if (dir.sqrMagnitude < 1e-4f || flat.magnitude < 0.25f)
            {
                // 两侧大致反向（比如一个朝前一个朝后）：取平均会得到「正上方俯视」这种退化视角，
                // 这时改用 A 的方向
                dir = dirA;
            }
            else
            {
                dir = dir.normalized;
            }

            var mag = Mathf.Max(m_OrbitA.magnitude, m_OrbitB.magnitude);

            // 按当前距离算画面横向半宽，再反解「要装下两人得放大多少倍」。
            // 注意这是**倍率**不是距离，得乘回 mag 上（两者量纲不同）
            var halfSpan = Vector3.Distance(m_TargetA.position, m_TargetB.position) * 0.5f + 1.5f;
            var halfWidth = HalfViewWidth(mag, m_FollowCompA != null ? m_FollowCompA : m_FollowCompB,
                camA != null ? camA : camB);
            if (halfWidth > 1e-3f)
            {
                var scale = halfSpan * m_GroupPadding / (halfWidth * m_GroupSafeWidth);
                if (scale > 1f)
                    mag *= scale;
            }

            return dir * mag;
        }

        /// <summary>指定长度下画面「横向半宽」对应的世界尺寸（垂直 FOV × 屏幕宽高比）。</summary>
        private static float HalfViewWidth(float distance, CinemachineFollow follow, Camera camera)
        {
            var vcam = follow != null ? follow.GetComponent<CinemachineCamera>() : null;
            var fov = vcam != null
                ? vcam.Lens.FieldOfView
                : (camera != null ? camera.fieldOfView : 60f);

            var aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            return distance * Mathf.Tan(Mathf.Clamp(fov, 1f, 179f) * 0.5f * Mathf.Deg2Rad) * aspect;
        }

        private void OnEnable()
        {
            if (m_Bound && m_RigA != null && m_RigB != null)
            {
                m_RigA.SetTargets(m_FollowA, m_AimA);
                m_RigB.SetTargets(m_FollowB, m_AimB);
            }
        }

        /// <summary>
        /// 视口法向上移动 1 个单位对应的世界向量（按目标到相机的距离缩放）。
        /// <para>视口跨度 1 对应 2 倍半宽 / 半高，所以分量各乘 2；法向的 x 分量吃宽高比，
        /// y 分量不吃 —— 斜切时偏移方向必须跟着法向走，否则角色会被推到错的位置。</para>
        /// </summary>
        private static Vector3 NormalToWorld(Camera camera, Vector2 normal, float aspect, Vector3 targetPosition)
        {
            var halfHeight = camera.orthographic
                ? camera.orthographicSize
                : Vector3.Distance(camera.transform.position, targetPosition)
                  * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (halfHeight <= 1e-4f)
                return Vector3.zero;

            var halfWidth = halfHeight * aspect;

            return camera.transform.right * (normal.x * 2f * halfWidth)
                 + camera.transform.up * (normal.y * 2f * halfHeight);
        }

        private void MoveAnchor(Transform anchor, Vector3 position)
        {
            if (m_Smooth <= 0f)
            {
                anchor.position = position;
                return;
            }

            var t = 1f - Mathf.Exp(-m_Smooth * Time.unscaledDeltaTime);
            anchor.position = Vector3.Lerp(anchor.position, position, t);
        }
    }
}
