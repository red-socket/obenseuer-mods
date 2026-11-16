using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Workshop
{
    // Set only for the length of one synchronous CraftDelayed call made by CancelAction (spec §7.3), on the main
    // thread. W11-W13 act only while it is set.
    internal static class CancelScope
    {
        internal static bool Active;
        internal static CraftingBase Machine;
    }

    internal static class CancelPatches
    {
        internal static bool Patched;
    }

    // W10: ProgressSlot.OnPointerClick(PointerEventData) postfix (SRC/ProgressSlot.cs:64-71). Mouse clicks and the
    // controller's A (SRC/InventoryNavigationHandler.cs:1697-1699) both arrive here. Only slots of the manufacturing
    // panel's running jobs count; the growing panel's harvest is untouched.
    [HarmonyPatch]
    internal static class ProgressSlotClickPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ProgressSlotClick;

        private static void Postfix(ProgressSlot __instance)
        {
            try
            {
                if (!Cfg.Cancel.Enabled.Value || __instance == null) return;
                CancelAction.OnSlotClick(__instance.gameObject);
            }
            catch (Exception e) { Log.Exception("cancel click", e); }
        }
    }

    // W11: CraftingBase.CreateItems(...) prefix, first: inside a cancel the job fails for sure (spec §7.3).
    [HarmonyPatch]
    internal static class CreateItemsPatch
    {
        private static MethodBase TargetMethod() => Vanilla.CreateItems;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(CraftingBase __instance, ref bool forcefail)
        {
            if (CancelScope.Active && __instance == CancelScope.Machine) forcefail = true;
        }
    }

    // W12: PlayerStats.IncreaseSkill(type, amount, notification) prefix, first, skipping: no skill for a cancelled job.
    // Covers both grants on that path: Recipe.GetCraftingResult's SkillSuccess (SRC/Recipe.cs:358) and CreateItems
    // (SRC/CraftingBase.cs:619).
    [HarmonyPatch]
    internal static class IncreaseSkillPatch
    {
        private static MethodBase TargetMethod() => Vanilla.IncreaseSkill;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal || !CancelScope.Active) return true;
            __result = false;
            return false;
        }
    }

    // W13: Recipe.RecipeStatsEditData.TestAndApply(bool) prefix, skipping: no fail damage, sound or achievement for a
    // cancelled job on a bench that needs the player (SRC/manufacturing.cs:545-548).
    [HarmonyPatch]
    internal static class TestAndApplyPatch
    {
        private static MethodBase TargetMethod() => Vanilla.TestAndApply;

        private static bool Prefix(bool __runOriginal) => !__runOriginal || !CancelScope.Active;
    }
}
