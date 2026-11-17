using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RecipeBook
{
    // World recipe papers (CollectibleRecipeBook) swap a pick the player has already collected for one they haven't,
    // from the same paper's own pool (spec §12.2, D20-D21; off by default, a balance extra). Only fresh rolls (first
    // visit, no saved record) and respawns change: a roll made while the game is loading waits in Pending until
    // LoadingDone, and OnLoadingGame returning true (a saved record replaced the roll) takes it out again, so a saved
    // choice is never touched. Writes only the game's own int IDs from the game's own FetchRecipes pool (SRC
    // CollectibleRecipeBook.cs:92) into the game's recipeIds before any save writes them. When the pool has nothing new
    // left, the game's ID stays.
    internal static class BookReroll
    {
        private static readonly List<CollectibleRecipeBook> Pending = new List<CollectibleRecipeBook>();
        private static readonly List<Recipe> Candidates = new List<Recipe>();
        private static readonly HashSet<int> CandidateIds = new HashSet<int>();

        internal static void Clear() => Pending.Clear();

        internal static void OnRolled(CollectibleRecipeBook book)
        {
            if (!Cfg.Hot.BookReroll || book == null || book.maxRecipes == -1) return;
            var ids = Vanilla.BookRecipeIds(book);
            if (!Vanilla.BookRecipesAdded(book) || ids == null || ids.Count == 0) return;
            if (SaveController.Loading || RecipeController.instance == null || RecipeDatabase.instance == null)
            {
                if (!Pending.Contains(book)) Pending.Add(book);
                return;
            }
            Reroll(book);
        }

        internal static void OnLoaded(CollectibleRecipeBook book, bool fromSave)
        {
            if (fromSave) Pending.Remove(book);
        }

        // LoadingDone fires synchronously right after Loading = false (SRC/SaveController.cs:833-838), after every
        // OnLoadingGame phase and before any later save.
        internal static void Flush()
        {
            if (Pending.Count == 0) return;
            var books = Pending.ToArray();
            Pending.Clear();
            if (!Cfg.Hot.BookReroll || RecipeController.instance == null || RecipeDatabase.instance == null) return;
            foreach (var b in books)
            {
                if (b == null || !Vanilla.BookRecipesAdded(b)) continue;   // destroyed (DestructibleList) or taken
                try { Reroll(b); }
                catch (Exception e) { Log.Exception("Books: re-roll", e); }
            }
        }

        // Error paths that clear Loading without LoadingDone (SRC/SaveController.cs:1785, :1805, :1811).
        internal static void Tick()
        {
            if (Pending.Count > 0 && !SaveController.Loading && RecipeController.instance != null) Flush();
        }

        private static void Reroll(CollectibleRecipeBook book)
        {
            if (!Known.Refresh()) return;
            var ids = Vanilla.BookRecipeIds(book);
            var pool = RecipeDatabase.instance.FetchRecipes(book.Recipes, book.Modifiers.ToArray(), book.RecipeTag, book.TagMode, book.SkillRange);
            Candidates.Clear();
            CandidateIds.Clear();
            int poolSize = 0;
            foreach (var r in pool)
            {
                if (r == null) continue;
                poolSize++;
                if (r.Type == RecipeType.Debug || Known.Collected(r.ID) || ids.Contains(r.ID) || !CandidateIds.Add(r.ID)) continue;
                Candidates.Add(r);
            }
            int unknown = Candidates.Count;
            bool changed = false, kept = false;
            var sb = Log.VerboseOn ? new StringBuilder() : null;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!Known.Collected(ids[i])) continue;
                if (Candidates.Count == 0) { kept = true; break; }
                var c = Candidates[UnityEngine.Random.Range(0, Candidates.Count)];
                sb?.Append(' ').Append(ids[i]).Append(" -> ").Append(c.ID);
                ids[i] = c.ID;
                Candidates.Remove(c);
                changed = true;
            }
            if (changed) Vanilla.BookUpdateTitle(book);
            if (sb != null && (changed || kept))
                Log.Verbose(changed
                    ? $"Books: re-roll {PathOf(book)}{sb} (pool {poolSize}, unknown {unknown})"
                    : $"Books: kept {PathOf(book)}: nothing new left (pool {poolSize})");
        }

        internal static string PathOf(Component c)
        {
            var sb = new StringBuilder();
            for (var t = c.transform; t != null; t = t.parent)
                sb.Insert(0, t.parent != null ? "/" + t.name : t.name);
            return c.gameObject.scene.name + "/" + sb;
        }
    }

    // P14: CollectibleRecipeBook.SetRandomRecipes() postfix (SRC :90). Runs on Start (first visit) and OnRespawn.
    [HarmonyPatch]
    internal static class SetRandomRecipesPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SetRandomRecipes;

        private static void Postfix(CollectibleRecipeBook __instance)
        {
            try { BookReroll.OnRolled(__instance); }
            catch (Exception e) { Log.Exception("Books: roll", e); }
        }
    }

    // P15: CollectibleRecipeBook.OnLoadingGame() postfix (SRC :34): true means the saved record replaced the roll (D21).
    [HarmonyPatch]
    internal static class BookOnLoadingGamePatch
    {
        private static MethodBase TargetMethod() => Vanilla.BookOnLoadingGame;

        private static void Postfix(CollectibleRecipeBook __instance, bool __result)
        {
            try { BookReroll.OnLoaded(__instance, __result); }
            catch (Exception e)
            {
                Log.Exception("Books: load", e);
                try { BookReroll.OnLoaded(__instance, true); } catch { }
            }
        }
    }
}
