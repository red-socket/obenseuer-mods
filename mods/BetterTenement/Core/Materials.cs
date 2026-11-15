using System;
using System.Collections.Generic;
using OS.Items;

namespace BetterTenement
{
    // Which items count as renovation materials, and which stacks are stolen (spec §2.2).
    internal static class Materials
    {
        private static HashSet<int> _ids;
        private static bool _complete;   // built with a TenementController; otherwise rebuilt on the next call

        internal static void Invalidate() => _ids = null;

        internal static bool IsMaterial(int id) => id >= 0 && Ids().Contains(id);

        // Stolen = owned by an NPC (owner id above 1, the player) without the owner's permission. Never reads
        // ItemStolenData, which is unreliable (U-002). Owners 10001 and 20002 count as stolen, like vanilla's label.
        internal static bool IsStolen(ItemStack s)
        {
            if (s == null || s.ownerId <= 1) return false;
            var perm = s.GetMetaOfType<ItemOwnerPermission>();
            return perm == null || !perm.ownerPermission || (Cfg.PermissionWithoutUseCountsAsStolen.Value && !perm.allowUse);
        }

        private static HashSet<int> Ids()
        {
            var tc = TenementController.instance;
            if (_ids != null && (_complete || tc == null)) return _ids;

            var set = new HashSet<int>();
            bool complete = false;
            if (tc != null)
            {
                try
                {
                    AddFromController(tc, set);
                    complete = true;
                }
                catch (Exception e)
                {
                    Log.WarnOnce("materials-scan", $"Could not read the renovation material list ({e.Message}); using the \"Tenement Resource\" category only.");
                    set.Clear();
                    complete = true;   // do not retry every call
                }
            }
            try
            {
                foreach (var item in ItemDatabase.database)
                    if (item != null && item.Categories != null && Array.IndexOf(item.Categories, "Tenement Resource") >= 0) set.Add(item.ID);
            }
            catch (Exception e) { Log.Exception("material categories", e); }
            foreach (int id in ParseIds(Cfg.ExtraItemIds.Value)) set.Add(id);
            foreach (int id in ParseIds(Cfg.ExcludeItemIds.Value)) set.Remove(id);

            _ids = set;
            _complete = complete;
            if (complete) Log.Verbose($"Renovation materials: {set.Count} item IDs.");
            return set;
        }

        // Every required item of every upgrade line the controller knows (general and per-apartment-type).
        private static void AddFromController(TenementController tc, HashSet<int> set)
        {
            var g = tc.general;
            if (g != null)
            {
                foreach (var grp in new[]
                         {
                             g.electricityUpgrades, g.waterUpgrades, g.heatingUpgrades, g.stairsUpgrades, g.elevatorUpgrades, g.facadeUpgrades,
                             g.roofUpgrades, g.basementUpgrades, g.saunaUpgrades, g.shroomsUpgrades, g.workshopUpgrades, g.mineUpgrades,
                             g.courtyardUpgrades, g.fenceUpgrades, g.saunaAndPierUpgrades, g.greenhouseUpgrades
                         })
                    AddGroup(grp, set);
            }
            if (tc.apartments == null) return;
            foreach (var a in tc.apartments)
            {
                var t = a?.type;
                if (t == null) continue;
                foreach (var grp in new[] { t.electricity, t.water, t.heating, t.bathroom, t.walls, t.balcony, t.balcony2, t.kitchen })
                    AddGroup(grp, set);
            }
        }

        private static void AddGroup(TenementUpgradeGroup grp, HashSet<int> set)
        {
            if (grp == null || grp.updateGroup == null) return;
            foreach (var u in grp.updateGroup)
            {
                if (u == null || u.requiredItems == null) continue;
                foreach (var ri in u.requiredItems)
                    if (ri != null && ri.Item != null && ri.Item.ID > 0) set.Add(ri.Item.ID);
            }
        }

        internal static IEnumerable<int> ParseIds(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            foreach (var part in s.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(part.Trim(), out int id)) yield return id;
        }
    }
}
