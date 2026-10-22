// ============================================================
// HYC Framework - Cinemachine 相机装配 · 双变体
// 文件: Runtime/Cinemachine/CinemachineDualVariant.cs
// 说明: 「同一个东西，A 相机看到一种、B 相机看到另一种」的落地组件。
//       两份视觉体分别放到 A / B 层，两台相机的 cullingMask 由
//       CinemachineDualWorld 拼装 —— 于是外观切换点由**接缝位置**决定，
//       物体自己一行交叉检测代码都不用写。
// ============================================================

using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>
    /// 双变体：一个逻辑体配两份视觉体，A 相机只看得见 <see cref="VariantA"/>、
    /// B 相机只看得见 <see cref="VariantB"/>。
    /// <para><b>切换点由接缝决定，不在这里</b>：合成是屏幕空间逐像素择一，
    /// 物体跨在接缝上就是「左半 A 变体 / 右半 B 变体」，飞到哪边就看到哪边。
    /// 所以「香蕉飞到右屏变成回旋镖」不需要任何代码，也不需要交叉判定。</para>
    /// <para><b>契约</b>：碰撞体与玩法状态只放逻辑根（本组件所在物体）；
    /// 两份变体是纯表现，不要挂碰撞体，也不要被玩法代码引用。</para>
    /// <para><b>前提</b>：层的 cullingMask 分配由 <see cref="CinemachineDualWorld"/> 负责，
    /// 本组件只负责把视觉体摆到正确的层上。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineDualVariant : MonoBehaviour
    {
        [Tooltip("A 侧视觉体。A 相机只看它（B 相机看不到）。")]
        [SerializeField] private GameObject m_VariantA;

        [Tooltip("B 侧视觉体。B 相机只看它（A 相机看不到）。留空表示这物体只在 A 侧存在。")]
        [SerializeField] private GameObject m_VariantB;

        [Tooltip("A 侧层名（要与 CinemachineDualWorld 的 SideALayerName 一致）。")]
        [SerializeField] private string m_SideALayerName = "HYC.WorldA";

        [Tooltip("B 侧层名（要与 CinemachineDualWorld 的 SideBLayerName 一致）。")]
        [SerializeField] private string m_SideBLayerName = "HYC.WorldB";

        [Tooltip("A 侧层号。非负时优先用层号、忽略层名（层没在 Tags and Layers 命名时用）。")]
        [SerializeField] private int m_SideALayer = -1;

        [Tooltip("B 侧层号。非负时优先用层号、忽略层名。")]
        [SerializeField] private int m_SideBLayer = -1;

        [Tooltip("外部视觉体（不是本物体子物体时）每帧跟随的目标。子物体留空即可。")]
        [SerializeField] private Transform m_FollowTarget;

        [Tooltip("Awake 时自动设层。视觉体是运行期实例化的就关掉它，实例化后手动调 Apply()。")]
        [SerializeField] private bool m_ApplyOnAwake = true;

        private bool m_WarnedColliders;

        /// <summary>A 侧视觉体。赋值立即重新设层。</summary>
        public GameObject VariantA
        {
            get => m_VariantA;
            set
            {
                m_VariantA = value;
                Apply();
            }
        }

        /// <summary>B 侧视觉体。赋值立即重新设层。</summary>
        public GameObject VariantB
        {
            get => m_VariantB;
            set
            {
                m_VariantB = value;
                Apply();
            }
        }

        /// <summary>是否两个视觉体都齐了。</summary>
        public bool IsWired => m_VariantA != null && m_VariantB != null;

        /// <summary>
        /// 直接用层号指定两侧（跳过层名解析），赋值后立即重新设层。
        /// <para>层没在 Project Settings &gt; Tags and Layers 里命名时用这个 ——
        /// <see cref="LayerMask.NameToLayer"/> 对无名层返回空串，解析不出层号。</para>
        /// </summary>
        public void SetLayers(int layerA, int layerB)
        {
            m_SideALayer = layerA;
            m_SideBLayer = layerB;
            Apply();
        }

        private void Awake()
        {
            if (m_ApplyOnAwake)
                Apply();
        }

        /// <summary>
        /// 把两份视觉体分别设到 A / B 层（含其全部子物体）。
        /// <para>层名解析失败时报错并返回 false，不会静默把变体留在默认层
        /// —— 那样两台相机会同时看到两份美术，叠在一起。</para>
        /// </summary>
        public bool Apply()
        {
            if (m_VariantA == null && m_VariantB == null)
                return false;

            var ok = true;

            if (m_VariantA != null)
                ok &= ApplyTo(m_VariantA, m_SideALayer, m_SideALayerName, "A");

            if (m_VariantB != null)
                ok &= ApplyTo(m_VariantB, m_SideBLayer, m_SideBLayerName, "B");

            return ok;
        }

        /// <summary>
        /// 逐侧开关变体。用来做「这物体此刻只属于某一侧」的表现
        /// （注意：关掉变体不等于改变它的归属，接缝仍然会切）。
        /// </summary>
        /// <param name="a">A 变体是否显示。</param>
        /// <param name="b">B 变体是否显示。</param>
        public void SetSideVisible(bool a, bool b)
        {
            if (m_VariantA != null)
                m_VariantA.SetActive(a);
            if (m_VariantB != null)
                m_VariantB.SetActive(b);
        }

        private bool ApplyTo(GameObject variant, int layerIndex, string layerName, string side)
        {
            // 层号优先：样例/原型常用无名层（28、29 之类），只给层名会解析失败
            var layer = layerIndex >= 0 ? Mathf.Clamp(layerIndex, 0, 31) : LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                Debug.LogError("[HYC.Cinemachine] 双变体 " + side + " 侧层 \"" + layerName + "\" 不存在。" +
                               "请到 Project Settings > Tags and Layers 添加，并确认与 CinemachineDualWorld 的层名一致；" +
                               "或改用 SetLayers(层号, 层号) 直接指定。");
                return false;
            }

            if (!m_WarnedColliders)
                WarnColliders(variant);

            SetLayerRecursively(variant, layer);
            return true;
        }

        private void WarnColliders(GameObject variant)
        {
            var colliders = variant.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0)
                return;

            Debug.LogWarning("[HYC.Cinemachine] 双变体 \"" + variant.name + "\" 子树里有 " +
                             colliders.Length + " 个碰撞体。变体是纯表现，碰撞体应只放在逻辑根上，" +
                             "否则同一处会有重复物理（角色会被挡住两次、射线也打两次）。");
            m_WarnedColliders = true;
        }

        /// <summary>把整个子树设到同一层。</summary>
        public static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
                return;

            root.layer = layer;
            var self = root.transform;
            for (var i = 0; i < self.childCount; i++)
                SetLayerRecursively(self.GetChild(i).gameObject, layer);
        }

        private void LateUpdate()
        {
            if (m_FollowTarget == null)
                return;

            Follow(m_VariantA);
            Follow(m_VariantB);
        }

        private void Follow(GameObject variant)
        {
            if (variant == null)
                return;

            var t = variant.transform;
            // 已经是子物体就不需要跟，父节点自己会带着走
            if (t.IsChildOf(m_FollowTarget))
                return;

            t.SetPositionAndRotation(m_FollowTarget.position, m_FollowTarget.rotation);
        }
    }
}
