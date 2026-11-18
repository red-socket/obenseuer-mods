using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace FieldGuide
{
    // Thin wrapper over the plugin's ManualLogSource (source "Field Guide"), copied from Recipe Book. Lines start with the
    // feature tag ("Help:", "Bought by:", "Traders:", "Places:", "Compat:"). Verbose lines only appear with
    // General.VerboseLog. Caught exceptions are logged once per site per scene (spec §11): NewScene clears those keys;
    // warnings marked once stay once per session. Hot paths never log per call.
    internal static class Log
    {
        private static ManualLogSource _src;
        private static readonly HashSet<string> SceneKeys = new HashSet<string>();
        private static readonly HashSet<string> SessionKeys = new HashSet<string>();

        internal static void Init(ManualLogSource src) => _src = src;

        internal static void Info(string msg) => _src?.LogInfo(msg);
        internal static void Warn(string msg) => _src?.LogWarning(msg);
        internal static void Error(string msg) => _src?.LogError(msg);

        internal static bool VerboseOn => Cfg.VerboseLog != null && Cfg.VerboseLog.Value;

        internal static void Verbose(string msg)
        {
            if (VerboseOn) _src?.LogInfo("[v] " + msg);
        }

        // Caught exception with stack trace, once per site per scene.
        internal static void Exception(string site, Exception e)
        {
            try
            {
                if (!SceneKeys.Add(site)) return;
                _src?.LogError($"{site}: {e}");
            }
            catch { }
        }

        internal static void WarnOnce(string key, string msg)
        {
            if (SessionKeys.Add("warn|" + key)) Warn(msg);
        }

        internal static void InfoOnce(string key, string msg)
        {
            if (SessionKeys.Add("info|" + key)) Info(msg);
        }

        internal static void NewScene() => SceneKeys.Clear();
    }
}
