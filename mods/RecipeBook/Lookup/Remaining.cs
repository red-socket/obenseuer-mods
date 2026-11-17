using System.Collections.Generic;
using OS.Items;
using UnityEngine;

namespace RecipeBook
{
    // Per-readable counts (spec §11, D17): how many of the recipes a magazine or book can teach are still new. The pool is
    // the game's own call (SRC/OS/Items/ItemReadable.cs:271) plus the fixed Recipes list (:269). "New" = not collected,
    // the rule behind the game's "New recipe" and "No new recipes left". "Unlock" = new, needs a recipe paper and not yet
    // crafted, so reading it adds a button at a station. Counts only, never names.
    internal static class Remaining
    {
        internal struct Counts
        {
            internal int Total, NewLeft, UnlockLeft;
        }

        private struct Entry
        {
            internal int Known, Cfg, Gen;
            internal Counts Value;
        }

        private static readonly Dictionary<int, Entry> Cache = new Dictionary<int, Entry>();
        private static readonly HashSet<int> Ids = new HashSet<int>();

        internal static void Clear() => Cache.Clear();

        internal static ItemReadable ReadableOf(Item item)
        {
            if (item?.Actions == null) return null;
            foreach (var a in item.Actions)
                if (a is ItemReadable r) return r;
            return null;
        }

        // False when the item teaches no recipes (newspapers, blueprint-only readables) or no game is running.
        internal static bool For(Item item, out Counts counts)
        {
            counts = default;
            var ir = ReadableOf(item);
            if (ir == null || RecipeDatabase.instance == null || !UsedIndex.Ready() || !Known.Refresh()) return false;
            if (Cache.TryGetValue(item.ID, out var e) && e.Known == Known.Version && e.Cfg == Cfg.Hot.Version && e.Gen == UsedIndex.Generation)
            {
                counts = e.Value;
                return counts.Total > 0;
            }
            counts = Compute(ir);
            Cache[item.ID] = new Entry { Known = Known.Version, Cfg = Cfg.Hot.Version, Gen = UsedIndex.Generation, Value = counts };
            return counts.Total > 0;
        }

        private static Counts Compute(ItemReadable ir)
        {
            var db = RecipeDatabase.instance;
            Ids.Clear();
            if (ir.RecipeTypes != null && ir.RecipeModifiers != null)
            {
                var pool = db.FetchRecipes(ir.RecipeTypes, ir.RecipeModifiers.ToArray(), ir.RecipeTag, ir.RecipeTagMode,
                    new Vector2Int(ir.RecipeSkillRangeMin, ir.RecipeSkillRangeMax));
                foreach (var r in pool)
                    if (r != null && r.Type != RecipeType.Debug) Ids.Add(r.ID);
            }
            if (ir.Recipes != null)
                foreach (var rr in ir.Recipes)
                {
                    if (rr == null) continue;
                    var r = UsedIndex.ById(rr.ID);
                    if (r != null && r.Type != RecipeType.Debug) Ids.Add(rr.ID);
                }

            var c = new Counts { Total = Ids.Count };
            foreach (int id in Ids)
            {
                if (Known.Collected(id)) continue;
                c.NewLeft++;
                var r = UsedIndex.ById(id);
                if (r != null && r.RecipeRequired && !Known.Learned(id)) c.UnlockLeft++;
            }
            return c;
        }
    }
}
