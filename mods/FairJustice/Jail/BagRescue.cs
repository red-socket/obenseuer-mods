using System;
using HarmonyLib;

namespace FairJustice
{
    // S-034 (spec §3.2B). When a stolen worn backpack is taken (sleep-event arrest search, or a paid fine), vanilla
    // deletes the bag stack and leaves its storage orphaned, so the player's own items inside are gone until a bag of
    // the same type is worn again. Rescue moves them to the inventory first. Whatever does not fit stays in that
    // storage (vanilla's orphan state). No world items are spawned: Event_Arrest teleports in the same coroutine.
    // With KeepPermittedItems (D-4), stacks the player was allowed to have are rescued too, and a worn bag the player
    // was allowed to have is not stolen at all: PermittedItems.Hide keeps the game from deleting it.
    internal static class BagRescue
    {
        internal static bool Patched;

        internal static void Rescue(string why)
        {
            if (!Cfg.RescueStolenBackpackContents.Value) return;
            var inv = Inventory.instance;
            if (inv == null || inv.CharacterSlots == null || inv.CharacterSlots.Length <= 5) return;
            var bag = inv.CharacterSlots[5].itemStack;
            // Exactly what GetStolenItems deletes, less a permitted bag when PermittedItems hides it.
            if (bag == null || bag.itemAmount <= 0 || bag.ownerId <= 1) return;
            bool keepPermitted = Cfg.KeepPermittedItems.Value;
            if (keepPermitted && Owned.HasPermission(bag.Meta)) return;
            var bp = Owned.WornBackpack();
            if (bp == null || bp.Slots == null) return;

            int moved = 0, left = 0;
            foreach (var s in bp.Slots)
            {
                var st = s?.itemStack;
                if (st == null || st.itemAmount <= 0 || st.itemReference == null) continue;
                if (st.ownerId > 1 && !(keepPermitted && Owned.HasPermission(st.Meta))) continue;   // stolen: vanilla deletes it
                var item = st.itemReference.Item;
                if (item == null) continue;
                int amount = st.itemAmount;
                int rem;
                // Owner <= 1: no theft check runs and no Fair Justice frame opens. A permitted stack (owner > 1) is a move
                // out of the player's own worn bag, so it opens an own-move frame like a shift-click would (P1 then skips
                // vanilla's PlayerMadeNoise; the permission means no crime and no mark either way). A whole-stack move,
                // like MoveToOtherInventory does it (stackAmount = the stack).
                var frame = st.ownerId > 1 ? OwnMoves.Open(s, item.ID, st.ownerId, "rescue") : null;
                try
                {
                    rem = ItemOperations.AddItemAndReturnRemaining(item, st.ownerId, inv.GetFirstSlot(), st.Meta, amount, amount,
                        new ItemOperations.ItemOperationSettings { ignoreBackpack = true });
                }
                finally
                {
                    if (frame != null) MoveFrames.Pop(frame);
                }
                if (rem <= 0)
                {
                    st.RemoveStack();
                    moved++;
                }
                else
                {
                    if (rem < amount) moved++;
                    st.itemAmount = rem;
                    left++;
                }
                s.Update();
            }
            if (moved == 0 && left == 0) return;
            Log.Info($"Rescue ({why}): moved {moved} of your stacks out of a stolen backpack; {left} left in its storage.");
            if (moved > 0)
                Toast.Show("rescue", "Stolen backpack", left == 0
                    ? "Your own items in it were moved to your inventory."
                    : $"Your own items in it were moved to your inventory; {left} {(left == 1 ? "stack" : "stacks")} did not fit and stay in that backpack's storage.");
        }
    }

    // P13: SleepEventController.RemoveStolenGoods() (private). Covers Event_Arrest and Event_GetCaught, including a
    // GetCaught with no arrest, and the pass-out guard's call. The rescue runs first, then permitted stacks (including
    // the ones just rescued) are set aside; vanilla then deletes the stolen stacks as before, and the postfix (or the
    // finalizer, on an exception) gives the set-aside stacks their owner back.
    [HarmonyPatch(typeof(SleepEventController), "RemoveStolenGoods")]
    internal static class RemoveStolenGoodsPatch
    {
        private static void Prefix(out PermittedItems.Hidden __state)
        {
            __state = null;
            if (!BagRescue.Patched) return;
            try { BagRescue.Rescue("arrest search"); }
            catch (Exception e) { Log.Exception("RemoveStolenGoods prefix", e); }
            try { __state = PermittedItems.Hide("arrest search"); }
            catch (Exception e) { Log.Exception("RemoveStolenGoods keep permitted", e); }
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(PermittedItems.Hidden __state)
        {
            try { PermittedItems.Restore(__state, "arrest search"); }
            catch (Exception e) { Log.Exception("RemoveStolenGoods postfix", e); }
        }

        private static Exception Finalizer(Exception __exception, PermittedItems.Hidden __state)
        {
            try { PermittedItems.Restore(__state, "arrest search"); }
            catch (Exception e) { Log.Exception("RemoveStolenGoods finalizer", e); }
            return __exception;
        }
    }
}
