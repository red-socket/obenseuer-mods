using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace DisplayOptions
{
    // Other mods on the methods we patch (spec §11.4), from Vital Signs' Compat. Foreign: bool prefixes, transpilers and
    // finalizers from any owner but us and our own mods (they can skip or rewrite vanilla). AnyForeign: any patch type from
    // another owner, used wherever a foreign postfix would write the same value as ours and lose to our Priority.Last one
    // (frame cap, quality caps, lamp cap, UI scale). Effects and the row only log (Overlap). Our own other mods (KnownFriends)
    // are designed to work together and are logged as Info only. Not cached: Display Options can scan before every plugin
    // has loaded (the lazy light-cap patch at launch), and the scans are rare.
    internal static class Compat
    {
        private const string KnownFriends = "redsocket.obenseuer.";

        private static readonly HashSet<string> Logged = new HashSet<string>();

        // True when another (non-friend) mod can skip or rewrite m. Logs once.
        internal static bool Foreign(MethodBase m, string feature, string consequence)
        {
            var (foreign, friends) = Scan(m, skippingOnly: true);
            Friends(m, friends, feature);
            if (foreign.Length == 0) return false;
            if (Logged.Add("foreign|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", foreign)}; {feature} {consequence}.");
            return true;
        }

        // True when another (non-friend) mod patches m in any way. Logs once.
        internal static bool AnyForeign(MethodBase m, string feature, string consequence)
        {
            var (foreign, friends) = Scan(m, skippingOnly: false);
            Friends(m, friends, feature);
            if (foreign.Length == 0) return false;
            if (Logged.Add("any|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", foreign)}; {feature} {consequence}.");
            return true;
        }

        // Additive patches keep working next to other mods; the overlap is logged once per method.
        internal static void Overlap(MethodBase m, string feature)
        {
            var (foreign, friends) = Scan(m, skippingOnly: true);
            Friends(m, friends, feature);
            if (foreign.Length == 0) return;
            if (Logged.Add("overlap|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", foreign)}; {feature} still applies where it can.");
        }

        // One Info line naming loaded plugins that sound like they touch the same settings (spec §11.4). Micro-props have
        // no method to scan, so this line is their only signal.
        internal static void KeywordScan()
        {
            try
            {
                var rx = new Regex(@"perf|fps|frame ?rate|vsync|shadow|post ?process|ui ?scale", RegexOptions.IgnoreCase);
                var hits = new List<string>();
                foreach (var kv in Chainloader.PluginInfos)
                {
                    var md = kv.Value?.Metadata;
                    string guid = md?.GUID ?? kv.Key ?? "", name = md?.Name ?? "";
                    if (guid == Plugin.Guid || guid.StartsWith(KnownFriends, StringComparison.Ordinal)) continue;
                    if (rx.IsMatch(guid) || rx.IsMatch(name)) hits.Add($"{name} ({guid})");
                }
                if (hits.Count > 0)
                    Log.Info($"Compat: also loaded: {string.Join(", ", hits.ToArray())}. If one of them changes the frame cap, shadows, " +
                             "camera effects or UI size, turn off the matching Display Options setting.");
            }
            catch (Exception e) { Log.Exception("compat (plugins)", e); }
        }

        private static void Friends(MethodBase m, string[] friends, string feature)
        {
            if (friends.Length > 0 && Logged.Add("friend|" + Key(m)))
                Log.Info($"Compat: {Key(m)} is also patched by {string.Join(", ", friends)} (one of our mods); {feature} is ordered to work with it.");
        }

        private static string Key(MethodBase m) => m == null ? "(null)" : m.DeclaringType?.Name + "." + m.Name;

        private static (string[] foreign, string[] friends) Scan(MethodBase m, bool skippingOnly)
        {
            var none = (new string[0], new string[0]);
            if (m == null) return none;
            try
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) return none;
                var foreign = new HashSet<string>();
                var friends = new HashSet<string>();
                void Add(string owner)
                {
                    if (owner == null || owner == Plugin.Guid) return;
                    if (owner.StartsWith(KnownFriends, StringComparison.Ordinal)) friends.Add(owner);
                    else foreign.Add(owner);
                }
                foreach (var p in info.Prefixes)
                    if (!skippingOnly || (p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool))) Add(p.owner);
                foreach (var p in info.Transpilers.Concat(info.Finalizers)) Add(p.owner);
                if (!skippingOnly)
                    foreach (var p in info.Postfixes) Add(p.owner);
                return (foreign.ToArray(), friends.ToArray());
            }
            catch (Exception e)
            {
                Log.Exception("compat scan", e);
                return none;
            }
        }
    }
}
