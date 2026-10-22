// ============================================================
// HYC Framework
// 文件: Runtime/BootstrapGate.cs
// 说明: 「仅场景运行」开关的运行时收口。
//
// 为什么需要这个文件：
//   编辑器工具栏的「仅场景运行」按钮要跳过整套框架引导（宿主 bootstrap 返回 false，
//   让 Entities 退回自动建默认世界），但 Runtime 程序集**不能引用 UnityEditor**
//   （asmdef 没挂 UnityEditor），所以这里只持有一个可写静态标志，真正的值由 Editor 侧的
//   PlaySceneToolbarButton 在 [InitializeOnLoad] 读 SessionState、以及点击按钮时直接写入。
//
// 时序保证：
//   - 开域重载开启时：点击设 SessionState → isPlaying → domain reload → [InitializeOnLoad]
//     把 SessionState 读进 ShouldSkip → 宿主 bootstrap 进 Play → 框架 Initialize 读到 true 即跳过。
//   - 关闭域重载时：点击直接置 ShouldSkip=true（静态字段不丢），同样生效。
//   两条路径都覆盖，不依赖宿主代码。
//
// 用法：宿主 bootstrap 调 FrameworkBootstrap.Initialize / FrameworkModeBootstrap.Initialize 时，
//   框架已在各自 Initialize 顶部查此标志；为真则直接 return false。宿主只要把框架返回值透传给
//   Entities（return frameworkBootstrap.Initialize(name)）即可，零改动。
// ============================================================

using System;

namespace HYC.Framework.Runtime
{
    /// <summary>
    /// 「仅场景运行」闸门：为真时框架两个引导入口跳过 World 创建与系统安装，返回 false，
    /// 使 Entities 退回自动建默认世界（Unity.* 默认系统），从而只跑当前场景。
    /// 由 Editor 侧（PlaySceneToolbarButton）写入，宿主无需改动。
    /// </summary>
    public static class BootstrapGate
    {
        /// <summary>
        /// 为真 = 跳过框架引导。Editor 侧在点击按钮 / 域重载后写入。
        /// </summary>
        public static bool ShouldSkip { get; set; }
    }
}
