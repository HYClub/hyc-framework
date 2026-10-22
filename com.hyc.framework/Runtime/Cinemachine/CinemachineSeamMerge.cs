// ============================================================
// HYC Framework - Cinemachine 相机装配 · 接缝合拢
// 文件: Runtime/Cinemachine/CinemachineSeamMerge.cs
// 说明: 「两边分开 → 合成一台相机」的时间轴。
//       相机位姿不归这里管：切机位交给 CinemachineBrain 的 DefaultBlend，
//       本组件只负责把接缝推出去、把两侧画面叠化，两者跑同一条曲线。
//       所以不需要新造相机系统，也不会和 Cinemachine 抢 transform。
// ============================================================

using System;
using Unity.Cinemachine;
using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>一次合并 / 分开的配置。</summary>
    [Serializable]
    public struct MergeProfile
    {
        [Tooltip("总时长（秒）。")]
        public float Duration;

        [Tooltip("结束时的接缝位移。±1.5 足以把接缝推出屏幕（只剩一侧）。")]
        public float PushTarget;

        [Tooltip("结束时的叠化强度，-1..1。用来软化接缝处的色差。")]
        public float CrossfadeTarget;

        [Tooltip("结束时的羽化宽度。0 = 硬边。")]
        public float FeatherTarget;

        [Tooltip("时间曲线。留空按线性。")]
        public AnimationCurve Ease;

        [Tooltip("合并后切到的机位名（rig 里的机位节点名）。留空则不切机位，只收接缝。")]
        public string MergedCameraName;

        [Tooltip("Cinemachine 位姿过渡时长（秒）。0 = 用 Brain 上当前设置的时长。")]
        public float BlendTime;
    }

    /// <summary>
    /// 合并 / 分开的时间轴：把「切机位 + 收接缝」两件事绑在一次播放里。
    /// <para><b>职责边界</b>：相机位姿由 Cinemachine 的 Blend 负责，
    /// 接缝参数由本组件负责，画面合成由 <see cref="CinemachineSeamCompositor"/> 负责。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineSeamMerge : MonoBehaviour
    {
        [Tooltip("接缝合成器。")]
        [SerializeField] private CinemachineSeamCompositor m_Compositor;

        [Tooltip("主相机所在的 rig（合并后机位从它上面切）。")]
        [SerializeField] private CinemachineRigBinder m_Rig;

        [Tooltip("Cinemachine 大脑。留空时在 rig 上查找。")]
        [SerializeField] private CinemachineBrain m_Brain;

        private MergeProfile m_Profile;
        private SeamState m_BaseSeam;
        private float m_Time;
        private bool m_Playing;
        private bool m_Reverse;
        private string m_ReturnCameraName;

        /// <summary>播放完成时触发。</summary>
        public event Action Completed;

        /// <summary>是否正在播放。</summary>
        public bool IsPlaying => m_Playing;

        /// <summary>播放进度 0..1（反向播放时同样从 0 到 1 表示「往反向走完」）。</summary>
        public float Progress => m_Profile.Duration <= 0f ? 1f : Mathf.Clamp01(m_Time / m_Profile.Duration);

        /// <summary>分开 → 合并。</summary>
        public void Play(in MergeProfile profile)
        {
            Begin(profile, false);
        }

        /// <summary>合并 → 分开。</summary>
        public void PlayReverse(in MergeProfile profile)
        {
            Begin(profile, true);
        }

        /// <summary>立刻中断并停在当前状态。</summary>
        public void Stop()
        {
            m_Playing = false;
        }

        private void Begin(MergeProfile profile, bool reverse)
        {
            if (m_Compositor == null)
            {
                Debug.LogWarning("[HYC.Cinemachine] 接缝合拢缺少 Compositor。");
                return;
            }

            m_Profile = profile;
            m_Reverse = reverse;
            m_Time = 0f;
            m_Playing = true;

            // 起点：反向时从「已合并」的接缝倒回基准接缝
            m_BaseSeam = m_Compositor.Seam;
            if (reverse)
            {
                m_BaseSeam.Push = profile.PushTarget;
                m_BaseSeam.Crossfade = profile.CrossfadeTarget;
                m_BaseSeam.Feather = profile.FeatherTarget;
                m_ReturnCameraName = profile.MergedCameraName;
            }

            ApplyCamera(profile, reverse);

            if (profile.Duration <= 0f)
                Finish();
        }

        private void ApplyCamera(MergeProfile profile, bool reverse)
        {
            var rig = ResolveRig();
            if (rig == null)
                return;

            var brain = ResolveBrain(rig);
            if (brain != null && profile.BlendTime > 0f)
            {
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.EaseInOut, profile.BlendTime);
            }

            if (string.IsNullOrEmpty(profile.MergedCameraName))
                return;

            if (reverse)
            {
                // 反向：切回合并前那台（调用方在 Play 之前由 binder.Active 决定）
                if (!string.IsNullOrEmpty(m_ReturnCameraName) && rig.TryGet(m_ReturnCameraName, out var back))
                    rig.SwitchTo(back);
                return;
            }

            if (rig.TryGet(profile.MergedCameraName, out var merged))
                rig.SwitchTo(merged);
        }

        private void Update()
        {
            if (!m_Playing)
                return;

            m_Time += Time.deltaTime;
            var raw = m_Profile.Duration <= 0f ? 1f : Mathf.Clamp01(m_Time / m_Profile.Duration);
            var t = m_Profile.Ease != null && m_Profile.Ease.length > 0 ? m_Profile.Ease.Evaluate(raw) : raw;

            ApplySeam(m_Reverse ? 1f - t : t);

            if (raw >= 1f)
                Finish();
        }

        private void ApplySeam(float t)
        {
            if (m_Compositor == null)
                return;

            var seam = m_BaseSeam;
            seam.Push = Mathf.Lerp(seam.Push, m_Profile.PushTarget, t);
            seam.Crossfade = Mathf.Lerp(seam.Crossfade, m_Profile.CrossfadeTarget, t);
            seam.Feather = Mathf.Lerp(seam.Feather, m_Profile.FeatherTarget, t);
            m_Compositor.SetSeam(seam);
        }

        private void Finish()
        {
            m_Playing = false;

            if (m_Reverse && m_Compositor != null)
            {
                // 回到完全分开的状态
                var seam = m_BaseSeam;
                seam.Push = 0f;
                seam.Crossfade = 0f;
                seam.Feather = 0f;
                m_Compositor.SetSeam(seam);
            }

            Completed?.Invoke();
        }

        private CinemachineRigBinder ResolveRig()
        {
            if (m_Rig == null)
                m_Rig = GetComponentInParent<CinemachineRigBinder>();
            return m_Rig;
        }

        private CinemachineBrain ResolveBrain(CinemachineRigBinder rig)
        {
            if (m_Brain == null && rig != null)
                m_Brain = rig.GetComponentInChildren<CinemachineBrain>(true);
            return m_Brain;
        }

        private void OnDestroy()
        {
            Completed = null;
        }
    }
}
