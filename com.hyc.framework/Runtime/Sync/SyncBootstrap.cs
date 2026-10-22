using System;
using HYC.Framework.Dots;
using Unity.Entities;
using UnityEngine;

namespace HYC.Framework.Sync
{
    /// <summary>
    /// 同步层装配入口：建单例、创建 Driver 入 A0 组、绑定传输、注册已存在的输入汇、校验游戏侧组特性。
    /// 在游戏 bootstrap 的 RegisterSystems / installer 内调用（须早于首次世界 Update，
    /// 保证 A0 组排序时 Driver 与输入系统的 [UpdateBefore] 约束生效）。
    /// 同时创建 <see cref="SyncSessionSystem"/>（链路监控，UpdateAfter Driver）。
    /// </summary>
    public static class SyncBootstrap
    {
        private static readonly Type InputSystemBase = typeof(LockstepInputSystem<>);
        private static readonly Type LogicSystemBase = typeof(LockstepLogicSystem);

        public static void Install(World world, ITransport transport)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (transport == null) throw new ArgumentNullException(nameof(transport));

            var em = world.EntityManager;

            var roleEntity = em.CreateEntity();
            em.AddComponentData(roleEntity, new SyncRoleSingleton { Role = transport.Role });
            switch (transport.Role)
            {
                case SyncRole.Host:
                    em.AddComponentData(roleEntity, new SyncRole_Host());
                    break;
                case SyncRole.Client:
                    em.AddComponentData(roleEntity, new SyncRole_Client());
                    break;
                default:
                    em.AddComponentData(roleEntity, new SyncRole_Observer());
                    break;
            }

            em.AddComponentData(em.CreateEntity(), new SyncTickSingleton());
            em.AddComponentData(em.CreateEntity(), new PlayerTableSingleton
            {
                LocalPlayerId = transport.LocalPlayerId,
                PlayerCount = transport.PeerCount,
            });

            var driver = world.GetOrCreateSystemManaged<LockstepDriver>();
            var a0 = world.GetOrCreateSystemManaged<UpdateGroup_A0>();
            a0.AddSystemToUpdateList(driver.SystemHandle);
            // 链路监控（UpdateAfter Driver）：断链/恢复抛事件供宿主弹 UI / 回主菜单
            var session = world.GetOrCreateSystemManaged<SyncSessionSystem>();
            a0.AddSystemToUpdateList(session.SystemHandle);
            a0.SortSystems();
            driver.Bind(transport);

            RegisterExistingSinks(world, driver);
            ValidateGameSystems(world);
        }

        private static void RegisterExistingSinks(World world, LockstepDriver driver)
        {
            foreach (var system in world.Systems)
                if (system is ILockstepInputSink sink)
                    driver.RegisterSink(sink);
        }

        /// <summary>
        /// 校验游戏侧同步系统自带组特性。ILPP 直读具体类型特性，泛型基类上的特性不会继承，
        /// 缺失表现为输入推进降速 / 逻辑脱离固定步长，故在装配期显式报错。
        /// </summary>
        private static void ValidateGameSystems(World world)
        {
            foreach (var system in world.Systems)
            {
                var type = system.GetType();
                if (IsSubclassOfRawGeneric(type, InputSystemBase))
                {
                    if (!type.IsDefined(typeof(UpdateInGroupAttribute), false)
                        || !type.IsDefined(typeof(UpdateBeforeAttribute), false))
                    {
                        Debug.LogError($"[HYC.Sync] {type.Name} 缺少组特性：须声明 [UpdateInGroup(typeof(UpdateGroup_A0))] 与 [UpdateBefore(typeof(LockstepDriver))]（特性不继承，见 61 号文档）。");
                    }
                }
                else if (LogicSystemBase.IsAssignableFrom(type))
                {
                    if (!type.IsDefined(typeof(UpdateInGroupAttribute), false))
                    {
                        Debug.LogError($"[HYC.Sync] {type.Name} 缺少组特性：须声明 [UpdateInGroup(typeof(UpdateGroup_A3))] 或自有组（特性不继承，见 61 号文档）。");
                    }
                }
            }
        }

        private static bool IsSubclassOfRawGeneric(Type type, Type rawGenericBase)
        {
            var baseType = type.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                var current = baseType.IsGenericType ? baseType.GetGenericTypeDefinition() : baseType;
                if (current == rawGenericBase) return true;
                baseType = baseType.BaseType;
            }
            return false;
        }
    }
}
