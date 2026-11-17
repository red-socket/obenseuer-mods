using System.Collections.Generic;
using System.Text;

namespace VitalSigns
{
    // What added bleeding since it was last 0 (spec §5.2-5.3). The game keeps one bleeding value and doesn't split its
    // health cost between causes, so neither do we: the "Bleeding" loss lists these causes as its detail.
    internal static class BleedCauses
    {
        private sealed class Cause { internal string Label; internal float Amount; }

        private static readonly List<Cause> List = new List<Cause>();

        internal static void Add(string label, float amount)
        {
            if (amount <= 0f) return;
            foreach (var c in List)
                if (c.Label == label) { c.Amount += amount; return; }
            List.Add(new Cause { Label = label, Amount = amount });
        }

        internal static void Clear() => List.Clear();

        // "Wound +18, Mussel +5"; cached until the list changes.
        private static string _cached;
        private static int _cachedHash;

        internal static string Summary()
        {
            if (List.Count == 0) return null;
            int hash = List.Count;
            foreach (var c in List) hash = hash * 31 + c.Label.GetHashCode() + (int)(c.Amount * 10f);
            if (_cached != null && hash == _cachedHash) return _cached;
            var sb = new StringBuilder();
            foreach (var c in List)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(c.Label).Append(" +").Append(Fmt.Num(c.Amount));
            }
            _cachedHash = hash;
            return _cached = sb.ToString();
        }
    }
}
