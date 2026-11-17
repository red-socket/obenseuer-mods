using System;
using System.Reflection;
using HarmonyLib;

namespace RecipeBook
{
    // P1: RecipeListUI.UpdateRecipes(...) postfix, Priority.Last (spec §5.2). Every station rebuild (enter, storage
    // change, craft start and done, power, Workshop Fetch) ends here, before the caller's Initialize*RecipeList(false),
    // so the game's navigation only ever sees visible buttons. Runs last so other mods' label edits survive; it only
    // hides buttons and adds badges, never re-calls UpdateRecipes or CheckItems and never edits currentRecipes or
    // recipeButtons. The filter and the badges fail independently.
    [HarmonyPatch]
    internal static class UpdateRecipesPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateRecipes;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(RecipeListUI __instance, CraftingBase activeCraftingBase, float equipmentQuality)
        {
            if (__instance == null) return;
            try { FilterBar.OnRebuilt(__instance); }
            catch (Exception e)
            {
                Log.Exception("Filter: rebuild", e);
                try { Filter.ShowAll(__instance); } catch { }
            }
            try { Badges.Apply(__instance, activeCraftingBase, equipmentQuality); }
            catch (Exception e) { Log.Exception("Badges: rebuild", e); }
        }
    }
}
