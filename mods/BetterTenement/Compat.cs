using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace BetterTenement
{
    // Other mods that can stop or change one of the methods we replace (spec §2.4): bool prefixes, transpilers and
    // finalizers from any owner but us. Void prefixes and postfixes cannot skip vanilla, so they are ignored.
    // Scanned lazily on the first call of the target (all plugins have loaded by then) and cached for the session.
    // Our replacing prefixes pass through when this is non-empty, whatever HarmonyX does with later prefixes.
    internal static class Compat
    {
        private static readonly Dictionary<MethodBase, string[]> Cache = new Dictionary<MethodBase, string[]>();

        internal static bool Foreign(MethodBase m, string feature)
        {
            if (m == null) return false;
            if (!Cache.TryGetValue(m, out var owners))
            {
                owners = Scan(m);
                Cache[m] = owners;
                if (owners.Length > 0)
                    Log.Warn($"{m.DeclaringType?.Name}.{m.Name} is also patched by {string.Join(", ", owners)}; {feature} uses its fallback.");
            }
            return owners.Length > 0;
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
