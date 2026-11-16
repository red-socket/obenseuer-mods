using System;
using System.Collections.Generic;
using OS.Items;

namespace Workshop
{
    // Carries out a plan (spec §4.5 "click"). Every move goes through the game's own three-argument
    // SlotController.MoveToOtherInventory, which never drops: what does not fit stays in the source. The result is judged
    // by the moved amount, never by the returned bool.
    internal static class Mover
    {
        internal sealed class Result
        {
            internal readonly Dictionary<int, int> Moved = new Dictionary<int, int>();   // item id -> units
            internal bool StationFull;
            internal bool UsedBag;
            internal readonly HashSet<Storage> Touched = new HashSet<Storage>();
        }

        internal static Result Run(CraftingBase station, Plan plan)
        {
            var res = new Result();
            var pins = Pins.Indices();
            foreach (var mv in plan.Moves)
            {
                try
                {
                    var src = mv.Src;
                    var st = src.Sc.itemStack;
                    // 1. Still the same stack, still enough, still allowed.
                    if (st == null || st.itemId != mv.ItemId || st.itemAmount < mv.Units || (mv.Whole && st.itemAmount != mv.Units)) continue;
                    if (!Sources.StillOk(src, station, pins)) continue;
                    // 2. Spoilage up to now at the source's own refrigeration.
                    if (Settle(st)) continue;
                    // 3. Target: a station stack of the same item with room, else an empty slot.
                    var target = Target(station, st);
                    if (target == null)
                    {
                        res.StationFull = true;
                        break;
                    }
                    // 4. The game's own move. Leaves Siblings intact, so overflow merges into other station slots.
                    src.Sc.MoveToOtherInventory(target, mv.Units, out int moved);
                    if (moved <= 0)
                    {
                        res.StationFull = true;
                        break;
                    }
                    res.Moved[mv.ItemId] = (res.Moved.TryGetValue(mv.ItemId, out var n) ? n : 0) + moved;
                    if (src.Kind == SourceKind.Bag) res.UsedBag = true;
                    if (src.Kind == SourceKind.Storage && src.Storage != null) res.Touched.Add(src.Storage);
                    Log.Verbose($"Fetch: {moved} × {mv.ItemId} from {src.Kind}{(src.Storage != null ? " " + src.Storage.name : "")} into {Stations.Name(station)}.");
                }
                catch (Exception e) { Log.Exception("fetch move", e); }
            }
            Touch(res);
            return res;
        }

        // Settles freshness up to now with the source slot's own refrigeration, as Slot.CheckItemMetas would
        // (SRC/Slot.cs:505-560), so the station's refrigeration only counts from the move on. Never
        // ItemMetaUtilities.UpdateItemMetas: it uses refrigeration 0. True when the stack has spoiled; the game swaps it
        // on its next redraw and Fetch leaves it alone.
        private static bool Settle(ItemStack st)
        {
            var meta = st.Meta;
            if (meta == null) return false;
            var per = ItemMetaUtilities.GetMetaOfType<ItemPerishabledata>(meta);
            if (per != null && per.CountPerishingTime(st.refigerated, st.spoilDisabled)) return true;
            var liquid = ItemMetaUtilities.GetMetaOfType<ItemLiquidData>(meta);
            if (liquid?.liquidItemMeta != null)
            {
                var lp = ItemMetaUtilities.GetMetaOfType<ItemPerishabledata>(liquid.liquidItemMeta);
                if (lp != null && lp.CountPerishingTime(st.refigerated, st.spoilDisabled || liquid.disablePerishable)) return true;
            }
            ItemMetaUtilities.GetMetaOfType<ItemPackageData>(meta)?.CountPerishingTimes(st.refigerated, st.spoilDisabled);
            return false;
        }

        private static SlotController Target(CraftingBase station, ItemStack st)
        {
            var slots = station.InputOutputStorage?.Slots;
            if (slots == null) return null;
            var item = st.itemReference?.Item;
            int stack = item != null ? Math.Max(1, item.Stackable) : 1;
            foreach (var sc in slots)
                if (sc?.itemStack != null && sc.itemStack.itemId == st.itemId && sc.itemStack.itemAmount > 0 && sc.itemStack.itemAmount < stack) return sc;
            foreach (var sc in slots)
                if (sc?.itemStack != null && (sc.itemStack.itemId < 0 || sc.itemStack.itemAmount <= 0)) return sc;
            return null;
        }

        // Mirrors Storage.RemoveItem (SRC/Storage.cs:1321-1332): the storage is marked changed and its onUpdate relay
        // fires. Not ContentsExternallyModified: that fires onSpawnItems.
        private static void Touch(Result res)
        {
            foreach (var s in res.Touched)
            {
                try
                {
                    if (s == null) continue;
                    Vanilla.StorageChanged(s) = true;
                    var relay = Vanilla.StorageOnUpdate(s);
                    if (relay != null) relay.triggerOutputs();
                }
                catch (Exception e) { Log.Exception("storage update", e); }
            }
            if (res.UsedBag)
                try { if (BackpackStorage.instance != null) BackpackStorage.instance.UpdateEmptyState(); }
                catch (Exception e) { Log.Exception("bag update", e); }
        }
    }
}
