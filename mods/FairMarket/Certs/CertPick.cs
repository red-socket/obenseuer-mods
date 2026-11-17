using System;
using System.Collections.Generic;
using OS.Items;

namespace FairMarket
{
    // Bug #5813 (spec §8.3). Vanilla FindValidCertificates (SRC/TradeSpecial.cs:126-183) walks the inventory in slot
    // order and adds min(stack, amount) entries per eligible stack until it has at least `amount`, then Sell takes one
    // certificate per entry. So a certificate made for another item can be used before the one made for this item, and
    // two stacks can give more entries than items sold (stacks of 2 and 5, selling 3: 5 certificates taken).
    //
    // Pick uses the same eligibility tests in the same order, minus the stack being sold, and fills exactly `amount`
    // entries: at Ari's (generic certificates) first those linked to this item, then unlinked ones, then those linked to
    // another item; at the Gold Shop (one certificate type, linked items only) a single tier. Vanilla returns true iff
    // the eligible stacks hold at least `amount` certificates in total, and so does Pick, so CanSell and the Sell button
    // are unchanged; Pick never uses more than vanilla.
    internal static class CertPick
    {
        internal static List<SlotController> Pick(TradeSpecial trade, ItemData itemData, int amount)
        {
            if (trade == null || itemData == null || itemData.item == null || amount <= 0 || Inventory.instance == null) return null;
            var slots = Inventory.instance.GetAllSlotsWithItemWithMeta<ItemCertificateData>();
            if (slots == null) return null;
            int soldId = itemData.item.ID;
            string tag = trade.requireCertificateTag;

            List<SlotController> linked = new List<SlotController>(), unlinked = new List<SlotController>(), other = new List<SlotController>();
            int salePrice = 0;
            bool generic = trade.acceptGenericCertificates;
            if (generic)
            {
                var st = itemData.stack;
                if (st == null || st.itemReference == null || st.itemReference.Item == null) return null;
                salePrice = st.itemReference.Item.GetItemSalePrice(st.Meta);   // the same expression as :151
            }
            else if (trade.certificateItem == null || trade.certificateItem.ID <= 0) return null;

            foreach (var s in slots)
            {
                var stack = s != null ? s.itemStack : null;
                if (stack == null || stack.itemAmount <= 0 || stack.itemReference == null || stack.itemReference.ID < 0) continue;
                if (ReferenceEquals(s, itemData.slotController)) continue;
                var c = Item.GetMetaOfType<ItemCertificateData>(stack.Meta);
                if (c == null) continue;
                if (!string.IsNullOrEmpty(tag) && !string.Equals(c.tag, tag, StringComparison.InvariantCultureIgnoreCase)) continue;
                if (generic)
                {
                    if (!c.ItemValueWithinRange(salePrice)) continue;
                    if (c.linkedItem != null && c.linkedItem.ID > 0 && c.linkedItem.ID == soldId) linked.Add(s);
                    else if (c.linkedItem == null || c.linkedItem.ID < 1) unlinked.Add(s);
                    else other.Add(s);
                }
                else
                {
                    if (trade.certificateItem.ID != stack.itemId || !c.CertificateValidForItem(itemData.item, itemData.meta)) continue;
                    linked.Add(s);
                }
            }

            var pick = new List<SlotController>(amount);
            int left = amount;
            Fill(linked, pick, ref left);
            Fill(unlinked, pick, ref left);
            Fill(other, pick, ref left);
            return left == 0 ? pick : null;
        }

        private static void Fill(List<SlotController> tier, List<SlotController> pick, ref int left)
        {
            foreach (var s in tier)
            {
                if (left <= 0) return;
                int n = Math.Min(s.itemStack.itemAmount, left);
                for (int i = 0; i < n; i++) pick.Add(s);
                left -= n;
            }
        }
    }
}
