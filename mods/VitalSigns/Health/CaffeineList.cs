using System.Collections.Generic;
using System.Text;
using OS.Items;

namespace VitalSigns
{
    // Items whose caffeine may still be pending (spec §5.5). Filled by H4 from the measured tirednessToAdd and Coffee
    // deltas; cleared by the game's own state (tirednessToAdd >= 0 and no Coffee pending), checked after each tiredness
    // tick and at 2 Hz, and at a fresh load. It only names the items: the overdose amount is always measured.
    internal static class CaffeineList
    {
        private sealed class Row { internal string Title; internal int Count; internal float Units; }

        private static readonly List<Row> Rows = new List<Row>();

        internal static void Add(string title, float units)
        {
            foreach (var r in Rows)
                if (r.Title == title) { r.Count++; r.Units += units; return; }
            Rows.Add(new Row { Title = title, Count = 1, Units = units });
        }

        internal static void Clear() => Rows.Clear();

        // The game has no caffeine left to process (SRC/PlayerStats.cs:283-286, SRC/PlayerStatsTiredness.cs:78-85).
        internal static void ClearIfSpent()
        {
            if (Rows.Count == 0) return;
            var ps = PlayerStats.instance;
            if (ps == null || ps.tiredness == null) return;
            if (ps.tiredness.tirednessToAdd >= 0f && (ps.unprocessedSpecialEffects == null ||
                !ps.unprocessedSpecialEffects.ContainsKey(ItemConsumable.SpecialEffect.Coffee)))
                Rows.Clear();
        }

        // "after Coffee ×3, Energy Drink", or "caffeine from before this load".
        internal static string Summary()
        {
            if (Rows.Count == 0) return Strings.CaffeineFromBefore;
            var sb = new StringBuilder();
            foreach (var r in Rows)
            {
                sb.Append(sb.Length == 0 ? Strings.After : ", ").Append(r.Title);
                if (r.Count > 1) sb.Append(" ×").Append(r.Count);
            }
            return sb.ToString();
        }
    }
}
