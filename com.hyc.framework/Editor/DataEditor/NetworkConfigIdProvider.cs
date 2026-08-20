using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 局域网 ID 构造器（TCP 客户端）。服务器是独立项目（发号/回收/多机唯一），
    /// Unity 侧只做客户端。文本行协议（UTF-8，\n 结尾，错误统一 ERR &lt;msg&gt;\n）：
    ///   REQ\n          -> OK &lt;id&gt; &lt;guid&gt;\n    请求新 ID
    ///   REL &lt;id&gt;\n     -> OK\n                释放/回收 ID
    ///   PING\n         -> PONG\n               存活检测
    /// </summary>
    public class NetworkConfigIdProvider : IConfigIdProvider
    {
        private const int ConnectTimeoutMs = 3000;
        private const int IoTimeoutMs = 2000;

        private readonly string mHost;
        private readonly int mPort;
        private readonly object mSync = new object();
        private TcpClient mClient;
        private Stream mStream;
        private ConfigIdState mState = ConfigIdState.Disconnected;

        public NetworkConfigIdProvider(string host, int port)
        {
            mHost = host;
            mPort = port;
        }

        public ConfigIdState State
        {
            get { lock (mSync) return mState; }
        }

        public string ServerInfo => mHost + ":" + mPort;

        public async Task ConnectAsync()
        {
            SetState(ConfigIdState.Connecting);
            try
            {
                var client = new TcpClient();
                var connectTask = client.ConnectAsync(mHost, mPort);
                if (await Task.WhenAny(connectTask, Task.Delay(ConnectTimeoutMs)).ConfigureAwait(false) != connectTask)
                {
                    client.Close();
                    SetState(ConfigIdState.Disconnected);
                    return;
                }
                await connectTask.ConfigureAwait(false);
                SetClient(client);
                var pong = await SendLineAsync("PING").ConfigureAwait(false);
                SetState(pong == "PONG" ? ConfigIdState.Connected : ConfigIdState.Disconnected);
            }
            catch
            {
                CloseClient();
            }
        }

        public async Task<ConfigId> RequestIdAsync()
        {
            if (State != ConfigIdState.Connected)
                await ConnectAsync().ConfigureAwait(false);
            var reply = await SendLineAsync("REQ").ConfigureAwait(false);
            if (string.IsNullOrEmpty(reply))
                throw new InvalidOperationException("ID 服务器无响应");
            var parts = reply.Split(' ');
            if (parts.Length != 3 || parts[0] != "OK")
                throw new InvalidOperationException("ID 服务器错误: " + reply);
            return new ConfigId
            {
                id = long.Parse(parts[1]),
                guid = parts[2],
            };
        }

        public async Task ReleaseIdAsync(long id)
        {
            if (State != ConfigIdState.Connected)
                return;
            await SendLineAsync("REL " + id).ConfigureAwait(false);
        }

        public async Task<bool> PingAsync()
        {
            try
            {
                if (State != ConfigIdState.Connected)
                {
                    await ConnectAsync().ConfigureAwait(false);
                    return State == ConfigIdState.Connected;
                }
                var pong = await SendLineAsync("PING").ConfigureAwait(false);
                if (pong != "PONG")
                    CloseClient();
                return pong == "PONG";
            }
            catch
            {
                CloseClient();
                return false;
            }
        }

        public bool Ping() => PingAsync().GetAwaiter().GetResult();

        private void SetClient(TcpClient client)
        {
            lock (mSync)
            {
                mClient = client;
                mStream = client.GetStream();
            }
        }

        private void CloseClient()
        {
            lock (mSync)
            {
                if (mStream != null)
                {
                    mStream.Dispose();
                    mStream = null;
                }
                if (mClient != null)
                {
                    mClient.Close();
                    mClient = null;
                }
                mState = ConfigIdState.Disconnected;
            }
        }

        private void SetState(ConfigIdState state)
        {
            lock (mSync) mState = state;
        }

        private async Task<string> SendLineAsync(string line)
        {
            Stream stream;
            lock (mSync)
            {
                if (mStream == null)
                    throw new InvalidOperationException("ID 服务器未连接");
                stream = mStream;
            }

            var data = Encoding.UTF8.GetBytes(line + "\n");
            stream.WriteTimeout = IoTimeoutMs;
            stream.ReadTimeout = IoTimeoutMs;
            await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);

            // 逐字节读行（消息都很短，够用且简单）
            var sb = new StringBuilder();
            var one = new byte[1];
            while (true)
            {
                var n = await stream.ReadAsync(one, 0, 1).ConfigureAwait(false);
                if (n <= 0)
                {
                    CloseClient();
                    throw new IOException("ID 服务器连接断开");
                }
                if (one[0] == (byte)'\n')
                    return sb.ToString();
                sb.Append((char)one[0]);
            }
        }
    }
}
