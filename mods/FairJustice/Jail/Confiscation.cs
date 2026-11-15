using System;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FairJustice
{
    // S-034 (spec §3.2A). Vanilla Crime.ConfiscateItems (SRC/Crime.cs:492-519) moves every owned stack into the cell
    // locker with deleteFailed; what does not fit is dropped at the locker as an expiring pickup (Storage.AddToSlots,
    // dropItems), which despawns once the player is more than 10 m away. This void prefix never skips vanilla: it first
    // takes out the owned stacks the locker might not hold (counted conservatively, ignoring merges) and puts them in
    // the jail's own orange bag (the vanilla overflow package, Prison.cs:368-381), so everything vanilla then moves
    // fits. Stolen goods are never bagged; vanilla deletes them as before.
    //
    // D-4 (KeepPermittedItems, spec §3.2D): vanilla also deletes every stack with ownerId > 0, including stacks the
    // player was allowed to have. Those go into the locker first, through the same Storage add vanilla uses for owned
    // stacks, so they keep their owner and metas (permission included). Only a permitted stack that does not fit goes
    // into the bag, which gives owner 0 as it does for everything (Prison.cs:368-381 does the same to other cells'
    // lockers). Stacks without permission are never bagged, so nothing stolen is ever made clean.
    internal static class Confiscation
    {
        internal static bool Patched;

        internal static void Run(Storage locker, ref bool clearStorage, Transform backpackspot)
        {
            if (!Patched) return;
            bool bagOn = Cfg.KeepOverflowInBag.Value, keepOn = Cfg.KeepPermittedItems.Value;
            if (!bagOn && !keepOn) return;
            var prison = Jail.Current;
            var inv = Inventory.instance;
            if (prison == null || inv == null || locker == null || locker.Slots == null || locker.Slots.Length == 0) return;
            if (!IsCellLocker(prison, locker)) return;   // a caller other than the prison: pure vanilla
            var point = Vanilla.OverflowPackagePoint != null ? Vanilla.OverflowPackagePoint(prison) : null;
            var prefab = Vanilla.OverflowPackagePrefab != null ? Vanilla.OverflowPackagePrefab(prison) : null;
            var bag = new Bag(point, prefab);

            int kept = 0, dropped = 0;
            if (keepOn) KeepPermitted(inv, locker, ref clearStorage, backpackspot, bagOn ? bag : null, ref kept, ref dropped);
            if (bagOn && bag.Available) BagOverflow(inv, locker, clearStorage, backpackspot, bag);

            if (kept > 0)
                Log.Info($"Confiscation: {kept} stacks you were allowed to have put in the cell locker with their owner kept" +
                         (dropped > 0 ? $"; {dropped} did not fit and were dropped at the locker as the game does." : "."));
            else if (dropped > 0)
                Log.Info($"Confiscation: {dropped} stacks you were allowed to have did not fit the cell locker; dropped at the locker as the game does.");

            if (bag.Count == 0) return;
            int bagged = bag.Count;
            Log.Info($"Confiscation: {bagged} stacks might not fit the cell locker; put in the bag.");
            Toast.Show("bag", "Jail", $"{bagged} {(bagged == 1 ? "stack" : "stacks")} that might not fit your cell locker {(bagged == 1 ? "is" : "are")} in the orange bag.");
            if (Cfg.WardenMentionsBag.Value)
            {
                // Vanilla writes "HasBagItems " with a trailing space (Prison.cs:351, :385), so the dialogue never sees it.
                try { DialogueLua.SetActorField("Generic_Prison_guard", "HasBagItems", true); }
                catch (Exception e) { Log.Exception("warden bag flag", e); }
            }
        }

        // D-4. Runs before BagOverflow, so these stacks get locker space first and keep their owner wherever possible.
        private static void KeepPermitted(Inventory inv, Storage locker, ref bool clearStorage, Transform backpackspot, Bag bag,
            ref int kept, ref int dropped)
        {
            var bagSlot = inv.CharacterSlots != null && inv.CharacterSlots.Length > 5 ? inv.CharacterSlots[5] : null;
            var worn = bagSlot?.itemStack;

            // A worn backpack the player was allowed to have goes to the cell's backpack spot, exactly as vanilla sends
            // an owned one (:499-505, same call). Vanilla's own step then finds the slot empty, and the owned and
            // permitted contents stay in the backpack's storage, as they do for an owned bag. Without a spot (every
            // vanilla cell has one) it is left to vanilla.
            if (backpackspot != null && worn != null && worn.itemAmount > 0 && worn.itemReference != null && Owned.IsPermitted(worn))
            {
                if (Vanilla.CanSpawnItem)
                {
                    // Once the bag is off, AllSlots no longer lists its storage and vanilla cannot reach the stolen stacks
                    // inside. Vanilla would delete them (the bag is still worn when it runs GetPlayerItems), so delete
                    // them here first, with the same test.
                    int deleted = 0;
                    var bp = Owned.WornBackpack();
                    if (bp?.Slots != null)
                        foreach (var s in bp.Slots)
                        {
                            var st = s?.itemStack;
                            if (st == null || st.itemAmount <= 0 || st.ownerId <= 0 || Owned.IsPermitted(st)) continue;
                            st.RemoveStack();
                            s.Update();
                            deleted++;
                        }
                    string title = worn.itemReference.Item?.Title;
                    Vanilla.SpawnItemToPosition(worn, backpackspot.position, true);
                    worn.RemoveStack();
                    Log.Info($"Confiscation: the {title} you were allowed to have went to the cell's backpack spot with your items in it" +
                             (deleted > 0 ? $"; {deleted} stolen stacks in it were confiscated as the game does." : "."));
                }
            }

            // Recomputed: without a worn bag, AllSlots no longer lists its storage (Inventory.cs:191-206).
            var wornSlots = WornSlotsMovingWithBag(inv, bagSlot, backpackspot);
            bool emptied = false;
            foreach (var s in inv.AllSlots())   // vanilla order: main slots, worn backpack, character slots
            {
                if (s == null || ReferenceEquals(s, bagSlot)) continue;   // the worn bag: handled above or left to vanilla
                if (wornSlots != null && Array.IndexOf(wornSlots, s) >= 0) continue;   // moves with an owned bag
                var st = s.itemStack;
                if (st == null || st.itemAmount <= 0 || st.itemReference == null || !Owned.IsPermitted(st)) continue;
                var item = st.itemReference.Item;
                if (item == null) continue;

                // Vanilla's first step, done here so the locker can take these stacks now; vanilla then skips it.
                if (!emptied && clearStorage)
                {
                    locker.Empty();
                    clearStorage = false;
                }
                emptied = true;

                int amount = st.itemAmount;
                // The Storage add vanilla's MoveStackToStorage reaches (Storage.cs:1136-1144), without its drop: the
                // stack keeps its ownerId and metas (ItemStack.AddItemToPanel, :238-240).
                int rem = locker.AddItemAndReturnRemaining(item, amount, st.ownerId, st.Meta);
                if (rem <= 0)
                {
                    Log.Verbose($"Confiscation: {amount} x {item.Title} (owner {st.ownerId}) put in the cell locker.");
                    st.RemoveStack();
                    s.Update();
                    kept++;
                    continue;
                }
                if (rem < amount)
                {
                    st.itemAmount = rem;
                    kept++;
                }
                // Did not (all) fit. With KeepOverflowInBag on, the rest goes into the bag (owner 0, metas kept).
                // Otherwise vanilla's own-item path: the locker takes what it can and the rest is dropped at the locker
                // (Storage.AddToSlots, dropItems).
                if (bag != null && locker.Slots[0].CheckIfAllowed(item) && bag.Put(st, item))
                {
                    s.Update();
                    continue;
                }
                Log.Verbose($"Confiscation: {rem} x {item.Title} (owner {st.ownerId}) did not fit the cell locker; the game drops it there.");
                ItemOperations.MoveStackToStorage(st, locker, deleteFailed: true);
                s.Update();
                dropped++;
            }
        }

        // The original overflow bag (spec §3.2A).
        private static void BagOverflow(Inventory inv, Storage locker, bool clearStorage, Transform backpackspot, Bag bag)
        {
            // Vanilla empties the locker first when clearStorage is set.
            int free = 0;
            if (clearStorage) free = locker.Slots.Length;
            else
                foreach (var s in locker.Slots)
                    if (s != null && s.IsEmpty()) free++;

            var bagSlot = inv.CharacterSlots != null && inv.CharacterSlots.Length > 5 ? inv.CharacterSlots[5] : null;
            var wornSlots = WornSlotsMovingWithBag(inv, bagSlot, backpackspot);

            foreach (var s in inv.AllSlots())   // vanilla order: main slots, worn backpack, character slots
            {
                if (s == null || ReferenceEquals(s, bagSlot)) continue;
                if (wornSlots != null && Array.IndexOf(wornSlots, s) >= 0) continue;   // moves with the bag
                var st = s.itemStack;
                if (st == null || st.itemAmount <= 0 || st.itemReference == null) continue;
                // Stolen (and, with KeepPermittedItems off, permission) stacks: vanilla deletes them. Bagging them would
                // launder them, since the bag gives everything to owner 0.
                if (st.ownerId > 0) continue;
                var item = st.itemReference.Item;
                if (item == null) continue;
                if (!locker.Slots[0].CheckIfAllowed(item))
                {
                    // Unstorable (a pig in hand): vanilla's drop, unchanged.
                    Log.Verbose($"Confiscation: {item.Title} cannot go in a locker; the game drops it as usual.");
                    continue;
                }
                // Worst case: every stack needs empty slots of its own (no merges). Conservative, so every stack left
                // to vanilla is guaranteed to fit.
                int need = (st.itemAmount + Math.Max(item.Stackable, 1) - 1) / Math.Max(item.Stackable, 1);
                if (need <= free)
                {
                    free -= need;
                    continue;
                }
                if (!bag.Put(st, item)) continue;
                s.Update();
            }
        }

        // Vanilla's exact condition (:499-507) for the worn bag going to the cell's backpack spot with its contents.
        private static SlotController[] WornSlotsMovingWithBag(Inventory inv, SlotController bagSlot, Transform backpackspot)
        {
            var worn = bagSlot?.itemStack;
            bool bagToSpot = backpackspot != null && worn != null && worn.ownerId < 1 && worn.itemAmount > 0 && worn.itemReference != null;
            return bagToSpot ? Owned.WornBackpack()?.Slots : null;
        }

        // The jail's orange bag, fetched or created on first use.
        private sealed class Bag
        {
            private readonly Transform _point;
            private readonly GameObject _prefab;
            private CollectiblePackage _pkg;
            internal int Count;

            internal Bag(Transform point, GameObject prefab)
            {
                _point = point;
                _prefab = prefab;
            }

            internal bool Available => _point != null && _prefab != null;

            // Moves the whole stack into the bag. The inventory stack is removed only once the bag really holds it;
            // otherwise vanilla handles the stack.
            internal bool Put(ItemStack st, Item item)
            {
                if (!Available) return false;
                if (_pkg == null) _pkg = GetOrCreateBag(_point, _prefab);
                if (_pkg == null) return false;
                int before = _pkg.items.Count;
                try { _pkg.AddItem(st.itemId, st.itemAmount, st.Meta); }
                catch (Exception e) { Log.Exception("bag AddItem", e); }
                if (_pkg.items.Count <= before) return false;
                Log.Verbose($"Confiscation: {st.itemAmount} x {item.Title} (owner {st.ownerId}) might not fit the cell locker; put in the bag.");
                st.RemoveStack();
                Count++;
                return true;
            }
        }

        private static bool IsCellLocker(Prison p, Storage s)
        {
            if (p.prisonCells == null) return false;
            foreach (var c in p.prisonCells)
                if (c != null && c.confiscatedStorage == s) return true;
            return false;
        }

        // Copied from Prison.ConfiscateItems (SRC/Prison.cs:368-381).
        private static CollectiblePackage GetOrCreateBag(Transform point, GameObject prefab)
        {
            CollectiblePackage pkg = point.childCount == 0
                ? Object.Instantiate(prefab, point).GetComponent<CollectiblePackage>()
                : point.GetChild(0).gameObject.GetComponent<CollectiblePackage>();
            if (pkg != null) pkg.useItemSpawnInfo = false;
            return pkg;
        }
    }

    // P12: Crime.ConfiscateItems(Storage, bool, Transform). Void prefix; vanilla always runs afterwards, so on any
    // error the result is never worse than vanilla. clearStorage is ref only so that KeepPermittedItems can empty the
    // locker itself (vanilla's first step) and then tell vanilla not to empty it again.
    [HarmonyPatch(typeof(Crime), nameof(Crime.ConfiscateItems), new[] { typeof(Storage), typeof(bool), typeof(Transform) })]
    internal static class ConfiscateItemsPatch
    {
        private static void Prefix(Storage targetStorage, ref bool clearStorage, Transform backpackspot)
        {
            try { Confiscation.Run(targetStorage, ref clearStorage, backpackspot); }
            catch (Exception e) { Log.Exception("ConfiscateItems prefix", e); }
        }
    }
}
