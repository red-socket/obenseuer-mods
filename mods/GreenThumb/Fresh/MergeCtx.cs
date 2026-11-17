namespace GreenThumb
{
    // The merge check running now (spec §8.4), set by B1 around the static SlotController.SlotHasSpaceForItem (main
    // thread only; the method does not nest). Unset outside it, so TidyInventory's Combine, ForceItemToSlot and
    // LiquidStorage keep vanilla's 0.025 tolerance.
    internal static class MergeCtx
    {
        internal struct Ctx
        {
            internal bool Set;
            internal bool Eligible;
            internal ItemStack Stack;
        }

        internal static Ctx Current;
    }
}
