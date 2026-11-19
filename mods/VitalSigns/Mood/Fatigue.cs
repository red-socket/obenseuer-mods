using System;
using System.Collections.Generic;
using OS.Items;
using UnityEngine;

namespace VitalSigns
{
    // Per-item medicine fatigue from PlayerStatsMentalHealth.usedItems (spec §7.1). Strength = clamp(1 - u/36000, 0, 1)
    // (GetUsedItemsValues, SRC/PlayerStatsMentalHealth.cs:345-361). u falls by sec/10 on the accumulated stats seconds
    // (:110-117), 360 an hour, asleep or awake, so the hours are exact game hours. Drinks are keyed by the container
    // (SRC/PlayerStatsMentalHealth.cs:240 uses itemData.item.ID, D15). Rows always read the live counter, so they stay
    // exact with the fatigue cap (D17): a capped entry shows the capped value the game itself will use.
    internal static class Fatigue
    {
        private sealed class Row { internal string Title; internal float U; internal float Depression, Effect; internal bool Medicine; }

        private const float Full = 36000f, PerHour = 360f;

        private static List<Row> Rows(PlayerStatsMentalHealth m)
        {
            var rows = new List<Row>();
            if (m == null || m.usedItems == null) return rows;
            var ps = PlayerStats.instance;
            float ma = ps != null && ps.addictions != null ? ps.addictions.MushroomAddiction : 0f;
            foreach (var kv in m.usedItems)
            {
                var row = new Row { Title = Game.ItemTitle(kv.Key), U = kv.Value };
                try
                {
                    var item = ItemDatabase.instance != null ? ItemDatabase.instance.FetchItemByID(kv.Key) : null;
                    var c = item != null ? item.GetActionOfType<ItemConsumable>() as ItemConsumable : null;
                    if (c != null)
                    {
                        row.Depression = c.Depression;
                        row.Effect = EffectNow(c, ma);
                        row.Medicine = c.Type == ItemConsumable.ConsumableType.Medicine;
                    }
                }
                catch (Exception) { }
                rows.Add(row);
            }
            return rows;
        }

        internal static float Strength(float u) => Mathf.Clamp01(1f - u / Full);

        // The sign of the item's mood effect as the game computes it now (GetItemMentalHealt, :294-305). The medicine and
        // food scaling there only scales a negative value, so the sign comes from GetMushroomAddictionValues (:363-377,
        // mirrored here) applied to the base Depression with the live mushroom addiction: above 50 it turns ordinary good
        // food bad and High/SMV food good. Quality and each item's freshness (:306-317) aren't known per entry; a fresh
        // item of normal quality is assumed.
        private static float EffectNow(ItemConsumable c, float ma)
        {
            float v = c.Depression;
            if (c.Type != ItemConsumable.ConsumableType.Drink && c.Type != ItemConsumable.ConsumableType.Food) return v;
            if (c.High <= 0f && c.SMV <= 0f && ma != 0f)
                v = v > 0f ? v + v * (ma / 50f) : !(ma > 50f) ? v * (1f - ma / 50f) : Mathf.Abs(v * (ma / 100f));
            if ((c.High > 0f || c.SMV > 0f) && ma != 0f)
                v = v < 0f ? v + v * (ma / 50f) : !(ma > 50f) ? v - v * (ma / 50f) : Mathf.Abs(ma / 100f) * -1f;
            return v;
        }

        internal static void Lines(List<UiLine> l, PlayerStatsMentalHealth m)
        {
            var rows = Rows(m);
            if (rows.Count == 0) { l.Add(UiLine.Dim(Strings.FatigueNone)); return; }

            // Good effects now first (medicines first, most fatigued first), then containers with no mood effect of their
            // own (drinks), then items that lower mood now, collapsed: fatigue only weakens good effects (:345-361).
            rows.Sort((a, b) =>
            {
                int ga = Group(a), gb = Group(b);
                if (ga != gb) return ga.CompareTo(gb);
                return b.U.CompareTo(a.U);
            });
            int other = 0;
            foreach (var r in rows)
            {
                int g = Group(r);
                if (g == 3) { other++; continue; }
                string title = g == 2 ? Strings.DrinksFrom(r.Title) : r.Title;
                l.Add(UiLine.Row(title + "   " + Strings.StrengthPct(Strength(r.U)), Detail(r.U, g == 2)));
            }
            if (other > 0) l.Add(UiLine.Dim(Strings.OtherFatigued(other)));
            float cap = FatigueCap.Limit;
            if (cap > 0f) l.Add(UiLine.Dim(Strings.FatigueCapNote(cap / PerHour)));
        }

        private static int Group(Row r) => r.Depression == 0f ? 2 : r.Effect < 0f ? (r.Medicine ? 0 : 1) : 3;

        private static string Detail(float u, bool container)
        {
            string s = u > Full
                ? Strings.NoEffectFor((u - Full) / PerHour, u / PerHour)
                : Strings.FullIn(u / PerHour);
            return container ? s + " " + Strings.ContainerNote : s;
        }

        // "Antidepressants: 40% strength, full in 60 h" for the most fatigued good medicine, or null.
        internal static string WorstMedicine(PlayerStatsMentalHealth m)
        {
            Row worst = null;
            foreach (var r in Rows(m))
                if (r.Medicine && r.Depression < 0f && (worst == null || r.U > worst.U)) worst = r;
            if (worst == null || worst.U <= 0f) return null;
            return Strings.TipMedicine(worst.Title, Strength(worst.U), worst.U / PerHour);
        }
    }
}
