using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace SoundSanity
{
    // Other mods on the methods we patch (spec §7), scanned once at the first gameplay scene, when every plugin has loaded.
    // Skipping prefixes, transpilers and finalizers from other owners are warnings (they can stop or rewrite what we hook);
    // other mods' postfixes are noted as Info. Our own mods (redsocket.obenseuer.*) don't patch audio. Our patches stay on
    // either way. A radio music mod (Obentunes) takes over interactive radios: Sound Sanity then leaves them to it.
    internal static class Compat
    {
        internal static bool ObentunesPresent { get; private set; }

        private static bool _detected, _reported;

        // At the first scene of any kind: every plugin has loaded, and no radio has played yet.
        internal static void DetectOnce()
        {
            if (_detected) return;
            _detected = true;
            DetectObentunes();
        }

        internal static void ReportOnce()
        {
            if (_reported) return;
            _reported = true;
            Report(Vanilla.FromClip, "routing");
            Report(Vanilla.CreateAudioSource, "routing");
            Report(Vanilla.RadioPlaySound, "radio music");
            Report(Vanilla.ChangeMixersInfos, "radio music");
            Report(Vanilla.SoundVolumePercent, "background radio music");
            Report(Vanilla.CraftingBaseStart, "machine volume");
            Report(Vanilla.PlaySound, "machine and radio volume");
        }

        private static void DetectObentunes()
        {
            try
            {
                foreach (var kv in Chainloader.PluginInfos)
                {
                    var md = kv.Value?.Metadata;
                    string guid = md?.GUID ?? kv.Key ?? "", name = md?.Name ?? "";
                    if (guid.IndexOf("obentunes", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("obentunes", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    ObentunesPresent = true;
                    Log.Warn($"Compat: {name} ({guid}) is installed; Sound Sanity leaves radios you switch on to it. Background radio music still follows Sound Sanity's settings.");
                    Live.MarkDirty(Live.Pass.Radios);
                    return;
                }
            }
            catch (Exception e) { Log.Exception("compat (plugins)", e); }
        }

        private static void Report(MethodBase m, string feature)
        {
            if (m == null) return;
            try
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) return;
                var skipping = new HashSet<string>();
                var additive = new HashSet<string>();
                foreach (var p in info.Prefixes)
                {
                    if (Mine(p.owner)) continue;
                    if (p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool)) skipping.Add(p.owner);
                    else additive.Add(p.owner);
                }
                foreach (var p in info.Transpilers.Concat(info.Finalizers))
                    if (!Mine(p.owner)) skipping.Add(p.owner);
                foreach (var p in info.Postfixes)
                    if (!Mine(p.owner)) additive.Add(p.owner);
                additive.ExceptWith(skipping);
                string key = m.DeclaringType?.Name + "." + m.Name;
                if (skipping.Count > 0)
                    Log.Warn($"Compat: {key} is also patched by {string.Join(", ", skipping.ToArray())}, which can skip or rewrite it; {feature} may not apply everywhere.");
                if (additive.Count > 0)
                    Log.Info($"Compat: {key} is also patched by {string.Join(", ", additive.ToArray())}; {feature} still applies.");
            }
            catch (Exception e) { Log.Exception("compat scan", e); }
        }

        private static bool Mine(string owner) => owner == null || owner == Plugin.Guid;
    }
}
