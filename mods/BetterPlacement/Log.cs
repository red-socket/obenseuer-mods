using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace BetterPlacement
{
    // Thin wrapper over the plugin's ManualLogSource (source "Better Placement"). Lines start with the feature tag
    // ("Align:", "Rotation:", "Angles:", "Flat items:", "Keys:"). Verbose lines only appear with General.VerboseLogging and
    // are throttled to one line per topic per 0.5 s (spec §9), since most of them come from per-frame code. Caught
    // exceptions and first-of-kind notes are logged once per key per session.
    internal static class Log
    {
        private const float VerboseSeconds = 0.5f;

        private static ManualLogSource _src;
        private static readonly HashSet<string> OnceKeys = new HashSet<string>();
        private static readonly Dictionary<string, float> LastVerbose = new Dictionary<string, float>();

        internal static void Init(ManualLogSource src) => _src = src;

        internal static void Info(string msg) => _src?.LogInfo(msg);
        internal static void Warn(string msg) => _src?.LogWarning(msg);
        internal static void Error(string msg) => _src?.LogError(msg);

        internal static bool VerboseOn => Cfg.VerboseLogging != null && Cfg.VerboseLogging.Value;

        internal static void Verbose(string msg)
        {
            if (VerboseOn) _src?.LogInfo("[v] " + msg);
        }

        // Per-frame sources: at most one line per topic per 0.5 real seconds. The message is built only when it is logged.
        internal static void Verbose(string topic, Func<string> msg)
        {
            if (!VerboseOn) return;
            float now = Time.unscaledTime;
            if (LastVerbose.TryGetValue(topic, out float last) && now - last < VerboseSeconds) return;
            LastVerbose[topic] = now;
            try { _src?.LogInfo("[v] " + msg()); }
            catch (Exception e) { Exception("verbose " + topic, e); }
        }

        // The same, with the message's inputs passed as an argument: per-frame callers pass a non-capturing lambda (cached
        // by the compiler), so nothing is allocated when verbose logging is off. A capturing lambda would allocate its
        // closure at the start of the caller's scope on every call, logged or not.
        internal static void Verbose<T>(string topic, T arg, Func<T, string> msg)
        {
            if (!VerboseOn) return;
            float now = Time.unscaledTime;
            if (LastVerbose.TryGetValue(topic, out float last) && now - last < VerboseSeconds) return;
            LastVerbose[topic] = now;
            try { _src?.LogInfo("[v] " + msg(arg)); }
            catch (Exception e) { Exception("verbose " + topic, e); }
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

        internal static void ErrorOnce(string key, string msg)
        {
            if (OnceKeys.Add("err|" + key)) Error(msg);
        }

        internal static void InfoOnce(string key, string msg)
        {
            if (OnceKeys.Add("info|" + key)) Info(msg);
        }
    }
}
