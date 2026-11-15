using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OS.Items;

namespace NoFloorSpill
{
    // S-088 byproduct half, #6570 (spec §2.3). When an ammo-style ingredient runs out, CraftingBase.RemoveAmmo adds its
    // junk (ItemAmmodata.itemsToSpawn: plastic trash, empty bottles, ...) through InputOutputStorage.AddItem *before*
    // removing the emptied ingredient (SRC/CraftingBase.cs:1293-1299), so a full machine drops the junk even though a
    // slot is about to free up. The transpiler swaps that one call for JunkSink.Add, which fills the machine as vanilla
    // does but queues the overflow instead of dropping it; the finalizer of the same RemoveAmmo call (after the removal)
    // routes the queue: freed slot (only when the machine's results go to a separate OutputStorage), then inventory (only
    // while the station window is open), then nearby storage, then vanilla's drop. Junk is never deleted on purpose.
    [HarmonyPatch]
    internal static class RemoveAmmoPatch
    {
        private static MethodBase TargetMethod() => Vanilla.RemoveAmmo;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            IlSwap.Swap(instructions, original, Vanilla.StorageAddItem, JunkSink.AddMethod, 1, true, JunkSink.Group);

        // Void: HarmonyX rethrows any exception from the original unchanged.
        private static void Finalizer(CraftingBase __instance) => JunkSink.Drain(__instance);
    }

    internal static class JunkSink
    {
        internal static readonly SwapGroup Group = new SwapGroup("Machine junk overflow");
        internal static readonly MethodInfo AddMethod = AccessTools.Method(typeof(JunkSink), nameof(Add));

        private sealed class Pending
        {
            internal readonly Storage S;
            internal readonly Item Item;
            internal readonly int Rem, Owner;
            internal readonly object[] M;

            internal Pending(Storage s, Item item, int rem, int owner, object[] m)
            {
                S = s;
                Item = item;
                Rem = rem;
                Owner = owner;
                M = m;
            }
        }

        // Main thread only; filled by Add and drained by the finalizer of the same RemoveAmmo call.
        private static readonly List<Pending> Queue = new List<Pending>();

        private static bool _compatChecked;

        // Called from RemoveAmmo in place of InputOutputStorage.AddItem(item, 1, slot.ownerId) (defaults: null, 0, false),
        // with the CraftingBase pushed last. The return value is popped by vanilla.
        public static bool Add(Storage s, Item item, int amount, int owner, object[] meta, int existedInSeconds, bool overflowToPlayer,
            CraftingBase cb)
        {
            if (!Group.Active || !Cfg.JunkEnabled.Value || s == null || item == null || cb == null
                || s.Slots == null || s.Slots.Length == 0)
                return s.AddItem(item, amount, owner, meta, existedInSeconds, overflowToPlayer); // exact vanilla (null s throws as in vanilla)

            if (!_compatChecked)
            {
                _compatChecked = true;
                Compat.Overlap(Vanilla.RemoveAmmo, "the machine junk fix");
            }

            object[] m = meta ?? item.CloneMeta(false, amount); // = Storage.cs:1247
            // Vanilla may turn these into another item (spoiled or full-grown); no 0.5.19 junk has them, so leave them to vanilla.
            if (ItemMetaUtilities.GetMetaOfType<ItemPerishabledata>(m) != null || ItemMetaUtilities.GetMetaOfType<ItemAnimalData>(m) != null)
                return s.AddItem(item, amount, owner, m, existedInSeconds, overflowToPlayer);

            Vanilla.StorageChanged(s) = true; // = Storage.cs:1244
            // = Storage.cs:1284 without the drop.
            int rem = s.Slots[0].AddItemAndReturnRemaining(item, amount, owner, m, false, null, false, 0, Vanilla.DropValues(s));
            if (rem > 0) Queue.Add(new Pending(s, item, rem, owner, m));
            return true;
        }

        internal static void Drain(CraftingBase cb)
        {
            if (Queue.Count == 0) return;
            var batch = Queue.ToArray();
            Queue.Clear();
            foreach (var p in batch)
            {
                try { Route(cb, p); }
                catch (Exception e) { Log.Exception("junk drop", e); }
            }
        }

        private static void Route(CraftingBase cb, Pending p)
        {
            int rem = p.Rem;
            string src = "?";
            try { src = cb != null ? Tally.StationName(cb.gameObject) : "?"; }
            catch (Exception e) { Log.Exception("junk name", e); }

            // Set just before each insert. An insert that throws may already have stored the item before its caller saw
            // the new remainder, so after such an exception nothing is dropped: dropping the stale rem could duplicate it.
            bool inserting = false;
            try
            {
                // 1. The slot the emptied ingredient just left (CraftingBase.cs:1299 ran before this finalizer), but only on
                //    a machine whose results go to a separate OutputStorage. CreateItems puts the result and returned
                //    containers into OutputStorage ?? InputOutputStorage (CraftingBase.cs:605, :660, :694), so on every
                //    other machine the freed slot is the room vanilla's result lands in later; junk there would make the
                //    result spill instead. In the 0.5.19 data only the Fine Logic mine upgrade has its own OutputStorage.
                var output = cb != null ? cb.OutputStorage : null;
                if (Cfg.JunkUseFreedSlot.Value && output != null && output != p.S)
                {
                    int before = rem;
                    inserting = true;
                    rem = p.S.Slots[0].AddItemAndReturnRemaining(p.Item, rem, p.Owner, p.M, false, null, false, 0, Vanilla.DropValues(p.S));
                    inserting = false;
                    Tally.Add(Kind.Junk, Outcome.Machine, src, p.Item, before - rem);
                }
                // 2. The inventory, under vanilla's main-output rule (overflowToPlayer = manuActive, Storage.cs:1286-1289):
                //    only while the station window is open. Junk from someone else's ingredient skips it (no theft, D-6).
                if (rem > 0 && Cfg.JunkToInventory.Value && cb != null && Vanilla.ManuActive(cb) && p.Owner < 2)
                {
                    int before = rem;
                    inserting = true;
                    rem = ItemOperations.AddItemAndReturnRemaining(p.Item, p.Owner, null, p.M, rem); // = Storage.cs:1288
                    inserting = false;
                    Tally.Add(Kind.Junk, Outcome.Inventory, src, p.Item, before - rem);
                }
                // 3. Player-owned storage near the machine (never the machine's own storages). Insert lowers rem after
                //    each storage; an exception inside one storage's insert still counts as an insert.
                if (rem > 0 && Cfg.JunkToNearbyStorage.Value && cb != null)
                {
                    inserting = true;
                    NearbyStorage.Insert(cb.gameObject, p.Item, ref rem, p.Owner, p.M, cb.gameObject, src, Kind.Junk,
                        cb.OutputStorage, cb.InputOutputStorage, p.S);
                    inserting = false;
                }
            }
            catch (Exception e)
            {
                Log.Exception("junk routing", e);
                if (inserting && rem > 0)
                {
                    Log.WarnOnce("junk-gaveup", $"Junk: an error interrupted putting away {rem} {Tally.ItemName(p.Item.ID)} from {src}; " +
                        "the rest was not dropped, so the item cannot be duplicated.");
                    rem = 0;
                }
                // Otherwise nothing was stored yet: fall through to the drop with the current rem.
            }

            if (rem > 0)
            {
                // = AddToSlots' dropper and drop values (Storage.cs:1280, :1284).
                var spot = Vanilla.OverflowSpot(p.S);
                ItemOperations.DropItem(p.Item, rem, p.Owner, p.M, spot != null ? spot : p.S.gameObject, Vanilla.DropValues(p.S));
                Tally.Add(Kind.Junk, Outcome.Dropped, src, p.Item, rem);
            }
        }
    }
}
