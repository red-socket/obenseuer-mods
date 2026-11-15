using System;
using HarmonyLib;

namespace FairJustice
{
    // Own-move scopes (spec §3.1 B). A move whose source is the player's main inventory, character slots or worn
    // backpack opens a MoveFrame; P1 then skips the theft path for that item. Moves from other storages, world pickups,
    // harvesting, Logic_Thief and item use open nothing and stay vanilla.
    internal static class OwnMoves
    {
        internal static bool Patched;

        internal static bool Live => Patched && StolenTags.Patched && Cfg.FreeInternalMoves.Value;

        internal static MoveFrame Open(SlotController source, int itemId, int owner, string kind)
        {
            if (!Live || itemId <= 1 || owner < 2 || !Owned.IsPlayerOwned(source)) return null;
            return MoveFrames.Push(itemId, owner, kind);
        }

        // D-3: after a free move of part of a stolen stack, mark what stays behind so the parts stack again. Only when
        // Fair Justice took over the check on this move.
        internal static void TagRemainder(SlotController src, MoveFrame f)
        {
            if (f == null || !f.skipped || !Cfg.TagSourceRemainder.Value || src == null) return;
            var st = src.itemStack;
            if (st == null || st.itemAmount <= 0 || st.itemId != f.itemId || st.ownerId != f.owner) return;
            if (Owned.HasPermission(st.Meta)) return;
            if (Owned.Tag(st)) Log.Verbose($"Stolen mark put on the {st.itemAmount} left in the source slot (id {st.itemId}).");
        }
    }

    // P4: SlotController.MoveToOtherInventory(SlotController, int, out int). Shift/ctrl/double-click quick moves, the
    // amount slider, controller moves, Take all on the backpack window, Inventory.Unequip, trade moves out of the
    // inventory. The (out int, int) overload forwards here and is not patched.
    [HarmonyPatch(typeof(SlotController), nameof(SlotController.MoveToOtherInventory),
        new[] { typeof(SlotController), typeof(int), typeof(int) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
    internal static class MoveToOtherInventoryPatch
    {
        private static void Prefix(SlotController __instance, out MoveFrame __state)
        {
            __state = null;
            try
            {
                var st = __instance.itemStack;
                if (st == null || st.itemId <= 1) return;
                __state = OwnMoves.Open(__instance, st.itemId, st.ownerId, "move");
            }
            catch (Exception e) { Log.Exception("MoveToOtherInventory prefix", e); }
        }

        private static void Postfix(SlotController __instance, MoveFrame __state)
        {
            if (__state == null) return;
            try { OwnMoves.TagRemainder(__instance, __state); }
            catch (Exception e) { Log.Exception("MoveToOtherInventory postfix", e); }
        }

        private static Exception Finalizer(Exception __exception, MoveFrame __state)
        {
            if (__state != null) MoveFrames.Pop(__state);
            return __exception;
        }
    }

    // P5: SlotController.DropItem(ItemData, out int, bool, bool, bool): drag and drop. data.slotController keeps its
    // identity after the drag-start decrement; sibling recursion re-enters and opens a nested frame.
    [HarmonyPatch(typeof(SlotController), nameof(SlotController.DropItem),
        new[] { typeof(ItemData), typeof(int), typeof(bool), typeof(bool), typeof(bool) },
        new[] { ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
    internal static class DropItemPatch
    {
        private static void Prefix(ItemData data, out MoveFrame __state)
        {
            __state = null;
            try
            {
                if (data == null || data.slotController == null) return;
                var item = data.stack?.itemReference?.Item;
                if (item == null) return;
                __state = OwnMoves.Open(data.slotController, item.ID, data.owner, "drag");
            }
            catch (Exception e) { Log.Exception("DropItem prefix", e); }
        }

        private static void Postfix(ItemData data, MoveFrame __state)
        {
            if (__state == null) return;
            try { OwnMoves.TagRemainder(data != null ? data.slotController : null, __state); }
            catch (Exception e) { Log.Exception("DropItem postfix", e); }
        }

        private static Exception Finalizer(Exception __exception, MoveFrame __state)
        {
            if (__state != null) MoveFrames.Pop(__state);
            return __exception;
        }
    }

    // P6: OS.Items.ItemEquipable.Invoke(ItemData, bool), the override (not Item.ItemAction.Invoke): equip and unequip
    // by use. Its base call runs the check on the source slot. World use has no slotController and stays vanilla;
    // consuming, reading and other uses are not touched.
    [HarmonyPatch(typeof(OS.Items.ItemEquipable), nameof(OS.Items.ItemEquipable.Invoke), new[] { typeof(ItemData), typeof(bool) })]
    internal static class EquipInvokePatch
    {
        private static void Prefix(ItemData itemData, out MoveFrame __state)
        {
            __state = null;
            try
            {
                if (!Cfg.IncludeEquipping.Value || itemData == null || itemData.slotController == null) return;
                var item = itemData.stack?.itemReference?.Item;
                if (item == null) return;
                __state = OwnMoves.Open(itemData.slotController, item.ID, itemData.owner, "equip");
            }
            catch (Exception e) { Log.Exception("ItemEquipable.Invoke prefix", e); }
        }

        private static Exception Finalizer(Exception __exception, MoveFrame __state)
        {
            if (__state != null) MoveFrames.Pop(__state);
            return __exception;
        }
    }
}
