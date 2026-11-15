using System.Collections.Generic;

namespace FairJustice
{
    // D-4 (KeepPermittedItems, spec §3.2D). Inventory.GetStolenItems (SRC/Inventory.cs:758-769) returns every carried
    // stack with ownerId > 1 and ignores ItemOwnerPermission; SleepEventController.RemoveStolenGoods (the arrest search,
    // :509-515) and Crime.PayFine (Crime.cs:375-392) delete that whole list. The game's own crime counts skip permitted
    // stacks (CountCrimes, CountStolenGoodsValue, CheckForAddedStolenItems), so they are not stolen goods.
    //
    // Hide() gives each permitted stack ownerId 1, which GetStolenItems skips, for the length of one vanilla call;
    // Restore() puts the real owner back (P13/P14 postfix, and their finalizer on an exception). The stacks never move,
    // so the owner and the permission meta come back exactly as they were. GetStolenItems itself is not patched: the
    // police checks (CountCrimes, Act_Police) and dialogue (GiveStolenItemsAndReturnHalfOfOC) keep vanilla behaviour.
    internal static class PermittedItems
    {
        private const int HiddenOwner = 1;

        internal sealed class Hidden
        {
            internal readonly List<ItemStack> Stacks = new List<ItemStack>();
            internal readonly List<int> Owners = new List<int>();
        }

        // Null when nothing was hidden.
        internal static Hidden Hide(string why)
        {
            if (!Cfg.KeepPermittedItems.Value) return null;
            var inv = Inventory.instance;
            if (inv == null) return null;
            Hidden h = null;
            foreach (var s in inv.AllSlots())   // the same slots GetStolenItems reads
            {
                var st = s?.itemStack;
                if (st == null || st.itemAmount <= 0 || st.ownerId <= 1 || !Owned.HasPermission(st.Meta)) continue;
                if (h == null) h = new Hidden();
                h.Stacks.Add(st);
                h.Owners.Add(st.ownerId);
            }
            if (h == null) return null;
            // Only after the scan, so an exception above can never leave a stack hidden with nobody to restore it.
            for (int i = 0; i < h.Stacks.Count; i++) h.Stacks[i].ownerId = HiddenOwner;
            Log.Verbose($"Keep permitted ({why}): {h.Stacks.Count} stacks set aside from the stolen-goods removal.");
            return h;
        }

        // Idempotent: the postfix and the finalizer both call it.
        internal static void Restore(Hidden h, string why)
        {
            if (h == null || h.Stacks.Count == 0) return;
            int n = 0;
            for (int i = 0; i < h.Stacks.Count; i++)
            {
                var st = h.Stacks[i];
                if (st.ownerId == HiddenOwner && st.itemAmount > 0)
                {
                    st.ownerId = h.Owners[i];
                    n++;
                }
                else
                    Log.WarnOnce("permitted changed", $"Keep permitted ({why}): a stack changed during the game's stolen-goods removal; " +
                                                      "it was left as the game left it. Another mod may have changed that removal.");
            }
            h.Stacks.Clear();
            h.Owners.Clear();
            if (n > 0) Log.Info($"Keep permitted ({why}): {n} stacks you were allowed to have were not taken as stolen goods.");
        }
    }
}
