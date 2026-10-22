using Unity.Entities;

namespace HYC.Framework.Sync
{
    /// <summary>
    /// 同步角色。Host = 房主/权威端；Client = 客户端；Observer = 观战（P2 随快照加入）。
    /// </summary>
    public enum SyncRole : byte
    {
        Host = 0,
        Client = 1,
        Observer = 2,
    }

    /// <summary>本端在同步会话中的角色（单例，由 SyncBootstrap.Install 写入）。</summary>
    public struct SyncRoleSingleton : IComponentData
    {
        public SyncRole Role;
    }

    /// <summary>Host 角色 tag（同单例实体，供系统按角色 RequireForUpdate）。</summary>
    public struct SyncRole_Host : IComponentData { }

    /// <summary>Client 角色 tag。</summary>
    public struct SyncRole_Client : IComponentData { }

    /// <summary>Observer 角色 tag。</summary>
    public struct SyncRole_Observer : IComponentData { }
}
