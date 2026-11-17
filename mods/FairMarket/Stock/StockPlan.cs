using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FairMarket
{
    // S-011 progression materials (spec §5). StorageSpawnCategoryDatabase deserializes StorageSpawnCategory.json into a
    // fresh list on every scene load (SRC/StorageSpawnCategoryDatabase.cs:113-116), and every consumer fetches a category
    // by name on each spawn (SRC/Storage.cs:1158), so edits made after the read never pile up and never reach a save.
    // Build only reads and records; Apply only appends (List<int>.Add, new liquid arrays). Nothing is removed and no
    // vanilla entry moves, so indices and the game's own weights (duplicates) stay as they are.
    internal sealed class StockPlan
    {
        // Exact stored names of the categories S1 edited in this database build; read by S2.
        internal static HashSet<string> EditedCategories = new HashSet<string>(StringComparer.Ordinal);

        private static string _lastSummary;

        // Shop aliases (spec §5.2), folded. The game's own list names work as well.
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            ["general trader"] = "General Store Bazaar",
            ["one stop shop"] = "General Store Trader",
            ["henning"] = "Hardware Store Stall",
            ["henning's shop"] = "Hardware Store Stall",
            ["keke"] = "Keke Roinaneva",
            ["keke's hoard"] = "Keke Roinaneva",
            ["ari"] = "Ari Pulsunmäki",
            ["auction"] = "Ari Pulsunmäki",
            ["brom's"] = "Broms",
        };

        // World container lists: accepted, with a warning (they change loot, not a shop).
        private static readonly HashSet<string> WorldLoot = new HashSet<string>
        {
            "storage", "minessteelcabinet", "industrial", "sewingsupplies", "mushroomslanternfactory", "largebag",
        };

        private sealed class Target
        {
            internal StorageSpawnCategory Cat;
            internal string Name;
            internal int Copies;
        }

        private readonly List<Action> _ops = new List<Action>();
        private readonly HashSet<string> _edited = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<int> _solidsUsed = new HashSet<int>();
        private readonly HashSet<string> _solidLists = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _liquidLists = new Dictionary<int, int>();
        private readonly List<string> _detail = new List<string>();
        private int _copies;

        internal static StockPlan Build(StorageSpawnCategoryDatabase db)
        {
            var plan = new StockPlan { _copies = Cfg.Stock.ExtraCopies.Value };
            bool onlyStocked = Cfg.Stock.OnlyWhereAlreadyStocked.Value;

            // Every stored name, its folded form and its category. "None" is GetStorageSpawnCategories' own first entry.
            var names = db.GetStorageSpawnCategories();
            var byFold = new Dictionary<string, string>();
            var cats = new List<StorageSpawnCategory>();
            foreach (var n in names)
            {
                if (n == "None") continue;
                var c = db.FetchStorageSpawnCategory(n);
                if (c == null) continue;
                cats.Add(c);
                string f = Names.Fold(n);
                if (f.Length > 0 && !byFold.ContainsKey(f)) byFold[f] = n;
            }

            // Liquids: any ID that some category spawns inside containers, with the reference object to reuse (spec §5.3).
            // Solids known to this build: any ID in a common or rare list.
            var liquidRef = new Dictionary<int, ItemReference>();
            var solids = new HashSet<int>();
            foreach (var c in cats)
            {
                var items = c.MetaValues?.liquid?.liquidItems;
                if (items != null)
                    foreach (var r in items)
                        if (r != null && r.ID > 0 && !liquidRef.ContainsKey(r.ID)) liquidRef[r.ID] = r;
                if (c.ItemCommon != null) foreach (int id in c.ItemCommon) solids.Add(id);
                if (c.ItemRare != null) foreach (int id in c.ItemRare) solids.Add(id);
            }

            var materials = new List<int>();
            foreach (var s in Names.List(Cfg.Stock.Materials.Value))
            {
                if (!int.TryParse(s.TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 0)
                {
                    Log.WarnOnce("stock id|" + s, $"Stock: '{s}' in Materials is not an item ID; skipped.");
                    continue;
                }
                if (!materials.Contains(id)) materials.Add(id);
            }

            var targets = new List<Target>();
            foreach (var entry in Names.List(Cfg.Stock.Traders.Value))
            {
                string name = entry;
                int copies = plan._copies;
                int colon = entry.LastIndexOf(':');
                if (colon > 0 && int.TryParse(entry.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                {
                    name = entry.Substring(0, colon).Trim();
                    copies = Math.Max(1, Math.Min(10, n));
                    if (copies != n) Log.WarnOnce("stock copies|" + entry, $"Stock: '{entry}' asks for {n} copies; using {copies} (1-10).");
                }
                string f = Names.Fold(name);
                string stored = Aliases.TryGetValue(f, out var alias) ? alias : byFold.TryGetValue(f, out var exact) ? exact : null;
                if (f == "anna markkanen" || f == "none" || (stored != null && stored.Trim().Length == 0))
                {
                    Log.WarnOnce("stock refused|" + f, $"Stock: '{name}' is refused: that list fills a menu in a fixed order (the canteen) or has no name.");
                    continue;
                }
                if (stored == null)
                {
                    Log.WarnOnce("stock unknown|" + f, $"Stock: no shop list called '{name}'; skipped. See the Traders description for the names.");
                    continue;
                }
                if (WorldLoot.Contains(Names.Fold(stored)))
                    Log.WarnOnce("stock world|" + f, $"Stock: '{name}' changes world containers, not a shop.");
                var cat = db.FetchStorageSpawnCategory(stored);
                if (cat == null) continue;
                if (targets.Exists(t => ReferenceEquals(t.Cat, cat))) continue;
                targets.Add(new Target { Cat = cat, Name = stored, Copies = copies });
            }

            foreach (int m in materials)
            {
                if (liquidRef.ContainsKey(m))
                {
                    foreach (var t in targets) plan.PlanLiquid(t, m, liquidRef[m], onlyStocked);
                    continue;
                }
                if (!solids.Contains(m) && !KnownItem(m))
                {
                    Log.WarnOnce("stock missing|" + m, $"Stock: item {m} is in no shop or loot list and isn't a known item yet; skipped this time.");
                    continue;
                }
                foreach (var t in targets) plan.PlanSolid(t, m, onlyStocked);
            }
            return plan;
        }

        // ItemDatabase may not be read yet on the first scene (its own Awake), so only a filled database can vouch for an
        // ID that no list has. A liquid nobody spawns has no reference object to reuse and is skipped.
        private static bool KnownItem(int id)
        {
            try
            {
                if (ItemDatabase.database == null || ItemDatabase.database.Count == 0) return false;
                var item = ItemDatabase.database.Find(i => i != null && i.ID == id);
                if (item == null) return false;
                if (item.Categories != null && Array.IndexOf(item.Categories, "Liquid") >= 0)
                {
                    Log.WarnOnce("stock liquid|" + id, $"Stock: item {id} is a liquid no shop sells in containers; skipped.");
                    return false;
                }
                return true;
            }
            catch { return false; }
        }

        private void PlanSolid(Target t, int m, bool onlyStocked)
        {
            var c = t.Cat;
            if (c.ItemCommon == null) return;
            bool has = c.ItemCommon.Contains(m) || (c.ItemRare != null && c.ItemRare.Contains(m));
            if (onlyStocked && !has) return;
            int k = t.Copies;
            _ops.Add(() => { for (int i = 0; i < k; i++) c.ItemCommon.Add(m); });
            _edited.Add(t.Name);
            _solidsUsed.Add(m);
            _solidLists.Add(t.Name);
            _detail.Add($"{t.Name}: +{k} x {m}");
        }

        // Liquids go only into a category's existing liquid list, as copies of an existing reference object (never
        // new ItemReference(int): ItemDatabase.instance may be null here). Appended indices have no allowedContainers
        // key, so any container in that shop can hold them. A list read in order (useLiquidsInOrder) never reaches the
        // appended entries, so it is left alone.
        private void PlanLiquid(Target t, int m, ItemReference fallback, bool onlyStocked)
        {
            var liq = t.Cat.MetaValues?.liquid;
            if (liq == null || liq.liquidItems == null || liq.liquidItems.Length == 0 || liq.useLiquidsInOrder) return;
            var own = Array.Find(liq.liquidItems, r => r != null && r.ID == m);
            if (own == null && onlyStocked) return;
            var r0 = own ?? fallback;
            int k = t.Copies;
            _ops.Add(() =>
            {
                var old = liq.liquidItems;
                var arr = new ItemReference[old.Length + k];
                Array.Copy(old, arr, old.Length);
                for (int i = 0; i < k; i++) arr[old.Length + i] = r0;
                liq.liquidItems = arr;
            });
            _edited.Add(t.Name);
            _liquidLists[m] = _liquidLists.TryGetValue(m, out int n) ? n + 1 : 1;
            _detail.Add($"{t.Name}: +{k} x {m} (liquid)");
        }

        internal void Apply()
        {
            foreach (var op in _ops) op();
            EditedCategories = new HashSet<string>(_edited, StringComparer.Ordinal);

            string summary = Summary();
            if (summary != _lastSummary)
            {
                _lastSummary = summary;
                Log.Info(summary);
                foreach (var d in _detail) Log.Verbose("Stock: " + d);
            }
        }

        private string Summary()
        {
            string liquids = _liquidLists.Count == 0 ? "" :
                " (" + string.Join(", ", _liquidLists.Select(kv => Game.ItemTitle(kv.Key) + ": " + kv.Value + (kv.Value == 1 ? " liquid list" : " liquid lists")).ToArray()) + ")";
            return $"Stock: +{_copies} copies of {_solidsUsed.Count} materials in {_solidLists.Count} shop lists{liquids}.";
        }

        // The feature is off or the plan failed: S2 must not re-roll anything.
        internal static void Clear() => EditedCategories = new HashSet<string>(StringComparer.Ordinal);
    }
}
