using System.Collections.Generic;

namespace RecipeBook
{
    // The player's recipe knowledge as ID sets (spec §10.2). "Collected" is RecipeController.collectedRecipes: recipe
    // papers, magazines and books, the same list as the game's HasRecipe and its "New recipe" note. "Learned" is
    // learnedRecipes: recipes crafted at least once (HasLearnedRecipe). Rebuilt when the controller, either list or
    // either count changes; Version is bumped on each rebuild so text caches can key on it.
    internal static class Known
    {
        private static readonly HashSet<int> CollectedIds = new HashSet<int>();
        private static readonly HashSet<int> LearnedIds = new HashSet<int>();
        private static RecipeController _rc;
        private static List<Recipe> _collected, _learned;
        private static int _collectedCount = -1, _learnedCount = -1;

        internal static int Version;

        // False when there is no game running (no RecipeController): callers show nothing.
        internal static bool Refresh()
        {
            var rc = RecipeController.instance;
            if (rc == null) return false;
            var c = rc.collectedRecipes;
            var l = rc.learnedRecipes;
            int cc = c != null ? c.Count : 0, lc = l != null ? l.Count : 0;
            if (ReferenceEquals(rc, _rc) && ReferenceEquals(c, _collected) && ReferenceEquals(l, _learned) && cc == _collectedCount && lc == _learnedCount)
                return true;
            _rc = rc;
            _collected = c;
            _learned = l;
            _collectedCount = cc;
            _learnedCount = lc;
            Fill(CollectedIds, c);
            Fill(LearnedIds, l);
            Version++;
            return true;
        }

        private static void Fill(HashSet<int> set, List<Recipe> list)
        {
            set.Clear();
            if (list == null) return;
            foreach (var r in list)
                if (r != null) set.Add(r.ID);
        }

        internal static bool Collected(int id) => CollectedIds.Contains(id);

        internal static bool Learned(int id) => LearnedIds.Contains(id);

        // The station rule (SRC/RecipeListUI.cs:36, :52): a recipe that needs no paper, or one the player has.
        internal static bool Visible(Recipe r) => r != null && (!r.RecipeRequired || Collected(r.ID) || Learned(r.ID));
    }
}
