using System;
using System.IO;
using UnityEngine;

namespace HYC.Framework.Runtime
{
    /// <summary>
    /// Lightweight global settings container stored as a ScriptableObject.
    /// The framework ships the base record; games add fields for their own
    /// toggles (input remap, V-Sync, target framerate, audio volumes, etc.).
    /// </summary>
    public sealed class FrameworkSettings : ScriptableObject
    {
        public string defaultLanguage = "Default";
        public int targetFramerate = 60;
        public bool vSync = true;
        public int remoteLogLevel = 2;

        private static FrameworkSettings _instance;

        public static FrameworkSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = ScriptableObject.CreateInstance<FrameworkSettings>();
                    LoadDefaults(_instance);
                }
                return _instance;
            }
        }

        private static void LoadDefaults(FrameworkSettings s)
        {
            // optionally load from Resources on the real project
        }

        /// <summary>Parses command-line switches like <c>--framerate=120</c> / <c>--language=English</c>.</summary>
        public void ApplyCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                var arg = args[i];
                var value = args[i + 1];
                switch (arg.ToLowerInvariant())
                {
                    case "--framerate":
                        if (int.TryParse(value, out var fr)) targetFramerate = fr;
                        break;
                    case "--language":
                        defaultLanguage = value;
                        break;
                    case "--v-sync":
                        if (bool.TryParse(value, out var vs)) vSync = vs;
                        break;
                }
            }
        }
    }

    // ReSharper disable once InconsistentNaming
    /// <summary>Alias for <see cref="FrameworkSettings"/>, kept small for discoverability.</summary>
    public static class GameSettings
    {
        public static FrameworkSettings Current => FrameworkSettings.Instance;
    }
}