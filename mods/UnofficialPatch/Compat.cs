using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace UnofficialPatch
{
    // Other mods that can stop or change one of the methods we patch (spec §0.7), copied from Fair Justice: bool
    // prefixes, transpilers and finalizers from any owner but us. Void prefixes and postfixes cannot skip vanilla, so
    // Foreign ignores them. Scanned lazily on the first call (all plugins have loaded by then) and cached.
    // Behaviour-changing prefixes call Foreign and step aside; additive patches call Overlap, which only logs.
    internal static class Compat
    {
        private static readonly Dictionary<MethodBase, string[]> Cache = new Dictionary<MethodBase, string[]>();
        private static readonly Dictionary<MethodBase, string[]> AllCache = new Dictionary<MethodBase, string[]>();
        private static readonly HashSet<string> Logged = new HashSet<string>();

        // True when another mod can skip or rewrite m; that fix then lets vanilla (or the other mod) run. Warns once.
        internal static bool Foreign(MethodBase m, string feature)
        {
            var owners = Owners(m, Cache, false);
            if (owners.Length == 0) return false;
            if (Logged.Add("yield|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", owners)}; {feature} steps aside and the game's own code runs.");
            return true;
        }

        // Additive patches keep working next to other mods; the overlap is logged once per method.
        internal static void Overlap(MethodBase m, string feature)
        {
            var owners = Owners(m, Cache, false);
            if (owners.Length == 0) return;
            if (Logged.Add("overlap|" + Key(m)))
                Log.Info($"Compat: {Key(m)} is also patched by {string.Join(", ", owners)}; {feature} still applies.");
        }

        // Any other owner at all, postfixes included. For fixes that write state another mod may also restore
        // (the sprint toggle on OpenSewerCharacterController.Awake). Warns once.
        internal static bool AnyOther(MethodBase m, string feature)
        {
            var owners = Owners(m, AllCache, true);
            if (owners.Length == 0) return false;
            if (Logged.Add("any|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", owners)}; {feature} steps aside.");
            return true;
        }

        private static string Key(MethodBase m) => m == null ? "(null)" : m.DeclaringType?.Name + "." + m.Name;

        private static string[] Owners(MethodBase m, Dictionary<MethodBase, string[]> cache, bool all)
        {
            if (m == null) return new string[0];
            if (!cache.TryGetValue(m, out var owners))
            {
                owners = Scan(m, all);
                cache[m] = owners;
            }
            return owners;
        }

        private static string[] Scan(MethodBase m, bool all)
        {
            try
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) return new string[0];
                var set = new HashSet<string>();
                foreach (var p in info.Prefixes)
                    if (p.owner != Plugin.Guid && p.PatchMethod != null && (all || p.PatchMethod.ReturnType == typeof(bool))) set.Add(p.owner);
                foreach (var p in info.Transpilers.Concat(info.Finalizers))
                    if (p.owner != Plugin.Guid) set.Add(p.owner);
                if (all)
                    foreach (var p in info.Postfixes)
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
