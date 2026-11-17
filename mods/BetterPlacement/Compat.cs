using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace BetterPlacement
{
    // Other mods on the methods we patch (spec §9). Run once, the first time the building menu opens (every plugin has
    // loaded by then). Each other owner is logged once as Info with the patch kinds it uses; there is no automatic
    // stand-down. Likely owners: Ultimate Building Mod, SnippitySnappity, the Obenseuer Modding Framework (their GUIDs are
    // not known). The trigger fix protects itself: its transpiler only applies when it finds exactly the two trigger reads.
    internal static class Compat
    {
        private const string KnownFriends = "redsocket.obenseuer.";

        private static bool _reported;

        internal static void Report()
        {
            if (_reported) return;
            _reported = true;
            var targets = new List<(MethodBase m, string feature)>
            {
                (Vanilla.ShowPreview, "alignment, rotation snapping, the trigger fix and flat items"),
                (Vanilla.Place, "the settle check and remembered angles"),
                (Vanilla.ChangeCurrentObject, "remembered angles"),
                (Vanilla.ChangeColor, "the settle check"),
                (Vanilla.UpdateConnectionIndicator, "alignment's connection lines"),
                (Vanilla.TriggerEnter, "flat items"),
                (Vanilla.TriggerExit, "flat items"),
                (Vanilla.GetRotationMode, "the controls hint"),
                (Vanilla.GetButtonInfo, "the controls hint"),
            };
            var logged = new HashSet<string>();
            foreach (var (m, feature) in targets)
            {
                if (m == null) continue;
                try
                {
                    var info = Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    var kinds = new Dictionary<string, SortedSet<string>>();
                    void Add(IEnumerable<Patch> patches, string kind)
                    {
                        foreach (var p in patches)
                        {
                            if (p.owner == null || p.owner == Plugin.Guid) continue;
                            if (!kinds.TryGetValue(p.owner, out var set)) kinds[p.owner] = set = new SortedSet<string>();
                            set.Add(kind);
                        }
                    }
                    Add(info.Prefixes, "prefix");
                    Add(info.Postfixes, "postfix");
                    Add(info.Transpilers, "transpiler");
                    Add(info.Finalizers, "finalizer");
                    foreach (var kv in kinds)
                    {
                        if (!logged.Add(kv.Key + "|" + m.Name)) continue;
                        string who = kv.Key.StartsWith(KnownFriends, StringComparison.Ordinal) ? kv.Key + " (one of our mods)" : kv.Key;
                        Log.Info($"Compat: {m.DeclaringType?.Name}.{m.Name} is also patched by {who} ({string.Join(", ", kv.Value.ToArray())}); " +
                                 $"{feature} still apply where they can.");
                    }
                }
                catch (Exception e) { Log.Exception("compat scan", e); }
            }
        }
    }
}
