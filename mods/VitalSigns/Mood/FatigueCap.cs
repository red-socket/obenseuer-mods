using System;
using System.Collections.Generic;
using System.Globalization;

namespace VitalSigns
{
    // Fatigue stacking cap (spec §7.5, D17). Vanilla adds 36000 to usedItems[id] for each medicine dose (food and drink:
    // |3600 * nutrition / 5|) with no upper bound (SRC/PlayerStatsMentalHealth.cs:240-266), and the counter falls by
    // sec/10, 360 a game hour (:110-117). GetUsedItemsValues (:345-361) zeroes a good effect at u >= 36000, so every dose
    // past the first adds another 100 game hours with no effect. The cap keeps vanilla's rule (a dose within the last
    // 100 h still blocks the next one, and spamming still stops helping) and only stops the stacking. It writes a plain
    // float into the vanilla dictionary, and only when a value is above the cap. NaN is left alone.
    internal static class FatigueCap
    {
        internal const float Dose = 36000f;
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        internal static bool Patched;

        // The cap as a counter value; 0 when the cap is off or its group isn't patched.
        internal static float Limit => Patched ? Cfg.Hot.FatigueLimit : 0f;

        // FatigueCapDoses to a counter value: 0 or less is off, anything between 0 and 1 counts as one dose, so a medicine
        // dose (36000) is never cut. Food, drink and smoke servings add |3600 * nutrition / 5|, and the largest good ones
        // reach 61200 (Nicotine Pouches 16440; Kebab Iskander 19510 59040, Cabbage Soup 57600), so a cap below about 1.7
        // shortens one such serving's own fatigue (documented in the config text and §7.5). The default 2 (72000) is above
        // every good single add; the only larger one is Sulphuric Acid (79200), which lowers mood, so its fatigue does nothing.
        internal static float ToLimit(float doses) => doses > 0f ? Math.Max(1f, doses) * Dose : 0f;

        // F1 postfix: the entry the game just added to (the item it used, the container for drinks, D15).
        internal static void AfterDose(PlayerStatsMentalHealth m, int id)
        {
            float cap = Limit;
            if (cap <= 0f || m == null || m.usedItems == null) return;
            if (!m.usedItems.TryGetValue(id, out float u) || !(u > cap)) return;
            m.usedItems[id] = cap;
            if (Log.VerboseOn) Log.Verbose($"Mood: fatigue for {Game.ItemTitle(id)} ({id}) {u.ToString("0", C)} -> {cap.ToString("0", C)} after a dose.");
        }

        // After a save is restored (SaveController.LoadingDone: loads from disk and doors; idempotent), so a save that
        // stacked up before the cap recovers at once instead of at that item's next dose.
        internal static void OnLoad()
        {
            float cap = Limit;
            var ps = PlayerStats.instance;
            var m = ps != null ? ps.mentalHealth : null;
            if (cap <= 0f || m == null || m.usedItems == null) return;
            List<int> over = null;
            foreach (var kv in m.usedItems)
                if (kv.Value > cap) (over ?? (over = new List<int>())).Add(kv.Key);
            if (over == null) return;
            string doses = (cap / Dose).ToString("0.##", C);
            foreach (int id in over)
            {
                float u = m.usedItems[id];
                m.usedItems[id] = cap;
                Log.Info($"Mood: fatigue for {Game.ItemTitle(id)} ({id}) {u.ToString("0", C)} -> {cap.ToString("0", C)} (saved value above the cap of {doses} doses).");
            }
        }
    }
}
