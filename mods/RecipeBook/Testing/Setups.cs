#if RECIPEBOOK_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace RecipeBook
{
    // Test-only setters and dumps for the in-game test plan (spec §18.1), compiled only with -p:RecipeBookTests=true
    // (the Quest Helper / Fair Market precedent). Never packaged. Set [Testing] Setup to "name arg arg" while a game is
    // running; it applies once and resets itself to empty. Use on a copy of a test save only.
    internal static class Setups
    {
        private static ConfigEntry<string> _setup;

        private static readonly Dictionary<string, Action<string[]>> All = new Dictionary<string, Action<string[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["collect"] = a => Add(Int(a, 0), learned: false),
            ["learn"] = a => Add(Int(a, 0), learned: true),
            ["collect-readable"] = a => CollectReadable(Int(a, 0), Int(a, 1)),
            ["collect-tag"] = a => CollectTag(Str(a, 0), Int(a, 1)),
            ["books"] = a => Books(),
            ["book-respawn"] = a => BookRespawn(Int(a, 0)),
            ["usedin"] = a => UsedIn(Int(a, 0)),
            ["pool"] = a => Pool(Int(a, 0)),
            ["badge-dump"] = a => BadgeDump(),
        };

        internal static void Bind(ConfigFile c)
        {
            _setup = c.Bind("Testing", "Setup", "", "TEST BUILD ONLY: " + string.Join(", ", All.Keys.ToArray()));
            _setup.SettingChanged += (s, a) => Run();
            Log.Warn("Testing: this is a test build with state setters. Never package it.");
        }

        private static int Int(string[] a, int i) =>
            i < a.Length && int.TryParse(a[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : throw new ArgumentException("argument " + (i + 1) + " missing");

        private static string Str(string[] a, int i) => i < a.Length ? a[i] : throw new ArgumentException("argument " + (i + 1) + " missing");

        private static void Run()
        {
            string line = (_setup.Value ?? "").Trim();
            if (line.Length == 0) return;
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!All.TryGetValue(parts[0], out var act) || GlobalState.instance == null || RecipeController.instance == null)
                Log.Warn($"Testing: no setup '{parts[0]}' or no game running.");
            else
            {
                try { act(parts.Skip(1).ToArray()); Log.Warn($"Testing: applied '{line}'."); }
                catch (Exception e) { Log.Exception("test setup", e); }
            }
            _setup.Value = "";
        }

        private static Recipe Recipe(int id) => RecipeDatabase.instance.FetchRecipesById(id) ?? throw new ArgumentException("unknown recipe " + id);

        private static void Add(int id, bool learned)
        {
            bool added = RecipeController.instance.AddRecipe(Recipe(id), learned);
            Log.Warn($"Testing: {(learned ? "learned" : "collected")} {id} ({(added ? "new" : "already had it")}).");
        }

        // Collects every recipe of the pool but `keep` random uncollected ones; prints the kept IDs.
        private static void CollectAllBut(IEnumerable<Recipe> pool, int keep, string what)
        {
            var open = pool.Where(r => r != null && r.Type != RecipeType.Debug && !RecipeController.instance.HasRecipe(r))
                .GroupBy(r => r.ID).Select(g => g.First()).ToList();
            var kept = new List<int>();
            for (int i = 0; i < keep && open.Count > 0; i++)
            {
                var r = open[UnityEngine.Random.Range(0, open.Count)];
                open.Remove(r);
                kept.Add(r.ID);
            }
            foreach (var r in open) RecipeController.instance.AddRecipe(r);
            Log.Warn($"Testing: {what}: collected {open.Count}, kept {kept.Count} new: {string.Join(", ", kept.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray())}");
        }

        private static void CollectReadable(int itemId, int keep)
        {
            var item = ItemDatabase.instance.FetchItemByID(itemId) ?? throw new ArgumentException("unknown item " + itemId);
            var ir = Remaining.ReadableOf(item) ?? throw new ArgumentException("not a readable");
            var pool = new List<Recipe>();
            if (ir.RecipeTypes != null && ir.RecipeModifiers != null)
                pool.AddRange(RecipeDatabase.instance.FetchRecipes(ir.RecipeTypes, ir.RecipeModifiers.ToArray(), ir.RecipeTag, ir.RecipeTagMode,
                    new Vector2Int(ir.RecipeSkillRangeMin, ir.RecipeSkillRangeMax)));
            if (ir.Recipes != null) pool.AddRange(ir.Recipes.Where(x => x != null).Select(x => x.Recipe));
            CollectAllBut(pool, keep, item.Title);
        }

        private static void CollectTag(string tag, int keep) =>
            CollectAllBut(RecipeDatabase.Recipes.Where(r => r.RecipeTags != null && r.RecipeTags.Contains(tag)), keep, "tag " + tag);

        private static List<CollectibleRecipeBook> SceneBooks() =>
            UnityEngine.Object.FindObjectsByType<CollectibleRecipeBook>(FindObjectsSortMode.None).OrderBy(BookReroll.PathOf).ToList();

        private static void Books()
        {
            var books = SceneBooks();
            Known.Refresh();
            for (int i = 0; i < books.Count; i++)
            {
                var b = books[i];
                var ids = Vanilla.BookRecipeIds(b);
                var pool = RecipeDatabase.instance.FetchRecipes(b.Recipes, b.Modifiers.ToArray(), b.RecipeTag, b.TagMode, b.SkillRange);
                int unknown = pool.Select(r => r.ID).Distinct().Count(id => !Known.Collected(id));
                var sb = new StringBuilder();
                foreach (int id in ids) sb.Append(id).Append(Known.Collected(id) ? "(c) " : "(new) ");
                Log.Warn($"Testing: book {i} {BookReroll.PathOf(b)}: ids {sb}added {Vanilla.BookRecipesAdded(b)}, max {b.maxRecipes}, pool {pool.Count}, unknown {unknown}, title '{b.title}'");
            }
            if (books.Count == 0) Log.Warn("Testing: no recipe books in this scene.");
        }

        private static void BookRespawn(int index)
        {
            var books = SceneBooks();
            if (index < 0 || index >= books.Count) throw new ArgumentException("no book " + index);
            books[index].OnRespawn();
        }

        private static void UsedIn(int itemId)
        {
            if (!UsedIndex.Ready() || !Known.Refresh()) throw new InvalidOperationException("index not ready");
            var found = new List<Recipe>();
            UsedIndex.Query(itemId, false, found, new HashSet<Recipe>());
            Log.Warn($"Testing: used in {itemId}, all ({found.Count}): {string.Join(", ", found.Select(r => r.Name).Distinct().ToArray())}");
            var known = found.Where(Known.Visible).Select(r => r.Name).Distinct().ToList();
            Log.Warn($"Testing: used in {itemId}, known ({known.Count}): {string.Join(", ", known.ToArray())}");
            var item = ItemDatabase.instance.FetchItemByID(itemId);
            if (item == null) return;
            Log.Warn($"Testing: tooltip block: {ExtraLines.ForTooltip(item, item.Meta) ?? "(none)"}");
            var set = ExtraLines.ForPanel(item, item.Meta);
            Log.Warn(set == null ? "Testing: panel lines: (none)" : $"Testing: panel lines: {set.RemainingFull} | {(set.Names.Count > 0 ? ExtraLines.PanelUsedIn(set, set.Names.Count) : "-")}");
        }

        private static void Pool(int itemId)
        {
            var item = ItemDatabase.instance.FetchItemByID(itemId) ?? throw new ArgumentException("unknown item " + itemId);
            if (!Remaining.For(item, out var c)) { Log.Warn($"Testing: {item.Title} teaches no recipes."); return; }
            Log.Warn($"Testing: {item.Title}: total {c.Total}, new {c.NewLeft}, unlock {c.UnlockLeft}");
        }

        private static void BadgeDump()
        {
            var bar = FilterBar.Active;
            var list = bar != null ? bar.List : null;
            if (list == null) throw new InvalidOperationException("open a station first");
            var cb = CraftingBase.currentCraftingBase;
            float eq = cb != null ? cb.EquipmentQualityWithUpgrades : 1f;
            var buttons = Vanilla.RecipeButtons(list);
            for (int i = 0; i < Math.Min(buttons.Count, list.currentRecipes.Count); i++)
            {
                var r = list.currentRecipes[i];
                var badge = buttons[i] != null ? buttons[i].transform.Find(Badges.BadgeName) : null;
                string text = badge != null ? badge.GetComponent<TMPro.TextMeshProUGUI>().text.Replace("\n", " | ") : "-";
                float p = r.CurrentFailChance(r.RelevantSkill, eq, r.SkillFactor);
                Log.Warn($"Testing: {r.ID} {r.Name}: badge [{text}] panel {(int)(p * 100f)}% skill {r.RelevantSkill} min {r.MinimumSkill} hard {r.HardMinimumSkill} check {RecipeInfoUI.CheckSkill(r)}");
            }
        }
    }
}
#endif
