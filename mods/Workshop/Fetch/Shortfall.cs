using System;
using System.Collections.Generic;
using OS.Items;

namespace Workshop
{
    // One recipe input and how much of it the station still lacks, in the game's own units: items, charges or litres.
    internal sealed class Need
    {
        internal Recipe.RecipeRequirementData Req;   // at the planned multiplier, before the station's stacks were counted
        internal float Missing;

        internal Item Item => Req.requiredItem;
    }

    // The station's shortfall for a recipe (spec §4.3), with exact vanilla parity: the requirements come from
    // CraftingBaseHelperFunctions.CreateRequirements(onlyExecutable: true) and are folded over the station's own
    // InputSlots with ProcessRequirementWithItemStack, the way StorageHasRequiredItems decides "craftable"
    // (SRC/CraftingBaseHelperFunctions.cs:8-41). onlyExecutable false is never used: it counts water, Tableware and
    // Utility as present. The variant extra (an untyped liquid container, mushrooms) is only reported.
    internal static class Shortfall
    {
        internal const float Epsilon = 1e-4f;

        internal static List<Need> Of(CraftingBase station, Recipe r, int k, out bool variantMissing)
        {
            variantMissing = false;
            var list = new List<Need>();
            if (station == null || r == null) return list;
            if (r.Input == null) r.SetItems();
            if (r.Input == null) return list;
            var reqs = CraftingBaseHelperFunctions.CreateRequirements(r, onlyExecutable: true);
            var slots = station.InputSlots;
            for (int i = 0; i < reqs.Length; i++)
            {
                var full = Copy(reqs[i], reqs[i].requiredAmount * Math.Max(1, k));
                var q = Copy(full, full.requiredAmount);
                if (slots != null)
                    foreach (var st in slots)
                        if (st != null) q = CraftingBaseHelperFunctions.ProcessRequirementWithItemStack(q, st, r);
                list.Add(new Need { Req = full, Missing = q.requiredAmount });
            }
            try
            {
                if (r.variantItemCategory != VariantCategory.None && ItemVariantManager.instance != null)
                {
                    CraftingBaseHelperFunctions.StorageHasRequiredItems(r, slots, out Item outLiquid, onlyExecutable: true);
                    variantMissing = !ItemVariantManager.instance.StoragePanelsHasVariant(r.variantItemCategory, slots, r.variantItemAmount, outLiquid, null, r.NoOutputs());
                }
            }
            catch (Exception e)
            {
                Log.Exception("variant check", e);
                variantMissing = true;
            }
            return list;
        }

        internal static bool AnyMissing(List<Need> needs)
        {
            foreach (var n in needs)
                if (n.Missing > Epsilon) return true;
            return false;
        }

        // The name of the first missing input (gate 9's reason), or null when nothing is missing.
        internal static string FirstMissing(CraftingBase station, Recipe r)
        {
            try
            {
                var needs = Of(station, r, 1, out bool variant);
                foreach (var n in needs)
                    if (n.Missing > Epsilon) return Game.Title(n.Item);
                if (variant) return VariantName(r);
            }
            catch (Exception e) { Log.Exception("shortfall", e); }
            return null;
        }

        internal static string VariantName(Recipe r)
        {
            try { return Game.Title(ItemVariantManager.instance.GetDefaultItem(r.variantItemCategory)); }
            catch { return "a container"; }
        }

        // How much one stack would count towards a requirement, by folding a copy with a large amount over that stack
        // alone. A second, larger fold only when the first is used up, so small amounts (0.3 L) stay precise in float.
        internal static float Contribution(Recipe.RecipeRequirementData req, ItemStack st, Recipe r)
        {
            if (req == null || st == null) return 0f;
            float big = 1e4f;
            var q = CraftingBaseHelperFunctions.ProcessRequirementWithItemStack(Copy(req, big), st, r);
            if (q.requiredAmount > 0f) return big - q.requiredAmount;
            big = 1e8f;
            q = CraftingBaseHelperFunctions.ProcessRequirementWithItemStack(Copy(req, big), st, r);
            return big - q.requiredAmount;
        }

        internal static Recipe.RecipeRequirementData Copy(Recipe.RecipeRequirementData r, float amount) => new Recipe.RecipeRequirementData
        {
            requiredItem = r.requiredItem,
            requiredAmount = amount,
            isGroup = r.isGroup,
            groupAllowMixed = r.groupAllowMixed,
            groupData = r.groupData,
            groupActiveItemID = r.groupActiveItemID,
        };
    }
}
