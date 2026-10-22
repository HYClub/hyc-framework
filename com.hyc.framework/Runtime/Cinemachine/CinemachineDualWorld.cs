// ============================================================
// HYC Framework - Cinemachine 相机装配 · 双世界
// 文件: Runtime/Cinemachine/CinemachineDualWorld.cs
// 说明: 「同一物理空间、两套美术」的落地方式：给两侧相机各自不同的 cullingMask，
//       于是同一个碰撞体在 A 眼里是箱子、在 B 眼里是蘑菇。
//       层由使用方在 Project Settings 里建，框架不改项目设置，只做解析与校验。
// ============================================================

using Unity.Cinemachine;
using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>
    /// 双世界分层裁剪：把两套可视内容按 Layer 分给两台相机，物理共用。
    /// <para><b>契约</b>：碰撞体放共享层（两侧都渲染），美术子物体分别放 A / B 层。
    /// 相机 A 只看「共享 + A」，相机 B 只看「共享 + B」。</para>
    /// <para><b>层从哪来</b>：优先用 Inspector 里手配的 LayerMask；为空时回落到
    /// <see cref="SideALayerName"/> 等约定名。两者都拿不到就报错，不会静默降级。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineDualWorld : MonoBehaviour
    {
        [Tooltip("A 侧层名（约定名）。手配了 SideAOverride 时忽略。")]
        [SerializeField] private string m_SideALayerName = "HYC.WorldA";

        [Tooltip("B 侧层名（约定名）。手配了 SideBOverride 时忽略。")]
        [SerializeField] private string m_SideBLayerName = "HYC.WorldB";

        [Tooltip("共享层名（两侧都渲染，碰撞体放这里）。手配了 SharedOverride 时忽略。")]
        [SerializeField] private string m_SharedLayerName = "HYC.WorldShared";

        [Tooltip("手配 A 侧层。为空时用约定名。")]
        [SerializeField] private LayerMask m_SideAOverride;

        [Tooltip("手配 B 侧层。为空时用约定名。")]
        [SerializeField] private LayerMask m_SideBOverride;

        [Tooltip("手配共享层。为空时用约定名。")]
        [SerializeField] private LayerMask m_SharedOverride;

        [Tooltip("两侧相机是否都能看到对方的专属层。默认关闭：各看各的世界。")]
        [SerializeField] private bool m_CrossVisible;

        private CinemachineRigBinder m_RigA;
        private CinemachineRigBinder m_RigB;
        private Camera m_CameraA;
        private Camera m_CameraB;
        private int m_BaseMaskA;
        private int m_BaseMaskB;
        private bool m_Bound;

        /// <summary>A 侧 rig。</summary>
        public CinemachineRigBinder RigA => m_RigA;

        /// <summary>B 侧 rig。</summary>
        public CinemachineRigBinder RigB => m_RigB;

        /// <summary>A 侧层名（约定名）。</summary>
        public string SideALayerName
        {
            get => m_SideALayerName;
            set => m_SideALayerName = value;
        }

        /// <summary>B 侧层名（约定名）。</summary>
        public string SideBLayerName
        {
            get => m_SideBLayerName;
            set => m_SideBLayerName = value;
        }

        /// <summary>共享层名（约定名）。</summary>
        public string SharedLayerName
        {
            get => m_SharedLayerName;
            set => m_SharedLayerName = value;
        }

        /// <summary>两侧是否互相可见。</summary>
        public bool CrossVisible
        {
            get => m_CrossVisible;
            set
            {
                m_CrossVisible = value;
                Apply();
            }
        }

        /// <summary>绑定两侧 rig 并立即应用分层。</summary>
        public void Bind(CinemachineRigBinder rigA, CinemachineRigBinder rigB)
        {
            Unbind();

            m_RigA = rigA;
            m_RigB = rigB;
            m_CameraA = rigA != null ? rigA.GetComponentInChildren<Camera>(true) : null;
            m_CameraB = rigB != null ? rigB.GetComponentInChildren<Camera>(true) : null;

            if (m_CameraA == null || m_CameraB == null)
            {
                Debug.LogWarning("[HYC.Cinemachine] 双世界需要两侧各自带 Camera 的 rig。");
                return;
            }

            m_BaseMaskA = m_CameraA.cullingMask;
            m_BaseMaskB = m_CameraB.cullingMask;
            m_Bound = true;

            Apply();
        }

        /// <summary>解绑并还原两侧相机原来的 cullingMask。</summary>
        public void Unbind()
        {
            if (m_Bound)
            {
                if (m_CameraA != null)
                    m_CameraA.cullingMask = m_BaseMaskA;
                if (m_CameraB != null)
                    m_CameraB.cullingMask = m_BaseMaskB;
            }

            m_Bound = false;
            m_RigA = null;
            m_RigB = null;
            m_CameraA = null;
            m_CameraB = null;
        }

        /// <summary>按当前配置重新下发 cullingMask。</summary>
        public void Apply()
        {
            if (!m_Bound)
                return;

            if (!TryResolveMask(out var sideA, out var sideB, out var shared))
            {
                Debug.LogError(
                    "[HYC.Cinemachine] 双世界层解析失败。请在 Project Settings > Tags and Layers 里添加：" +
                    m_SideALayerName + " / " + m_SideBLayerName + " / " + m_SharedLayerName +
                    "，或在 Inspector 里手配三个 LayerMask。");
                return;
            }

            // 原掩码当「公共世界」，再并上共享层与自身侧；
            // 不互看时把对侧专属层剔掉（否则基础掩码是 Everything 时两边都看得到全部）
            var maskA = m_BaseMaskA | shared | sideA;
            var maskB = m_BaseMaskB | shared | sideB;

            if (!m_CrossVisible)
            {
                maskA &= ~sideB;
                maskB &= ~sideA;
            }

            m_CameraA.cullingMask = maskA;
            m_CameraB.cullingMask = maskB;
        }

        /// <summary>逐侧控制「另一侧能不能看到我这边的内容」。</summary>
        /// <param name="aVisibleToB">A 侧内容是否让 B 相机也看到。</param>
        /// <param name="bVisibleToA">B 侧内容是否让 A 相机也看到。</param>
        public void SetCrossVisible(bool aVisibleToB, bool bVisibleToA)
        {
            if (!m_Bound)
                return;

            if (!TryResolveMask(out var sideA, out var sideB, out var shared))
                return;

            var maskA = m_BaseMaskA | shared | sideA;
            var maskB = m_BaseMaskB | shared | sideB;

            if (!bVisibleToA)
                maskA &= ~sideB;
            if (!aVisibleToB)
                maskB &= ~sideA;

            m_CameraA.cullingMask = maskA;
            m_CameraB.cullingMask = maskB;
        }

        /// <summary>
        /// 解析三个层掩码。任何一层没解析到都返回 false。
        /// <para>⚠️ <c>LayerMask.NameToLayer</c> 对不存在的层返回 <c>-1</c>，而
        /// <c>1 &lt;&lt; -1</c> 在 C# 里不会报错（位移量按低 5 位掩码），结果是第 31 层。
        /// 所以必须先判 <c>&gt;= 0</c>，否则「层名打错一个字」会悄悄把第 31 层渲进来。</para>
        /// </summary>
        public bool TryResolveMask(out int sideA, out int sideB, out int shared)
        {
            sideA = ResolveMask(m_SideAOverride, m_SideALayerName);
            sideB = ResolveMask(m_SideBOverride, m_SideBLayerName);
            shared = ResolveMask(m_SharedOverride, m_SharedLayerName);

            return sideA != 0 && sideB != 0 && shared != 0;
        }

        private static int ResolveMask(LayerMask configured, string fallbackName)
        {
            if (configured.value != 0)
                return configured.value;

            var layer = LayerMask.NameToLayer(fallbackName);
            if (layer < 0)
                return 0;

            return 1 << layer;
        }

        private void OnValidate()
        {
            ValidateLayer(m_SideAOverride, m_SideALayerName);
            ValidateLayer(m_SideBOverride, m_SideBLayerName);
            ValidateLayer(m_SharedOverride, m_SharedLayerName);
        }

        private static void ValidateLayer(LayerMask configured, string name)
        {
            if (configured.value != 0 || string.IsNullOrEmpty(name))
                return;

            if (LayerMask.NameToLayer(name) < 0)
                Debug.LogWarning("[HYC.Cinemachine] 层 \"" + name + "\" 不存在，" +
                                 "请到 Project Settings > Tags and Layers 添加，或在 Inspector 手配 LayerMask。");
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
