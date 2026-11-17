using System;
using System.Collections.Generic;
using System.Linq;

namespace QuestHelper
{
    // "What blocks me" (spec §5.2 Needs line, §6.3): each curated need with a have/missing mark. RM and OC go through the
    // game's own Money.CheckMoneyDialogue, TaskItems through PlayerHasTaskItemId, items through
    // Inventory.CountItemsDialogue (carried items only, so "not in your inventory", never "missing"); a liquid need
    // through the same CheckForItemDialogueLiquidQuality call the dialogue makes.
    internal static class Blockers
    {
        internal static string Line(List<Need> needs)
        {
            if (needs == null || needs.Count == 0) return null;
            var parts = needs.Select(Describe).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            return parts.Length == 0 ? null : Strings.Needs(string.Join(" · ", parts));
        }

        internal static string Describe(Need n)
        {
            try
            {
                if (n.Any != null && n.Any.Count > 0)
                {
                    bool met = n.Any.Any(Met);
                    string label = n.Label ?? string.Join(" or ", n.Any.Select(LabelOf).ToArray());
                    string count = n.ShowCount ? CountText(n.Any) : null;
                    return label + " (" + (met ? Strings.Have : Strings.Missing) + (count != null ? ", " + count : "") + ")";
                }
                if (n.Item > 0)
                    return LabelOf(n) + " (" + (Met(n) ? Strings.InInventory : Strings.NotInInventory) + ")";
                if (n.Rm > 0 || n.Oc > 0)
                    return LabelOf(n) + " (" + (Met(n) ? Strings.HaveEnough : Strings.NotEnough) + ")";
                return LabelOf(n) + " (" + (Met(n) ? Strings.Have : Strings.Missing) + ")";
            }
            catch (Exception e)
            {
                Log.Exception("need", e);
                return null;
            }
        }

        private static string CountText(List<Need> any)
        {
            var item = any.FirstOrDefault(x => x.Item > 0);
            return item == null ? null : Strings.YouHave(ItemCount(item.Item));
        }

        internal static string LabelOf(Need n)
        {
            if (!string.IsNullOrEmpty(n.Label)) return n.Label;
            if (n.Item > 0 && n.Liquid > 0) return Strings.Litres(n.Liquid, Game.ItemTitle(n.Item));
            if (n.Item > 0) return (n.N > 1 ? n.N + " × " : "") + Game.ItemTitle(n.Item);
            if (n.Rm > 0) return Strings.Rm(n.Rm);
            if (n.Oc > 0) return Strings.Oc(n.Oc);
            return n.TaskItem ?? n.ObjCompleted ?? "?";
        }

        internal static bool Met(Need n)
        {
            if (n.Any != null && n.Any.Count > 0) return n.Any.Any(Met);
            if (!string.IsNullOrEmpty(n.TaskItem))
                return TaskItemsManager.instance != null && TaskItemsManager.instance.PlayerHasTaskItemId(n.TaskItem);
            if (!string.IsNullOrEmpty(n.ObjCompleted))
            {
                int us = n.ObjCompleted.IndexOf('_');
                return us > 0 && TaskState.ObjCompleted(n.ObjCompleted.Substring(0, us), n.ObjCompleted.Substring(us + 1));
            }
            if (n.Item > 0 && n.Liquid > 0)
                return Inventory.instance != null && Inventory.instance.CheckForItemDialogueLiquidQuality(n.Item, n.Liquid, n.MinQuality);
            if (n.Item > 0) return ItemCount(n.Item) >= n.N;
            if (n.Rm > 0) return Money.instance != null && Money.instance.CheckMoneyDialogue(n.Rm, true);
            if (n.Oc > 0) return Money.instance != null && Money.instance.CheckMoneyDialogue(n.Oc, false);
            return false;
        }

        private static int ItemCount(int id)
        {
            try { return Inventory.instance != null ? (int)Inventory.instance.CountItemsDialogue(id) : 0; }
            catch { return 0; }
        }
    }
}
