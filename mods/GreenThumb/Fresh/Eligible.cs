using System.Collections.Generic;
using OS.Items;

namespace GreenThumb
{
    // Which stacks freshness bands and the weighted merge apply to (spec §8.1, D11). Per item ID, cached and cleared when
    // the Scope changes: stackable, spoils (a perishable template with a loss rate), no liquid, animal or package data,
    // not an Animal or Face item, and in scope (Produce = Food and Plant, the 24 farm items; Food = all food). Per pair:
    // the perishable parameters match, so healed and unhealed stacks (UnofficialPatch) or fridge-only items never mix.
    internal static class Eligible
    {
        private static readonly Dictionary<int, bool> Cache = new Dictionary<int, bool>();

        internal static void Clear() => Cache.Clear();

        internal static bool Item(Item item)
        {
            if (item == null || item.ID <= 1) return false;
            if (!Cache.TryGetValue(item.ID, out bool ok))
            {
                ok = Compute(item);
                Cache[item.ID] = ok;
            }
            return ok && item.Stackable > 1;
        }

        // B3: the stack's own item, through its cached reference (no database scan once cached).
        internal static bool Item(ItemStack s)
        {
            if (s == null || s.itemId <= 1) return false;
            if (Cache.TryGetValue(s.itemId, out bool ok) && !ok) return false;
            return Item(s.itemReference?.Item);
        }

        private static bool Compute(Item item)
        {
            var c = item.Categories;
            if (c == null) return false;
            if (item.ifInCategory("Animal") || item.ifInCategory("Face")) return false;
            bool food = item.ifInCategory("Food");
            if (!food) return false;
            if (Cfg.Hot.Scope == StackScope.Produce && !item.ifInCategory("Plant")) return false;
            var meta = item.Meta;
            if (meta == null) return false;
            bool perishes = false;
            foreach (var m in meta)
            {
                if (Excluded(m)) return false;
                if (m is ItemPerishabledata p && p.integrityLossPerHour > 0f) perishes = true;
            }
            return perishes;
        }

        internal static bool Excluded(object m) => m is ItemLiquidData || m is ItemAnimalData || m is ItemPackageData;

        // Neither array holds liquid, animal or package data. No allocation.
        internal static bool PlainMeta(object[] meta)
        {
            if (meta == null) return true;
            for (int i = 0; i < meta.Length; i++)
                if (Excluded(meta[i])) return false;
            return true;
        }

        internal static bool SameParams(ItemPerishabledata a, ItemPerishabledata b)
        {
            if (a == null || b == null) return false;
            if (a.integrityLossPerHour != b.integrityLossPerHour || a.refigerationFactor != b.refigerationFactor) return false;
            if (a.perishingDisabled != b.perishingDisabled || a.dontDestroyAtZero != b.dontDestroyAtZero) return false;
            int sa = a.spoiledItem != null ? a.spoiledItem.ID : 0, sb = b.spoiledItem != null ? b.spoiledItem.ID : 0;
            return sa == sb;
        }

        internal static ItemPerishabledata FirstPerishable(object[] meta)
        {
            if (meta == null) return null;
            for (int i = 0; i < meta.Length; i++)
                if (meta[i] is ItemPerishabledata p) return p;
            return null;
        }
    }
}
