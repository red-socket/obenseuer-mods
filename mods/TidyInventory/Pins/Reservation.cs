using System;
using System.Collections.Generic;

namespace TidyInventory
{
    // The slot reservation (spec §3.1, §3.2). Each main inventory slot gets an OnDroppedCheck veto that keeps a pinned
    // slot for its container family. The game honours that veto in SlotController.AddItemAndReturnRemaining (:67, which
    // still tries Siblings) and AddItem(ref ...) (:133); DropItem ignores it (P4 handles drag and drop),
    // SlotHasSpaceForItem doesn't ask (P16) and ForceItemToSlot (a drag returning to its origin) bypasses it, which is
    // right. Delegates are never serialized (SRC/FullSerializer fsMetaType.cs:196), and loading restores stacks into the
    // same SlotController objects (SRC/Inventory.cs:160-189), so the veto survives loads within a scene.
    internal static class Reservation
    {
        internal static readonly Dictionary<SlotController, int> Index = new Dictionary<SlotController, int>();
        internal static readonly Dictionary<Slot, int> SlotIndex = new Dictionary<Slot, int>();

        private static SlotController[] _slots;
        private static Func<Item, int, int, object, bool>[] _ours;

        // Depth counters: a yielded slot accepts anything for the duration of a vanilla call that puts a different item
        // back into the same slot (spoilage, growth, an equip swap). Incremented in prefixes, decremented in finalizers.
        private static readonly Dictionary<SlotController, int> YieldDepth = new Dictionary<SlotController, int>();

        // P1: Inventory.Start postfix. Character slots also have playerSlot set, so the index comes from Slots only.
        internal static void OnInventoryStart(Inventory inv)
        {
            Index.Clear();
            SlotIndex.Clear();
            YieldDepth.Clear();
            PinOverlay.OnInventoryStart();
            _slots = inv != null ? inv.Slots : null;
            if (_slots == null) { _ours = null; return; }
            _ours = new Func<Item, int, int, object, bool>[_slots.Length];
            for (int i = 0; i < _slots.Length; i++)
            {
                var sc = _slots[i];
                if (sc == null) continue;
                Index[sc] = i;
                if (sc.slot != null) SlotIndex[sc.slot] = i;
                int idx = i;
                var prev = sc.OnDroppedCheck;
                Func<Item, int, int, object, bool> ours = (item, amt, owner, meta) =>
                    (prev == null || prev(item, amt, owner, meta)) && Allows(idx, sc, item);
                _ours[i] = ours;
                sc.OnDroppedCheck = ours;
            }
            Log.Verbose($"Pins: reservation installed on {_slots.Length} inventory slots.");
        }

        // The veto itself. Never throws: an exception allows (vanilla behaviour).
        private static bool Allows(int i, SlotController sc, Item item)
        {
            try { return !Vetoes(i, sc, item); }
            catch (Exception e) { Log.Exception("reservation check", e); return true; }
        }

        // True when the pinned slot i must refuse item.
        private static bool Vetoes(int i, SlotController sc, Item item)
        {
            if (item == null || !PinStore.Active || !PinStore.Map.TryGetValue(i, out var p)) return false;
            if (Yielding(sc)) return false;
            if (PinStore.InFamily(p, item.ID)) return false;
            if (!Cfg.Pins.KeepReservationWhenEmpty.Value && !Game.NonEmpty(sc)) return false;
            return true;
        }

        internal static bool Vetoes(SlotController sc, Item item) =>
            sc != null && PinStore.Active && Index.TryGetValue(sc, out int i) && Vetoes(i, sc, item);

        internal static bool IsPinnedMain(SlotController sc, out int index, out Pin pin)
        {
            pin = default;
            index = -1;
            if (sc == null || !PinStore.Active || !Index.TryGetValue(sc, out index)) return false;
            return PinStore.Map.TryGetValue(index, out pin);
        }

        internal static bool IsPinnedMain(SlotController sc) => IsPinnedMain(sc, out _, out _);

        // Filled: non-empty and in the pinned family. Only filled slots are locked; foreign content never is.
        internal static bool Locked(SlotController sc) =>
            IsPinnedMain(sc, out _, out var p) && Game.NonEmpty(sc) && PinStore.InFamily(p, sc.itemStack.itemId);

        internal static bool MoveLocked(SlotController sc) => Cfg.Pins.BlockManualMoves.Value && Locked(sc);

        // Selling a pinned stack is always blocked, whatever BlockManualMoves says.
        internal static bool SellLocked(SlotController sc) => Locked(sc);

        // SellLocked for an item on its way somewhere: the slot's own stack, or items of the pin's family being dragged
        // out of a pinned slot. A drag takes its amount out of the slot when it starts (Slot.OnBegingDrag → TakeItem,
        // SRC/Slot.cs:374), so a whole dragged stack leaves its slot empty and Locked alone would let it through.
        internal static bool SellLockedItem(ItemData d)
        {
            if (d == null) return false;
            var sc = d.slotController;
            if (SellLocked(sc)) return true;
            return d.isDragging && d.item != null && IsPinnedMain(sc, out _, out var p) && PinStore.InFamily(p, d.item.ID);
        }

        // ---- Yield ----

        internal static bool Yielding(SlotController sc) => sc != null && YieldDepth.TryGetValue(sc, out int d) && d > 0;

        internal static bool Enter(SlotController sc)
        {
            if (sc == null || !IsPinnedMain(sc)) return false;
            YieldDepth.TryGetValue(sc, out int d);
            YieldDepth[sc] = d + 1;
            return true;
        }

        internal static void Exit(SlotController sc)
        {
            if (sc == null || !YieldDepth.TryGetValue(sc, out int d)) return;
            if (d <= 1) YieldDepth.Remove(sc);
            else YieldDepth[sc] = d - 1;
        }

        // On each pin change and at LoadingDone: another mod may have replaced our veto. We never re-install it.
        internal static void Verify()
        {
            if (_slots == null || _ours == null || PinStore.Map.Count == 0) return;
            foreach (var i in PinStore.Map.Keys)
            {
                if (i < 0 || i >= _slots.Length || _slots[i] == null) continue;
                if (!ReferenceEquals(_slots[i].OnDroppedCheck, _ours[i]))
                {
                    Log.WarnOnce("veto-replaced", "Pins: another mod replaced the inventory slot check; pinned slots may take other items.");
                    return;
                }
            }
        }

        internal static SlotController SlotAt(int i) => _slots != null && i >= 0 && i < _slots.Length ? _slots[i] : null;
    }
}
