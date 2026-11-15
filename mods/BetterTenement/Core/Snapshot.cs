using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterTenement
{
    // S-028 snapshot refresh (spec §3.2.2). Vanilla records a contractor's chest contents only when that contractor's
    // menu was opened in the scene (TenementController.cs:1151-1177, called at :195 on save). We write the same
    // dictionary, with vanilla keys and float values, when a resource chest closes and on save.
    internal static class Snapshot
    {
        internal static void Write(TenementController tc)
        {
            if (tc == null || Vanilla.ResourceStorages == null || Vanilla.Snapshots == null || Vanilla.CurrentSceneContractor == null) return;
            var chests = Game.ResourceChests(tc);
            if (chests.Count == 0 || chests.Any(c => c.Slots == null)) return;

            if (Vanilla.CurrentSceneContractor(tc) != null)
            {
                if (Vanilla.RefreshResourceStorageSnapshot == null) return;
                Vanilla.RefreshResourceStorageSnapshot.Invoke(tc, null);
                Log.Verbose($"Snapshot (vanilla refresh): {Vanilla.CurrentSceneContractor(tc).Id}");
                return;
            }

            string id = Contractors.IdForActiveScene();
            if (id == null) return;   // Bazaar Books or any other scene: no contractor owns this chest
            var dict = new Dictionary<int, float>();
            foreach (var chest in chests)
            {
                foreach (var sc in chest.Slots)
                {
                    if (sc == null || sc.itemStack == null) continue;
                    if (sc.itemStack.itemId != -1 && sc.itemStack.itemAmount > 0)
                    {
                        if (dict.ContainsKey(sc.itemStack.itemId)) dict[sc.itemStack.itemId] += sc.itemStack.itemAmount;
                        else dict.Add(sc.itemStack.itemId, sc.itemStack.itemAmount);
                    }
                }
            }
            var snaps = Vanilla.Snapshots(tc);
            if (snaps == null) return;
            snaps[id] = dict;
            Log.Verbose($"Snapshot: {id}: {dict.Count} item types");
        }
    }
}
