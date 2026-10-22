// ============================================================
// HYC Framework - 样例 ① · 同一个大场景，双人分屏
// 文件: Samples~/HYCSimple/Cinemachine/SameWorldSplitDemo.cs
// 说明: 只有一个场景、一个世界。两个角色都在这块地图上，左半屏的相机跟红方块、
//       右半屏的相机跟蓝方块 —— 两半看的是**同一张地图的不同角度**，
//       不是两个互不相干的世界。走的是接缝合成路径（Composite），
//       所以接缝可以任意角度 / 任意占比，还能在 Game 视图里直接拖。
// ============================================================

using HYC.Framework.Cinemachine;
using UnityEngine;

namespace HYC.Simple.DualView
{
    /// <summary>
    /// ① 同场景双人分屏：一个大场景、两个角色、两台各自跟随的相机。
    /// <para><b>怎么玩（全部在 Game 视图里用鼠标，不用切 Scene 视图）</b>：</para>
    /// <list type="bullet">
    /// <item>按住<b>发光的接缝线</b>拖动 → 分屏线跟着鼠标走（竖切 / 横切 / 斜切都行）。</item>
    /// <item>按住<b>红 / 蓝方块</b>拖动 → 角色在地图里跑，同一台相机跟着它走，
    /// 于是那一半屏的取景整个平移 —— 两半看的始终是同一张地图。</item>
    /// <item><b>右键按住拖动</b> → 像角色自己转视角那样，让鼠标所在那一侧的相机绕着自己的角色转
    /// （左右 = 转圈，上下 = 抬高压低）；两侧可以各转各的，所以「左右两半看的角度不同」是常态。</item>
    /// <item>把角色拖到接缝附近，能看到自己的角色被切成两半（各半屏裁的是同一张整屏图）。</item>
    /// </list>
    /// <para><b>为什么必须走合成</b>：<c>Camera.rect</c> 下相机宽高比是半屏，水平视野被压窄，
    /// 两个机位看到的不是「同一张图的两块」，接缝处会错位。详见 59 号 §1.2 / §1.3。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SameWorldSplitDemo : MonoBehaviour
    {
        /// <summary>左上角 IMGUI 面板占位：鼠标落在这块里就交给按钮，不做拖拽。</summary>
        private static readonly Rect k_PanelRect = new Rect(12f, 12f, 356f, 392f);

        /// <summary>接缝的抓取半径（像素）。</summary>
        private const float k_SeamGrabPx = 34f;

        /// <summary>角色的抓取半径（像素）。</summary>
        private const float k_PlayerGrabPx = 90f;

        /// <summary>右键转视角：每像素多少度（左右转圈 / 上下抬压）。</summary>
        private const float k_OrbitYawSpeed = 0.32f;
        private const float k_OrbitPitchSpeed = 0.22f;

        /// <summary>上下抬压的限位（相对初始俯角的增量，度）。</summary>
        private const float k_PitchMin = -40f;
        private const float k_PitchMax = 35f;

        /// <summary>
        /// 两台相机都看整个世界，**只排除内置 UI 层**（5）——
        /// 否则宿主工程里常驻的 HUD / 调试 UI 会被画进每一半屏。
        /// </summary>
        private const int k_CullingMask = ~(1 << 5);

        private static readonly Vector3 k_PlayerAStart = new Vector3(-9f, 1.1f, 0f);
        private static readonly Vector3 k_PlayerBStart = new Vector3(9f, 1.1f, 0f);

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
        private Camera m_CameraA;
        private Camera m_CameraB;
        private DragMode m_Drag;

        private bool m_Glow = true;

        /// <summary>两侧各自的环绕角（0 = A，1 = B）。右键拖动改它，两侧可以完全不同。</summary>
        private readonly float[] m_Yaw = new float[2];
        private readonly float[] m_Pitch = new float[2];

        /// <summary>正在被右键拖拽转视角的那一侧；-1 = 没有。</summary>
        private int m_OrbitSide = -1;

        /// <summary>建 rig 时的初始跟随偏移，转视角时以它为基准绕（绕完还是同一个俯角/距离）。</summary>
        private Vector3 m_OrbitBase = new Vector3(0f, 9f, -14f);

        private void Awake()
        {
            DemoFactory.DirectionalLight(transform, new Vector3(52f, -34f, 0f), new Color(1f, 0.97f, 0.92f));

            BuildWorld();

            m_PlayerA = BuildPlayer("Player A", k_PlayerAStart, new Color(0.95f, 0.32f, 0.26f));
            m_PlayerB = BuildPlayer("Player B", k_PlayerBStart, new Color(0.28f, 0.68f, 0.98f));

            // 两台相机看同一张地图，只是各自跟一个角色
            var rigA = BuildRig("Rig A", m_PlayerA, 0, out m_CameraA, withListener: true);
            var rigB = BuildRig("Rig B", m_PlayerB, 1, out m_CameraB, withListener: false);

            m_View = gameObject.AddComponent<CinemachineDualView>();
            m_View.Bind(rigA, rigB, m_PlayerA, m_PlayerB);

            // 合成模式下相机渲整屏再被裁，角色会落在接缝上。
            // 保持 AutoFraming = true（默认），框架的接缝取景会把每个角色推到自己那半的中间。
            m_View.Separate(SplitStyle.Vertical);

            // 接缝取景接管之后才拿得到基准偏移（Bind 时从 rig 的跟随组件读的）
            var baseOrbit = m_View.GetOrbit(0);
            if (baseOrbit.sqrMagnitude > 1e-4f)
                m_OrbitBase = baseOrbit;

            // 接缝光带：一条数学上存在的线在画面上是看不见的，玩家不知道往哪儿按
            ApplyGlow();
        }

        // ---------- 搭场景：一个大世界 ----------

        private void BuildWorld()
        {
            var world = DemoFactory.Empty("World", transform, Vector3.zero);

            DemoFactory.Ground("Ground", world.transform, 90f, new Color(0.19f, 0.2f, 0.23f), 0);

            // 中央高塔 + 两侧拱门：两台相机朝向不同，看到的地标也不一样，
            // 这样「两半是同一张地图的不同角度」一眼就能看出来
            DemoFactory.Box("Tower", world.transform, new Vector3(0f, 7f, 16f),
                new Vector3(5f, 14f, 5f), new Color(0.45f, 0.42f, 0.38f), 0);
            DemoFactory.Box("Tower Top", world.transform, new Vector3(0f, 15f, 16f),
                new Vector3(7f, 1.2f, 7f), new Color(0.75f, 0.45f, 0.2f), 0);

            for (var i = 0; i < 7; i++)
            {
                var x = -21f + i * 7f;
                if (Mathf.Abs(x) < 3f)
                    continue;                       // 塔的位置留空

                var h = 3f + (i % 3) * 1.4f;
                DemoFactory.Box("Gate L" + i, world.transform, new Vector3(x, h * 0.5f, 16f),
                    new Vector3(1.6f, h, 1.6f), new Color(0.34f, 0.36f, 0.4f), 0);
                DemoFactory.Box("Gate R" + i, world.transform, new Vector3(x, 1.2f, -8f),
                    new Vector3(1.6f, 2.4f, 1.6f), new Color(0.28f, 0.3f, 0.34f), 0);
            }

            // 散落的箱子：拖角色移动时，前景视差很明显
            for (var i = 0; i < 8; i++)
            {
                var angle = i * 45f * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(angle) * 14f, 0.7f, Mathf.Cos(angle) * 9f + 3f);
                DemoFactory.Box("Crate " + i, world.transform, p, Vector3.one * 1.4f,
                    new Color(0.52f, 0.36f, 0.22f), 0);
            }
        }

        private Transform BuildPlayer(string name, Vector3 position, Color color)
        {
            var player = DemoFactory.Box(name, transform, position, Vector3.one * 1.8f, color, 0);
            return player.transform;
        }

        /// <summary>
        /// 建一侧 rig：底层 <c>Camera</c> 出图 + <c>vcam</c> 跟随自己的角色。
        /// <para>两台 rig <b>各带一个 Brain</b>：框架的分屏调度器会给第 2 份 rig 自动分配
        /// <c>OutputChannels.Channel01</c>，Brain 只认与自己 <c>ChannelMask</c> 相交的机位，
        /// 所以两个 Brain 各驱动各的相机、不会互抢（两个 Brain 抢同一台机位 ⇒ 两半画面一模一样且不报错）。</para>
        /// </summary>
        private CinemachineRigBinder BuildRig(string name, Transform target, int depth,
            out Camera camera, bool withListener)
        {
            var rigRoot = new GameObject(name);
            rigRoot.transform.SetParent(transform, false);

            // 机位与 Camera 必须平级：机位挂到 Camera 底下，Brain 每帧写回 Camera 位姿会经
            // 父子关系反喂给机位，跟随类的机会发飘（固定机位尤其致命，见 57/58 号）
            camera = DemoFactory.CreateCamera(name + " Camera", rigRoot.transform,
                new Vector3(0f, 9f, -14f), Quaternion.Euler(28f, 0f, 0f),
                k_CullingMask, new Color(0.06f, 0.07f, 0.09f), depth, withListener);

            DemoFactory.FollowCam(name + " Follow Cam", rigRoot.transform, target,
                new Vector3(0f, 9f, -14f), lookHeight: 1.2f);

            // 先建机位、再挂 binder：binder 在 Awake 里一次性扫描机位并缓存
            var binder = rigRoot.AddComponent<CinemachineRigBinder>();
            binder.SetTargets(target, target);      // 接缝取景启用后会改写成「跟随角色 / 看向瞄准锚点」

            return binder;
        }

        // ---------- Game 视图里的鼠标拖拽 ----------

        /// <summary>
        /// 用 <see cref="Event.current"/> 而不是 <c>Input.GetMouseButton*</c>：
        /// 后者在「Player Settings → Active Input Handling = Input System Package」的工程里会直接抛
        /// <c>InvalidOperationException</c>。IMGUI 事件两条输入路径都通，样例不该逼使用方改设置。
        /// </summary>
        private void OnGUI()
        {
            if (m_View == null)
                return;                             // Awake 中途失败时不画面板（也别抛空引用）

            HandleMouse();
            DrawPanel();
        }

        private void HandleMouse()
        {
            if (m_View == null || m_CameraA == null || m_CameraB == null)
                return;

            var e = Event.current;

            // IMGUI 的鼠标原点在左上，视口坐标原点在左下
            var gui = e.mousePosition;
            if (k_PanelRect.Contains(gui))
            {
                if (e.type == EventType.MouseUp)
                {
                    m_Drag = DragMode.None;
                    m_OrbitSide = -1;
                }
                return;                             // 面板区域交给按钮
            }

            var vp = new Vector2(gui.x / Screen.width, 1f - gui.y / Screen.height);

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    m_Drag = Pick(vp, gui);
                    break;

                // 右键：转鼠标所在那一侧的视角。合屏后两侧是同一景，一起转
                case EventType.MouseDown when e.button == 1:
                    m_OrbitSide = PickSide(vp);
                    e.Use();                        // 吃掉事件，否则 Game 视图会弹右键菜单
                    break;

                // MouseDrag 事件本身不带可靠的 button 字段，靠上面的 m_OrbitSide 判断
                case EventType.MouseDrag when m_OrbitSide >= 0:
                    Orbit(m_OrbitSide, e.delta);
                    e.Use();
                    break;

                case EventType.MouseDrag when m_Drag != DragMode.None:
                    ApplyDrag(vp);
                    break;

                case EventType.MouseUp when e.button == 1:
                    m_OrbitSide = -1;
                    e.Use();
                    break;

                case EventType.MouseUp:
                    m_Drag = DragMode.None;
                    m_OrbitSide = -1;               // 兜底：在窗口外松手时收不到右键的 MouseUp
                    break;
            }
        }

        /// <summary>鼠标落在接缝的哪一侧（0 = A，1 = B）。接缝的有符号距离为负就是 A 侧。</summary>
        private int PickSide(Vector2 vp) => m_View.CurrentSeam.SignedDistance(vp) < 0f ? 0 : 1;

        /// <summary>
        /// 右键转视角。**合屏时两侧一起转**：并拢后本来就是同一景，
        /// 只转一侧会变成「左右两半视角不同」，等于把刚合好的画面又劈开。
        /// </summary>
        private void Orbit(int side, Vector2 delta)
        {
            var dYaw = delta.x * k_OrbitYawSpeed;       // 往右拖 → 视点往右转
            var dPitch = delta.y * k_OrbitPitchSpeed;   // 往下拖 → 俯视更狠（相机抬高）

            if (m_View.MergeBlend > 0.5f)
            {
                Turn(0, dYaw, dPitch);
                Turn(1, dYaw, dPitch);
                return;
            }

            Turn(side, dYaw, dPitch);
        }

        private void Turn(int side, float dYaw, float dPitch)
        {
            m_Yaw[side] += dYaw;
            m_Pitch[side] = Mathf.Clamp(m_Pitch[side] + dPitch, k_PitchMin, k_PitchMax);
            ApplyOrbit(side);
        }

        /// <summary>
        /// 把「环绕角」换成跟随偏移交给框架：绕世界 Y 转 yaw、绕世界 X 抬 pitch，
        /// 都是叠加在建 rig 时的基准偏移上，所以转一圈回来还是原来的距离和俯角。
        /// </summary>
        private void ApplyOrbit(int side)
        {
            var rot = Quaternion.AngleAxis(m_Yaw[side], Vector3.up)
                      * Quaternion.AngleAxis(m_Pitch[side], Vector3.right);
            m_View.SetOrbit(side, rot * m_OrbitBase);
        }

        private void ResetOrbit()
        {
            for (var i = 0; i < 2; i++)
            {
                m_Yaw[i] = 0f;
                m_Pitch[i] = 0f;
                ApplyOrbit(i);
            }
        }

        /// <summary>判断这一下点在了接缝上还是某个角色上。</summary>
        private DragMode Pick(Vector2 vp, Vector2 gui)
        {
            var seam = m_View.CurrentSeam;
            seam.GetFrame(out _, out var normal);

            // 法向单位向量在像素空间会被宽高比拉伸，先换到像素再量距离
            var normalPx = new Vector2(normal.x * Screen.width, normal.y * Screen.height);
            var seamPx = Mathf.Abs(seam.SignedDistance(vp)) * normalPx.magnitude;

            var toA = DistancePx(m_PlayerA, m_CameraA, gui);
            var toB = DistancePx(m_PlayerB, m_CameraB, gui);

            if (seamPx < k_SeamGrabPx)
                return DragMode.Seam;
            if (toA < k_PlayerGrabPx && toA <= toB)
                return DragMode.PlayerA;
            if (toB < k_PlayerGrabPx)
                return DragMode.PlayerB;

            return DragMode.None;
        }

        /// <summary>
        /// 角色投影到屏幕的距离。**每侧用自己的相机**：合成模式下两台相机都渲整屏，
        /// 屏幕坐标与各自 RT 的归一化坐标一一对应，所以这里的投影就是玩家看见的位置。
        /// </summary>
        private static float DistancePx(Transform target, Camera camera, Vector2 gui)
        {
            if (target == null || camera == null)
                return float.MaxValue;

            var sp = camera.WorldToScreenPoint(target.position);
            if (sp.z < 0f)
                return float.MaxValue;              // 在相机背后

            // WorldToScreenPoint 原点在左下，GUI 在左上
            var px = new Vector2(sp.x, Screen.height - sp.y);
            return Vector2.Distance(px, gui);
        }

        private void ApplyDrag(Vector2 vp)
        {
            switch (m_Drag)
            {
                case DragMode.Seam:
                    DragSeam(vp);
                    break;
                case DragMode.PlayerA:
                    DragPlayer(m_PlayerA, m_CameraA, vp);
                    break;
                case DragMode.PlayerB:
                    DragPlayer(m_PlayerB, m_CameraB, vp);
                    break;
            }
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

        /// <summary>把角色拖到「鼠标射线 × 它所在水平面」的交点上（相机有俯角，不能直接用屏幕坐标当世界坐标）。</summary>
        private static void DragPlayer(Transform target, Camera camera, Vector2 vp)
        {
            if (target == null || camera == null)
                return;

            var ray = camera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
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

            GUILayout.Label("① 同一个大场景 · 左右各跟一个角色");
            GUILayout.Label("左键按接缝拖 → 拖分屏线；左键按方块拖 → 拖角色");
            GUILayout.Label("右键按住拖 → 转鼠标那一侧的视角（合屏后两侧一起转）");
            GUILayout.Label("当前：" + m_View.Style + (m_View.IsSeparated ? "" : "（已合并成一屏）"));
            GUILayout.Space(6f);

            // 分屏 / 合屏：合屏是「两台相机收拢到两人中点」——两人并拢成一景，
            // 不是把某一侧挤出屏幕。要「溶化保留某一侧」用 Combine(时长, 侧)。
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("分屏 Split"))
                m_View.Separate(m_View.Style);
            if (GUILayout.Button("合屏 Merge"))
                m_View.CombineAroundPlayers();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            if (GUILayout.Button("竖着切 Vertical"))
                m_View.Separate(SplitStyle.Vertical);
            if (GUILayout.Button("横着切 Horizontal"))
                m_View.Separate(SplitStyle.Horizontal);
            if (GUILayout.Button("斜着切 Diagonal"))
                m_View.Separate(SplitStyle.Diagonal);

            GUILayout.Space(6f);
            GUILayout.Label("接缝视觉");

            if (GUILayout.Button(m_Glow ? "接缝光带：开（点一下关）" : "接缝光带：关（点一下开）"))
            {
                m_Glow = !m_Glow;
                ApplyGlow();
            }

            GUILayout.Space(6f);

            if (GUILayout.Button("两个角色回原位"))
            {
                m_PlayerA.position = k_PlayerAStart;
                m_PlayerB.position = k_PlayerBStart;
            }

            if (GUILayout.Button("两侧视角复位"))
                ResetOrbit();

            GUILayout.EndArea();
        }

        private void ApplyGlow()
        {
            if (m_View == null)
                return;

            if (m_Glow)
                m_View.SetGlow(0.022f, new Color(1f, 0.86f, 0.35f, 0.9f), 0.55f);
            else
                m_View.ClearGlow();
        }
    }
}
