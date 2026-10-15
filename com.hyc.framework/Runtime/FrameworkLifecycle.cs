// ============================================================
// HYC Framework
// 文件: Runtime/FrameworkLifecycle.cs
// 说明: 进程级静态资源的统一收口 + World 销毁钩子
//
// 为什么需要这个文件：
//   框架的 Manager(BTManager / ConfigManager / LocalizationManager ...) 是
//   **进程级静态单例**，但它们持有的 native 容器(NativeHashMap / NativeArray /
//   BlobAssetReference) 有真正的生命周期。以前每个 Manager 各写一个 DisposeAll，
//   却**没有任何地方调用它们** —— 于是 Persistent 分配的内存一直泄漏到进程结束，
//   关闭 Domain Reload 时还会留下失效句柄。
//
//   这里提供唯一收口，并挂一个空系统自动在 World 销毁时触发：
//   - World.Dispose() 时 Unity 会销毁所有 system，FrameworkCleanupSystem.OnDestroy 随之执行；
//   - 进入 Play 前 SubsystemRegistration 也会走一遍，覆盖「关闭 Domain Reload」这条路。
//
// 单 World 假设：静态单例本就是全局的，销毁**任意** World 都会回收全局数据。
//   框架主流程只有一个 World，行为可预期；若宿主有多个 World 并希望互不影响，
//   请给本类型加 [DisableAutoCreation]，改由宿主在最后一个 World 销毁时手动调
//   FrameworkLifecycle.DisposeAll()。
// ============================================================

using HYC.Framework.BT;
using HYC.Framework.Config;
using HYC.Framework.Dots;
using HYC.Framework.Loc;
using Unity.Entities;
using UnityEngine;

namespace HYC.Framework
{
    /// <summary>
    /// 框架进程级静态资源的统一回收入口。幂等，可重复调用。
    /// </summary>
    public static class FrameworkLifecycle
    {
        /// <summary>
        /// 释放全部持有原生容器的静态表。
        /// </summary>
        /// <param name="reason">仅用于日志，便于定位是谁触发的（World 销毁 / 进入 Play 前 / 手动）。</param>
        public static void DisposeAll(string reason = null)
        {
            // BT：树 Blob + per-node 执行态 + 黑板 + 事件总线 + 节点注册表
            BTManager.DisposeAll();
            BTBlackboardRegistry.DisposeAll();
            BTEventBus.ClearAll();
            BTGameNodeRegistry.Clear();

            // 配置：ConfigBlobTable 实现 IDisposable，Clear() 内部会释放 blob 与索引
            ConfigManager.Clear();

            // 有意**不**清 LocalizationManager：它持有的是「本地化文本数据」而非原生资源，
            // 且已经自带「Reload 失败保留原数据」的保护。World 销毁后数据仍在，
            // 宿主重建 World 时可直接复用；真要重载请显式调 LocalizationManager.Reload()。
        }

        /// <summary>
        /// 引导期数据补加载入：在 <c>StreamingAssets.PreloadAsync()</c> 完成之后调用一次。
        ///
        /// <para>本地化的 <see cref="LocalizationBlobSystem"/> 挂了
        /// [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]，在 NeedsPreload 平台
        /// （WebGL / Android）上可能早于预取完成就触发而主动跳过；这里补一次，
        /// 保证本地化就绪，且<b>宿主不需要为平台写不同代码</b>。</para>
        ///
        /// 幂等：已加载过（IDs 非空）则不重复读。
        /// </summary>
        public static void EnsureBootDataLoaded()
        {
            if (LocalizationManager.IDs == null || LocalizationManager.IDs.Length == 0)
                LocalizationBlobSystem.Load();
        }

        /// <summary>
        /// 进入 Play 前（含关闭 Domain Reload 的情形）重置全部静态态。
        ///
        /// 关闭 Domain Reload 时 C# 静态字段会保留，但 Unity 已经回收了所有原生分配 ——
        /// 此时若还留着 <c>_initialized = true</c>，下次访问会拿到失效的 Native 容器句柄。
        /// 所以这里走一遍完整清理（各 DisposeAll 均已做成幂等）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnteringPlayMode()
        {
            DisposeAll("EnterPlayMode(SubsystemRegistration)");
        }
    }

    /// <summary>
    /// 空系统，唯一职责是在 World 销毁时回收框架静态资源。
    /// 放在 Root 组末尾，OnUpdate 为空（ISystem 是值类型，空转开销可忽略）。
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_Root), OrderLast = true)]
    public partial struct FrameworkCleanupSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
        }

        public void OnUpdate(ref SystemState state)
        {
        }

        public void OnDestroy(ref SystemState state)
        {
            FrameworkLifecycle.DisposeAll("WorldDestroyed");
        }
    }
}