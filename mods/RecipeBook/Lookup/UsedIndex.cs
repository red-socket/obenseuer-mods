using System;
using System.Collections.Generic;
using System.Diagnostics;
using OS.Items;

namespace RecipeBook
{
    // Ingredient -> recipes index, item groups and an id -> title map (spec §10.1). Built lazily on first use, one pass
    // over each database, and rebuilt when the databases change: RecipeDatabase.LoadDatabase creates new Recipe objects,
    // so the identity of Recipes[0] plus the two counts is the generation key (no LoadDatabase patch needed). Recipes are
    // kept by reference: IDs 10421 and -1 are shared by several recipes. Variant ingredients (variantItemCategory) are
    // not indexed (D15).
    internal static class UsedIndex
    {
        private static object _first;
        private static int _recipeCount = -1, _itemCount = -1;

        private static readonly Dictionary<int, string> Titles = new Dictionary<int, string>();
        private static readonly Dictionary<int, List<int>> GroupsOf = new Dictionary<int, List<int>>();
        private static readonly Dictionary<int, List<Recipe>> Direct = new Dictionary<int, List<Recipe>>();
        private static readonly Dictionary<int, Recipe> FirstById = new Dictionary<int, Recipe>();
        private static readonly HashSet<int> SharedIds = new HashSet<int>();

        // Bumped on every build; the search keys and text caches compare it.
        internal static int Generation;

        internal static bool Ready()
        {
            var recipes = RecipeDatabase.Recipes;
            var items = ItemDatabase.database;
            if (recipes == null || recipes.Count == 0 || items == null || items.Count == 0) return false;
            if (ReferenceEquals(recipes[0], _first) && recipes.Count == _recipeCount && items.Count == _itemCount) return true;
            Build(recipes, items);
            return true;
        }

        private static void Build(List<Recipe> recipes, List<Item> items)
        {
            var sw = Stopwatch.StartNew();
            Titles.Clear();
            GroupsOf.Clear();
            Direct.Clear();
            FirstById.Clear();
            SharedIds.Clear();

            foreach (var item in items)
            {
                if (item == null) continue;
                Titles[item.ID] = item.Title ?? "";
                var g = item.GetMetaOfType<ItemGroupData>();
                if (g?.itemGroup == null) continue;
                foreach (var member in g.itemGroup)
                {
                    if (member?.item == null) continue;
                    if (!GroupsOf.TryGetValue(member.item.ID, out var list)) GroupsOf[member.item.ID] = list = new List<int>(2);
                    if (!list.Contains(item.ID)) list.Add(item.ID);
                }
            }

            int ingredients = 0;
            foreach (var r in recipes)
            {
                // The first recipe per ID, as RecipeDatabase.FetchRecipesById (SRC/RecipeDatabase.cs:106) returns it.
                if (r != null && !FirstById.ContainsKey(r.ID)) FirstById[r.ID] = r;
                else if (r != null) SharedIds.Add(r.ID);
                // The serialized input, never Recipe.Input (null until SetItems, SRC/Recipe.cs:489-494).
                if (r == null || r.Type == RecipeType.Debug || r.input == null) continue;
                foreach (var key in r.input.Keys)
                {
                    if (key == null) continue;
                    if (!Direct.TryGetValue(key.ID, out var list))
                    {
                        Direct[key.ID] = list = new List<Recipe>(4);
                        ingredients++;
                    }
                    if (!list.Contains(r)) list.Add(r);
                }
            }

            _first = recipes[0];
            _recipeCount = recipes.Count;
            _itemCount = items.Count;
            Generation++;
            Log.Verbose($"Used in: index {recipes.Count} recipes, {ingredients} ingredients, {GroupsOf.Count} grouped items ({sw.ElapsedMilliseconds} ms)");
        }

        // RecipeDatabase.FetchRecipesById without the linear scan; only valid after Ready().
        internal static Recipe ById(int id) => FirstById.TryGetValue(id, out var r) ? r : null;

        // An ID that several database recipes share (10421 and -1 on 0.5.19): never cache by it.
        internal static bool Shared(int id) => id == -1 || SharedIds.Contains(id);

        internal static string TitleOf(int id) => Titles.TryGetValue(id, out var t) ? t : "";

        // Every recipe whose input lists the item or a group it belongs to (spec §10.2). Liquids keep only recipes that
        // take filled containers (metaSettings.useLiquidContainers). Debug recipes are never indexed. Fills `into`
        // without duplicates (by reference).
        internal static void Query(int itemId, bool liquid, List<Recipe> into, HashSet<Recipe> seen)
        {
            Add(itemId, liquid, into, seen);
            if (GroupsOf.TryGetValue(itemId, out var groups))
                foreach (int g in groups) Add(g, liquid, into, seen);
        }

        private static void Add(int key, bool liquid, List<Recipe> into, HashSet<Recipe> seen)
        {
            if (!Direct.TryGetValue(key, out var list)) return;
            foreach (var r in list)
            {
                if (liquid && (r.metaSettings == null || !r.metaSettings.useLiquidContainers)) continue;
                if (seen.Add(r)) into.Add(r);
            }
        }

        // The item and product IDs of a recipe for the search key (spec §6.3), from the serialized dictionaries, falling
        // back to the database recipe with the same ID when they are null (station lists can hold copies).
        internal static void ItemIds(Recipe r, List<int> into)
        {
            var input = r.input;
            var output = r.output;
            if ((input == null || output == null) && r.ID > 0 && RecipeDatabase.instance != null)
            {
                var db = RecipeDatabase.instance.FetchRecipesById(r.ID);
                if (db != null)
                {
                    input = input ?? db.input;
                    output = output ?? db.output;
                }
            }
            if (input != null) foreach (var k in input.Keys) if (k != null) into.Add(k.ID);
            if (output != null) foreach (var k in output.Keys) if (k != null) into.Add(k.ID);
        }
    }
}
