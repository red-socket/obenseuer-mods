using System;
using System.Reflection;
using HarmonyLib;

namespace FairMarket
{
    // L1: RecipeInfoUI.ShowRecipe(Recipe, int, float, float, bool, bool) postfix (spec §8.1). Only the dishwasher's own
    // recipes (useOnlyStolenItems and keepItemOwnerOnFail, as Dishwasher.GenerateRecipe builds them) in the
    // manufacturing panel's recipe info. Workshop's W1 adds a row to the RecipeInfo root; this edits only the details
    // text, so both apply.
    [HarmonyPatch]
    internal static class ShowRecipePatch
    {
        private static MethodBase TargetMethod() => Vanilla.ShowRecipe;

        private static void Postfix(RecipeInfoUI __instance, Recipe recipe, float equipmentQuality)
        {
            if (!Cfg.Hot.PreciseOdds) return;
            try
            {
                if (recipe == null || !recipe.useOnlyStolenItems || !recipe.keepItemOwnerOnFail) return;
                if (!(CraftingBase.currentCraftingBase is Dishwasher)) return;
                var mp = ManufacturingPanel.instance;
                var info = mp != null && mp.manufacturingPanelUI != null && mp.manufacturingPanelUI.recipeListUI != null
                    ? mp.manufacturingPanelUI.recipeListUI.recipeInfo : null;
                if (info == null || !ReferenceEquals(info, __instance) || Vanilla.RecipeInfoListPanel == null) return;
                WashInfo.Rewrite(WashInfo.Details(Vanilla.RecipeInfoListPanel(__instance)), recipe, equipmentQuality);
            }
            catch (Exception e) { Log.Exception("wash info", e); }
        }
    }

    // L2: Dishwasher.CheckItems(bool) postfix (spec §8.2): runs whenever the machine's contents are checked.
    [HarmonyPatch]
    internal static class DishwasherCheckPatch
    {
        private static MethodBase TargetMethod() => Vanilla.DishwasherCheckItems;

        private static void Postfix(Dishwasher __instance)
        {
            if (!Cfg.Hot.SkipNote || __instance == null) return;
            try
            {
                var mp = ManufacturingPanel.instance;
                if (mp == null || !ReferenceEquals(mp.ActiveManu, __instance)) return;
                SkipNote.Show(__instance, SkipNote.Reasons(__instance));
            }
            catch (Exception e) { Log.Exception("wash skips", e); }
        }
    }
}
