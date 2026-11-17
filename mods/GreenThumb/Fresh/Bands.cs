using System;
using OS.Items;
using UnityEngine;

namespace GreenThumb
{
    // S-061b freshness bands (spec §8.2-8.4, D11, D12). ItemMetaUtilities.CompareItemMetas only lets two perishable
    // stacks merge within 0.025 integrity (SRC/ItemMetaUtilities.cs:211), so produce harvested a few hours apart never
    // stacks. B2 replaces that one Abs() with Distance, which returns 0 for an eligible pair in the same band. Edges at 30%
    // and 70% are always band edges: inside a band, price, mood factor and unit-hours to rot are linear in integrity, so
    // the amount-weighted merge (B3) conserves them. Vanilla's own 0.025 tolerance still applies across an edge (a 31%
    // and a 29% stack join, as in the game, and B3 weights them); bands only ever allow more merges, never fewer (D11).
    internal static class Bands
    {
        // Called by the patched CompareItemMetas in place of Mathf.Abs(a.integrity - b.integrity). a = meta1's perishable
        // (the target stack: MetaMergeAllowed passes itemStack.Meta first, and only meta1 is walked), b = the incoming.
        public static float Distance(ItemPerishabledata a, ItemPerishabledata b)
        {
            float v = Mathf.Abs(a.integrity - b.integrity);   // exact vanilla
            try
            {
                var ctx = MergeCtx.Current;
                if (!Cfg.Hot.Bands || !ctx.Set || !ctx.Eligible) return v;
                if (v <= 0.025f) return v;   // never refuse what vanilla allows
                if (!Eligible.SameParams(a, b)) return v;
                float ia = a.integrity - Pending(a, ctx.Stack);
                if (ia <= 0f || b.integrity <= 0f) return v;
                return Band(ia) == Band(b.integrity) ? 0f : v;
            }
            catch (Exception e)
            {
                Log.Exception("Stacks: band test", e);
                return v;
            }
        }

        // The number of edges at or below I. Never floor(I/W), which can put 0.7 in the wrong band through float error.
        internal static int Band(float i)
        {
            var edges = Cfg.Hot.Edges;
            int n = 0;
            for (int k = 0; k < edges.Length; k++)
                if (i >= edges[k]) n++;
            return n;
        }

        // The decay a slot draw would charge the stack right now (RemoveMinutesFromIntegrity's math, SRC/OS/Items/
        // ItemPerishabledata.cs:98-133, with the stack's refrigeration as Slot.CheckItemMetas passes it, Slot.cs:522).
        // Writes nothing.
        internal static float Pending(ItemPerishabledata a, ItemStack s)
        {
            if (a == null || s == null || a.perishingDisabled || s.spoilDisabled || a.integrityLossPerHour == 0f) return 0f;
            var t = TimeOfDayAzure.instance;
            var now = t != null ? t.currentTimeAndDay : null;
            if (now == null || a.lastChecked == null) return 0f;
            ulong last = a.lastChecked.TotalMinutes, cur = now.TotalMinutes;
            if (last == 0 || cur <= last)
            {
                if (last != 0 && cur < last)
                    Log.WarnOnce("clock behind", "Stacks: a stack was last checked after the current game time (an older save?); its pending decay counts as 0.");
                return 0f;
            }
            float min = cur - last;
            float f = a.refigerationFactor == 0f ? 1f : Mathf.Clamp01(1f - s.refigerated / a.refigerationFactor);
            return min / 60f * a.integrityLossPerHour * f;
        }
    }
}
