using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace GreenThumb
{
    // Other mods that can stop or change one of the methods we patch (spec §12), copied from Vital Signs: bool prefixes,
    // transpilers and finalizers from any owner but us. Our own other mods (KnownFriends) are designed to work together:
    // their skipping patches are logged as Info, everyone else's as a warning. Void prefixes and postfixes can't skip
    // vanilla, so they are ignored. Scanned once at the first level load (all plugins have loaded by then) and cached.
    // H1 and Q1 stand down when Foreign is true for their target (HarvestAll.ForeignSkip, Ghost.ForeignSkip); every other
    // patch stays on.
    internal static class Compat
    {
        private const string KnownFriends = "redsocket.obenseuer.";

        private static readonly Dictionary<MethodBase, (string[] foreign, string[] friends)> Cache =
            new Dictionary<MethodBase, (string[] foreign, string[] friends)>();
        private static readonly HashSet<string> Logged = new HashSet<string>();

        // True when another (non-friend) mod can skip or rewrite m. Logs once.
        internal static bool Foreign(MethodBase m, string feature, string consequence)
        {
            var (foreign, friends) = Owners(m);
            if (friends.Length > 0 && Logged.Add("friend|" + Key(m)))
                Log.Info($"Compat: {Key(m)} is also patched by {string.Join(", ", friends)} (one of our mods); {feature} is ordered to work with it.");
            if (foreign.Length == 0) return false;
            if (Logged.Add("foreign|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", foreign)}; {feature} {consequence}.");
            return true;
        }

        // Additive patches keep working next to other mods; the overlap is logged once per method.
        internal static void Overlap(MethodBase m, string feature)
        {
            var (foreign, friends) = Owners(m);
            if (friends.Length > 0 && Logged.Add("friend|" + Key(m)))
                Log.Info($"Compat: {Key(m)} is also patched by {string.Join(", ", friends)} (one of our mods); {feature} is ordered to work with it.");
            if (foreign.Length == 0) return;
            if (Logged.Add("overlap|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", foreign)}; {feature} still applies where it can.");
        }

        private static string Key(MethodBase m) => m == null ? "(null)" : m.DeclaringType?.Name + "." + m.Name;

        private static (string[] foreign, string[] friends) Owners(MethodBase m)
        {
            if (m == null) return (new string[0], new string[0]);
            if (!Cache.TryGetValue(m, out var owners))
            {
                owners = Scan(m);
                Cache[m] = owners;
            }
            return owners;
        }

        private static (string[] foreign, string[] friends) Scan(MethodBase m)
        {
            try
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) return (new string[0], new string[0]);
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
                return (new string[0], new string[0]);
            }
        }
    }
}
