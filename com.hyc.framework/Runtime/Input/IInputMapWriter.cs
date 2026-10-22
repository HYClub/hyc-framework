using System.Collections.Generic;
using Unity.Entities;
using UnityEngine.InputSystem;

namespace HYC.Framework.Input
{
    /// <summary>
    /// 单个 action map 的「写入器」契约。生成的 XxxInputComponentWriter 实现它，把自己注册进
    /// <see cref="InputMapWriterRegistry"/>。框架驱动系统每帧遍历所有玩家 + 所有写入器，
    /// 把该 map 的当前输入写进玩家实体上的对应组件。
    /// </summary>
    public interface IInputMapWriter
    {
        /// <summary>对应 InputActionAsset 里的 action map 名（生成时写死）。</summary>
        string MapName { get; }

        /// <summary>把 map 的当前输入写进 player 实体上的组件。</summary>
        /// <param name="em">世界 EntityManager。</param>
        /// <param name="player">承载该 map 组件的玩家实体。</param>
        /// <param name="map">该玩家的 action map 实例（来自其 InputActionAsset 实例）。</param>
        /// <param name="prev">该玩家的上一帧代理值表（边沿检测用）。</summary>
        void Write(EntityManager em, Entity player, InputActionMap map, Dictionary<string, float> prev);
    }
}
