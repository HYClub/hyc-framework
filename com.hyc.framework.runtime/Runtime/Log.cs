using System;
using System.IO;
using UnityEngine;

namespace HYC.Framework.Runtime
{
    /// <summary>Severity levels mirrored from Unity's log types.</summary>
    public enum LogLevel
    {
        Trace = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Fatal = 4
    }

    /// <summary>
    /// Application log facility. Wraps <see cref="Debug"/> and optionally
    /// writes to a rolling file under <c>PersistentDataPath/logs</c>. The
    /// remote-log level gates which rows go to the sink; hook
    /// <see cref="OnLog"/> to forward to a server/console.
    /// </summary>
    public static class Log
    {
        public static event Action<LogLevel, string, string, string> OnLog;

        private static StreamWriter _file;
        private static readonly object Sync = new object();
        private static string _session;

        public static void Open(string dir = null)
        {
            if (_file != null) return;
            var baseDir = dir ?? Path.Combine(Application.persistentDataPath, "logs");
            Directory.CreateDirectory(baseDir);
            _session = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(baseDir, "framework_" + _session + ".log");
            lock (Sync) { _file = new StreamWriter(path, true) { AutoFlush = true }; }
            Debug.Log("Log file: " + path);
        }

        public static void Close()
        {
            lock (Sync)
            {
                if (_file != null) { _file.Flush(); _file.Dispose(); _file = null; }
            }
        }

        public static void Trace(string message) => Write(LogLevel.Trace, message);
        public static void Info(string message) => Write(LogLevel.Info, message);
        public static void Warn(string message) => Write(LogLevel.Warning, message);
        public static void Error(string message) => Write(LogLevel.Error, message);
        public static void Error(Exception ex, string context = null) => Write(LogLevel.Error, context == null ? ex.ToString() : context + " :: " + ex);

        public static void Format(LogLevel level, string fmt, params object[] args) => Write(level, string.Format(fmt, args));

        private static void Write(LogLevel level, string message)
        {
            var stamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var row = "[" + stamp + "][" + level + "] " + message;

            lock (Sync)
            {
                _file?.WriteLine(row);
            }

            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Info:
                    Debug.Log(row);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(row);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    Debug.LogError(row);
                    break;
            }

            OnLog?.Invoke(level, row, null, null);
        }
    }
}