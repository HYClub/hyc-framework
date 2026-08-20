using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Runtime
{
    /// <summary>
    /// Command-line argument parser used by the launcher/CI to override boot
    /// settings (account, token, server, language, window size). Decoupled from
    /// any game-specific launcher; mirror of the source <c>GameCommandArgs</c>.
    /// </summary>
    public class GameCommandArgs
    {
        private static GameCommandArgs _instance;
        public static GameCommandArgs Instance => _instance ??= new GameCommandArgs();
        public static GameCommandArgs GetInstance() => Instance;

        private struct Commands
        {
            public const string Account = "account";
            public const string Token = "token";
            public const string AppId = "appid";
            public const string ServerIp = "serverip";
            public const string ServerPort = "serverport";
            public const string Lang = "lang";
            public const string Width = "width";
            public const string Height = "height";
            public const string FullScreen = "fullscreen";

            public static bool Contain(string command)
                => command is Account or Token or AppId or ServerIp or ServerPort or Lang or Width or Height or FullScreen;
        }

        private readonly Dictionary<string, string> _args = new Dictionary<string, string>();

        public bool openWithMiniLauncher { get; set; }

        public string account => _args.TryGetValue(Commands.Account, out var v) ? v : string.Empty;
        public string openID => account;
        public string token => _args.TryGetValue(Commands.Token, out var v) ? v : string.Empty;
        public string appId => _args.TryGetValue(Commands.AppId, out var v) ? v : string.Empty;
        public string serverIp => _args.TryGetValue(Commands.ServerIp, out var v) ? v : string.Empty;

        public int serverPort => TryInt(Commands.ServerPort);
        public string lang => _args.TryGetValue(Commands.Lang, out var v) ? v : string.Empty;
        public int width => TryInt(Commands.Width);
        public int height => TryInt(Commands.Height);

        /// <summary>-1 = use in-game setting, 0 = windowed, 1 = fullscreen.</summary>
        public int fullScreen => TryInt(Commands.FullScreen);

        public bool hasScreenSizeArg => width > 0 && height > 0;

        private int TryInt(string key)
            => _args.TryGetValue(key, out var v) && int.TryParse(v, out var r) ? r : -1;

        private GameCommandArgs()
        {
            var args = Environment.GetCommandLineArgs();
            openWithMiniLauncher = args.Length > 0;

            for (var i = 0; i < args.Length; i++)
            {
                var raw = args[i];
                if (raw.Length < 2 || !raw.StartsWith("--", StringComparison.Ordinal)) continue;

                var command = raw.Substring(2).ToLowerInvariant();
                if (Commands.Contain(command) && i + 1 < args.Length)
                {
                    if (!_args.TryAdd(command, args[++i]))
                        Debug.LogError($"解析到重复的命令 : {command}");
                }
            }
        }
    }
}
