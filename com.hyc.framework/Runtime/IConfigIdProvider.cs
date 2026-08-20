using System;
using System.Threading.Tasks;

namespace HYC.Framework.Config
{
    /// <summary>ID 服务器连接状态。</summary>
    public enum ConfigIdState
    {
        Disconnected, // 红
        Connecting,   // 黄
        Connected,    // 绿
    }

    /// <summary>一组配置标识：id 全局唯一（long），guid 标准 Guid 字符串。</summary>
    public struct ConfigId
    {
        public long id;
        public string guid;

        /// <summary>生成规则：id 取 Guid 前 8 字节转 long，guid 为标准 Guid 字符串。本地/内网统一。</summary>
        public static ConfigId Generate()
        {
            // id==0 视为"未分配"，极小概率碰撞时换一个 Guid
            for (var i = 0; i < 4; i++)
            {
                var g = Guid.NewGuid();
                var bytes = g.ToByteArray();
                var id = BitConverter.ToInt64(bytes, 0);
                if (id != 0)
                    return new ConfigId { id = id, guid = g.ToString() };
            }
            return new ConfigId { id = DateTime.UtcNow.Ticks, guid = Guid.NewGuid().ToString() };
        }
    }

    /// <summary>
    /// ID 构造器抽象：本地或内网实现，统一提供 {id, guid}。
    /// 本地=进程内生成；内网=连局域网服务器发号（保证多机唯一）。
    /// </summary>
    public interface IConfigIdProvider
    {
        ConfigIdState State { get; }
        string ServerInfo { get; }

        /// <summary>连接/初始化（本地无操作，内网建立连接）。</summary>
        Task ConnectAsync();

        /// <summary>请求一组新 ID。</summary>
        Task<ConfigId> RequestIdAsync();

        /// <summary>释放 ID（删除资产时归还，本地记录回收，内网通知服务器）。</summary>
        Task ReleaseIdAsync(long id);

        /// <summary>检测连接（内网用），返回是否可用。</summary>
        bool Ping();
    }
}
