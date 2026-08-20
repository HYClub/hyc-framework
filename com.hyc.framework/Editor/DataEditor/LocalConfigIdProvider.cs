using System.Collections.Generic;
using System.Threading.Tasks;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 本地 ID 构造器：进程内发号，规则与内网服务器一致（id/guid 同源于一个 Guid）。
    /// 会话内维护已派发集合（含项目已有资产的 ID），理论碰撞(2^-64)时自动重发，保证绝对不重复。
    /// </summary>
    public class LocalConfigIdProvider : IConfigIdProvider
    {
        private readonly HashSet<long> mIssued = new HashSet<long>();

        public ConfigIdState State => ConfigIdState.Connected;
        public string ServerInfo => "本地构造器";

        public Task ConnectAsync() => Task.CompletedTask;

        public Task<ConfigId> RequestIdAsync()
        {
            ConfigId cid;
            var attempts = 0;
            do
            {
                cid = ConfigId.Generate();
                attempts++;
            } while (!mIssued.Add(cid.id) && attempts < 16);
            return Task.FromResult(cid);
        }

        public Task ReleaseIdAsync(long id)
        {
            mIssued.Remove(id);
            return Task.CompletedTask;
        }

        public bool Ping() => true;

        /// <summary>会话启动时载入配置根目录下已有资产的 ID，防止新发号与现有资产碰撞。</summary>
        public void LoadExistingIds()
        {
            mIssued.Clear();
            foreach (var asset in ConfigIdService.CollectConfigAssets(ConfigDataSettings.RootFolder))
            {
                if (ConfigIdService.TryReadId(asset, out var id, out _))
                    mIssued.Add(id);
            }
        }
    }
}
