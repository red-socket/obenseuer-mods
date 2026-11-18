using System;
using System.Collections.Generic;

namespace FieldGuide
{
    // The kinds of station that get a guide (spec §6). The enum names are also the config names: the per-kind switches
    // under [Popups] and the entries of [Popups] Hidden. Never rename one after release, or saved Hidden lists lose it.
    internal enum HelpKind
    {
        Sauna = 0,
        Mashing = 1,
        Corker = 2,
        Biogas = 3,
        Compost = 4,
    }

    internal static class Kinds
    {
        internal static readonly HelpKind[] All = { HelpKind.Sauna, HelpKind.Mashing, HelpKind.Corker, HelpKind.Biogas, HelpKind.Compost };

        internal static string Name(HelpKind k) => k.ToString();

        // "Sauna, Mashing" (any case, any spacing; unknown names are ignored) → the set.
        internal static HashSet<HelpKind> Parse(string text)
        {
            var set = new HashSet<HelpKind>();
            if (string.IsNullOrEmpty(text)) return set;
            foreach (var raw in text.Split(','))
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;
                foreach (var k in All)
                    if (string.Equals(s, Name(k), StringComparison.OrdinalIgnoreCase)) set.Add(k);
            }
            return set;
        }

        // The set → "Sauna, Mashing", in enum order.
        internal static string Format(HashSet<HelpKind> set)
        {
            var parts = new List<string>();
            foreach (var k in All)
                if (set.Contains(k)) parts.Add(Name(k));
            return string.Join(", ", parts.ToArray());
        }
    }
}
