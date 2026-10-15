using HYC.Framework.BT;
using NUnit.Framework;
using Unity.Entities;

namespace HYC.Framework.Tests
{
    /// <summary>
    /// <see cref="HYC.Framework.BT.BTManager"/> 的运行态释放契约。
    ///
    /// <para><b>背景（本轮修的 P0 泄漏）</b>：<c>BTManager._nodeStates</c> 为每个
    /// <c>(实体, 树ID)</c> 持有一份 <c>Allocator.Persistent</c> 的 NativeArray，
    /// 唯一释放路径原本是「树跑完（结果 != Running）→ <c>ClearNodeStates</c>」。
    /// 若实体在树 Running 期间被销毁，组件消失、Query 不再返回，
    /// 于是那份 NativeArray<b>永不释放</b>——纯托管引用计数看不到它，但它是真原生内存。</para>
    ///
    /// <para><b>修复方式</b>：新增 <c>ReleaseEntityRuntimeState</c> 作为合并入口，
    /// 由 <c>BTInterpreterSystem</c> 用「两份 NativeHashSet 追踪上一帧/本帧活跃实体」
    /// 在帧末兜底调用。</para>
    ///
    /// <para><b>这些用例守的是"调用不会崩、重复调用安全"</b>——
    /// 真正的泄漏守恒需要跑 World + 实体销毁流程，属于集成测试范畴。
    /// 能在 EditMode 里钉住的是：清理入口对"从未注册过的实体"必须安全，
    /// 否则帧末兜底会对每个消失的实体都调它，一次异常就把整帧打断。</para>
    /// </summary>
    public class BTManagerRuntimeStateTests
    {
        [Test]
        public void ReleaseEntityRuntimeState_对从未注册过的实体_安全不抛()
        {
            // 帧末兜底会对每个"上一帧活跃、本帧消失"的实体调用它。
            // 现实中必然出现"该实体从未注册任何树"的情况，必须静默通过。
            var neverRegistered = new Entity { Index = 999999, Version = 1 };
            Assert.DoesNotThrow(() => BTManager.ReleaseEntityRuntimeState(neverRegistered));
        }

        [Test]
        public void ReleaseEntityRuntimeState_重复调用_安全()
        {
            // 兜底逻辑下一帧可能再次命中同一实体（若活跃集合判断有偏差）
            var e = new Entity { Index = 123456, Version = 1 };
            Assert.DoesNotThrow(() =>
            {
                BTManager.ReleaseEntityRuntimeState(e);
                BTManager.ReleaseEntityRuntimeState(e);
                BTManager.ReleaseEntityRuntimeState(e);
            });
        }

        [Test]
        public void ReleaseEntityRuntimeState_对默认构造的Entity_安全()
        {
            // Entity.Null / 默认值：不该因Index=0 就崩
            Assert.DoesNotThrow(() => BTManager.ReleaseEntityRuntimeState(default));
            Assert.DoesNotThrow(() => BTManager.ReleaseEntityRuntimeState(Entity.Null));
        }

        [Test]
        public void ClearAllRuntimeStates_在无注册时_安全不抛()
        {
            // World 销毁时会调；若此时没有任何运行态却抛异常，会让退出流程崩掉
            Assert.DoesNotThrow(() => BTManager.ClearAllRuntimeStates());
        }

        [Test]
        public void DisposeAll_可重复调用而不抛()
        {
            // FrameworkLifecycle.DisposeAll 在两处钩子都会调（进 Play 前 + World 销毁时），
            // 所以"重复调用安全"是硬要求。
            Assert.DoesNotThrow(() =>
            {
                BTManager.DisposeAll();
                BTManager.DisposeAll();
            });
        }

        [Test]
        public void DisposeAll之后_仍可继续释放运行态()
        {
            // 兜底清理若在 DisposeAll 之后才跑（时序不确定），不能崩
            BTManager.DisposeAll();
            Assert.DoesNotThrow(() =>
                BTManager.ReleaseEntityRuntimeState(new Entity { Index = 42, Version = 1 }));
        }
    }
}