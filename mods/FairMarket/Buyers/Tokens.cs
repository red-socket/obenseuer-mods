using System;
using System.Collections.Generic;
using System.Globalization;

namespace FairMarket
{
    // One trader's extra goods (spec §6.3). Allow and Exclude hold tokens in the game's own form (a category in exact
    // case, _item_<English title>, _meta_<Type>), checked with vanilla SlotController.CheckIfInAllowedCategoy; #id and
    // ! are ours. Excludes only narrow our extras: the trader's own forbidden list still wins, and its own lists are
    // never reduced or changed.
    internal sealed class Rules
    {
        internal string[] Allow = new string[0];
        internal HashSet<int> AllowIds = new HashSet<int>();
        internal string[] ExcludeTokens = new string[0];
        internal HashSet<int> ExcludeIds = new HashSet<int>();
        internal string[] ForbiddenUnion = new string[0];   // trade.forbiddenCategories ∪ ExcludeTokens, built per trade
        internal string Source = "";

        internal bool Empty => Allow.Length == 0 && AllowIds.Count == 0;

        // Adds another rule set's tokens (a trader named by both a default entry and Custom).
        internal void Merge(Rules o)
        {
            Allow = Union(Allow, o.Allow);
            ExcludeTokens = Union(ExcludeTokens, o.ExcludeTokens);
            AllowIds.UnionWith(o.AllowIds);
            ExcludeIds.UnionWith(o.ExcludeIds);
            Source = Source.Length == 0 ? o.Source : Source + " + " + o.Source;
        }

        internal static string[] Union(string[] a, string[] b)
        {
            var l = new List<string>(a ?? new string[0]);
            if (b != null)
                foreach (var s in b)
                    if (s != null && !l.Contains(s)) l.Add(s);
            return l.ToArray();
        }
    }

    internal static class Tokens
    {
        // Parses "Food, !Plant, #10500, _item_Rat Meat". Certificate is refused everywhere (it would open certificate
        // trades to C1's tiers and has no meaning as a food buyer).
        internal static Rules Parse(string text, string source)
        {
            var r = new Rules { Source = source };
            var allow = new List<string>();
            var excl = new List<string>();
            foreach (var raw in Names.List(text))
            {
                bool neg = raw.StartsWith("!", StringComparison.Ordinal);
                string t = (neg ? raw.Substring(1) : raw).Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("#", StringComparison.Ordinal))
                {
                    if (int.TryParse(t.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id > 0)
                        (neg ? r.ExcludeIds : r.AllowIds).Add(id);
                    else Log.WarnOnce("buyers id|" + source + "|" + t, $"Buyers: '{t}' in {source} is not an item ID; skipped.");
                    continue;
                }
                if (string.Equals(t, "Certificate", StringComparison.OrdinalIgnoreCase))
                {
                    Log.WarnOnce("buyers cert|" + source, $"Buyers: the Certificate category in {source} is refused.");
                    continue;
                }
                (neg ? excl : allow).Add(t);
            }
            r.Allow = allow.ToArray();
            r.ExcludeTokens = excl.ToArray();
            return r;
        }

        // Checks the tokens against the item database once it is filled (spec §6.3). Each problem is logged once.
        internal static void Validate(Rules r)
        {
            if (r == null || ItemDatabase.database == null || ItemDatabase.database.Count == 0) return;
            var cats = new HashSet<string>(StringComparer.Ordinal);
            var titles = new Dictionary<string, int>();
            var ids = new HashSet<int>();
            foreach (var item in ItemDatabase.database)
            {
                if (item == null) continue;
                ids.Add(item.ID);
                if (item.Categories != null) foreach (var c in item.Categories) if (c != null) cats.Add(c);
                if (!string.IsNullOrEmpty(item.Title))
                {
                    string k = item.Title.ToLowerInvariant();
                    titles[k] = titles.TryGetValue(k, out int n) ? n + 1 : 1;
                }
            }
            foreach (var t in Rules.Union(r.Allow, r.ExcludeTokens)) Check(t, r.Source, cats, titles);
            foreach (var id in r.AllowIds) if (!ids.Contains(id)) Log.WarnOnce("buyers noid|" + id, $"Buyers: no item #{id} ({r.Source}).");
            foreach (var id in r.ExcludeIds) if (!ids.Contains(id)) Log.WarnOnce("buyers noid|" + id, $"Buyers: no item #{id} ({r.Source}).");
            if (Array.IndexOf(r.Allow, "Animal") >= 0)
                Log.WarnOnce("buyers animal|" + r.Source, $"Buyers: {r.Source} buys Animal: your own cat is always excluded; pigs and chicks are not.");
        }

        private static void Check(string t, string source, HashSet<string> cats, Dictionary<string, int> titles)
        {
            string lower = t.ToLowerInvariant();
            if (lower.StartsWith("_meta_", StringComparison.Ordinal)) return;
            if (lower.StartsWith("_item_", StringComparison.Ordinal))
            {
                string title = lower.Substring(6);
                if (!titles.TryGetValue(title, out int n))
                    Log.WarnOnce("buyers title|" + lower, $"Buyers: no item called '{t.Substring(6)}' ({source}); use #ID instead.");
                else if (n > 1)
                    Log.WarnOnce("buyers dup|" + lower, $"Buyers: {n} items are called '{t.Substring(6)}' ({source}); all of them match. Use #ID for one.");
                return;
            }
            if (cats.Contains(t)) return;
            foreach (var c in cats)
                if (string.Equals(c, t, StringComparison.OrdinalIgnoreCase))
                {
                    Log.WarnOnce("buyers case|" + t, $"Buyers: category names are case-sensitive: '{t}' -> '{c}' ({source}).");
                    return;
                }
            Log.WarnOnce("buyers cat|" + t, $"Buyers: no category called '{t}' ({source}); skipped by the game's check.");
        }
    }
}
