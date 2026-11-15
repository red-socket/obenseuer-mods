using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace UnofficialPatch
{
    // Thin wrapper over the plugin's ManualLogSource (source "Unofficial Patch"). Every line starts with the fix tag
    // ("Rent:", "Scam payout:", ...). Verbose lines only appear with General.VerboseLogging; caught exceptions are
    // logged once per key per session (spec §0.2).
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
    }
}
