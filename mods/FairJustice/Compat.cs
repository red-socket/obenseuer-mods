using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace FairJustice
{
    // Other mods that can stop or change one of the methods we patch (spec §2.4): bool prefixes, transpilers and
    // finalizers from any owner but us. Void prefixes and postfixes cannot skip vanilla, so they are ignored.
    // Scanned lazily on the first call of the target (all plugins have loaded by then) and cached for the session.
    // Behaviour-changing prefixes (own-move skip, night release, PayFine order, pass-out guard) call Foreign and step
    // aside; skip-only grace prefixes call Overlap, which only logs.
    internal static class Compat
    {
        private static readonly Dictionary<MethodBase, string[]> Cache = new Dictionary<MethodBase, string[]>();
        private static readonly HashSet<string> Logged = new HashSet<string>();

        // True when another mod patches m; that feature then lets vanilla (or the other mod) run. Warns once per method.
        internal static bool Foreign(MethodBase m, string feature)
        {
            var owners = Owners(m);
            if (owners.Length == 0) return false;
            if (Logged.Add("yield|" + Key(m)))
                Log.Warn($"{Key(m)} is also patched by {string.Join(", ", owners)}; {feature} steps aside and the game's own code runs.");
            return true;
        }

        // Skip-only prefixes keep working next to other mods; the overlap is logged once per method.
        internal static void Overlap(MethodBase m, string feature)
        {
            var owners = Owners(m);
            if (owners.Length == 0) return;
            if (Logged.Add("overlap|" + Key(m)))
                Log.Info($"{Key(m)} is also patched by {string.Join(", ", owners)}; {feature} still applies (it only skips one detection for a few seconds).");
        }

        private static string Key(MethodBase m) => m.DeclaringType?.Name + "." + m.Name;

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

        private static string[] Scan(MethodBase m)
        {
            try
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) return new string[0];
                var set = new HashSet<string>();
                foreach (var p in info.Prefixes)
                    if (p.owner != Plugin.Guid && p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool)) set.Add(p.owner);
                foreach (var p in info.Transpilers.Concat(info.Finalizers))
                    if (p.owner != Plugin.Guid) set.Add(p.owner);
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
