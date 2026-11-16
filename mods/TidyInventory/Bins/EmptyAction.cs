using System;
using System.Collections.Generic;
using System.Linq;

namespace TidyInventory
{
    // U-011 / S-092 "Empty" (spec §4.3): the first click arms the button ("Sure?"), a second click within ConfirmSeconds
    // deletes every stack in the open bin that Bins.Protected allows, each with the game's own SlotController.RemoveItem
    // (never Storage.Empty, which would also reset spawns). Nothing is created or moved.
    internal static class EmptyAction
    {
        internal static void Click()
        {
            try { ClickInner(); }
            catch (Exception e) { Log.Exception("empty", e); }
        }

        private static void ClickInner()
        {
            var bin = Storage.active;
            var inv = Inventory.instance;
            if (bin == null || inv == null || bin.Slots == null || inv.ForeignSlots != bin.Slots) return;
            if (!Cfg.Bins.Enabled.Value || !Bins.IsBin(bin) || Game.TradeActive || Game.SliderOpen) return;
            if (Game.DragActive)
            {
                Hints.Notify(Strings.DropFirst);
                return;
            }

            var deletable = new List<SlotController>();
            var kept = new Dictionary<Bins.Keep, int>();
            foreach (var sc in bin.Slots)
            {
                if (!Game.NonEmpty(sc)) continue;
                if (Bins.Protected(sc, out var why)) { kept.TryGetValue(why, out int n); kept[why] = n + 1; }
                else deletable.Add(sc);
            }
            int keptCount = kept.Values.Sum();
            if (deletable.Count == 0)
            {
                Confirm.Reset("empty");
                Hints.Notify(keptCount > 0 ? Strings.NothingToThrowKept(kept) : Strings.NothingToThrow);
                return;
            }

            if (!Confirm.IsArmed("empty", bin))
            {
                Confirm.Arm("empty", bin, Cfg.Bins.ConfirmSeconds.Value, () => StorageButtons.SetEmptyLabel(false));
                StorageButtons.SetEmptyLabel(true);
                return;
            }
            Confirm.Reset("empty");

            int items = 0;
            var removed = new HashSet<SlotController>();
            foreach (var sc in deletable)
            {
                if (!Game.NonEmpty(sc)) continue;
                items += sc.itemStack.itemAmount;
                sc.RemoveItem();
                removed.Add(sc);
            }
            StorageButtons.PlayTakeAllSound();
            Game.ClearHoverFor(removed.Contains);
            Hints.Notify(Strings.ThrewAway(items, kept));
            Log.Info($"Bins: emptied \"{bin.title}\": {items} items in {removed.Count} stacks thrown away, {keptCount} stacks kept.");
            if (kept.Count > 0) Log.Verbose("Bins: kept " + string.Join(", ", kept.Select(kv => $"{kv.Value} {kv.Key}").ToArray()) + ".");
        }
    }
}
