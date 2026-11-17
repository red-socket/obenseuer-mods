using System.Collections.Generic;
using System.Text;

namespace RecipeBook
{
    // The folded text each recipe is searched by (spec §6.3): its name and, with Search.MatchIngredients, the titles of
    // its input and output items (group items contribute their own title, such as "Water"). Cached by recipe ID, never
    // by reference (station lists may hold per-rebuild copies), except shared IDs (-1, 10421), which are rebuilt each
    // time: a few lookups. The cache is dropped when the used-in index is rebuilt or the setting changes.
    internal static class SearchKeys
    {
        private static readonly Dictionary<int, string> Cache = new Dictionary<int, string>();
        private static readonly List<int> Ids = new List<int>(8);
        private static readonly StringBuilder Sb = new StringBuilder(128);
        private static int _generation = -1;
        private static bool _ingredients;

        internal static string For(Recipe r)
        {
            if (r == null) return "";
            bool ready = UsedIndex.Ready();
            if (UsedIndex.Generation != _generation || Cfg.Hot.MatchIngredients != _ingredients)
            {
                Cache.Clear();
                _generation = UsedIndex.Generation;
                _ingredients = Cfg.Hot.MatchIngredients;
            }
            bool cacheable = ready && !UsedIndex.Shared(r.ID);
            if (cacheable && Cache.TryGetValue(r.ID, out var key)) return key;
            key = Build(r, ready);
            if (cacheable) Cache[r.ID] = key;
            return key;
        }

        private static string Build(Recipe r, bool ready)
        {
            Sb.Length = 0;
            Sb.Append(Fold.Of(r.Name));
            if (Cfg.Hot.MatchIngredients && ready)
            {
                Ids.Clear();
                UsedIndex.ItemIds(r, Ids);
                foreach (int id in Ids)
                {
                    string t = UsedIndex.TitleOf(id);
                    if (t.Length > 0) Sb.Append(' ').Append(Fold.Of(t));
                }
            }
            return Sb.ToString();
        }
    }
}
