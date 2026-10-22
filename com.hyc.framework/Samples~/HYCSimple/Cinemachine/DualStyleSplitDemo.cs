// ============================================================
// HYC Framework - 样例 ② · 同一个场景，两侧看到的东西不一样
// 文件: Samples~/HYCSimple/Cinemachine/DualStyleSplitDemo.cs
// 说明: 场景只有一份、位置固定，玩家不会拖着场景走。两侧的差别全部来自
//       渲染层：A 层的东西只有 A 相机看得见，B 层的只有 B 相机看得见。
//       于是同一个位置上的方块，左半屏是 Cube、右半屏是 Sphere ——
//       物体越过切线的瞬间就「变」了，不需要任何交叉检测代码。
//       走的是接缝合成路径（Composite），两台相机看同一张图。
// ============================================================

using HYC.Framework.Cinemachine;
using UnityEngine;

namespace HYC.Simple.DualView
{
    /// <summary>
    /// ② 同场景视觉分叉：一台逻辑体配两套视觉体，按层分给两台相机。
    /// <para><b>怎么玩（全部在 Game 视图里用鼠标）</b>：</para>
    /// <list type="bullet">
    /// <item>按住<b>发光的接缝线</b>拖动 → 分屏线跟着鼠标走（竖切 / 横切 / 斜切都行）。</item>
    /// <item>按住<b>橙色 / 青色方块</b>拖动 → 角色在场景里跑，方块压到接缝上时逐像素变形。</item>
    /// <item>「跟着两人切」模式下拖角色 → 接缝自动跟着两人连线转。</item>
    /// </list>
    /// <para><b>为什么必须走合成</b>：<c>Camera.rect</c> 下相机宽高比是半屏，
    /// 水平视野被压窄，左右两半根本不是同一张图切开的，接缝处会错位。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DualStyleSplitDemo : MonoBehaviour
    {
        private static readonly Vector3 k_CameraPosition = new Vector3(0f, 17f, -25f);
        private static readonly Vector3 k_CameraLookAt = new Vector3(0f, 2.5f, 4f);

        /// <summary>左上角 IMGUI 面板占位：鼠标落在这块里就交给按钮，不做拖拽。</summary>
        private static readonly Rect k_PanelRect = new Rect(12f, 12f, 350f, 506f);

        /// <summary>接缝的抓取半径（像素）。</summary>
        private const float k_SeamGrabPx = 34f;

        /// <summary>角色的抓取半径（像素）。</summary>
        private const float k_PlayerGrabPx = 90f;

        private enum DragMode
        {
            None,
            Seam,
            PlayerA,
            PlayerB,
        }

        private CinemachineDualView m_View;
        private Transform m_PlayerA;
        private Transform m_PlayerB;
        private Camera m_Camera;
        private DragMode m_Drag;

        private bool m_Glow = true;
        private bool m_Tinted;

        private void Awake()
        {
            DemoFactory.DirectionalLight(transform, new Vector3(55f, -32f, 0f), Color.white);

            BuildSharedWorld();
            BuildVariantRow();

            m_PlayerA = BuildPlayer("Player A", new Vector3(-9f, 1.2f, 5f),
                new Color(1f, 0.55f, 0.15f), new Color(1f, 0.85f, 0.25f));
            m_PlayerB = BuildPlayer("Player B", new Vector3(9f, 1.2f, 5f),
                new Color(0.2f, 0.75f, 0.95f), new Color(0.35f, 0.95f, 0.85f));

            // 两台相机看同一张图：共享层两边都看，各自专属层只看自己那份
            var rigA = BuildRig("Rig A", (1 << DemoFactory.LayerShared) | (1 << DemoFactory.LayerA), 0, withVcam: true);
            var rigB = BuildRig("Rig B", (1 << DemoFactory.LayerShared) | (1 << DemoFactory.LayerB), 1, withVcam: false);

            m_Camera = rigA.GetComponentInChildren<Camera>(true);

            m_View = gameObject.AddComponent<CinemachineDualView>();
            m_View.Bind(rigA, rigB, m_PlayerA, m_PlayerB);

            // 合成模式下相机渲整屏再被裁，角色会落在接缝上 —— 那是「各跟各的玩家」时才需要的补偿，
            // 本例两台相机是固定机位，必须关掉，否则会被主动推开
            m_View.AutoFraming = false;

            m_View.Separate(SplitStyle.Vertical);

            // 接缝光带：一条数学上存在的线在画面上是看不见的，玩家不知道往哪儿按。
            // 加法混合的青蓝色呼吸光带 = 「这里能拖」的唯一提示。
            ApplyGlow();

            // 镜像相机从合成器的两侧 rig 里取相机，所以必须等合成器建好（Separate 之后）再解析
            var mirror = gameObject.AddComponent<CinemachineDualMirror>();
            mirror.Refresh();
            if (!mirror.IsMirroring)
                Debug.LogWarning("[HYCSimple] 镜像相机未解析到两台相机，接缝处可能出现折痕。");
        }

        // ---------- 搭场景（全部固定不动） ----------

        private void BuildSharedWorld()
        {
            var world = DemoFactory.Empty("Shared World", transform, Vector3.zero);
            DemoFactory.Ground("Ground", world.transform, 60f, new Color(0.18f, 0.19f, 0.22f), DemoFactory.LayerShared);

            // 背景墙，用来衬出接缝两侧的亮暗差
            DemoFactory.Box("Back Wall", world.transform, new Vector3(0f, 5f, 22f),
                new Vector3(60f, 10f, 1f), new Color(0.1f, 0.11f, 0.14f), DemoFactory.LayerShared);

            for (var i = 0; i < 9; i++)
            {
                var x = -24f + i * 6f;
                DemoFactory.Box("Column " + i, world.transform, new Vector3(x, 2.5f, 14f),
                    new Vector3(1.4f, 5f, 1.4f), new Color(0.28f, 0.3f, 0.34f), DemoFactory.LayerShared);
            }
        }

        /// <summary>一排「同位置、两套外形」的物体：左半看到 Cube，右半看到 Sphere。</summary>
        private void BuildVariantRow()
        {
            for (var i = 0; i < 5; i++)
            {
                var x = -12f + i * 6f;
                var root = DemoFactory.Empty("Variant " + i, transform, new Vector3(x, 0f, -2f));

                DemoFactory.Box("Pedestal", root.transform, new Vector3(0f, 0.4f, 0f),
                    new Vector3(2.6f, 0.8f, 2.6f), new Color(0.24f, 0.25f, 0.28f), DemoFactory.LayerShared);

                var cube = DemoFactory.Box("A · Cube", root.transform, new Vector3(0f, 1.9f, 0f),
                    Vector3.one * 2.2f, new Color(0.92f, 0.28f, 0.22f), DemoFactory.LayerA);
                var sphere = DemoFactory.Ball("B · Sphere", root.transform, new Vector3(0f, 1.9f, 0f),
                    Vector3.one * 2.2f, new Color(0.2f, 0.78f, 0.55f), DemoFactory.LayerB);

                var variant = root.AddComponent<CinemachineDualVariant>();
                // 先定层号（样例用无名层 28/29，不要求改 Tags and Layers），再挂视觉体
                variant.SetLayers(DemoFactory.LayerA, DemoFactory.LayerB);
                variant.VariantA = cube;      // 赋值即递归设层
                variant.VariantB = sphere;
            }
        }

        private Transform BuildPlayer(string name, Vector3 position, Color colorA, Color colorB)
        {
            var root = DemoFactory.Empty(name, transform, position);

            var cube = DemoFactory.Box(name + " · A", root.transform, Vector3.zero,
                Vector3.one * 1.6f, colorA, DemoFactory.LayerA);
            var sphere = DemoFactory.Ball(name + " · B", root.transform, Vector3.zero,
                Vector3.one * 1.6f, colorB, DemoFactory.LayerB);

            var variant = root.AddComponent<CinemachineDualVariant>();
            variant.SetLayers(DemoFactory.LayerA, DemoFactory.LayerB);
            variant.VariantA = cube;
            variant.VariantB = sphere;

            return root.transform;
        }

        /// <summary>
        /// 建一侧 rig。
        /// <para><paramref name="withVcam"/> = true 的那侧才是「有视角」的一侧：一台固定机位
        /// （<c>Unity.Cinemachine.CinemachineCamera</c>，不挂 Body/Aim ⇒ 相机就停在机位位姿上）。
        /// 另一侧不挂 Brain、也没有机位 —— 它的相机位姿由 <see cref="CinemachineDualMirror"/> 每帧
        /// 从有视角那侧复制。两个 Brain 抢同一台相机是本 demo 最容易踩的坑，所以刻意只留一个。</para>
        /// </summary>
        private CinemachineRigBinder BuildRig(string name, int cullingMask, int depth, bool withVcam)
        {
            var rigRoot = new GameObject(name);
            rigRoot.transform.SetParent(transform, false);

            var rotation = Quaternion.LookRotation(k_CameraLookAt - k_CameraPosition);

            DemoFactory.CreateCamera(name + " Camera", rigRoot.transform,
                k_CameraPosition, rotation, cullingMask, new Color(0.05f, 0.06f, 0.08f), depth,
                addListener: depth == 0, addBrain: withVcam);

            // 机位与 Camera 平级：见 SameWorldSplitDemo.BuildRig 的注释
            if (withVcam)
                DemoFactory.FixedCam(name + " Fixed Cam", rigRoot.transform, k_CameraPosition, rotation);

            return rigRoot.AddComponent<CinemachineRigBinder>();
        }

        // ---------- Game 视图里的鼠标拖拽 ----------

        /// <summary>
        /// 用 <see cref="Event.current"/> 而不是 <c>Input.GetMouseButton*</c>：
        /// 后者在「Player Settings → Active Input Handling = Input System Package」的工程里会直接抛
        /// <c>InvalidOperationException</c>。IMGUI 事件两条输入路径都通，样例不该逼使用方改设置。
        /// </summary>
        private void OnGUI()
        {
            HandleMouse();
            DrawPanel();
        }

        private void HandleMouse()
        {
            if (m_View == null || m_Camera == null)
                return;

            var e = Event.current;

            // IMGUI 的鼠标坐标原点在左上，视口坐标原点在左下
            var gui = e.mousePosition;
            if (k_PanelRect.Contains(gui))
            {
                if (e.type == EventType.MouseUp)
                    m_Drag = DragMode.None;
                return;                       // 面板区域交给按钮
            }

            var vp = new Vector2(gui.x / Screen.width, 1f - gui.y / Screen.height);

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    m_Drag = Pick(vp, gui);
                    break;

                case EventType.MouseDrag when m_Drag != DragMode.None:
                    Apply(vp);
                    break;

                case EventType.MouseUp:
                    m_Drag = DragMode.None;
                    break;
            }
        }

        /// <summary>判断这一下点在了接缝上还是角色上。</summary>
        private DragMode Pick(Vector2 vp, Vector2 gui)
        {
            var seam = m_View.CurrentSeam;
            seam.GetFrame(out _, out var normal);

            // 法向单位向量在像素空间会被宽高比拉伸，先换到像素再量距离
            var normalPx = new Vector2(normal.x * Screen.width, normal.y * Screen.height);
            var seamPx = Mathf.Abs(seam.SignedDistance(vp)) * normalPx.magnitude;

            // 跟随类模式下接缝位置由驱动源每帧重算，拖了也会被覆盖 ⇒ 先判角色
            var seamDraggable = m_View.Style == SplitStyle.Vertical
                                || m_View.Style == SplitStyle.Horizontal
                                || m_View.Style == SplitStyle.Diagonal;

            var toA = DistancePx(m_PlayerA, gui);
            var toB = DistancePx(m_PlayerB, gui);

            if (seamDraggable && seamPx < k_SeamGrabPx)
                return DragMode.Seam;
            if (toA < k_PlayerGrabPx && toA <= toB)
                return DragMode.PlayerA;
            if (toB < k_PlayerGrabPx)
                return DragMode.PlayerB;

            return DragMode.None;
        }

        private float DistancePx(Transform target, Vector2 gui)
        {
            if (target == null)
                return float.MaxValue;

            var sp = m_Camera.WorldToScreenPoint(target.position);
            if (sp.z < 0f)
                return float.MaxValue;        // 在相机背后

            // WorldToScreenPoint 原点在左下，GUI 在左上
            var px = new Vector2(sp.x, Screen.height - sp.y);
            return Vector2.Distance(px, gui);
        }

        private void Apply(Vector2 vp)
        {
            if (m_Drag == DragMode.Seam)
            {
                DragSeam(vp);
                return;
            }

            var target = m_Drag == DragMode.PlayerA ? m_PlayerA : m_PlayerB;
            DragPlayer(target, vp);
        }

        /// <summary>把接缝平移到「正好过鼠标点」的位置 —— 绝对定位，天然幂等，不用记增量。</summary>
        private void DragSeam(Vector2 vp)
        {
            var seam = m_View.CurrentSeam;
            seam.GetFrame(out _, out var normal);

            // 鼠标到接缝的有符号距离：沿法线挪这么多，接缝就正好穿过鼠标
            var delta = Vector2.Dot(vp - seam.Point, normal);
            seam.Point += normal * delta;
            m_View.SetSeam(seam);
        }

        /// <summary>把角色拖到鼠标射线与它所在水平面的交点上（固定机位，平面法线取世界 Y）。</summary>
        private void DragPlayer(Transform target, Vector2 vp)
        {
            if (target == null)
                return;

            var ray = m_Camera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
            var plane = new Plane(Vector3.up, new Vector3(0f, target.position.y, 0f));
            if (!plane.Raycast(ray, out var enter))
                return;

            var hit = ray.GetPoint(enter);
            target.position = new Vector3(hit.x, target.position.y, hit.z);
        }

        // ---------- 演示面板 ----------

        private void DrawPanel()
        {
            // 每帧控件集合必须完全一致（IMGUI 铁律）：按钮常驻，只换标题文字
            GUILayout.BeginArea(k_PanelRect, GUI.skin.box);

            GUILayout.Label("② 同场景不同风格 · 位置固定，只改视觉");
            GUILayout.Label("鼠标按住发光接缝 → 拖动分屏线");
            GUILayout.Label("鼠标按住橙 / 青方块 → 拖动角色");
            GUILayout.Label("当前：" + m_View.Style +
                            (m_View.IsSeparated ? "" : "（已合并成一屏）") +
                            (m_View.Style == SplitStyle.FollowPlayers ? "（拖角色带接缝走）" : ""));
            GUILayout.Space(6f);

            // 分屏 / 合并屏：两例都只能做「溶化保留某一侧」——本样例两台相机是**固定机位**
            // （AutoFraming = false，没有两人可并拢），走不了 CombineAroundPlayers 的收拢路线。
            // 想看真正的「并拢成一景」用 SameWorldSplitDemo。再点「分屏」会按当前切法重新对中。
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("分屏 Split"))
                m_View.Separate(m_View.Style);
            if (GUILayout.Button("合并屏 Combine"))
                m_View.Combine(0.6f);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            if (GUILayout.Button("竖切 Vertical"))
                m_View.Separate(SplitStyle.Vertical);
            if (GUILayout.Button("横切 Horizontal"))
                m_View.Separate(SplitStyle.Horizontal);
            if (GUILayout.Button("斜切 Diagonal"))
                m_View.Separate(SplitStyle.Diagonal);
            if (GUILayout.Button("跟着两人切 Follow Players"))
                m_View.Separate(SplitStyle.FollowPlayers);

            GUILayout.Space(6f);
            GUILayout.Label("分屏线压缩（A 侧占比）");

            if (GUILayout.Button("A 窄（30%）"))
                SetSideAShare(0.3f);
            if (GUILayout.Button("对半（50%）"))
                SetSideAShare(0.5f);
            if (GUILayout.Button("A 宽（70%）"))
                SetSideAShare(0.7f);

            GUILayout.Space(6f);
            GUILayout.Label("接缝视觉");

            if (GUILayout.Button(m_Glow ? "接缝光带：开（点一下关）" : "接缝光带：关（点一下开）"))
            {
                m_Glow = !m_Glow;
                ApplyGlow();
            }

            GUILayout.Space(6f);
            GUILayout.Label("染色");

            if (GUILayout.Button(m_Tinted ? "清掉染色（已压黑 / 压白）" : "A 侧压黑 / B 侧压白"))
            {
                m_Tinted = !m_Tinted;
                if (m_Tinted)
                    m_View.Tint(new Color(0f, 0f, 0f, 0.85f), new Color(1f, 1f, 1f, 0.6f));
                else
                    m_View.ClearTint();
            }

            GUILayout.EndArea();
        }

        private void ApplyGlow()
        {
            if (m_View == null)
                return;

            if (m_Glow)
                m_View.SetGlow(0.022f, new Color(0.25f, 0.92f, 1f, 0.9f), 0.55f);
            else
                m_View.ClearGlow();
        }

        /// <summary>
        /// 让 A 侧占屏幕 <paramref name="share"/>。
        /// <para>跟随模式下改 <see cref="CinemachineDualView.SplitWeight"/>；
        /// 固定接缝下把接缝沿法线推到目标位置（用框架公开的 <c>GetNormalSpan</c> 反算）。</para>
        /// </summary>
        private void SetSideAShare(float share)
        {
            if (m_View == null)
                return;

            if (m_View.Style == SplitStyle.FollowPlayers)
            {
                m_View.SplitWeight = share;
                return;
            }

            var seam = m_View.CurrentSeam;
            seam.GetNormalSpan(out var min, out var max);
            var span = max - min;
            if (span <= 1e-5f)
                return;

            seam.GetFrame(out _, out var normal);
            seam.Point += normal * (min + share * span);
            m_View.SetSeam(seam);
        }
    }
}
