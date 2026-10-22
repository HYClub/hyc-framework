// ============================================================
// HYC Framework - 样例 · 双屏演示的搭场景小工具
// 文件: Samples~/HYCSimple/Cinemachine/DemoFactory.cs
// 说明: 两个双屏 demo 共用。全部用 Unity 内置 primitive 搭，
//       材质从 primitive 自带材质克隆（它已经适配项目当前渲染管线），
//       所以不 hardcode "Standard" / "Universal Render Pipeline/Lit"。
//       相机一律走框架推荐的结构：底层 Camera 只负责出图，
//       机位是 CinemachineCamera（跟随机位挂 Follow + RotationComposer）。
// ============================================================

using Unity.Cinemachine;
using UnityEngine;

namespace HYC.Simple.DualView
{
    internal static class DemoFactory
    {
        /// <summary>A 侧内容层。样例直接用层号，避免要求使用方改 Tags and Layers。</summary>
        public const int LayerA = 28;

        /// <summary>B 侧内容层。</summary>
        public const int LayerB = 29;

        /// <summary>两侧共享内容层（Default）。</summary>
        public const int LayerShared = 0;

        /// <summary>样例统一视场角。真实项目按机位类型给（见 framework 的 RigKinds 配方）。</summary>
        public const float DefaultFov = 55f;

        public static GameObject Empty(string name, Transform parent, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go;
        }

        public static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Color color, int layer)
        {
            return Primitive(PrimitiveType.Cube, name, parent, position, size, color, layer);
        }

        public static GameObject Ball(string name, Transform parent, Vector3 position, Vector3 size, Color color, int layer)
        {
            return Primitive(PrimitiveType.Sphere, name, parent, position, size, color, layer);
        }

        /// <summary>地面。Plane primitive 原始尺寸是 10×10。</summary>
        public static GameObject Ground(string name, Transform parent, float size, Color color, int layer)
        {
            return Primitive(PrimitiveType.Plane, name, parent, Vector3.zero,
                new Vector3(size * 0.1f, 1f, size * 0.1f), color, layer);
        }

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent,
            Vector3 position, Vector3 size, Color color, int layer)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.layer = layer;

            // 样例不做物理；而且双变体的子树里若有碰撞体会被 CinemachineDualVariant 告警。
            // 必须用 DestroyImmediate：Destroy 要等到帧末，而同一帧里就会给变体设层并触发那次检查
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);

            Paint(go, color);
            return go;
        }

        /// <summary>改颜色：克隆自带材质后按管线写 _BaseColor / _Color，两个都写也不冲突。</summary>
        public static void Paint(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null || renderer.sharedMaterial == null)
                return;

            var material = new Material(renderer.sharedMaterial) { name = go.name + " (Demo)" };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            renderer.sharedMaterial = material;
        }

        public static Camera CreateCamera(string name, Transform parent, Vector3 position, Quaternion rotation,
            int cullingMask, Color background, int depth, bool addListener, bool addBrain = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;

            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.cullingMask = cullingMask;
            camera.fieldOfView = DefaultFov;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 400f;
            camera.depth = depth;

            if (addListener)
                go.AddComponent<AudioListener>();

            // Brain 负责把机位算出来的位姿 / 镜头参数塞给这台底层 Camera。
            // 没有机位要驱动时才传 false（镜像侧），否则两个 Brain 会抢同一台相机。
            if (addBrain)
            {
                var brain = go.AddComponent<CinemachineBrain>();
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.EaseInOut, 0.35f);
            }

            return camera;
        }

        /// <summary>
        /// 跟随机位：Body = <see cref="CinemachineFollow"/>（目标 + 固定偏移），
        /// Aim = <see cref="CinemachineRotationComposer"/>（始终看向目标）。
        /// <para>偏移用世界轴理解即可（样例里的方块从不旋转，各绑定模式结果一致）。</para>
        /// </summary>
        public static CinemachineCamera FollowCam(string name, Transform parent, Transform target,
            Vector3 offset, float lookHeight, int priority = 20)
        {
            var vcam = CreateVcam(name, parent, Vector3.zero, Quaternion.identity, DefaultFov, priority);
            vcam.Follow = target;
            vcam.LookAt = target;

            var follow = vcam.gameObject.AddComponent<CinemachineFollow>();
            follow.FollowOffset = offset;

            // 阻尼归零：拖方块时相机立刻到位，示例里看得清楚（默认 1 秒会有明显拖影）
            follow.TrackerSettings.PositionDamping = Vector3.zero;
            follow.TrackerSettings.RotationDamping = Vector3.zero;

            var composer = vcam.gameObject.AddComponent<CinemachineRotationComposer>();
            composer.TargetOffset = new Vector3(0f, lookHeight, 0f);
            composer.Damping = Vector2.zero;

            return vcam;
        }

        /// <summary>
        /// 固定机位：<b>不挂 Body / Aim</b> —— 相机就停在这台机位的位姿上
        /// （CM 每帧把机位的 Transform 当作 CameraState 初值，没有管线组件就原样输出）。
        /// </summary>
        public static CinemachineCamera FixedCam(string name, Transform parent,
            Vector3 position, Quaternion rotation, int priority = 20)
        {
            return CreateVcam(name, parent, position, rotation, DefaultFov, priority);
        }

        private static CinemachineCamera CreateVcam(string name, Transform parent,
            Vector3 position, Quaternion rotation, float fov, int priority)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;

            var vcam = go.AddComponent<CinemachineCamera>();

            // 以默认镜头为底只改 FOV：其余（近远裁剪 / 正交等）跟项目默认一致
            var lens = vcam.Lens;
            lens.FieldOfView = fov;
            vcam.Lens = lens;

            // PrioritySettings 有 int 隐式转换，赋值顺带把 Enabled 打开
            // —— 只写 Value 不写 Enabled 的机位会被 CM 当成优先级 0 忽略掉
            vcam.Priority = priority;
            return vcam;
        }

        public static void DirectionalLight(Transform parent, Vector3 euler, Color color)
        {
            var go = new GameObject("Directional Light");
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(euler);

            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
        }
    }
}
