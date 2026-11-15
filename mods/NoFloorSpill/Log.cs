using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace NoFloorSpill
{
    // Thin wrapper over the plugin's ManualLogSource (source "No Floor Spill"). Every line starts with the feature tag
    // ("Refuse:", "Junk:", "Storage:"). Verbose lines only appear with General.VerboseLogging; caught exceptions,
    // IL mismatches and first-of-kind notes are logged once per key per session (spec §6).
    internal static class Log
    {
        private static ManualLogSource _src;
        private static readonly HashSet<string> OnceKeys = new HashSet<string>();

        internal static void Init(ManualLogSource src) => _src = src;

        internal static void Info(string msg) => _src?.LogInfo(msg);
        internal static void Warn(string msg) => _src?.LogWarning(msg);
        internal static void Error(string msg) => _src?.LogError(msg);

        internal static bool VerboseOn => Cfg.VerboseLogging != null && Cfg.VerboseLogging.Value;

        internal static void Verbose(string msg)
        {
            if (VerboseOn) _src?.LogInfo("[v] " + msg);
        }

        // Caught exception with stack trace, once per key per session.
        internal static void Exception(string key, Exception e)
        {
            if (!OnceKeys.Add("ex|" + key)) return;
            _src?.LogError($"{key}: {e}");
        }

        internal static void WarnOnce(string key, string msg)
        {
            if (OnceKeys.Add("warn|" + key)) Warn(msg);
        }

        internal static void InfoOnce(string key, string msg)
        {
            if (OnceKeys.Add("info|" + key)) Info(msg);
        }
    }
}
