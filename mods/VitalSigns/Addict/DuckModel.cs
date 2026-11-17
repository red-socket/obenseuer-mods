using System;
using System.Collections.Generic;
using UnityEngine;

namespace VitalSigns
{
    // The duck readout (spec §8.4): no ETA and no cure advice; duck addiction doesn't wear off with time. The count is
    // the game's own inventory query (inventory, backpack and worn slots), called at most once per 0.5 s rebuild and
    // only while the page or tooltip is shown.
    internal static class DuckModel
    {
        private static bool Has(PlayerStatsDuck d)
        {
            try { return d != null && TaskItemsManager.instance != null && d.HasDuckAddiction(); }
            catch { return false; }
        }

        private static int Count(PlayerStatsDuck d)
        {
            try
            {
                var item = Vanilla.DuckRequiredItem(d);
                return item != null && Inventory.instance != null ? Inventory.instance.GetItemAmountInInventory(item) : -1;
            }
            catch { return -1; }
        }

        private static void Read(List<string> out1, PlayerStatsDuck d)
        {
            out1.Add(Strings.DuckCraving(d.currentStatus, d.negativeEffects));
            int n = Count(d);
            if (n >= 0) out1.Add(Strings.DuckCount(n, Mathf.CeilToInt(Math.Max(d.currentDucksNeedAmount, 0.5f))));
            out1.Add(Strings.DuckNoDecay);
        }

        internal static void Page(List<UiLine> l, PlayerStats ps)
        {
            var d = ps.duck;
            if (!Has(d)) return;
            l.Add(UiLine.Space());
            l.Add(UiLine.Heading(Strings.DuckHeading));
            var lines = new List<string>();
            Read(lines, d);
            foreach (var s in lines) l.Add(UiLine.Text(s));
        }

        internal static string Tooltip()
        {
            var ps = PlayerStats.instance;
            var d = ps != null ? ps.duck : null;
            if (!Has(d)) return null;
            var lines = new List<string>();
            Read(lines, d);
            string more = Strings.More;
            if (more != null) lines.Add(more);
            return string.Join("\n", lines.ToArray());
        }

        // The mood page's duck line (spec §7.1).
        internal static string MoodLine(PlayerStats ps)
        {
            var d = ps.duck;
            if (!Has(d) || d.currentStatus < d.negativeEffects) return null;
            return DuckTime.Patched && Cfg.Hot.DuckFix ? Strings.DuckMoodPerHour(d.currentStatus / 10000f * 3600f) : Strings.DuckMoodPerFrame;
        }
    }
}
