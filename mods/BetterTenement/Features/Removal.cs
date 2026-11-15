using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace BetterTenement
{
    // U-001 stolen-first renovations and the S-028 remainder fix (spec §3.1).
    // Vanilla RemoveRequiredItems (TenementController.cs:1179-1187) passes the full amount to every resource chest and
    // then charges the inventory whatever the last chest left. We replace it with four tiers and a running remainder:
    // T1 stolen chest stacks, T2 stolen carried stacks, T3 clean chest stacks (vanilla's slot loop), T4 inventory
    // (vanilla call). FixRemainder decides how T3/T4 charge (see CleanTiers); with both settings off vanilla runs.
    internal static class Removal
    {
        internal static bool Patched;

        private static MethodBase _target;
        internal static MethodBase Target =>
            _target ?? (_target = AccessTools.Method(typeof(TenementController), nameof(TenementController.RemoveRequiredItems), new[] { typeof(int), typeof(int) }));

        internal sealed class Tally { internal int StolenChest, StolenInv, CleanChest, CleanInv; }

        // Opened by the TakeRequiredItemsAndMoney prefix, read by its postfix, cleared by its finalizer.
        internal static Dictionary<int, Tally> Receipt;

        private static Tally TallyFor(int id)
        {
            if (Receipt == null) return new Tally();   // RemoveRequiredItems called from elsewhere: count and drop
            if (!Receipt.TryGetValue(id, out var t)) Receipt[id] = t = new Tally();
            return t;
        }

        internal static bool Active => Patched && (Cfg.FixRemainder.Value || Cfg.StolenFirst.Value);

        // Returns true to let vanilla (or the other mod) run, false when we did the removal.
        internal static bool Run(TenementController tc, int id, int amount)
        {
            if (!Active || Vanilla.ResourceStorages == null) return true;
            if (Compat.Foreign(Target, "stolen-first removal")) return true;

            int remaining = amount;
            bool started = false, cleanRan = false;
            var tally = TallyFor(id);
            List<Storage> chests;
            try
            {
                chests = Game.ResourceChests(tc);
                bool stolenFirst = Cfg.StolenFirst.Value;

                // T1: stolen stacks in the chests, chest by chest, slot by slot.
                if (stolenFirst)
                {
                    var touched = new List<Storage>();
                    foreach (var chest in chests)
                    {
                        if (remaining <= 0) break;
                        if (chest.Slots == null) continue;
                        foreach (var s in chest.Slots)
                        {
                            if (remaining <= 0) break;
                            var st = s?.itemStack;
                            if (st == null || st.itemId != id || st.itemAmount <= 0 || !Materials.IsStolen(st)) continue;
                            started = true;
                            int before = remaining;
                            remaining = ItemOperations.RemoveItemAmountFromSlot(s, id, remaining);
                            tally.StolenChest += before - remaining;
                            if (!touched.Contains(chest)) touched.Add(chest);
                        }
                    }
                    foreach (var chest in touched) MarkChanged(chest);
                }

                // T2: stolen stacks the player carries (main, backpack, character slots: the set vanilla's RemoveItem walks).
                // Inventory.RemoveItem(owner: X) would also take a permission stack of owner X, so we walk the slots ourselves
                // and call TakeItem per unit, as vanilla does. TakeItem has no crime check.
                if (stolenFirst && Cfg.StolenFirstIncludesInventory.Value && remaining > 0 && Inventory.instance != null)
                {
                    foreach (var s in Inventory.instance.AllSlots())
                    {
                        if (remaining <= 0) break;
                        while (remaining > 0 && s != null && s.itemStack != null && s.itemStack.itemId == id && s.itemStack.itemAmount > 0
                               && s.itemStack.itemReference != null && Materials.IsStolen(s.itemStack))
                        {
                            started = true;
                            s.TakeItem();
                            remaining--;
                            tally.StolenInv++;
                        }
                    }
                }

                started |= remaining > 0;
                cleanRan = true;
                CleanTiers(chests, id, ref remaining, tally);
            }
            catch (Exception e)
            {
                if (!started)
                {
                    Log.Exception("renovation material removal (vanilla takes over)", e);
                    return true;
                }
                // Never let vanilla run after we took something: it would charge again. The remainder is updated per
                // slot, so the clean tiers can safely carry on from it. Without FixRemainder they run only if they had not
                // started: vanilla-style charging asks every chest for the full amount and would charge a second time.
                Log.Exception($"renovation material removal (id {id}, amount {amount}, remaining {remaining})", e);
                if (Cfg.FixRemainder.Value || !cleanRan)
                {
                    try { CleanTiers(Game.ResourceChests(tc), id, ref remaining, tally); }
                    catch (Exception e2) { Log.Exception("renovation material removal fallback", e2); }
                }
                return false;
            }
            if (remaining > 0) Log.Warn($"Renovation material {id}: {remaining} short.");   // vanilla ignores this silently too
            return false;
        }

        // T3 + T4. T1 already emptied every stolen chest stack of this id whenever T3 still has work, so T3 only takes
        // clean stacks when stolen-first is on. T3 walks the slots itself, exactly as Storage.RemoveItem does
        // (Storage.cs:1321-1331, same RemoveItemAmountFromSlot defaults), so the remainder is updated per slot before
        // the chest's onUpdate relay fires.
        // FixRemainder on: each chest is charged only what is still missing, the inventory only the rest.
        // FixRemainder off: vanilla's charging (TenementController.cs:1179-1187): every chest is asked for the amount
        // still missing when T3 starts, and the inventory for whatever the last chest could not give.
        private static void CleanTiers(List<Storage> chests, int id, ref int remaining, Tally tally)
        {
            if (remaining <= 0) return;
            bool fix = Cfg.FixRemainder.Value;
            int need = remaining;
            int lastLeft = remaining;   // vanilla mode: what the inventory is asked for
            foreach (var chest in chests)
            {
                if (fix && remaining <= 0) break;
                if (chest == null || chest.Slots == null) continue;
                int left = fix ? remaining : need;
                try
                {
                    foreach (var s in chest.Slots)
                    {
                        if (left <= 0) break;
                        int before = left;
                        left = ItemOperations.RemoveItemAmountFromSlot(s, id, left);
                        int took = Math.Max(0, before - left);
                        tally.CleanChest += took;
                        remaining = Math.Max(0, remaining - took);
                    }
                }
                finally
                {
                    lastLeft = Math.Max(0, left);
                    MarkChanged(chest);
                }
            }
            int ask = fix ? remaining : lastLeft;
            if (ask <= 0 || Inventory.instance == null) return;
            // Count what actually left the inventory, even if the vanilla call throws half way.
            int had = Inventory.instance.GetItemAmountInInventory(id);
            try { Inventory.instance.RemoveItems(id, ask, ignoreOwner: true, -1, ignoreCrime: true); }
            finally
            {
                int took = Math.Max(0, had - Inventory.instance.GetItemAmountInInventory(id));
                tally.CleanInv += took;
                remaining = Math.Max(0, remaining - took);
            }
        }

        // Marks a chest as changed and fires its onUpdate relay, as Storage.RemoveItem does. A listener that throws is
        // logged and never interrupts a removal that is half done.
        internal static void MarkChanged(Storage chest)
        {
            if (Vanilla.StorageChanged != null) Vanilla.StorageChanged(chest) = true;
            if (Vanilla.OnUpdate == null) return;
            var relay = Vanilla.OnUpdate(chest);
            if (relay == null) return;
            try { relay.triggerOutputs(); }
            catch (Exception e) { Log.Exception("chest update listener", e); }
        }

        // Fallback when another mod owns RemoveRequiredItems: in each chest, the slots that already hold a required
        // material get the stolen stacks first, then the clean ones, each in their old order. Nothing else moves.
        internal static void SortStolenFirst(TenementUpgrade upgrade)
        {
            var tc = TenementController.instance;
            if (tc == null || upgrade == null || upgrade.requiredItems == null) return;
            foreach (var chest in Game.ResourceChests(tc))
            {
                if (chest.Slots == null || chest.storageSlots == null || chest.Slots.Length != chest.storageSlots.Length) continue;
                bool changed = false;
                foreach (var ri in upgrade.requiredItems)
                {
                    if (ri?.Item == null) continue;
                    int id = ri.Item.ID;
                    var idx = new List<int>();
                    for (int i = 0; i < chest.Slots.Length; i++)
                    {
                        var st = chest.storageSlots[i];
                        if (st != null && st.itemId == id && st.itemAmount > 0) idx.Add(i);
                    }
                    if (idx.Count < 2) continue;
                    var stacks = idx.Select(i => chest.storageSlots[i]).ToList();
                    var ordered = stacks.Where(Materials.IsStolen).Concat(stacks.Where(st => !Materials.IsStolen(st))).ToList();
                    for (int k = 0; k < idx.Count; k++)
                    {
                        if (ReferenceEquals(chest.storageSlots[idx[k]], ordered[k])) continue;
                        chest.storageSlots[idx[k]] = ordered[k];
                        chest.Slots[idx[k]].itemStack = ordered[k];
                        var ui = chest.Slots[idx[k]].slot;
                        if (ui != null) ui.UpdateSlot();
                        changed = true;
                    }
                }
                if (changed)
                {
                    if (Vanilla.StorageChanged != null) Vanilla.StorageChanged(chest) = true;
                    Log.Verbose($"Stolen-first fallback: sorted \"{chest.title}\" before payment.");
                }
            }
        }

        internal static void Notify()
        {
            var r = Receipt;
            if (r == null || r.Count == 0) return;
            foreach (var kv in r)
                Log.Verbose($"Renovation material {kv.Key} x{kv.Value.StolenChest + kv.Value.StolenInv + kv.Value.CleanChest + kv.Value.CleanInv}: " +
                            $"stolen chest {kv.Value.StolenChest}, stolen carried {kv.Value.StolenInv}, clean chest {kv.Value.CleanChest}, clean carried {kv.Value.CleanInv}");
            if (!Cfg.ConsumptionNotification.Value || Notifications.instance == null) return;
            int a = r.Values.Sum(t => t.StolenChest), b = r.Values.Sum(t => t.StolenInv);
            int c = r.Values.Sum(t => t.CleanChest), d = r.Values.Sum(t => t.CleanInv);
            if (a + b == 0) return;   // vanilla behaviour, no news

            var stolen = new List<string>();
            if (a > 0) stolen.Add($"{a} from the chest");
            if (b > 0) stolen.Add($"{b} carried");
            string text = $"Used {a + b} stolen first ({string.Join(", ", stolen)})";
            var clean = new List<string>();
            if (c > 0) clean.Add($"{c} clean from the chest");
            if (d > 0) clean.Add($"{d} carried");
            if (clean.Count > 0) text += "; " + string.Join(", ", clean);
            Notifications.instance.CreateNotification("Renovation materials", text);
        }
    }

    // R1: TenementController.RemoveRequiredItems(int, int); single caller TenementUpgrade.cs:188.
    [HarmonyPatch(typeof(TenementController), nameof(TenementController.RemoveRequiredItems), new[] { typeof(int), typeof(int) })]
    internal static class RemoveRequiredItemsPatch
    {
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(TenementController __instance, int id, int amount)
        {
            try { return Removal.Run(__instance, id, amount); }
            catch (Exception e)
            {
                Log.Exception("RemoveRequiredItems prefix", e);
                return true;
            }
        }
    }

    // R2: TenementUpgrade.TakeRequiredItemsAndMoney() (TenementUpgrade.cs:179). Opens the receipt, runs the fallback
    // sort when another mod replaces R1, and shows the notification afterwards.
    [HarmonyPatch(typeof(TenementUpgrade), nameof(TenementUpgrade.TakeRequiredItemsAndMoney))]
    internal static class TakeRequiredItemsPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(TenementUpgrade __instance)
        {
            Removal.Receipt = null;
            try
            {
                if (!Removal.Patched) return;
                Removal.Receipt = new Dictionary<int, Removal.Tally>();
                var tc = TenementController.instance;
                if (tc != null && !tc.ignoreUpgradePricesAndItems && Cfg.StolenFirst.Value && Cfg.FallbackSortWhenConflict.Value
                    && Compat.Foreign(Removal.Target, "stolen-first removal"))
                    Removal.SortStolenFirst(__instance);
            }
            catch (Exception e) { Log.Exception("TakeRequiredItemsAndMoney prefix", e); }
        }

        private static void Postfix()
        {
            try { if (Removal.Patched) Removal.Notify(); }
            catch (Exception e) { Log.Exception("renovation material notification", e); }
        }

        private static Exception Finalizer(Exception __exception)
        {
            Removal.Receipt = null;
            return __exception;
        }
    }
}
