using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace FairJustice
{
    // Thin wrapper over the plugin's ManualLogSource (source "Fair Justice").
    // Verbose lines only appear with General.VerboseLogging; caught exceptions are rate-limited per key.
    internal static class Log
    {
        private static ManualLogSource _src;
        private static readonly Dictionary<string, DateTime> LastByKey = new Dictionary<string, DateTime>();
        private static readonly HashSet<string> OnceKeys = new HashSet<string>();

        internal static void Init(ManualLogSource src) => _src = src;

        internal static void Info(string msg) => _src?.LogInfo(msg);
        internal static void Warn(string msg) => _src?.LogWarning(msg);
        internal static void Error(string msg) => _src?.LogError(msg);

        internal static void Verbose(string msg)
        {
            if (Cfg.VerboseLogging != null && Cfg.VerboseLogging.Value) _src?.LogInfo("[v] " + msg);
        }

        // Caught exception with stack trace, at most once per key per 60 s.
        internal static void Exception(string key, Exception e)
        {
            if (!Allow(key, 60)) return;
            _src?.LogError($"{key}: {e}");
        }

        internal static void WarnOnce(string key, string msg)
        {
            if (OnceKeys.Add(key)) Warn(msg);
        }

        private static bool Allow(string key, double seconds)
        {
            var now = DateTime.UtcNow;
            lock (LastByKey)
            {
                if (LastByKey.TryGetValue(key, out var last) && (now - last).TotalSeconds < seconds) return false;
                LastByKey[key] = now;
                return true;
            }
        }
    }
}
