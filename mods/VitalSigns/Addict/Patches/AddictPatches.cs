using System;
using System.Reflection;
using HarmonyLib;

namespace VitalSigns
{
    // C1: PlayerStatsAddictions.UpdateValues(ItemConsumable) postfix, Priority.Last (spec §8.1).
    [HarmonyPatch]
    internal static class AddictionItemPatch
    {
        private static MethodBase TargetMethod() => Vanilla.AddictionsUpdateValues;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PlayerStatsAddictions __instance)
        {
            try { Clamp.AfterItem(__instance); }
            catch (Exception e) { Log.Exception("addiction clamp", e); }
        }
    }

    // C2: PlayerStatsEdit.ChangeAlcoholAddiction / ChangeMushroomAddiction postfixes, Priority.Last. Reached through
    // UnityEvent delegates from scene events, so there is no call-site inlining.
    [HarmonyPatch]
    internal static class EditAlcoholPatch
    {
        private static MethodBase TargetMethod() => Vanilla.EditAlcoholAddiction;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            try { if (PlayerStats.instance != null) Clamp.Alcohol(PlayerStats.instance.addictions); }
            catch (Exception e) { Log.Exception("alcohol event clamp", e); }
        }
    }

    [HarmonyPatch]
    internal static class EditMushroomPatch
    {
        private static MethodBase TargetMethod() => Vanilla.EditMushroomAddiction;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            try { if (PlayerStats.instance != null) Clamp.Mushroom(PlayerStats.instance.addictions); }
            catch (Exception e) { Log.Exception("mushroom event clamp", e); }
        }
    }

    // C3: PlayerStats.OnLoadingGame() postfix, Priority.Last (spec §8.2): the values are restored inside, by
    // DeSerializeData. Only when it returned true. Runs on loads and doors; idempotent.
    [HarmonyPatch]
    internal static class LoadClampPatch
    {
        private static MethodBase TargetMethod() => Vanilla.StatsOnLoadingGame;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PlayerStats __instance, bool __result)
        {
            if (!__result) return;
            try { Clamp.OnLoad(__instance != null ? __instance.addictions : null); }
            catch (Exception e) { Log.Exception("load clamp", e); }
        }
    }

    // D1: PlayerStats.DeltaSeconds(int) prefix + void finalizer: set and restore the running call's seconds, never sum.
    // A call skipped by another prefix (Time Keeper's splitter) sets -1; its 1-second pieces set 1.
    [HarmonyPatch]
    internal static class DeltaSecondsPatch
    {
        private static MethodBase TargetMethod() => Vanilla.DeltaSeconds;

        private static void Prefix(int sec, bool __runOriginal, out int __state)
        {
            __state = SecScope.Sec;
            SecScope.Sec = __runOriginal ? sec : -1;
        }

        private static void Finalizer(int __state) => SecScope.Sec = __state;
    }

    // D2: PlayerStatsDuck.UpdateStats() prefix + postfix (spec §8.3). Never skips the original.
    [HarmonyPatch]
    internal static class DuckPatch
    {
        private static MethodBase TargetMethod() => Vanilla.DuckUpdateStats;

        private static void Prefix(out int __state)
        {
            __state = 0;
            try { __state = DuckTime.Before(); }
            catch (Exception e)
            {
                __state = 0;
                Log.Exception("duck prefix", e);
            }
        }

        private static void Postfix(PlayerStatsDuck __instance, int __state, bool __runOriginal)
        {
            if (!__runOriginal || __state <= 0) return;
            DuckTime.After(__instance, __state);
        }
    }
}
