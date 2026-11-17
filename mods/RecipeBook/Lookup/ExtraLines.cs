using System;
using System.Collections.Generic;
using System.Text;
using OS.Items;

namespace RecipeBook
{
    // The "New recipes left" and "Used in" lines (spec §10.3, §10.4, §11). Both texts are cached per key and per
    // (Known.Version, Cfg.Hot.Version, UsedIndex.Generation), so a hover or a selection change costs a dictionary
    // lookup and the tooltip string instance stays stable. Scope carries the tooltip block from ToolTip.ConstructDataString
    // (P10) to the TooltipUI.UpdateTooltip calls it makes (P11); outside that call it is null, so HoverTip, Better
    // Tenement's ToolTipInfo, the task items and the journal tooltips are never touched.
    internal static class ExtraLines
    {
        internal static string Scope;

        // The panel's candidate lines (spec §10.4). Names are in display order; the fit loop takes as many as fit.
        internal sealed class PanelSet
        {
            internal string RemainingFull, RemainingShort;
            internal readonly List<string> Names = new List<string>();
            internal int NameCount;
            internal bool Any => RemainingFull != null || NameCount > 0;
        }

        private struct Key : IEquatable<Key>
        {
            internal int Item, Use;
            internal bool Liquid;
            public bool Equals(Key o) => Item == o.Item && Use == o.Use && Liquid == o.Liquid;
            public override bool Equals(object o) => o is Key k && Equals(k);
            public override int GetHashCode() => (Item * 397) ^ (Use * 31) ^ (Liquid ? 1 : 0);
        }

        private sealed class Entry<T>
        {
            internal int Known, Cfg, Gen;
            internal T Value;
        }

        private static readonly Dictionary<Key, Entry<string>> TipCache = new Dictionary<Key, Entry<string>>();
        private static readonly Dictionary<Key, Entry<PanelSet>> PanelCache = new Dictionary<Key, Entry<PanelSet>>();
        private static readonly List<Recipe> Found = new List<Recipe>();
        private static readonly HashSet<Recipe> Seen = new HashSet<Recipe>();
        private static readonly List<string> Names = new List<string>();
        private static readonly StringBuilder Sb = new StringBuilder(256);

        internal static void Clear()
        {
            TipCache.Clear();
            PanelCache.Clear();
            Remaining.Clear();
            Scope = null;
        }

        // ---- Tooltip (P10, P11) ----

        internal static string ForTooltip(Item item, object[] metas)
        {
            if (!Cfg.Hot.LinesTooltip || (!Cfg.Hot.UsedInOn && !Cfg.Hot.RemainingOn)) return null;
            if (item == null || !UsedIndex.Ready() || !Known.Refresh()) return null;
            var key = KeyOf(item, metas);
            if (TipCache.TryGetValue(key, out var e) && Fresh(e)) return e.Value;

            Sb.Length = 0;
            if (Cfg.Hot.RemainingOn && Remaining.For(item, out var c))
            {
                Sb.Append("\n\n<b>");
                if (c.NewLeft == 0) Sb.Append(Strings.NoneLeft).Append("</b>");
                else
                {
                    Sb.Append(Strings.NewLeftLabel).Append("</b> ").Append(Strings.NewLeft(c.NewLeft, c.Total));
                    if (c.UnlockLeft > 0) Sb.Append(Strings.UnlockTip(c.UnlockLeft));
                }
            }
            if (Cfg.Hot.UsedInOn)
            {
                int total = Collect(key.Use, key.Liquid);
                if (total > 0)
                {
                    int shown = Math.Min(total, Cfg.Hot.TooltipNames);
                    // The vanilla "Required in" block's style (SRC/TenementController.cs:1309); names never parse as tags.
                    Sb.Append("\n\n<b>").Append(Strings.UsedIn).Append("</b> <noparse>");
                    for (int i = 0; i < shown; i++)
                    {
                        if (i > 0) Sb.Append(", ");
                        Sb.Append(Names[i]);
                    }
                    Sb.Append("</noparse>").Append(Strings.More(total - shown));
                }
            }
            string text = Sb.Length > 0 ? Sb.ToString() : null;
            TipCache[key] = Stamp(new Entry<string> { Value = text });
            return text;
        }

        // ---- Item panel (P12) ----

        internal static PanelSet ForPanel(Item item, object[] metas)
        {
            if (!Cfg.Hot.LinesPanel || (!Cfg.Hot.UsedInOn && !Cfg.Hot.RemainingOn)) return null;
            if (item == null || !UsedIndex.Ready() || !Known.Refresh()) return null;
            var key = KeyOf(item, metas);
            if (PanelCache.TryGetValue(key, out var e) && Fresh(e)) return e.Value;

            var set = new PanelSet();
            if (Cfg.Hot.RemainingOn && Remaining.For(item, out var c))
            {
                if (c.NewLeft == 0) set.RemainingFull = set.RemainingShort = Strings.NoneLeft;
                else
                {
                    set.RemainingFull = Strings.NewLeftLabel + " " + Strings.NewLeft(c.NewLeft, c.Total) +
                        (c.UnlockLeft > 0 ? Strings.UnlockPanel(c.UnlockLeft) : "");
                    set.RemainingShort = Strings.NewLeftShort(c.NewLeft, c.Total);
                }
            }
            if (Cfg.Hot.UsedInOn)
            {
                set.NameCount = Collect(key.Use, key.Liquid);
                for (int i = 0; i < Math.Min(set.NameCount, Cfg.Hot.PanelNames); i++) set.Names.Add(Names[i]);
            }
            var result = set.Any ? set : null;
            PanelCache[key] = Stamp(new Entry<PanelSet> { Value = result });
            return result;
        }

        // "Used in: A, B, C (+N more)" with the first `names` names (panel form, plain text).
        internal static string PanelUsedIn(PanelSet set, int names)
        {
            Sb.Length = 0;
            Sb.Append(Strings.UsedIn).Append(' ');
            for (int i = 0; i < names; i++)
            {
                if (i > 0) Sb.Append(", ");
                Sb.Append(set.Names[i]);
            }
            Sb.Append(Strings.More(set.NameCount - names));
            return Sb.ToString();
        }

        // ---- Shared ----

        // A container holding liquid looks up the liquid (and its groups); an empty one, or any other item, itself (D15).
        private static Key KeyOf(Item item, object[] metas)
        {
            var liq = Item.GetMetaOfType<ItemLiquidData>(metas);
            bool liquid = liq != null && liq.liquidItem != null && liq.liquidAmount > 0f;
            return new Key { Item = item.ID, Use = liquid ? liq.liquidItem.ID : item.ID, Liquid = liquid };
        }

        private static bool Fresh<T>(Entry<T> e) => e.Known == Known.Version && e.Cfg == Cfg.Hot.Version && e.Gen == UsedIndex.Generation;

        private static Entry<T> Stamp<T>(Entry<T> e)
        {
            e.Known = Known.Version;
            e.Cfg = Cfg.Hot.Version;
            e.Gen = UsedIndex.Generation;
            return e;
        }

        // Fills Names with the distinct recipe names that use the item (spec §10.2): known only when KnownOnly is on,
        // de-duplicated by name, crafted first, then collected, then the rest, alphabetical within each group. Returns
        // the number of names.
        private static int Collect(int itemId, bool liquid)
        {
            Found.Clear();
            Seen.Clear();
            Names.Clear();
            UsedIndex.Query(itemId, liquid, Found, Seen);
            if (Found.Count == 0) return 0;
            if (Cfg.Hot.KnownOnly) Found.RemoveAll(r => !Known.Visible(r));
            Found.Sort((a, b) =>
            {
                int ga = Group(a), gb = Group(b);
                if (ga != gb) return ga.CompareTo(gb);
                return StringComparer.OrdinalIgnoreCase.Compare(a.Name ?? "", b.Name ?? "");
            });
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Found)
                if (!string.IsNullOrEmpty(r.Name) && names.Add(r.Name)) Names.Add(r.Name);
            return Names.Count;
        }

        private static int Group(Recipe r) => Known.Learned(r.ID) ? 0 : Known.Collected(r.ID) ? 1 : 2;
    }
}
