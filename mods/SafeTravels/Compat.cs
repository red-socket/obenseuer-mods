using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SafeTravels
{
    // Other mods that can stop or change one of the methods we patch or call (spec §3.10), copied from Unofficial
    // Patch: bool prefixes, transpilers and finalizers from any owner but us and our own other mods (KnownFriends,
    // which are designed to work together). Void prefixes and postfixes cannot skip vanilla, so they are ignored.
    // Scanned lazily (all plugins have loaded by then) and cached.
    internal static class Compat
    {
        private const string KnownFriends = "redsocket.obenseuer.";

        private static readonly Dictionary<MethodBase, string[]> Cache = new Dictionary<MethodBase, string[]>();
        private static readonly HashSet<string> Logged = new HashSet<string>();

        // True when another mod can skip or rewrite m. Warns once.
        internal static bool Foreign(MethodBase m, string feature, string consequence)
        {
            var owners = Owners(m);
            if (owners.Length == 0) return false;
            if (Logged.Add("foreign|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", owners)}; {feature} {consequence}.");
            return true;
        }

        // Additive patches and plain calls keep working next to other mods; the overlap is logged once per method.
        internal static void Overlap(MethodBase m, string feature)
        {
            var owners = Owners(m);
            if (owners.Length == 0) return;
            if (Logged.Add("overlap|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", owners)}; {feature} still applies where it can.");
        }

        private static string Key(MethodBase m) => m == null ? "(null)" : m.DeclaringType?.Name + "." + m.Name;

        private static string[] Owners(MethodBase m)
        {
            if (m == null) return new string[0];
            if (!Cache.TryGetValue(m, out var owners))
            {
                owners = Scan(m);
                Cache[m] = owners;
            }
            return owners;
        }

        private static bool Friend(string owner) => owner == Plugin.Guid || (owner != null && owner.StartsWith(KnownFriends, StringComparison.Ordinal));

        private static string[] Scan(MethodBase m)
        {
            try
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) return new string[0];
                var set = new HashSet<string>();
                foreach (var p in info.Prefixes)
                    if (!Friend(p.owner) && p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool)) set.Add(p.owner);
                foreach (var p in info.Transpilers.Concat(info.Finalizers))
                    if (!Friend(p.owner)) set.Add(p.owner);
                return set.ToArray();
            }
            catch (Exception e)
            {
                Log.Exception("compat scan", e);
                return new string[0];
            }
        }
    }
}
