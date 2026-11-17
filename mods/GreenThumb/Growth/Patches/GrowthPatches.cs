using System;
using System.Reflection;
using HarmonyLib;

namespace GreenThumb
{
    // G1: grow.GetCurrentStage() postfix (D2). In ShowPlantLife mode a fruit plant that can't ripen any more fruit (all
    // picked on Cucumber and Peas, a spoiled hop) is labelled Spoil, so the vanilla number (the plant's remaining life,
    // > 0 here because StopGrow is false) reads "Spoiling X%". Growing, Spoil, Rotting and plants without growSpoil are
    // never touched. Runs every frame on the aimed plant and per plant with the panel open: a few field reads per fruit.
    [HarmonyPatch]
    internal static class StagePatch
    {
        private static MethodBase TargetMethod() => Vanilla.GrowGetCurrentStage;

        private static void Postfix(grow __instance, ref ProcessStage __result)
        {
            if (__result != ProcessStage.GrowingFruits || !Cfg.Hot.FruitPercent || !Cfg.Hot.ShowPlantLife) return;
            try
            {
                FruitProgress.Evaluate(__instance, out bool ripe, out bool canGrow, out float best);
                FruitProgress.Remember(__instance, ripe, canGrow, best);
                if (!ripe && !canGrow) __result = ProcessStage.Spoil;
            }
            catch (Exception e) { Log.Exception("Growth: stage", e); }
        }
    }

    // G2: grow.GetCurrentStageProcess() postfix (D1). While the (patched) stage is GrowingFruits: 1 when a fruit is ripe,
    // otherwise the most advanced fruit capped at 99%, so 100% always means "something to pick" and the bar only fills
    // within a cycle. The stage comes from the patched GetCurrentStage, so the label and the number always agree; that
    // call never comes back here (GetCurrentStage doesn't call the process). 0 only in ZeroPercent mode.
    [HarmonyPatch]
    internal static class ProcessPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GrowGetCurrentStageProcess;

        private static void Postfix(grow __instance, ref float __result)
        {
            if (!Cfg.Hot.FruitPercent) return;
            try
            {
                FruitProgress.Forget();
                if (__instance.GetCurrentStage() != ProcessStage.GrowingFruits) return;
                if (!FruitProgress.Recall(__instance, out bool ripe, out bool canGrow, out float best))
                    FruitProgress.Evaluate(__instance, out ripe, out canGrow, out best);
                __result = ripe ? 1f : canGrow ? Math.Min(best, 0.99f) : 0f;
            }
            catch (Exception e) { Log.Exception("Growth: progress", e); }
        }
    }
}
