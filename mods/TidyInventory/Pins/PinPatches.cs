using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OS.Items;
using UnityEngine;

namespace TidyInventory
{
    // Pinned Slots (U-013 / S-056, spec §3): the reservation, the blocking patches and the redirect. Every bool prefix
    // passes through when another mod already skipped the original (__runOriginal false), and every body falls back to
    // vanilla on an exception: a prefix returns true and __result is left alone. Nothing here moves, creates or removes
    // an item: blocks return vanilla's own "not allowed" result, and the redirect only picks the start slot of an add
    // vanilla was already making.
    internal static class PinPatches
    {
        internal static bool Patched;         // "pins" group (P1-P13)
        internal static bool ReturnPatched;   // P14, P15
        internal static bool CapacityPatched; // P16, P16a-c

        // Depth counters (main thread): P16a-c let rewards through the capacity check, P15a keeps an emptied skinned or
        // permitted container in its own slot.
        internal static int CapacityExempt;
        internal static int NoRedirect;

        private static readonly Dictionary<string, float> BlockLogged = new Dictionary<string, float>();
        private static int _redirects;

        internal static void OnSceneLoaded()
        {
            if (_redirects > 0) Log.Verbose($"Pins: {_redirects} adds went to pinned slots first in the last scene.");
            _redirects = 0;
        }

        // Verbose, at most once per choke point every 2 real seconds.
        internal static void Blocked(string where, SlotController sc)
        {
            if (!Log.VerboseOn) return;
            float now = Time.unscaledTime;
            if (BlockLogged.TryGetValue(where, out float at) && now - at < 2f) return;
            BlockLogged[where] = now;
            int i = -1;
            if (sc != null) Reservation.Index.TryGetValue(sc, out i);
            Log.Verbose($"Pins: blocked {where} on pinned slot {i + 1}.");
        }

        // ---- §3.5 ReturnItemsToPin (P14, P15) ----

        // New copies of a pinned item start at its slot. Only for adds that start in the main inventory (no slot given,
        // or any main slot); storage, character and backpack starts are never redirected. Vanilla then fills that slot,
        // overflows through Siblings and runs the crime check with this main slot (SRC/ItemOperations.cs:79-83, :142-146).
        internal static void Redirect(Item item, int amount, int owner, object[] meta, ref SlotController slot)
        {
            if (!ReturnPatched || NoRedirect > 0 || !Cfg.Pins.ReturnItemsToPin.Value || !PinStore.Active || item == null || item.ID <= 1 || amount <= 0) return;
            if (slot != null && !Reservation.Index.ContainsKey(slot)) return;
            foreach (var kv in PinStore.Map)
            {
                if (!PinStore.InFamily(kv.Value, item.ID)) continue;
                var sc = Reservation.SlotAt(kv.Key);
                if (sc == null) continue;
                bool fits = false;
                if (!Game.NonEmpty(sc)) fits = Cfg.Pins.KeepReservationWhenEmpty.Value && sc.CheckIfAllowed(item);
                else if (sc.itemStack.itemId == item.ID)
                {
                    // True when everything fits; false with rem < amount when part of it does.
                    bool all = sc.SlotHasSpaceForItem(item, out int rem, owner, amount, 0, meta);
                    fits = all || rem < amount;
                }
                if (!fits) continue;
                if (slot != sc) _redirects++;
                slot = sc;
                return;
            }
        }

        // P4: a different member of a locked pin's family dropped on it. Vanilla pours (LiquidContainersSwap) only when
        // neither side has an owner and there is one liquid to move (SRC/SlotController.cs:427-433, :460-490); otherwise it
        // swaps the whole pinned stack out (:272-283). True when the pour path will run.
        internal static bool PourLikely(ItemData data, SlotController target)
        {
            if (data.owner >= 1 || target.itemStack.ownerId >= 1) return false;
            var a = ItemMetaUtilities.GetMetaOfType<ItemLiquidData>(data.meta)?.liquidItem;
            var b = target.itemStack.GetMetaOfType<ItemLiquidData>()?.liquidItem;
            if (a == null && b == null) return false;
            return a == null || b == null || a.ID == b.ID;
        }
    }

    // P1: Inventory.Start (SRC/Inventory.cs:213), private. Installs the reservation veto on the main slots.
    [HarmonyPatch]
    internal static class InventoryStartPatch
    {
        private static MethodBase TargetMethod() => Vanilla.InventoryStart;

        private static void Postfix(Inventory __instance)
        {
            try { Reservation.OnInventoryStart(__instance); }
            catch (Exception e) { Log.Exception("inventory start", e); }
        }
    }

    // P2: Inventory.SortSlots(SlotController[]) (SRC/Inventory.cs:483). Sorting the main inventory sorts every slot but
    // the pinned ones, whatever they hold. Storage sorts (InventoryPanelUI.cs:71-78, Trade.cs:343) pass through.
    [HarmonyPatch]
    internal static class SortSlotsPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SortSlots;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref SlotController[] slotToSort, bool __runOriginal)
        {
            if (!__runOriginal) return true;
            try
            {
                if (!PinStore.Active) return true;
                var inv = Inventory.instance;
                if (inv == null || inv.Slots == null) return true;
                if (slotToSort != null && !ReferenceEquals(slotToSort, inv.Slots)) return true;
                var keep = new List<SlotController>(inv.Slots.Length);
                for (int i = 0; i < inv.Slots.Length; i++)
                    if (!PinStore.Map.ContainsKey(i)) keep.Add(inv.Slots[i]);
                if (keep.Count == inv.Slots.Length) return true;
                if (keep.Count == 0) return false;
                slotToSort = keep.ToArray();
                Log.Verbose($"Pins: sorting {keep.Count} slots, {inv.Slots.Length - keep.Count} pinned slots left in place.");
                return true;
            }
            catch (Exception e) { Log.Exception("sort", e); return true; }
        }
    }

    // P3: SlotController.MoveToOtherInventory(SlotController, int, out int) (SRC/SlotController.cs:666): shift, ctrl and
    // double-click with a storage open, hold-move, the slider, the controller's Move Item, SellAndMove, Top up, Stash.
    // Fair Justice's prefix on the same method still runs (HarmonyX runs every prefix) and its finalizer pops its frame.
    [HarmonyPatch]
    internal static class MoveToOtherInventoryPatch
    {
        private static MethodBase TargetMethod() => Vanilla.MoveToOtherInventory;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SlotController __instance, ref int movedAmount, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal) return true;
            try
            {
                if (!Reservation.MoveLocked(__instance)) return true;
                movedAmount = 0;
                __result = false;
                PinPatches.Blocked("move", __instance);
                return false;
            }
            catch (Exception e) { Log.Exception("move block", e); return true; }
        }
    }

    // P4: SlotController.DropItem(ItemData, out int, bool, bool, bool) (SRC/SlotController.cs:195), drag and drop. The game
    // reads OnDroppedCheck here but overwrites the result (:205-215), so the reservation is enforced here instead.
    // (0) The bottle recycler's drop slot (its OnDropped is BottleRecyclingDrop.ItemInserted, which recycles the item
    //     for coins, SRC/BottleRecyclingDrop.cs:21-27): a pinned stack's items are refused like a veto, whatever
    //     BlockManualMoves says (P10 covers the double-click).
    // (1) Target pinned: a non-family item gets vanilla's own not-allowed result (remaining = amount, false: Slot.OnDrop
    //     returns it and the sibling loop moves on); the same ID only tops up (a full pinned stack is never swapped out);
    //     another member of the family keeps vanilla's pour (LiquidContainersSwap), but on a locked pin never the
    //     whole-stack swap vanilla falls back to when the pour can't run (an owned bottle, PourLikely).
    // (2) Source pinned (any state): no swap that would push the displaced stack into the source's veto, where
    //     AddItem (:252, :275) would spill it to other slots or the floor.
    [HarmonyPatch]
    internal static class SlotDropItemPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SlotDropItem;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SlotController __instance, ItemData data, ref int remaining, ref bool allowItemSwap, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal) return true;
            try
            {
                if (!PinStore.Active || data == null) return true;
                var item = data.item;
                if (item == null || item.ID <= 1) return true;   // money goes straight to the wallet (:217-227)

                if (__instance.OnDropped?.Target is BottleRecyclingDrop && Reservation.SellLockedItem(data))
                {
                    remaining = data.amount;
                    __result = false;
                    PinPatches.Blocked("recycling drop", data.slotController);
                    return false;
                }

                if (Reservation.IsPinnedMain(__instance) && !Reservation.Yielding(__instance))
                {
                    if (Reservation.Vetoes(__instance, item))
                    {
                        remaining = data.amount;
                        __result = false;
                        PinPatches.Blocked("drop onto", __instance);
                        return false;
                    }
                    if (item.ID == __instance.itemStack.itemId) allowItemSwap = false;
                    else if (allowItemSwap && Reservation.MoveLocked(__instance) && !PinPatches.PourLikely(data, __instance))
                    {
                        allowItemSwap = false;
                        PinPatches.Blocked("swap out of", __instance);
                    }
                }

                var src = data.slotController;
                if (allowItemSwap && src != null && src != __instance && Game.NonEmpty(__instance) && Reservation.IsPinnedMain(src))
                {
                    var displaced = __instance.itemStack.itemReference?.Item ?? ItemDatabase.instance?.FetchItemByID(__instance.itemStack.itemId);
                    if (displaced != null && Reservation.Vetoes(src, displaced))
                    {
                        allowItemSwap = false;
                        PinPatches.Blocked("swap into", src);
                    }
                }
                return true;
            }
            catch (Exception e) { Log.Exception("drop check", e); return true; }
        }
    }

    // P5: ItemData.CantDrag (SRC/ItemData.cs:745), private. Gates all six drag entry points (mouse and controller), so
    // ReturnOriginal never runs with a stale origAmount. Never forced during an active drag.
    [HarmonyPatch]
    internal static class CantDragPatch
    {
        private static MethodBase TargetMethod() => Vanilla.CantDrag;

        private static void Postfix(ItemData __instance, ref bool __result)
        {
            try
            {
                if (__result || __instance == null || __instance.isDragging) return;
                if (Reservation.MoveLocked(__instance.slotController)) __result = true;
            }
            catch (Exception e) { Log.Exception("drag check", e); }
        }
    }

    // P6: ItemData.DropItem(int, bool) (SRC/ItemData.cs:507): the Remove Area, the item panel's Drop, the slider's Drop
    // and DropItemToTheGround.
    [HarmonyPatch]
    internal static class ItemDropItemPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ItemDropItem;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ItemData __instance, bool __runOriginal)
        {
            if (!__runOriginal) return true;
            try
            {
                if (__instance == null || __instance.isDragging || !Reservation.MoveLocked(__instance.slotController)) return true;
                PinPatches.Blocked("drop", __instance.slotController);
                return false;
            }
            catch (Exception e) { Log.Exception("drop block", e); return true; }
        }
    }

    // P7: AmountSliderUI.ShowSlider(ItemData, AmountSliderType) (SRC/AmountSliderUI.cs:152). No Move or Drop slider for a
    // locked pin, and no Sell slider for a pinned stack (Trade.TradeAndMove opens it before CanSell, Trade.cs:388-392).
    // The Plant slider is never blocked.
    [HarmonyPatch]
    internal static class ShowSliderPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ShowSlider;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ItemData itemData, AmountSliderType type, bool __runOriginal)
        {
            if (!__runOriginal) return true;
            try
            {
                var sc = itemData != null ? itemData.slotController : null;
                bool block = (type == AmountSliderType.Default || type == AmountSliderType.Drop) && Reservation.MoveLocked(sc)
                             || type == AmountSliderType.Trade && Reservation.SellLocked(sc);
                if (!block) return true;
                PinPatches.Blocked("slider", sc);
                return false;
            }
            catch (Exception e) { Log.Exception("slider block", e); return true; }
        }
    }

    // P8, P9: Trade.CanSell (SRC/Trade.cs:519) and the TradeSpecial override (SRC/TradeSpecial.cs:27, no base call).
    // Together they cover drag-to-shop, right-click drop-one, quick-sell, MakeTrade, SellAndMove and the Sell button.
    // Runs before vanilla's "amount == 0 is fine". SellLockedItem also catches a whole pinned stack being dragged (the
    // drag has already taken it out of its slot) when BlockManualMoves is off.
    [HarmonyPatch]
    internal static class TradeCanSellPatch
    {
        private static MethodBase TargetMethod() => Vanilla.TradeCanSell;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ItemData itemData, ref bool __result, bool __runOriginal) => CanSellBlock.Prefix(itemData, ref __result, __runOriginal);
    }

    [HarmonyPatch]
    internal static class TradeSpecialCanSellPatch
    {
        private static MethodBase TargetMethod() => Vanilla.TradeSpecialCanSell;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ItemData itemData, ref bool __result, bool __runOriginal) => CanSellBlock.Prefix(itemData, ref __result, __runOriginal);
    }

    internal static class CanSellBlock
    {
        internal static bool Prefix(ItemData itemData, ref bool __result, bool runOriginal)
        {
            if (!runOriginal) return true;
            try
            {
                if (!Reservation.SellLockedItem(itemData)) return true;
                var sc = itemData.slotController;
                __result = false;
                PinPatches.Blocked("sell", sc);
                return false;
            }
            catch (Exception e) { Log.Exception("sell block", e); return true; }
        }
    }

    // P10: ItemData.QuickMove(int) (SRC/ItemData.cs:547), only its bottle recycling branch, which destroys the item for
    // money (:558-569). The liquid, trade and move branches are handled by vanilla and the patches above.
    [HarmonyPatch]
    internal static class QuickMovePatch
    {
        private static MethodBase TargetMethod() => Vanilla.QuickMove;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ItemData __instance, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal) return true;
            try
            {
                var panel = BottleRecyclingPanel.instance;
                if (panel == null || !Vanilla.RecyclingOpen(panel) || __instance == null || !Reservation.SellLocked(__instance.slotController)) return true;
                __result = false;
                PinPatches.Blocked("recycling", __instance.slotController);
                return false;
            }
            catch (Exception e) { Log.Exception("recycling block", e); return true; }
        }
    }

    // P11: Slot.CheckItemMetas() (SRC/Slot.cs:505), the parameterless entry. Spoilage (:519-534, :595-607) and growth
    // (:556-574, :634-650) remove the stack and add the new item to the same slot; the pinned slot yields for that,
    // so the spoiled food stays where it was as foreign content and the pin keeps its family. Re-entrant: a counter.
    [HarmonyPatch]
    internal static class CheckItemMetasPatch
    {
        private static MethodBase TargetMethod() => Vanilla.CheckItemMetas;

        private static void Prefix(Slot __instance, out bool __state)
        {
            __state = false;
            try { __state = __instance != null && Reservation.Enter(__instance.slotcontrol); }
            catch (Exception e) { Log.Exception("item meta yield", e); }
        }

        private static Exception Finalizer(Exception __exception, Slot __instance, bool __state)
        {
            if (__state)
            {
                try { Reservation.Exit(__instance.slotcontrol); }
                catch (Exception e) { Log.Exception("item meta yield", e); }
            }
            return __exception;
        }
    }

    // P12: Inventory.Equip(string, SlotController) (SRC/Inventory.cs:1639), the only caller of SwapItems and
    // EquipBackpack. The old worn item lands in the pinned slot exactly as in vanilla (:1558, :1698).
    [HarmonyPatch]
    internal static class EquipPatch
    {
        private static MethodBase TargetMethod() => Vanilla.Equip;

        private static void Prefix(SlotController item, out bool __state)
        {
            __state = false;
            try { __state = Reservation.Enter(item); }
            catch (Exception e) { Log.Exception("equip yield", e); }
        }

        private static Exception Finalizer(Exception __exception, SlotController item, bool __state)
        {
            if (__state)
            {
                try { Reservation.Exit(item); }
                catch (Exception e) { Log.Exception("equip yield", e); }
            }
            return __exception;
        }
    }

    // P13: GlobalState.OnLoadingGame (SRC/GlobalState.cs:35). The pins arrive with the save's globals, before the late
    // loaders add anything to the inventory.
    [HarmonyPatch]
    internal static class GlobalStateLoadPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GlobalStateOnLoadingGame;

        private static void Postfix(GlobalState __instance, bool __result)
        {
            try
            {
                if (__result && __instance != null && __instance == GlobalState.instance) PinStore.ReadFrom(__instance, "load");
            }
            catch (Exception e) { Log.Exception("pin load", e); }
        }
    }

    // P14: ItemOperations.AddItems(Item, int, int, object[], SlotController, bool, bool) (SRC/ItemOperations.cs:66).
    // Before Fair Justice's prefix, which snapshots slot ?? GetFirstSlot() and its siblings.
    [HarmonyPatch]
    internal static class AddItemsPatch
    {
        private static MethodBase TargetMethod() => Vanilla.AddItems;

        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("redsocket.obenseuer.fairjustice")]
        private static void Prefix(Item item, int amount, int owner, object[] meta, ref SlotController slot)
        {
            try { PinPatches.Redirect(item, amount, owner, meta, ref slot); }
            catch (Exception e) { Log.Exception("pin return", e); }
        }
    }

    // P15: ItemOperations.AddItemAndReturnRemaining(Item, int, SlotController, object[], int, int, ItemOperationSettings)
    // (SRC/ItemOperations.cs:96): quick moves into the inventory, Take all, buying, Unequip, No Floor Spill leftovers.
    [HarmonyPatch]
    internal static class AddItemAndReturnRemainingPatch
    {
        private static MethodBase TargetMethod() => Vanilla.AddItemAndReturnRemaining;

        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("redsocket.obenseuer.fairjustice")]
        private static void Prefix(Item item, int owner, ref SlotController slot, object[] meta, int amount)
        {
            try { PinPatches.Redirect(item, amount, owner, meta, ref slot); }
            catch (Exception e) { Log.Exception("pin return", e); }
        }
    }

    // P16: the instance SlotController.SlotHasSpaceForItem (SRC/SlotController.cs:791), never the static overload. A
    // reserved slot has no room for other items, so InventoryIsNotFull (shops, pickups, harvest) and Growing agree with
    // the veto. Not inside P16a-c: callers that ignore a "full" answer would lose the item instead of dropping it.
    [HarmonyPatch]
    internal static class SlotHasSpacePatch
    {
        private static MethodBase TargetMethod() => Vanilla.SlotHasSpaceForItem;

        private static void Postfix(SlotController __instance, Item item, int amount, ref int remaining, ref bool __result)
        {
            try
            {
                if (PinPatches.CapacityExempt > 0 || !Cfg.Pins.CountReservedAsFull.Value || item == null || item.ID <= 1) return;
                if (!Reservation.Vetoes(__instance, item)) return;
                remaining = amount;
                __result = false;
            }
            catch (Exception e) { Log.Exception("pin capacity", e); }
        }
    }

    // P16a-c: Inventory.AddItemDialogue and AddItemDialogueAmount (SRC/Inventory.cs:592-615: Lua quest rewards, the
    // treatments, the ticket machine) and GrowItem.GetItem (bonus seeds and the spoiled yield, SRC/GrowItem.cs:262, :279)
    // check InventoryIsNotFull and silently skip the item when it says full; their callers ignore that. While they run,
    // P16 passes through, so a reserved slot counts as space as in vanilla and the veto drops the item at the player's
    // feet (SlotController.AddItem with dropItems) instead of the reward never being created.
    internal static class CapacityExemptCore
    {
        internal static void Enter() => PinPatches.CapacityExempt++;

        internal static Exception Exit(Exception e)
        {
            if (PinPatches.CapacityExempt > 0) PinPatches.CapacityExempt--;
            return e;
        }
    }

    [HarmonyPatch]
    internal static class AddItemDialoguePatch
    {
        private static MethodBase TargetMethod() => Vanilla.AddItemDialogue;

        [HarmonyPriority(Priority.First)]
        private static void Prefix() => CapacityExemptCore.Enter();

        private static Exception Finalizer(Exception __exception) => CapacityExemptCore.Exit(__exception);
    }

    [HarmonyPatch]
    internal static class AddItemDialogueAmountPatch
    {
        private static MethodBase TargetMethod() => Vanilla.AddItemDialogueAmount;

        [HarmonyPriority(Priority.First)]
        private static void Prefix() => CapacityExemptCore.Enter();

        private static Exception Finalizer(Exception __exception) => CapacityExemptCore.Exit(__exception);
    }

    [HarmonyPatch]
    internal static class GrowItemGetItemPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GrowItemGetItem;

        [HarmonyPriority(Priority.First)]
        private static void Prefix() => CapacityExemptCore.Enter();

        private static Exception Finalizer(Exception __exception) => CapacityExemptCore.Exit(__exception);
    }

    // P15a: ItemInfo.UseLiquid(ItemData, ItemLiquidData) (SRC/ItemInfo.cs:211-245). Drinking a container empty removes it
    // and re-adds the empty one with this slot as the start (P14 would redirect it), then writes the copied ItemSkinData
    // and ItemOwnerPermission onto this slot's stack. When the stack carries either, the redirect is off for the call,
    // so the empty container stays in its slot with its skin and permission. Plain containers still go to their pin.
    [HarmonyPatch]
    internal static class UseLiquidPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UseLiquid;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(ItemData itemData, out bool __state)
        {
            __state = false;
            try
            {
                var st = itemData != null && itemData.slotController != null ? itemData.slotController.itemStack : null;
                if (st == null || st.GetMetaOfType<ItemSkinData>() == null && st.GetMetaOfType<ItemOwnerPermission>() == null) return;
                PinPatches.NoRedirect++;
                __state = true;
            }
            catch (Exception e) { Log.Exception("liquid redirect guard", e); }
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state && PinPatches.NoRedirect > 0) PinPatches.NoRedirect--;
            return __exception;
        }
    }

    // P17: InventoryNavigationHandler.StartDragging(bool) (SRC/InventoryNavigationHandler.cs:1869), private. Without it the
    // controller sets Dragging = true for a drag that P5 refused (a phantom drag).
    [HarmonyPatch]
    internal static class StartDraggingPatch
    {
        private static MethodBase TargetMethod() => Vanilla.StartDragging;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(bool __runOriginal)
        {
            if (!__runOriginal) return true;
            try
            {
                var slot = InventoryNavigationData.Instance != null ? InventoryNavigationData.Instance.CurrentSlot : null;
                var sc = slot != null ? slot.slotcontrol : null;
                if (!Reservation.MoveLocked(sc)) return true;
                PinPatches.Blocked("controller drag", sc);
                return false;
            }
            catch (Exception e) { Log.Exception("controller drag block", e); return true; }
        }
    }
}
