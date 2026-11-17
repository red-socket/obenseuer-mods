using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace FairMarket
{
    // Thin wrapper over the plugin's ManualLogSource (source "Fair Market"), copied from Vital Signs. Lines start with the
    // feature tag ("Stock:", "Buyers:", "Hours:", "Wash:", "Certs:", "Invoices:", "Keys:", "Compat:"). Verbose lines only
    // appear with General.VerboseLog; caught exceptions and first-of-kind notes are logged once per key per session
    // (spec §12). Hot paths never log per call.
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
    }
}
