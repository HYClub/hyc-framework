using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine.InputSystem;

namespace HYC.Framework.Input
{
    /// <summary>一名玩家在输入系统侧的槽位：其 InputActionAsset 实例 + 上一帧代理值表。</summary>
    public struct InputPlayerSlot
    {
        public InputActionAsset asset;
        public Dictionary<string, float> prev;
    }

    /// <summary>
    /// 玩家注册表：宿主建好玩家实体、挂上各 XxxInputComponent 后，调用
    /// <see cref="Register"/> 把「实体 ↔ 该玩家 InputActionAsset 实例」登记进来。
    /// 支持运行时动态加入/退出（本地多人：先 1 人，按键再加 1 人）。
    /// 设备分配（键盘/手柄 A/B）由游戏层用 InputUser 配对，框架只认「实体 + asset 实例」，设备无关。
    /// </summary>
    public static class InputPlayerRegistry
    {
        /// <summary>
        /// 每玩家、每个组件类型桶里存的一组回调。闭包内已用 <typeparamref name="TComp"/> 从
        /// EntityManager 读出最新组件值再喂给注册方，调用方只管拿值，无需自己 Query。
        /// </summary>
        static readonly Dictionary<Entity, Dictionary<Type, List<Action<EntityManager, Entity>>>> Handlers
            = new Dictionary<Entity, Dictionary<Type, List<Action<EntityManager, Entity>>>>();

        static readonly Dictionary<Entity, InputPlayerSlot> Players = new Dictionary<Entity, InputPlayerSlot>();

        public static void Register(Entity player, InputActionAsset asset)
        {
            if (asset == null) return;
            Players[player] = new InputPlayerSlot { asset = asset, prev = new Dictionary<string, float>() };
        }

        public static void Unregister(Entity player)
        {
            if (Players.TryGetValue(player, out var slot))
            {
                // 关掉该玩家所有 action map，停止监听
                if (slot.asset != null)
                    foreach (var m in slot.asset.actionMaps)
                        m.Disable();
                Players.Remove(player);
            }
            // 一并清掉该玩家注册的回调，避免悬挂引用
            Handlers.Remove(player);
        }

        public static IReadOnlyDictionary<Entity, InputPlayerSlot> Slots => Players;

        /// <summary>
        /// 注册一个「组件值回调」：每当驱动系统为本玩家写好 <typeparamref name="TComp"/> 后，
        /// 立即用最新组件值回调 <paramref name="handler"/>（<see cref="InputComponentDriverSystem"/> 挂在 A0 组，随每 tick 执行）。
        /// <para>典型用法：UI 层不必自己写 SystemBase 每帧 Query，直接注册 lambda 即可收到输入脉冲。</para>
        /// </summary>
        /// <typeparam name="TComp">玩家实体上的某输入组件（如 UIInputComponent / SenseInputComponent）。必须是 unmanaged 的 IComponentData。</typeparam>
        /// <param name="player">要监听的玩家实体。</param>
        /// <param name="handler">收到最新组件值的回调，直接读 <c>justPressed</c> / <c>active</c> 等即可。</param>
        public static void RegisterHandler<TComp>(Entity player, Action<TComp> handler)
            where TComp : unmanaged, IComponentData
        {
            if (handler == null) return;
            if (!Handlers.TryGetValue(player, out var byType))
            {
                byType = new Dictionary<Type, List<Action<EntityManager, Entity>>>();
                Handlers[player] = byType;
            }
            if (!byType.TryGetValue(typeof(TComp), out var list))
            {
                list = new List<Action<EntityManager, Entity>>();
                byType[typeof(TComp)] = list;
            }
            // 闭包捕获 TComp：调用时由 EntityManager 读最新组件值再喂给 handler
            list.Add((em, p) => handler(em.GetComponentData<TComp>(p)));
        }

        /// <summary>
        /// 注销某个玩家、某组件类型的全部回调。若想彻底清掉该玩家所有回调，改用 <see cref="Unregister"/>。
        /// </summary>
        /// <typeparam name="TComp">先前注册时使用的组件类型。</typeparam>
        /// <param name="player">先前注册时使用的玩家实体。</param>
        public static void UnregisterHandler<TComp>(Entity player)
            where TComp : unmanaged, IComponentData
        {
            if (Handlers.TryGetValue(player, out var byType))
                byType.Remove(typeof(TComp));
        }

        /// <summary>
        /// 由 <see cref="InputComponentDriverSystem"/> 在写完本玩家全部输入组件后调用：
        /// 触发该玩家所有已注册回调，回调内读取的是本 tick 刚写好的组件值。
        /// </summary>
        public static void InvokeHandlers(EntityManager em, Entity player)
        {
            if (!Handlers.TryGetValue(player, out var byType)) return;
            foreach (var list in byType.Values)
                for (int i = 0; i < list.Count; i++)
                    list[i](em, player);
        }
    }
}
