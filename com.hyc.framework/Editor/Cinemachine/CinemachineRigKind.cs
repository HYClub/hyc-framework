// ============================================================
// HYC Framework - Cinemachine 相机装配 · 机位类型配方
// 文件: Editor/Cinemachine/CinemachineRigKind.cs
// 说明: 机位类型（RigKind，枚举本体已下沉到 Runtime）→ CM 3.1 组件配方；
//       以及「更多 More」里可添加的管线/扩展组件目录。组件一律按类型名从
//       Unity.Cinemachine 程序集解析——CM 版本升级只改本文件的
//       清单，窗口与工厂逻辑零改动；类型不存在时优雅跳过并告警。
//       清单按 CM 3.1 非 deprecated 类型维护（CM 2 的
//       Transposer/Composer/POV/FreeLook 等在 CM 3 已移入 Deprecated）。
// ============================================================

using System;
using Unity.Cinemachine;

namespace HYC.Framework.Cinemachine.Editor
{
    /// <summary>单个机位类型的组件配方。</summary>
    internal readonly struct RigKindRecipe
    {
        public readonly RigKind Kind;
        public readonly string DisplayName;
        /// <summary>CM 组件类型名（Unity.Cinemachine 命名空间，无序敏感——先 Body 后 Aim）。</summary>
        public readonly string[] ComponentNames;

        public RigKindRecipe(RigKind kind, string displayName, string[] componentNames)
        {
            Kind = kind;
            DisplayName = displayName;
            ComponentNames = componentNames;
        }
    }

    /// <summary>
    /// 机位类型配方表（唯一真源）。新增机位类型 = 枚举加一项 + 本表加一行。
    /// </summary>
    internal static class RigKinds
    {
        public static readonly RigKindRecipe[] All =
        {
            new RigKindRecipe(RigKind.FreeLook,    "FreeLook 环绕",        new[] { "CinemachineOrbitalFollow", "CinemachineRotationComposer" }),
            new RigKindRecipe(RigKind.ThirdPerson, "ThirdPerson 第三人称", new[] { "CinemachineThirdPersonFollow", "CinemachineRotationComposer" }),
            new RigKindRecipe(RigKind.Framed,      "Framed 构图跟随",      new[] { "CinemachinePositionComposer", "CinemachineRotationComposer" }),
            new RigKindRecipe(RigKind.PanTilt,     "PanTilt 输入视角",     new[] { "CinemachinePanTilt" }),
            new RigKindRecipe(RigKind.Dolly,       "Dolly 轨道",           new[] { "CinemachineSplineDolly", "CinemachineRotationComposer" }),
        };

        /// <summary>按类型取配方；越界回落第一项。</summary>
        public static RigKindRecipe Get(RigKind kind)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Kind == kind)
                    return All[i];
            return All[0];
        }
    }

    /// <summary>
    /// CM 组件目录：供 Rig Editor「更多 More」里的 + 添加使用。
    /// <list type="bullet">
    /// <item><see cref="PipelineComponents"/>：管线组件（<see cref="CinemachineComponentBase"/> 子类，占 Body/Aim/Noise 阶段）。</item>
    /// <item><see cref="Extensions"/>：扩展组件（<see cref="CinemachineExtension"/> 子类；CinemachineInputAxisController 不是 Extension 但添加方式相同，一并收录）。</item>
    /// </list>
    /// </summary>
    internal static class CmComponentCatalog
    {
        /// <summary>可添加的管线组件类型名（CM 3.1 非 deprecated 全集）。</summary>
        public static readonly string[] PipelineComponents =
        {
            "CinemachineFollow",
            "CinemachineOrbitalFollow",
            "CinemachineThirdPersonFollow",
            "CinemachineHardLockToTarget",
            "CinemachinePositionComposer",
            "CinemachineRotationComposer",
            "CinemachineHardLookAt",
            "CinemachineRotateWithFollowTarget",
            "CinemachinePanTilt",
            "CinemachineSplineDolly",
            "CinemachineBasicMultiChannelPerlin",
        };

        /// <summary>可添加的扩展组件类型名（CM 3.1 非 deprecated 全集 + 输入轴控制器）。</summary>
        public static readonly string[] Extensions =
        {
            "CinemachineDeoccluder",
            "CinemachineDecollider",
            "CinemachineConfiner2D",
            "CinemachineConfiner3D",
            "CinemachinePixelPerfect",
            "CinemachineGroupFraming",
            "CinemachineCameraOffset",
            "CinemachineFollowZoom",
            "CinemachineRecomposer",
            "CinemachineStoryboard",
            "CinemachineVolumeSettings",
            "CinemachineImpulseListener",
            "CinemachineShotQualityEvaluator",
            "CinemachineInputAxisController",
        };

        private static readonly System.Reflection.Assembly s_CmAssembly = typeof(CinemachineCamera).Assembly;

        /// <summary>
        /// 按类型名从 Unity.Cinemachine 程序集解析类型。
        /// 版本漂移（清单里的类型被移除）或条件编译未包含时返回 null，调用方跳过并告警。
        /// </summary>
        public static Type Find(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;
            return s_CmAssembly.GetType("Unity.Cinemachine." + typeName, false);
        }
    }
}
