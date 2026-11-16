using System;
using System.Collections.Generic;
using OS.Items;

namespace FewerClicks
{
    // The single guarded call into the game's own auto-eat (spec §2 P4, PlayerAutoConsume.ConsumeNeeds,
    // SRC/PlayerAutoConsume.cs:109). The game picks the food; every item goes through ItemInfo.UseItemDirectly ->
    // ItemConsumable.Invoke(worlduse: true): stats, item removal, returned empties, 0.1 L sips with the empty swap,
    // and the stolen-item noise all stay vanilla. The settings we change for the call (PlayerStats.AutoConsume, which is
    // saved, and the static needs' threshold and allowUnsafe) are restored in finally inside the same synchronous call,
    // so no save can ever see them.
    internal static class ConsumeCall
    {
        internal enum Outcome { Ate, NotHungry, NothingSafe, NotReady }

        internal struct Result
        {
            internal Outcome Outcome;
            internal List<string> Used;   // "ate Bread", "drank Water"
        }

        private struct SlotState
        {
            internal int Id, Amount, LiquidId;
            internal float Litres;
        }

        private static int _eaten;

        private static void Count() => _eaten++;

        internal static Result Run(float threshold, bool safeOnly, int maxItems)
        {
            var stats = PlayerStats.instance;
            var inv = Inventory.instance;
            if (stats == null || inv == null || inv.Slots == null || stats.hungerAndThirst == null) return new Result { Outcome = Outcome.NotReady };
            if (!ItemInfoFix.EnsureItemInfo()) return new Result { Outcome = Outcome.NotReady };

            var before = Snapshot(inv.Slots);
            var needs = Vanilla.Needs != null ? Vanilla.Needs.GetValue(null) as Array : null;
            bool tune = needs != null && Vanilla.NeedThreshold != null && Vanilla.NeedAllowUnsafe != null;
            if (!tune)
                Log.WarnOnce("needs", "Quick Eat: the game's auto-eat settings could not be read; Threshold and SafeFoodOnly are ignored " +
                    "(the game's own values, 50 and unsafe fallback, apply).");
            var oldThreshold = tune ? new float[needs.Length] : null;
            var oldUnsafe = tune ? new bool[needs.Length] : null;
            bool enabled = PlayerAutoConsume.Enabled;
            _eaten = 0;
            PlayerAutoConsume.Consumed += Count;
            try
            {
                PlayerAutoConsume.Enabled = true;
                if (tune)
                    for (int i = 0; i < needs.Length; i++)
                    {
                        var n = needs.GetValue(i);
                        oldThreshold[i] = (float)Vanilla.NeedThreshold.GetValue(n);
                        oldUnsafe[i] = (bool)Vanilla.NeedAllowUnsafe.GetValue(n);
                        Vanilla.NeedThreshold.SetValue(n, threshold);
                        if (safeOnly) Vanilla.NeedAllowUnsafe.SetValue(n, false);
                    }
                PlayerAutoConsume.ConsumeNeeds(maxItems);
            }
            finally
            {
                PlayerAutoConsume.Consumed -= Count;
                if (tune)
                    for (int i = 0; i < needs.Length; i++)
                    {
                        try
                        {
                            var n = needs.GetValue(i);
                            Vanilla.NeedThreshold.SetValue(n, oldThreshold[i]);
                            Vanilla.NeedAllowUnsafe.SetValue(n, oldUnsafe[i]);
                        }
                        catch (Exception e) { Log.Exception("quick eat restore", e); }
                    }
                PlayerAutoConsume.Enabled = enabled;
            }

            if (_eaten > 0) return new Result { Outcome = Outcome.Ate, Used = Diff(before, inv.Slots) };
            var h = stats.hungerAndThirst;
            float limit = tune ? threshold : PlayerAutoConsume.needThreshold;
            return new Result { Outcome = h.Hunger >= limit || h.Thirst >= limit ? Outcome.NothingSafe : Outcome.NotHungry };
        }

        private static SlotState[] Snapshot(SlotController[] slots)
        {
            var a = new SlotState[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                var st = slots[i]?.itemStack;
                if (st == null || st.itemId == -1 || st.itemAmount <= 0) { a[i] = new SlotState { Id = -1 }; continue; }
                var liquid = st.GetMetaOfType<ItemLiquidData>();
                a[i] = new SlotState
                {
                    Id = st.itemId,
                    Amount = st.itemAmount,
                    LiquidId = liquid?.liquidItem != null ? liquid.liquidItem.ID : -1,
                    Litres = liquid != null ? liquid.GetLiquidAmount(st.itemAmount) : 0f,
                };
            }
            return a;
        }

        // Names of the stacks that went down (or whose liquid went down). Increases (returned empties) are ignored.
        private static List<string> Diff(SlotState[] before, SlotController[] slots)
        {
            var after = Snapshot(slots);
            var used = new List<string>();
            for (int i = 0; i < before.Length && i < after.Length; i++)
            {
                var b = before[i];
                if (b.Id == -1) continue;
                var a = after[i];
                if (b.LiquidId != -1 && (a.Id != b.Id || a.LiquidId != b.LiquidId || a.Litres < b.Litres - 0.001f))
                    AddOnce(used, "drank " + Title(b.LiquidId));
                else if (a.Id != b.Id || a.Amount < b.Amount)
                    AddOnce(used, (IsDrink(b.Id) ? "drank " : "ate ") + Title(b.Id));
            }
            return used;
        }

        private static void AddOnce(List<string> l, string s)
        {
            if (!l.Contains(s)) l.Add(s);
        }

        private static string Title(int id)
        {
            try
            {
                var item = ItemDatabase.instance != null ? ItemDatabase.instance.FetchItemByID(id) : null;
                return item != null ? item.Title : "something";
            }
            catch (Exception) { return "something"; }
        }

        private static bool IsDrink(int id)
        {
            try
            {
                var item = ItemDatabase.instance != null ? ItemDatabase.instance.FetchItemByID(id) : null;
                return item?.FirstAction is ItemConsumable c && c.Type == ItemConsumable.ConsumableType.Drink;
            }
            catch (Exception) { return false; }
        }
    }
}
