using System;
using System.Collections.Generic;
using System.Linq;
using OS.Items;

namespace TidyInventory
{
    // S-062 "Combine partials" (spec §5.2): pools the charges of part-used single items of one kind (matchboxes, pill
    // packs, glue) into full units plus one remainder, in one container. Charges are whole numbers and are conserved
    // exactly: kept units get their new charge in place, emptied units are removed with SlotController.RemoveItem, and
    // each removed unit yields exactly the leftover the game gives when a unit runs out (an empty bottle, a plate),
    // added through the game's own ItemOperations.AddItem(id, owner, slot) call (SRC/OS/Items/ItemAmmodata.cs:124).
    // Only the player's own items (owner <= 1): the game's theft check returns at once for them (SRC/Inventory.cs:408),
    // so there is no crime path. Every cluster is planned and checked before anything changes; a cluster that can't be
    // done exactly is skipped and left as it was.
    internal static class Combine
    {
        private sealed class Unit
        {
            internal SlotController Sc;
            internal ItemAmmodata Ammo;
            internal int Charges, Index;
            internal bool Pinned;
        }

        private struct GroupKey : IEquatable<GroupKey>
        {
            internal int ItemId, Owner, Max, SpoilDisabled;
            internal float Refrigerated;

            public bool Equals(GroupKey o) => ItemId == o.ItemId && Owner == o.Owner && Max == o.Max && SpoilDisabled == o.SpoilDisabled && Refrigerated.Equals(o.Refrigerated);
            public override bool Equals(object obj) => obj is GroupKey k && Equals(k);
            public override int GetHashCode() => ((ItemId * 397 ^ Owner) * 397 ^ Max) * 397 ^ SpoilDisabled ^ Refrigerated.GetHashCode();
        }

        // Per item ID: the parts of eligibility that come from the item definition (Items.json), minus the live
        // config switch for flat-price items.
        private static readonly Dictionary<int, bool> BaseEligible = new Dictionary<int, bool>();

        internal static void RunInventory()
        {
            try
            {
                var inv = Inventory.instance;
                if (inv == null || inv.Slots == null || !Game.InventoryVisible) return;
                Run(inv.Slots, null);
            }
            catch (Exception e) { Log.Exception("combine (inventory)", e); }
        }

        internal static void RunStorage()
        {
            try
            {
                var s = Storage.active;
                var inv = Inventory.instance;
                if (s == null || s.Slots == null || inv == null || inv.ForeignSlots != s.Slots || !Game.StorageVisible) return;
                if (s.StorageOwner != null && s.StorageOwner.ID > 1 && !s.PermissionToTake) return;
                if (Bins.IsBin(s, ignorePlayerPlacedOnly: true)) return;   // bins get Empty, never Combine
                Run(s.Slots, s);
            }
            catch (Exception e) { Log.Exception("combine (storage)", e); }
        }

        private static void Run(SlotController[] slots, Storage storage)
        {
            if (Game.DragActive)
            {
                Hints.Notify(Strings.DropFirst);
                return;
            }
            if (Game.SliderOpen || Game.TradeActive || SaveController.Loading) return;

            // 1. Tick every candidate first: integrity becomes current and food can spoil now rather than mid-plan
            //    (P11 keeps a pinned slot's spoiled item in place). Unofficial Patch's heal prefix runs here too.
            foreach (var sc in slots)
                if (Game.NonEmpty(sc) && sc.itemStack.itemAmount == 1 && ItemEligible(Item(sc))) sc.UpdateNow();

            // 2. Units, grouped.
            var groups = new Dictionary<GroupKey, List<Unit>>();
            var order = new List<GroupKey>();
            for (int i = 0; i < slots.Length; i++)
            {
                var u = UnitOf(slots[i], i, storage == null);
                if (u == null) continue;
                var st = u.Sc.itemStack;
                var key = new GroupKey
                {
                    ItemId = st.itemId, Owner = st.ownerId < 1 ? -1 : st.ownerId, Max = (int)Math.Round(u.Ammo.maxAmmo),
                    Refrigerated = st.refigerated, SpoilDisabled = st.spoilDisabled ? 1 : 0,
                };
                if (!groups.TryGetValue(key, out var list))
                {
                    groups[key] = list = new List<Unit>();
                    order.Add(key);
                }
                list.Add(u);
            }

            var done = new List<string>();
            var skipped = new List<string>();
            var changed = new HashSet<SlotController>();
            foreach (var key in order)
                foreach (var cluster in Clusters(groups[key]))
                    Do(key, cluster, done, skipped, changed);

            Game.CloseInfoFor(changed.Contains);
            if (done.Count == 0 && skipped.Count == 0) Hints.Notify(Strings.NothingToCombine);
            else
            {
                var parts = new List<string>();
                if (done.Count > 0) parts.Add(Strings.Combined(string.Join(", ", done.ToArray())));
                parts.AddRange(skipped);
                Hints.Notify(string.Join(" ", parts.ToArray()));
            }
            if (done.Count > 0)
                Log.Info($"Combine: {(storage == null ? "inventory" : "\"" + storage.title + "\"")}: {string.Join(", ", done.ToArray())}.");
        }

        private static Item Item(SlotController sc) =>
            sc.itemStack.itemReference?.Item ?? ItemDatabase.instance?.FetchItemByID(sc.itemStack.itemId);

        // ---- Eligibility ----

        private static bool ItemEligible(Item item)
        {
            if (item == null || item.Stackable > 1) return false;
            if (!BaseEligible.TryGetValue(item.ID, out bool ok))
            {
                ok = false;
                try
                {
                    var def = ItemMetaUtilities.GetMetaOfType<ItemAmmodata>(item.Meta);
                    ok = def != null && def.destroyWhenEmpty && def.showAsAmount && !def.characterSlotDrain
                         && Integral(def.maxAmmo) && Math.Round(def.maxAmmo) >= 2
                         && ItemMetaUtilities.GetMetaOfType<ItemConnectivityData>(item.Meta) == null
                         && (def.itemsToSpawn == null || def.spawnItemsByRemovedAmount || def.itemsToSpawn.Count <= 1);
                    if (ok && item.Actions != null)
                        foreach (var a in item.Actions)
                            if (a is ItemReloadable || a is ItemEquipable || a is ItemElectronic) { ok = false; break; }
                }
                catch (Exception e) { Log.Exception("combine eligibility", e); ok = false; }
                BaseEligible[item.ID] = ok;
            }
            if (!ok) return false;
            if (Cfg.Combine.IncludeFlatPriceItems.Value) return true;
            var d = ItemMetaUtilities.GetMetaOfType<ItemAmmodata>(item.Meta);
            return d != null && d.ammoAffectsPrice;
        }

        private static bool Integral(float f) => Math.Abs(f - Math.Round(f)) < 1e-4;

        private static Unit UnitOf(SlotController sc, int index, bool inventory)
        {
            if (!Game.NonEmpty(sc) || sc.itemStack.itemAmount != 1 || sc.itemStack.ownerId > 1) return null;
            var item = Item(sc);
            if (!ItemEligible(item)) return null;
            var meta = sc.itemStack.Meta;
            if (ItemMetaUtilities.GetMetaOfType<ItemTaskItemData>(meta) != null || ItemMetaUtilities.GetMetaOfType<ItemShopData>(meta) != null
                || ItemMetaUtilities.GetMetaOfType<ItemOwnerPermission>(meta) != null) return null;
            var ammo = ItemMetaUtilities.GetMetaOfType<ItemAmmodata>(meta);
            if (ammo == null || !Integral(ammo.ammo) || !Integral(ammo.maxAmmo) || Math.Round(ammo.maxAmmo) < 2) return null;
            int a = (int)Math.Round(ammo.ammo);
            if (a < 1 || a > (int)Math.Round(ammo.maxAmmo)) return null;
            return new Unit { Sc = sc, Ammo = ammo, Charges = a, Index = index, Pinned = inventory && Reservation.IsPinnedMain(sc) };
        }

        // ---- Clustering: greedy in slot order around an anchor, within vanilla's own stacking tolerances ----

        private static IEnumerable<List<Unit>> Clusters(List<Unit> units)
        {
            var left = new List<Unit>(units);
            while (left.Count > 1)
            {
                var anchor = left[0];
                var cluster = new List<Unit> { anchor };
                for (int i = 1; i < left.Count; i++)
                    if (Matches(anchor, left[i])) cluster.Add(left[i]);
                foreach (var u in cluster) left.Remove(u);
                if (cluster.Count > 1) yield return cluster;
            }
        }

        private static bool Matches(Unit a, Unit b)
        {
            var ma = WithoutAmmo(a.Sc.itemStack.Meta);
            var mb = WithoutAmmo(b.Sc.itemStack.Meta);
            int owner = a.Sc.itemStack.ownerId < 1 ? -1 : a.Sc.itemStack.ownerId;
            // Both directions: CompareItemMetas only walks the first array.
            if (!ItemMetaUtilities.CompareItemMetas(ma, mb, out _, owner) || !ItemMetaUtilities.CompareItemMetas(mb, ma, out _, owner)) return false;

            var qa = ItemMetaUtilities.GetMetaOfType<ItemQualityData>(ma);
            var qb = ItemMetaUtilities.GetMetaOfType<ItemQualityData>(mb);
            if ((qa == null) != (qb == null)) return false;
            if (qa != null && Cfg.Combine.StrictQuality.Value && !(qa.fixedQuality && qb.fixedQuality) && !qa.quality.Equals(qb.quality)) return false;

            var pa = ItemMetaUtilities.GetMetaOfType<ItemPerishabledata>(ma);
            var pb = ItemMetaUtilities.GetMetaOfType<ItemPerishabledata>(mb);
            if ((pa == null) != (pb == null)) return false;
            if (pa != null && (pa.perishingDisabled != pb.perishingDisabled || !pa.integrityLossPerHour.Equals(pb.integrityLossPerHour)
                               || (pa.spoiledItem?.ID ?? -1) != (pb.spoiledItem?.ID ?? -1))) return false;
            return true;
        }

        private static object[] WithoutAmmo(object[] meta) => meta == null ? new object[0] : meta.Where(m => !(m is ItemAmmodata)).ToArray();

        // ---- Plan (dry run), apply, check ----

        private static void Do(GroupKey key, List<Unit> units, List<string> done, List<string> skipped, HashSet<SlotController> changed)
        {
            var first = units[0];
            string title = Item(first.Sc)?.Title ?? key.ItemId.ToString();
            for (int i = 0; i < units.Count; i++)
                for (int j = i + 1; j < units.Count; j++)
                    if (ReferenceEquals(units[i].Ammo, units[j].Ammo))
                    {
                        Skip(title, Strings.SkipShared, "shared charge data", skipped);
                        return;
                    }

            int M = key.Max;
            int T = units.Sum(u => u.Charges);
            int k = units.Count;
            int full = T / M, rem = T % M;
            int keep = full + (rem > 0 ? 1 : 0);
            // Pinned units first, then full ones, then slot order. Among unpinned units the full ones are kept first, so a
            // full unit is only drawn from when pinned part-used units need its charges (pinned 2/6, 6/6, 2/6 -> 6/6 + 4/6).
            var kept = units.OrderByDescending(u => u.Pinned).ThenByDescending(u => u.Charges == M).ThenBy(u => u.Index).ToList();
            int pinned = units.Count(u => u.Pinned);
            if (pinned > keep) keep = pinned;
            int removedCount = k - keep;

            // Fill kept units in order to M, leaving 1 charge for each kept unit still to come. T >= k >= keep holds
            // because every unit has at least 1 charge.
            var plan = new int[keep];
            int left = T;
            for (int j = 0; j < keep; j++)
            {
                int give = Math.Min(M, left - (keep - 1 - j));
                plan[j] = give;
                left -= give;
            }
            if (left != 0 || plan.Any(p => p < 1 || p > M))
            {
                Log.Error($"Combine: plan for {title} does not add up (T {T}, M {M}, keep {keep}); nothing changed.");
                return;
            }
            // No unit to free (5/6 + 5/6): still top up to full units plus one remainder (6/6 + 4/6), but only when that
            // leaves fewer part-used units than now; otherwise it is already as full as it gets.
            if (removedCount <= 0 && plan.Count(p => p < M) >= units.Count(u => u.Charges < M)) return;
            var removed = kept.Skip(keep).ToList();
            kept = kept.Take(keep).ToList();

            // Leftover preflight: the freed slot must take the leftover (a wine rack only takes wine).
            var leftovers = new Dictionary<Unit, Item>();
            foreach (var u in removed)
            {
                var a = u.Ammo;
                if (a.spawnItemsByRemovedAmount || a.itemsToSpawn == null || a.itemsToSpawn.Count == 0) continue;
                var L = a.itemsToSpawn[0]?.Item;
                int owner = u.Sc.itemStack.ownerId < 1 ? -1 : u.Sc.itemStack.ownerId;
                if (L == null || !u.Sc.CheckIfAllowed(L) || (u.Sc.OnDroppedCheck != null && !u.Sc.OnDroppedCheck(L, 1, owner, null)))
                {
                    Skip(title, Strings.SkipNoRoom, $"no room for {L?.Title ?? "the leftover"} in slot {u.Index + 1}", skipped);
                    return;
                }
                leftovers[u] = L;
            }

            // Apply, in place on each unit's own charge data. Never clone ammo meta: CloneMeta maps 0 to maxAmmo
            // (SRC/Item.cs:929).
            for (int j = 0; j < keep; j++)
            {
                kept[j].Ammo.ammo = plan[j];
                changed.Add(kept[j].Sc);
            }
            foreach (var u in removed)
            {
                u.Sc.RemoveItem();
                changed.Add(u.Sc);
            }
            foreach (var u in removed)
                if (leftovers.TryGetValue(u, out var L))
                    ItemOperations.AddItem(L.ID, key.Owner, u.Sc);   // the slot is empty now; the owner class is the unit's
            foreach (var u in kept) u.Sc.UpdateNow();

            // Check: same total, the planned number of units, none at 0.
            float after = kept.Sum(u => u.Ammo.ammo);
            int units2 = kept.Count(u => Game.NonEmpty(u.Sc) && ItemMetaUtilities.GetMetaOfType<ItemAmmodata>(u.Sc.itemStack.Meta) == u.Ammo);
            if (Math.Abs(after - T) > 1e-3 || units2 != keep || kept.Any(u => u.Ammo.ammo < 1f))
                Log.Error($"Combine: check failed for {title}: before {T} charges in {k} units, after {after} in {units2} (planned {keep}). " +
                          $"Kept: {string.Join(", ", kept.Select(u => $"slot {u.Index + 1}={u.Ammo.ammo}").ToArray())}; " +
                          $"removed: {string.Join(", ", removed.Select(u => $"slot {u.Index + 1}").ToArray())}.");

            done.Add(removedCount > 0 ? Strings.CombinedEntry(title, k, keep) : Strings.ToppedUpEntry(title));
            Log.Verbose($"Combine: {title} (id {key.ItemId}) {k} → {keep}, {T} charges, {leftovers.Count} leftovers.");
        }

        private static void Skip(string title, string shown, string why, List<string> skipped)
        {
            skipped.Add(Strings.Skipped(title, shown));
            Log.Verbose($"Combine: skipped {title}: {why}.");
        }
    }
}
