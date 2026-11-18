using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace MessageLog
{
    // Thin wrapper over the plugin's ManualLogSource (source "Message Log"), copied from Fair Market. Lines start with the
    // feature tag ("Capture:", "Dialogue:", "Storage:", "Load:", "Window:", "Typing:", "Keys:", "Compat:", "PT-BR:").
    // Verbose lines only appear with General.VerboseLogging; first-of-kind notes are logged once per key per session
    // (spec §11). Hot paths (N1, D1, T1-T5, the save and load event handlers) log through Once: the first exception in
    // full, then only a count every 100 repeats.
    internal static class Log
    {
        private static ManualLogSource _src;
        private static readonly HashSet<string> OnceKeys = new HashSet<string>();
        private static readonly Dictionary<string, int> Repeats = new Dictionary<string, int>();

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

        // Hot-path exception: the first one in full, then a counter line every 100 repeats. Never throws.
        internal static void Once(string site, Exception e)
        {
            try
            {
                if (OnceKeys.Add("once|" + site))
                {
                    _src?.LogError($"{site}: {e}");
                    return;
                }
                Repeats.TryGetValue(site, out int n);
                Repeats[site] = ++n;
                if (n % 100 == 0) _src?.LogError($"{site}: {n} more errors of the same kind (last: {e.GetType().Name}: {e.Message})");
            }
            catch { }
        }

        internal static void WarnOnce(string key, string msg)
        {
            if (OnceKeys.Add("warn|" + key)) Warn(msg);
        }
    }
}
