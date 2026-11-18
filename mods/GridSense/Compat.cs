using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace GridSense
{
    // Other mods on the methods we patch (spec §9), from Display Options' Compat: bool prefixes, transpilers and finalizers
    // from any owner but us (they can skip or rewrite vanilla). Void prefixes and postfixes can't, so they are ignored. Our
    // own other mods (KnownFriends) are designed to work together and are logged as Info only. There are no stand-downs:
    // every Grid Sense patch is additive and idempotent. Not cached: the lazy flicker group can be scanned later than the
    // three launch-time groups, and the scans are rare.
    internal static class Compat
    {
        private const string KnownFriends = "redsocket.obenseuer.";

        private static readonly HashSet<string> Logged = new HashSet<string>();

        // Additive patches keep working next to other mods; the overlap is logged once per method.
        internal static void Overlap(MethodBase m, string feature)
        {
            var (foreign, friends) = Scan(m);
            if (friends.Length > 0 && Logged.Add("friend|" + Key(m)))
                Log.Info($"Compat: {Key(m)} is also patched by {string.Join(", ", friends)} (one of our mods); {feature} is ordered to work with it.");
            if (foreign.Length == 0) return;
            if (Logged.Add("overlap|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", foreign)}; {feature} still applies where it can.");
        }

        private static string Key(MethodBase m) => m == null ? "(null)" : m.DeclaringType?.Name + "." + m.Name;

        private static (string[] foreign, string[] friends) Scan(MethodBase m)
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
                    if (p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool)) Add(p.owner);
                foreach (var p in info.Transpilers.Concat(info.Finalizers)) Add(p.owner);
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
