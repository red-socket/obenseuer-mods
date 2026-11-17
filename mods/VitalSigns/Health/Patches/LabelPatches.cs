using System;
using System.Reflection;
using HarmonyLib;

namespace VitalSigns
{
    // Label hooks (spec §4, L1-L4): exact contexts that rename losses which the damage type alone would mislabel. Each is
    // applied in its own try (Plugin), so a failed one only loses its label.
    internal static class LabelHook
    {
        internal static int Enter(K kind)
        {
            try { return Tracker.Live ? Tracker.Enter(new Frame { Kind = kind }) : 0; }
            catch (Exception e) { Log.Exception("label prefix " + kind, e); return 0; }
        }

        internal static void Exit(int state)
        {
            try { Tracker.Exit(state); }
            catch (Exception e) { Log.Exception("label finalizer", e); }
        }
    }

    // L1: PlayerStatsHungerAndThirst.EmptyToToilet(int). Its only bleeding write is crumpled paper (+1.5,
    // SRC/PlayerStatsHungerAndThirst.cs:186). Callers: the hunger tick (:80, :236) and Toilet.OnSitEnd (SRC/Toilet.cs:170).
    [HarmonyPatch]
    internal static class ToiletPatch
    {
        private static MethodBase TargetMethod() => Vanilla.EmptyToToilet;
        private static void Prefix(out int __state) => __state = LabelHook.Enter(K.Toilet);
        private static void Finalizer(int __state) => LabelHook.Exit(__state);
    }

    // L2: Depression.HurtYourself(DamageTypeNames), static. Callers: the mood breakdown (SRC/PlayerStatsMentalHealth.cs:87,
    // :100) and BlackoutEvent (SRC/BlackoutEvent.cs:18); the mood-tick frame tells them apart.
    [HarmonyPatch]
    internal static class SelfHarmPatch
    {
        private static MethodBase TargetMethod() => Vanilla.HurtYourself;
        private static void Prefix(out int __state) => __state = LabelHook.Enter(K.SelfHarm);
        private static void Finalizer(int __state) => LabelHook.Exit(__state);
    }

    // L3: TreatmentFungalDisease.BuyTreatment(Treatment), the override. Growths removal hurts with Violence (:160).
    [HarmonyPatch]
    internal static class FungalTreatmentPatch
    {
        private static MethodBase TargetMethod() => Vanilla.FungalBuyTreatment;
        private static void Prefix(out int __state) => __state = LabelHook.Enter(K.FungalTreatment);
        private static void Finalizer(int __state) => LabelHook.Exit(__state);
    }

    // L4: Recipe.RecipeStatsEditData.TestAndApply(bool): crafting side effects (SRC/Recipe.cs:143-168).
    [HarmonyPatch]
    internal static class CraftingPatch
    {
        private static MethodBase TargetMethod() => Vanilla.CraftTestAndApply;
        private static void Prefix(out int __state) => __state = LabelHook.Enter(K.Crafting);
        private static void Finalizer(int __state) => LabelHook.Exit(__state);
    }
}
