using System;

namespace TidyInventory
{
    // The sale value of a stack (price × amount, spec §4.2): the game's own unrounded sale price for that meta.
    // Anything that throws counts as priceless, so value protections keep it.
    internal static class ItemValue
    {
        internal static float Of(ItemStack stack)
        {
            try
            {
                if (stack == null || stack.itemAmount <= 0) return 0f;
                var item = stack.itemReference?.Item ?? ItemDatabase.instance?.FetchItemByID(stack.itemId);
                if (item == null) return float.PositiveInfinity;
                return item.GetItemSalePriceUnrounded(stack.Meta) * stack.itemAmount;
            }
            catch (Exception) { return float.PositiveInfinity; }
        }

        internal static float Of(Item item, object[] meta, float amount)
        {
            try
            {
                if (item == null) return float.PositiveInfinity;
                return item.GetItemSalePriceUnrounded(meta) * amount;
            }
            catch (Exception) { return float.PositiveInfinity; }
        }
    }
}
