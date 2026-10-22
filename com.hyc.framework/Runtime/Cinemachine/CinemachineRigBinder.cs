// ============================================================
// HYC Framework - Cinemachine 相机装配 · 运行期访问门面
// 文件: Runtime/Cinemachine/CinemachineRigBinder.cs
// 说明: 挂在 rig prefab 根节点，是这个 rig 在运行期的唯一入口：
//       ① 挂点（Follow / LookAt）下发到所有机位；
//       ② 列出 rig 里的机位；
//       ③ 按类型 / 节点名 / 机位引用切换。
//       切换靠改 Priority —— CM 3 没有「激活某台相机」的 API，
//       活跃相机 = Priority 最高的那台。首次扫描时快照各机位的基础
//       优先级，切换时把目标抬到「基础最高 + 10」、其余复原基础值，
//       这样反复切换不会让优先级无限膨胀、原有梯度也不被破坏。
// ============================================================

using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace HYC.Framework.Cinemachine
{
    /// <summary>
    /// Cinemachine 相机装配（rig prefab）的运行期门面。
    /// <para>实例化 rig 后通过它绑挂点、列机位、切机位：</para>
    /// <code>
    /// var rig = Instantiate(prefab).GetComponent&lt;CinemachineRigBinder&gt;();
    /// rig.SetTargets(playerHead, playerHead);
    /// rig.SwitchTo(RigKind.ThirdPerson);
    /// </code>
    /// <para><b>优先级所有权</b>：切换机位会改写全部机位的 Priority，
    /// 宿主不要再手工改 Priority，否则下次切换会被本组件复原成基础值。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinemachineRigBinder : MonoBehaviour
    {
        /// <summary>激活机位相对基础最高优先级的抬升量（保证它严格最高，且不破坏原梯度）。</summary>
        private const int k_ActivePriorityBump = 10;

        [Tooltip("跟随目标（位置跟踪）。为空表示不改写各相机的 Follow。")]
        [SerializeField] Transform m_FollowTarget;

        [Tooltip("朝向目标（LookAt）。为空表示不改写各相机的 LookAt。")]
        [SerializeField] Transform m_LookAtTarget;

        private CinemachineCamera[] m_Cameras;
        private int[] m_BasePriorities;

        /// <summary>活跃机位发生变化时触发（切换后确实换了机位才触发）。</summary>
        public event Action<CinemachineCamera> ActiveChanged;

        /// <summary>跟随目标。赋值即下发到所有机位。</summary>
        public Transform FollowTarget
        {
            get => m_FollowTarget;
            set
            {
                m_FollowTarget = value;
                ApplyTargets();
            }
        }

        /// <summary>朝向目标（LookAt）。赋值即下发到所有机位。</summary>
        public Transform LookAtTarget
        {
            get => m_LookAtTarget;
            set
            {
                m_LookAtTarget = value;
                ApplyTargets();
            }
        }

        /// <summary>rig 下全部机位（惰性扫描并缓存，顺序 = prefab 子节点顺序）。</summary>
        public IReadOnlyList<CinemachineCamera> Cameras
        {
            get
            {
                EnsureScanned();
                return m_Cameras;
            }
        }

        /// <summary>当前活跃机位（优先级最高且处于启用状态的那台）；无可用机位时为 null。</summary>
        public CinemachineCamera Active
        {
            get
            {
                EnsureScanned();
                CinemachineCamera best = null;
                var bestPriority = int.MinValue;
                for (int i = 0; i < m_Cameras.Length; i++)
                {
                    var camera = m_Cameras[i];
                    if (camera == null || !camera.isActiveAndEnabled)
                        continue;
                    int priority = camera.Priority;
                    if (priority > bestPriority)
                    {
                        bestPriority = priority;
                        best = camera;
                    }
                }
                return best;
            }
        }

        void Awake()
        {
            EnsureScanned();
            ApplyTargets();
        }

        // ---------- 挂点 ----------

        /// <summary>
        /// 一次设置两个挂点并立即下发。传 null 表示该目标不参与改写
        /// （各机位保留编辑期指向的 RigTarget 占位）。
        /// </summary>
        public void SetTargets(Transform follow, Transform lookAt)
        {
            m_FollowTarget = follow;
            m_LookAtTarget = lookAt;
            ApplyTargets();
        }

        /// <summary>
        /// 把当前挂点下发到所有机位（含未激活的）。两个挂点都为空时不做任何事。
        /// <para>批量改挂点时先赋值属性、再调一次本方法可省掉中间下发，
        /// 但直接赋值属性本身就会下发，非必要不必手调。</para>
        /// </summary>
        public void ApplyTargets()
        {
            if (m_FollowTarget == null && m_LookAtTarget == null)
                return;

            var cameras = GetComponentsInChildren<CinemachineVirtualCameraBase>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (camera == null)
                    continue;
                if (m_FollowTarget != null)
                    camera.Follow = m_FollowTarget;
                if (m_LookAtTarget != null)
                    camera.LookAt = m_LookAtTarget;
            }
        }

        // ---------- 查机位 ----------

        /// <summary>按机位类型查（同类型有多个时取 prefab 里靠前的那个）。</summary>
        public bool TryGet(RigKind kind, out CinemachineCamera camera)
        {
            EnsureScanned();
            for (int i = 0; i < m_Cameras.Length; i++)
            {
                var candidate = m_Cameras[i];
                if (candidate == null)
                    continue;
                var tag = candidate.GetComponent<CinemachineRigCameraTag>();
                if (tag != null && tag.Kind == kind)
                {
                    camera = candidate;
                    return true;
                }
            }
            camera = null;
            return false;
        }

        /// <summary>按机位节点名查（名字取 prefab 里的节点名，如 <c>vcam_FreeLook</c>）。</summary>
        public bool TryGet(string cameraName, out CinemachineCamera camera)
        {
            EnsureScanned();
            if (!string.IsNullOrEmpty(cameraName))
            {
                for (int i = 0; i < m_Cameras.Length; i++)
                {
                    var candidate = m_Cameras[i];
                    if (candidate != null
                        && string.Equals(candidate.name, cameraName, StringComparison.Ordinal))
                    {
                        camera = candidate;
                        return true;
                    }
                }
            }
            camera = null;
            return false;
        }

        /// <summary>按类型取机位；取不到返 null（不抛异常）。</summary>
        public CinemachineCamera Get(RigKind kind)
        {
            TryGet(kind, out var camera);
            return camera;
        }

        /// <summary>按节点名取机位；取不到返 null（不抛异常）。</summary>
        public CinemachineCamera Get(string cameraName)
        {
            TryGet(cameraName, out var camera);
            return camera;
        }

        // ---------- 切机位 ----------

        /// <summary>切到指定类型的机位；该类型不存在时返 false 且保持不变。</summary>
        public bool SwitchTo(RigKind kind)
        {
            return TryGet(kind, out var camera) && SwitchTo(camera);
        }

        /// <summary>切到指定节点名的机位；名字不存在时返 false 且保持不变。</summary>
        public bool SwitchTo(string cameraName)
        {
            return TryGet(cameraName, out var camera) && SwitchTo(camera);
        }

        /// <summary>
        /// 切到指定机位：目标抬到「基础最高优先级 + 10」，其余机位复原基础值。
        /// 机位不在本 rig 内（或为 null）时返 false。
        /// </summary>
        public bool SwitchTo(CinemachineCamera camera)
        {
            if (camera == null)
                return false;

            EnsureScanned();
            var index = Array.IndexOf(m_Cameras, camera);
            if (index < 0)
                return false;

            var previous = Active;
            var targetPriority = BaseMaxPriority() + k_ActivePriorityBump;
            for (int i = 0; i < m_Cameras.Length; i++)
            {
                var candidate = m_Cameras[i];
                if (candidate == null)
                    continue;
                candidate.Priority = i == index ? targetPriority : m_BasePriorities[i];
            }

            if (!ReferenceEquals(previous, camera))
                ActiveChanged?.Invoke(camera);
            return true;
        }

        /// <summary>
        /// 丢弃机位缓存并重新扫描。仅在运行期增删机位后需要调用
        /// （同时会重新快照基础优先级）。
        /// </summary>
        public void Rescan()
        {
            m_Cameras = null;
            EnsureScanned();
        }

        // ---------- 内部 ----------

        private void EnsureScanned()
        {
            if (m_Cameras != null)
                return;

            m_Cameras = GetComponentsInChildren<CinemachineCamera>(true);
            m_BasePriorities = new int[m_Cameras.Length];
            for (int i = 0; i < m_Cameras.Length; i++)
            {
                // 显式走 int 局部变量：PrioritySettings 与 int 双向隐式转换，
                // 直接写三元表达式会因找不到公共类型而 CS0172。
                var basePriority = 0;
                var camera = m_Cameras[i];
                if (camera != null)
                    basePriority = camera.Priority;
                m_BasePriorities[i] = basePriority;
            }
        }

        private int BaseMaxPriority()
        {
            var max = 0;
            for (int i = 0; i < m_BasePriorities.Length; i++)
            {
                if (m_BasePriorities[i] > max)
                    max = m_BasePriorities[i];
            }
            return max;
        }
    }
}
