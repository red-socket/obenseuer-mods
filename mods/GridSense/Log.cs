using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace GridSense
{
    // Thin wrapper over the plugin's ManualLogSource (source "Grid Sense"), copied from Display Options. Lines start with
    // the feature tag ("Shop:", "Build:", "Flicker:", "Watts:", "Compat:"). Verbose lines only appear with
    // General.VerboseLog; caught exceptions and first-of-kind notes are logged once per key per session (spec §8).
    // Hot paths (P1, P2, P3) never log per call.
    internal static class Log
    {
        private static ManualLogSource _src;
        private static readonly HashSet<string> OnceKeys = new HashSet<string>();

        internal static void Init(ManualLogSource src) => _src = src;

        internal static void Info(string msg) => _src?.LogInfo(msg);
        internal static void Warn(string msg) => _src?.LogWarning(msg);
        internal static void Error(string msg) => _src?.LogError(msg);

        internal static bool VerboseOn => Cfg.VerboseLog != null && Cfg.VerboseLog.Value;

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
