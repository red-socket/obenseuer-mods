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
    internal static class Confiscation
    {
        internal static bool Patched;

        internal static void Run(Storage locker, bool clearStorage, Transform backpackspot)
        {
            if (!Patched || !Cfg.KeepOverflowInBag.Value) return;
            var prison = Jail.Current;
            var inv = Inventory.instance;
            if (prison == null || inv == null || locker == null || locker.Slots == null || locker.Slots.Length == 0) return;
            if (!IsCellLocker(prison, locker)) return;   // a caller other than the prison: pure vanilla
            if (Vanilla.OverflowPackagePoint == null || Vanilla.OverflowPackagePrefab == null) return;
            var point = Vanilla.OverflowPackagePoint(prison);
            var prefab = Vanilla.OverflowPackagePrefab(prison);
            if (point == null || prefab == null) return;

            // Vanilla empties the locker first when clearStorage is set.
            int free = 0;
            if (clearStorage) free = locker.Slots.Length;
            else
                foreach (var s in locker.Slots)
                    if (s != null && s.IsEmpty()) free++;

            // Vanilla's exact condition (:499-507) for the worn bag going to the cell's backpack spot with its contents.
            var bagSlot = inv.CharacterSlots != null && inv.CharacterSlots.Length > 5 ? inv.CharacterSlots[5] : null;
            var bag = bagSlot?.itemStack;
            bool bagToSpot = backpackspot != null && bag != null && bag.ownerId < 1 && bag.itemAmount > 0 && bag.itemReference != null;
            var wornSlots = bagToSpot ? Owned.WornBackpack()?.Slots : null;

            CollectiblePackage pkg = null;
            int bagged = 0;
            foreach (var s in inv.AllSlots())   // vanilla order: main slots, worn backpack, character slots
            {
                if (s == null || ReferenceEquals(s, bagSlot)) continue;
                if (wornSlots != null && Array.IndexOf(wornSlots, s) >= 0) continue;   // moves with the bag
                var st = s.itemStack;
                if (st == null || st.itemAmount <= 0 || st.itemReference == null) continue;
                // Stolen (and permission) stacks: vanilla deletes them. Bagging them would launder them, since the bag
                // gives everything to owner 0.
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
                if (pkg == null) pkg = GetOrCreateBag(point, prefab);
                if (pkg == null) break;
                int before = pkg.items.Count;
                try { pkg.AddItem(st.itemId, st.itemAmount, st.Meta); }
                catch (Exception e) { Log.Exception("bag AddItem", e); }
                // Remove from the inventory only once the bag really holds it; otherwise vanilla handles the stack.
                if (pkg.items.Count > before)
                {
                    Log.Verbose($"Confiscation: {st.itemAmount} x {item.Title} might not fit the cell locker; put in the bag.");
                    st.RemoveStack();
                    s.Update();
                    bagged++;
                }
            }

            if (bagged == 0) return;
            Log.Info($"Confiscation: {bagged} stacks might not fit the cell locker; put in the bag.");
            Toast.Show("bag", "Jail", $"{bagged} {(bagged == 1 ? "stack" : "stacks")} that might not fit your cell locker {(bagged == 1 ? "is" : "are")} in the orange bag.");
            if (Cfg.WardenMentionsBag.Value)
            {
                // Vanilla writes "HasBagItems " with a trailing space (Prison.cs:351, :385), so the dialogue never sees it.
                try { DialogueLua.SetActorField("Generic_Prison_guard", "HasBagItems", true); }
                catch (Exception e) { Log.Exception("warden bag flag", e); }
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
    // error the result is never worse than vanilla.
    [HarmonyPatch(typeof(Crime), nameof(Crime.ConfiscateItems), new[] { typeof(Storage), typeof(bool), typeof(Transform) })]
    internal static class ConfiscateItemsPatch
    {
        private static void Prefix(Storage targetStorage, bool clearStorage, Transform backpackspot)
        {
            try { Confiscation.Run(targetStorage, clearStorage, backpackspot); }
            catch (Exception e) { Log.Exception("ConfiscateItems prefix", e); }
        }
    }
}
