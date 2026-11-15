using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace UnofficialPatch
{
    // S-093 coffee volume (spec §9, D-4; OFF by default). Recipe 10017 "Coffee" (CoffeeMaker, a LiquidContainer variant
    // recipe) outputs 0.3 L of Coffee (13380) on success and failure, more than the 0.2 L mug holds. Vanilla reserves
    // ceil(0.3 / 0.2) = 2 mugs when the stack has two or more (ItemVariantManager.cs:332, CraftingBase.cs:752), so the
    // full 0.3 L is only lost down to 0.2 L with a single mug. Lowering the output therefore also cuts multi-mug brews
    // from 0.3 to 0.2 L, which is a balance change; hence the option is off by default. The recipe is changed after
    // RecipeDatabase loads, rather than the cup capacity, which would change every liquid and only new cups. Brews
    // already running keep their 0.3 L. Only a value still equal to vanilla's 0.3 is changed; idempotent. Runtime
    // only, and not reverted mid-session: switching the option off takes effect at the next launch.
    internal static class CoffeeVolume
    {
        internal static bool Patched;

        private const int RecipeId = 10017, CoffeeId = 13380;
        private const float VanillaLitres = 0.3f;

        internal static void Apply()
        {
            if (!Patched || !Cfg.CoffeeVolumeFix.Value) return;
            var recipes = RecipeDatabase.Recipes;
            if (recipes == null || recipes.Count == 0) return;
            Recipe r = null;
            foreach (var x in recipes)
                if (x != null && x.ID == RecipeId) { r = x; break; }
            if (r == null) return;

            float litres = Cfg.CoffeeRecipeOutputLitres.Value;
            int n = Fix(r.output, litres) + Fix(r.outputFail, litres);
            if (n == 0) return;
            if (ItemDatabase.instance != null) r.SetItems();   // rebuilds the Item-keyed Output/OutputFail from output/outputFail
            Log.Info($"Spoilage: the coffee maker now brews {litres} L of coffee instead of {VanillaLitres} L.");
        }

        private static int Fix(Dictionary<ItemReference, float> d, float litres)
        {
            if (d == null) return 0;
            ItemReference key = null;
            foreach (var kv in d)
                if (kv.Key != null && kv.Key.ID == CoffeeId && kv.Value == VanillaLitres) { key = kv.Key; break; }
            if (key == null) return 0;
            d[key] = litres;
            return 1;
        }
    }

    // RecipeDatabase.LoadDatabase(), private (Awake, OnEnable and Reload all go through it).
    [HarmonyPatch]
    internal static class RecipeLoadDatabasePatch
    {
        private static MethodBase TargetMethod() => Vanilla.RecipeLoadDatabase;

        private static void Postfix()
        {
            try { CoffeeVolume.Apply(); }
            catch (Exception e) { Log.Exception("Spoilage: coffee recipe", e); }
        }
    }
}
