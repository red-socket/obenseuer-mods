using System;
using System.Collections.Generic;
using System.Linq;
using OS.Items;
using UnityEngine;

namespace FewerClicks
{
    // U-005 / S-049(a) "Top up" (spec §3): moves matching items from the inventory onto stacks the open storage already
    // has, capped at each stack's free space, so nothing reaches an empty slot (#5826). Every move is the vanilla
    // shift-click call (SlotController.MoveToOtherInventory into a storage slot): owner, stolen flag, liquid and quality
    // rules, OnDropped and Fair Justice's frame all run unchanged, and storage targets get no theft check, exactly as
    // with shift-click.
    internal static class TopUp
    {
        internal static bool Patched;

        // A storage whose slots live in the storage panel (not crafting, vending or put-item storages).
        internal static bool IsTopUpStorage(StoragePanelUI ui, Storage storage)
        {
            if (ui == null || storage == null || ui.storageSlotPanel == null || Vanilla.SlotPanel == null) return false;
            return Vanilla.SlotPanel(storage) == ui.storageSlotPanel.transform;
        }

        internal static void Update()
        {
            if (!Patched || !Cfg.TopUp.Enabled.Value || Storage.active == null) return;
            if (Keys.Down(Cfg.TopUp.Hotkey)) Run();
        }

        // Button, hotkey and controller A.
        internal static void Run()
        {
            try { RunInner(); }
            catch (Exception e) { Log.Exception("top up", e); }
        }

        private static void RunInner()
        {
            if (!Patched || !Cfg.TopUp.Enabled.Value) return;
            var chest = Storage.active;
            var inv = Inventory.instance;
            if (chest == null || chest.Slots == null || chest.Slots.Length == 0 || inv == null) { Skip("no storage open"); return; }
            var ui = inv.storagePanelUI;
            if (inv.ForeignSlots != chest.Slots) { Skip("storage not bound to the inventory"); return; }
            if (!IsTopUpStorage(ui, chest)) { Skip("not a storage-panel storage"); return; }
            if (GameUIController.instance == null || !GameUIController.instance.StorageIsVisible()) { Skip("storage panel hidden"); return; }
            if (ui.takeAllButton == null || !ui.takeAllButton.activeSelf) { Skip("Take all hidden"); return; }
            // Re-checked here: a move during a trade would bypass Trade.MakeTrade.
            if (Game.TradeActive) { Skip("trade"); return; }
            if (Game.ConsoleOpen || Game.WriteLock || Game.SliderOpen) { Skip("console, typing or amount slider"); return; }
            if (Game.DragActive())
            {
                Game.Notify("Drop the item you are dragging first");
                return;
            }

            var sources = new List<SlotController>();
            if (inv.Slots != null) sources.AddRange(inv.Slots);
            Storage bp = null;
            if (Cfg.TopUp.IncludeBackpack.Value && BackpackStorage.instance != null)
            {
                bp = BackpackStorage.instance.GetStorage();
                if (bp != null && bp != chest && bp.Slots != null) sources.AddRange(bp.Slots);
                else bp = null;
            }
            var bpSlots = bp != null ? new HashSet<SlotController>(bp.Slots) : null;

            // Item types the storage holds before the run: the only candidates for both passes.
            var heldBefore = new HashSet<int>();
            foreach (var t in chest.Slots)
                if (t?.itemStack != null && t.itemStack.itemAmount > 0 && t.itemStack.itemId > 1) heldBefore.Add(t.itemStack.itemId);

            var skipped = new Dictionary<string, int>();
            var usable = new List<SlotController>();
            foreach (var s in sources)
            {
                var ss = s?.itemStack;
                if (ss == null || ss.itemAmount <= 0 || ss.itemReference == null || !heldBefore.Contains(ss.itemId)) continue;
                if (Movable(s, skipped)) usable.Add(s);
            }

            int items = 0, usedBackpack = 0;
            var stacksTouched = new HashSet<SlotController>();
            var sourcesUsed = new HashSet<SlotController>();

            // Strict pass: per target in slot order, then per source, smallest stacks first.
            foreach (var t in chest.Slots)
            {
                var ts = t?.itemStack;
                if (ts == null || ts.itemAmount <= 0 || ts.itemReference == null || ts.itemId <= 1) continue;
                var item = ts.itemReference.Item;
                if (item == null || ts.itemAmount >= item.Stackable || !t.CheckIfAllowed(item)) continue;

                foreach (var s in Matching(usable, ts.itemId))
                {
                    int free = item.Stackable - t.itemStack.itemAmount;   // read live: the stack size follows the difficulty
                    if (free <= 0) break;
                    var ss = s.itemStack;
                    if (ss == null || ss.itemId != t.itemStack.itemId || ss.itemAmount <= 0) continue;
                    int n = Math.Min(free, ss.itemAmount);
                    if (n <= 0) continue;

                    var settings = new ItemOperations.ItemOperationSettings();
                    if (ss.Meta != null && ItemMetaUtilities.GetMetaOfType<ItemOwnerPermission>(ss.Meta) != null)
                    {
                        // As SlotController.AddItemAndReturnRemaining does for owner-permission items (SRC/SlotController.cs:75-84).
                        settings.disableLiquidMerge = true;
                        settings.disableQualityMix = true;
                    }
                    // Check only (never onlycheck:false). rem != 0 means the merge would leave items over.
                    if (!SlotController.SlotHasSpaceForItem(item, t, t.itemStack, out int rem, ss.ownerId, n, n, ss.Meta, true, false, settings) || rem != 0)
                    {
                        Count(skipped, "won't stack");
                        continue;
                    }
                    // No siblings for this one call: whatever does not fit stays in the source instead of spilling into
                    // empty slots (#5826). Restored in finally.
                    var sib = t.Siblings;
                    t.Siblings = null;
                    int moved;
                    try { s.MoveToOtherInventory(t, n, out moved); }
                    finally { t.Siblings = sib; }
                    if (moved > 0)
                    {
                        items += moved;
                        stacksTouched.Add(t);
                        sourcesUsed.Add(s);
                        if (bpSlots != null && bpSlots.Contains(s)) usedBackpack++;
                    }
                    Deselect(s);
                }
            }

            // AllowNewStacks pass (default off): leftovers of types the storage held before the run, through the vanilla
            // shift-click spill into the storage's siblings (Better Tenement's Stash path).
            if (Cfg.TopUp.AllowNewStacks.Value)
            {
                var full = new HashSet<int>();
                foreach (var s in usable.Where(x => x.itemStack.itemAmount > 0 && heldBefore.Contains(x.itemStack.itemId)).ToList())
                {
                    var ss = s.itemStack;
                    if (ss.itemReference == null || ss.itemAmount <= 0 || full.Contains(ss.itemId)) continue;
                    int id = ss.itemId, amount = ss.itemAmount;
                    s.MoveToOtherInventory(chest.Slots[0], amount, out int m);
                    if (m > 0)
                    {
                        items += m;
                        sourcesUsed.Add(s);
                        if (bpSlots != null && bpSlots.Contains(s)) usedBackpack++;
                    }
                    else full.Add(id);
                    Deselect(s);
                }
            }

            if (usedBackpack > 0)
            {
                try { BackpackStorage.instance.UpdateEmptyState(); }
                catch (Exception e) { Log.Exception("top up backpack state", e); }
            }

            if (items > 0)
            {
                try
                {
                    if (Vanilla.TakeAllSound != null) MiscAudioPlayer.PlaySound(Vanilla.TakeAllSound(ui));
                }
                catch (Exception e) { Log.Exception("top up sound", e); }
            }
            else
            {
                try { ui.StorageFull(); }
                catch (Exception e) { Log.Exception("top up full flash", e); }
            }

            if (Log.VerboseOn)
            {
                int k = skipped.Values.Sum();
                string reasons = k == 0 ? "" : " (" + string.Join(", ", skipped.Select(p => $"{p.Value} {p.Key}").ToArray()) + ")";
                Log.Verbose($"Top up \"{chest.title}\": {items} items into {stacksTouched.Count} stacks from {sourcesUsed.Count} sources; {k} skipped{reasons}.");
            }
        }

        // Movable sources with the target's item ID, smallest stacks first (amounts read now, after earlier moves).
        private static List<SlotController> Matching(List<SlotController> usable, int id)
        {
            return usable.Where(s => s.itemStack.itemId == id && s.itemStack.itemAmount > 0).OrderBy(s => s.itemStack.itemAmount).ToList();
        }

        // Vanilla quick-move refuses disabled or move-locked items (SRC/ItemData.cs:549-556). A drained liquid container
        // turns into its empty item on add (ItemStack.AddItemToPanel), which is not a top-up.
        private static bool Movable(SlotController s, Dictionary<string, int> skipped)
        {
            var ss = s.itemStack;
            var slot = s.slot;
            if (slot != null)
            {
                var data = slot.GetItemItemData();
                if (data != null)
                {
                    bool moveOff = false;
                    if (Vanilla.ItemMoveDisabled != null) moveOff = Vanilla.ItemMoveDisabled(data);
                    else Log.WarnOnce("topup-movedisabled", "Top up: ItemData.itemMoveDisabled is missing; only itemDisabled is checked.");
                    if (data.itemDisabled || moveOff) { Count(skipped, "locked"); return false; }
                }
            }
            var liquid = ss.GetMetaOfType<ItemLiquidData>();
            if (liquid != null && liquid.GetLiquidAmount() <= 0f && liquid.emptyContainerItem != null) { Count(skipped, "empty container"); return false; }
            return true;
        }

        // QuickMove parity (SRC/ItemData.cs:577-580): a source that emptied is no longer shown as selected.
        private static void Deselect(SlotController s)
        {
            try
            {
                if (s.itemStack != null && (s.itemStack.itemId == -1 || s.itemStack.itemAmount <= 0) && s.slot != null)
                    s.slot.SetSelectedStatus(false);
            }
            catch (Exception e) { Log.Exception("top up deselect", e); }
        }

        private static void Count(Dictionary<string, int> d, string key)
        {
            d.TryGetValue(key, out int n);
            d[key] = n + 1;
        }

        private static void Skip(string why) => Log.Verbose("Top up: nothing done (" + why + ").");
    }
}
