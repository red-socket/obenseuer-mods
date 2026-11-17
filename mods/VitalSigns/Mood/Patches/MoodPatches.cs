using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace VitalSigns
{
    // M1: PlayerStatsMentalHealth.UpdateStats(int) prefix + finalizer. Marks the mood tick (for M1's snapshot and for the
    // self-harm and damage labels) and pushes a MoodTick frame for the damage log.
    [HarmonyPatch]
    internal static class MoodTickPatch
    {
        private static MethodBase TargetMethod() => Vanilla.MentalUpdateStats;

        private static void Prefix(out int __state)
        {
            __state = 0;
            try
            {
                MoodModel.TickStart();
                if (Tracker.Live) __state = Tracker.Enter(new Frame { Kind = K.MoodTick });
            }
            catch (Exception e) { Log.Exception("mood tick prefix", e); }
        }

        private static void Finalizer(int __state)
        {
            try
            {
                MoodModel.TickDone();
                Tracker.Exit(__state);
            }
            catch (Exception e) { Log.Exception("mood tick finalizer", e); }
        }
    }

    // M1: PlayerStatsHygiene.GetMentalHealthEffect() postfix: during the mood tick, snapshot the exact inputs of :75-76.
    [HarmonyPatch]
    internal static class HygieneEffectPatch
    {
        private static MethodBase TargetMethod() => Vanilla.HygieneMentalEffect;

        private static void Postfix(float __result)
        {
            try { MoodModel.OnHygieneEffect(__result); }
            catch (Exception e) { Log.Exception("hygiene effect postfix", e); }
        }
    }

    // I1: PlayerStatsUI.UpdateColor(...) prefix, about 39 calls a frame: an int compare and a cached bool. Index 9 is the
    // Mental health icon, hidden below 33 (SRC/PlayerStatsUI.cs:229). Off by default (D9).
    [HarmonyPatch]
    internal static class MoodIconPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateColor;

        private static void Prefix(int index, ref bool invisible)
        {
            if (index != 9 || !invisible || !Cfg.Hot.AlwaysShowIcon) return;
            try
            {
                if (!Game.MentalHealthDisabled) invisible = false;
            }
            catch (Exception e) { Log.Exception("mood icon", e); }
        }
    }

    // G1: PlayerStatsMentalHealth.MentalHealthStatsUpdate() transpiler (spec §7.4).
    [HarmonyPatch]
    internal static class GamblingTermPatch
    {
        private static MethodBase TargetMethod() => Vanilla.MentalHealthStatsUpdate;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => GamblingGate.Transpile(instructions);
    }
}
