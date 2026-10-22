using System;
using HYC.Framework.Dots;
using Unity.Entities;

namespace HYC.Framework.Sync
{
    /// <summary>
    /// 链路会话监控（A0 组，UpdateAfter Driver）：轮询传输 <see cref="ITransport.IsConnected"/>，
    /// 断链时抛 <see cref="LinkLost"/>、恢复时抛 <see cref="LinkRestored"/>（仅边沿触发）。
    ///
    /// 用途：P1 断链后世界默认永久等齐（<see cref="LockstepDriver.MaxStallFrames"/>=0），
    /// 宿主订阅本事件弹「对方已断线」UI / 回主菜单；重链的实质处理（快照追赶）是 P2 范围，
    /// 当前只作通知，不做状态重建。
    ///
    /// 事件为实例事件：装配后由宿主经 <c>world.GetExistingSystemManaged&lt;SyncSessionSystem&gt;()</c>
    /// 取本系统订阅，退局 / 切场景时记得退订——系统实例随 World 销毁，故刻意不用静态事件
    /// （静态事件会跨 World 泄漏、重复触发）。
    /// </summary>
    [UpdateInGroup(typeof(UpdateGroup_A0))]
    [UpdateAfter(typeof(LockstepDriver))]
    [DisableAutoCreation]
    public partial class SyncSessionSystem : SystemBase
    {
        private LockstepDriver _driver;
        private bool _wasConnected;
        private bool _initialized;

        /// <summary>链路中断（「已连接 → 断开」边沿触发；初始即断开不触发）。</summary>
        public event Action LinkLost;

        /// <summary>链路恢复（「已断开 → 连接」边沿触发；初始即连通不触发）。</summary>
        public event Action LinkRestored;

        protected override void OnCreate()
        {
            RequireForUpdate<SyncTickSingleton>();
        }

        protected override void OnUpdate()
        {
            if (_driver == null)
            {
                _driver = World.GetExistingSystemManaged<LockstepDriver>();
                if (_driver == null) return; // 同步层未装配：静默不工作
            }

            bool connected = _driver.IsConnected;
            if (!_initialized)
            {
                // 首观测只记基线，不算边沿——初始即连通时不该误报「恢复」
                _wasConnected = connected;
                _initialized = true;
                return;
            }

            if (connected != _wasConnected)
            {
                _wasConnected = connected;
                if (connected)
                    LinkRestored?.Invoke();
                else
                    LinkLost?.Invoke();
            }
        }
    }
}
