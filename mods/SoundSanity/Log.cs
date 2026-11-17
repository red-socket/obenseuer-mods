using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Audio;

namespace SoundSanity
{
    // Thin wrapper over the plugin's ManualLogSource (source "Sound Sanity"). Lines start with the feature tag
    // ("Routing:", "Machines:", "Radio music:", "Compat:"). Verbose lines only appear with General.VerboseLogging and are
    // capped per scene; first-of-kind notes and caught exceptions are logged once per key per session (spec §7).
    internal static class Log
    {
        private const int RerouteCap = 40;
        private const int VerbosePerScene = 200;

        private static ManualLogSource _src;
        private static readonly HashSet<string> OnceKeys = new HashSet<string>();
        private static int _reroutes, _verbose;
        private static readonly HashSet<AudioMixer> SeenCopies = new HashSet<AudioMixer>();

        internal static void Init(ManualLogSource src) => _src = src;

        internal static void Info(string msg) => _src?.LogInfo(msg);
        internal static void Warn(string msg) => _src?.LogWarning(msg);
        internal static void Error(string msg) => _src?.LogError(msg);

        internal static bool VerboseOn => Cfg.VerboseLogging != null && Cfg.VerboseLogging.Value;

        internal static void Verbose(string msg)
        {
            if (!VerboseOn || _verbose >= VerbosePerScene) return;
            if (++_verbose == VerbosePerScene) msg += " (further verbose lines in this scene not logged)";
            _src?.LogInfo("[v] " + msg);
        }

        internal static void OnSceneLoaded() => _verbose = 0;

        internal static void InfoOnce(string key, string msg)
        {
            if (OnceKeys.Add("info|" + key)) Info(msg);
        }

        internal static void WarnOnce(string key, string msg)
        {
            if (OnceKeys.Add("warn|" + key)) Warn(msg);
        }

        // Caught exception with stack trace, once per key per session.
        internal static void Exception(string key, Exception e)
        {
            if (!OnceKeys.Add("ex|" + key)) return;
            _src?.LogError($"{key}: {e}");
        }

        // The runtime routing check (spec §7): one Info line per (case, original group, clip or object name) per session,
        // at most RerouteCap of them; later ones go to Verbose.
        internal static void Reroute(Router.Case kind, AudioMixerGroup original, AudioMixerGroup target, AudioSource s, string where)
        {
            if (kind == Router.Case.Duplicate && original != null && original.audioMixer != null && SeenCopies.Add(original.audioMixer))
                Info($"Routing: found a second copy of the game's mixer ('{original.audioMixer.name}', id {original.audioMixer.GetInstanceID()}); " +
                     "sounds wired to it are moved to the real one.");
            string what = s.clip != null ? s.clip.name : "(no clip)";
            string key = $"{kind}|{(original != null ? original.name : "null")}|{(s.clip != null ? s.clip.name : s.gameObject.name)}";
            if (!OnceKeys.Add("route|" + key)) return;
            string from = kind == Router.Case.Null ? "had no mixer group"
                : kind == Router.Case.MainGroup ? "played through the master group only"
                : $"was on '{original.name}' of the mixer copy";
            string line = $"Routing: '{what}' on '{s.gameObject.name}' ({SceneName(s)}) {from}; now plays through {(target != null ? target.name : "?")} [{where}].";
            if (_reroutes < RerouteCap)
            {
                Info(line);
                if (++_reroutes == RerouteCap) Info("Routing: further re-routes are not logged (turn on VerboseLogging to see them).");
            }
            else Verbose(line);
        }

        private static string SceneName(Component c)
        {
            try { var sc = c.gameObject.scene; return sc.IsValid() ? sc.name : "no scene"; }
            catch { return "?"; }
        }
    }
}
