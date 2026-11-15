using System;
using HarmonyLib;

namespace FairJustice
{
    // S-034 (spec §3.2B). When a stolen worn backpack is taken (sleep-event arrest search, or a paid fine), vanilla
    // deletes the bag stack and leaves its storage orphaned, so the player's own items inside are gone until a bag of
    // the same type is worn again. Rescue moves them to the inventory first. Whatever does not fit stays in that
    // storage (vanilla's orphan state). No world items are spawned: Event_Arrest teleports in the same coroutine.
    internal static class BagRescue
    {
        internal static bool Patched;

        internal static void Rescue(string why)
        {
            if (!Cfg.RescueStolenBackpackContents.Value) return;
            var inv = Inventory.instance;
            if (inv == null || inv.CharacterSlots == null || inv.CharacterSlots.Length <= 5) return;
            var bag = inv.CharacterSlots[5].itemStack;
            // Exactly what GetStolenItems deletes.
            if (bag == null || bag.itemAmount <= 0 || bag.ownerId <= 1) return;
            var bp = Owned.WornBackpack();
            if (bp == null || bp.Slots == null) return;

            int moved = 0, left = 0;
            foreach (var s in bp.Slots)
            {
                var st = s?.itemStack;
                if (st == null || st.itemAmount <= 0 || st.ownerId > 1 || st.itemReference == null) continue;
                var item = st.itemReference.Item;
                if (item == null) continue;
                int amount = st.itemAmount;
                // Owner <= 1, so no theft check runs and no Fair Justice frame opens. A whole-stack move, like
                // MoveToOtherInventory does it (stackAmount = the stack).
                int rem = ItemOperations.AddItemAndReturnRemaining(item, st.ownerId, inv.GetFirstSlot(), st.Meta, amount, amount,
                    new ItemOperations.ItemOperationSettings { ignoreBackpack = true });
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
            Log.Info($"Rescue ({why}): moved {moved} owned stacks out of a stolen backpack; {left} left in its storage.");
            if (moved > 0)
                Toast.Show("rescue", "Stolen backpack", left == 0
                    ? "Your own items in it were moved to your inventory."
                    : $"Your own items in it were moved to your inventory; {left} {(left == 1 ? "stack" : "stacks")} did not fit and stay in that backpack's storage.");
        }
    }

    // P13: SleepEventController.RemoveStolenGoods() (private). Covers Event_Arrest and Event_GetCaught, including a
    // GetCaught with no arrest. Vanilla then deletes the stolen stacks as before.
    [HarmonyPatch(typeof(SleepEventController), "RemoveStolenGoods")]
    internal static class RemoveStolenGoodsPatch
    {
        private static void Prefix()
        {
            if (!BagRescue.Patched) return;
            try { BagRescue.Rescue("arrest search"); }
            catch (Exception e) { Log.Exception("RemoveStolenGoods prefix", e); }
        }
    }
}
