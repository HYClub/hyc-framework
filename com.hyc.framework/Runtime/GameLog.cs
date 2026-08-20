using System;
using System.IO;
using UnityEngine;

namespace HYC.Framework.Runtime
{
    /// <summary>
    /// Optional log-to-file hook. Startup is called implicitly on load; the
    /// flags (SaveLog / SaveLogWarning / SaveLogError) are supplied by the
    /// owning host (e.g. from StartUp/GameSetting) before the log source is
    /// swapped. Decoupled QK re-implementation of the source <c>GameLog</c>.
    /// </summary>
    public static class GameLog
    {
        private static string _logFilePath;
        private static DebugLogExt _logHandler;

        public static bool SaveLog;
        public static bool SaveLogWarning;
        public static bool SaveLogError;

        /// <summary>File path used when logging is enabled; override for tests.</summary>
        public static string LogFilePath => _logFilePath;

        /// <summary>Call from the host with the desired flags; returns true if file logging started.</summary>
        public static void Startup(string dataPath, bool saveLog, bool saveLogWarning, bool saveLogError)
        {
            SaveLog = saveLog;
            SaveLogWarning = saveLogWarning;
            SaveLogError = saveLogError;
            StartupInternal(dataPath);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Startup()
        {
            if (SaveLog || SaveLogWarning || SaveLogError)
                StartupInternal(Application.dataPath);
        }

        private static void StartupInternal(string dataPath)
        {
            if (!(SaveLog || SaveLogWarning || SaveLogError)) return;

            _logFilePath = dataPath + "/../log.json";
            var folder = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            if (File.Exists(_logFilePath))
                File.Delete(_logFilePath);

            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;

            if (_logHandler == null)
                _logHandler = new DebugLogExt();
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Log:
                    if (!SaveLog) return;
                    break;
                case LogType.Warning:
                    if (!SaveLogWarning) return;
                    break;
                default:
                    if (!SaveLogError) return;
                    break;
            }

            File.AppendAllText(_logFilePath, type + "\n" + message + "\n" + stackTrace + "\n");
        }

        /// <summary>
        /// Wraps the default logger; in builds prefixes each message with a
        /// timestamp. Drops exceptions in-editor (they surface in the console).
        /// </summary>
        private class DebugLogExt : ILogHandler
        {
            private readonly ILogHandler _handler;

            public DebugLogExt()
            {
                _handler = Debug.unityLogger.logHandler;
                Debug.unityLogger.logHandler = this;
            }

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
#if UNITY_EDITOR
                _handler.LogFormat(logType, context, format, args);
#else
                _handler.LogFormat(logType, context, $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}] {format}", args);
#endif
            }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
#if !UNITY_EDITOR
                _handler.LogFormat(LogType.Error, context,
                    $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss.fff}] {{0}}", "Exception");
#endif
                _handler.LogException(exception, context);
            }
        }
    }
}