using System;
using System.Reflection;
using HarmonyLib;

namespace QuestHelper
{
    // P5: GlobalState.OnSavingGame() prefix (spec §3.2, §8), the house pattern Workshop uses: only the live GlobalState,
    // only when this load's journal was read (an unknown format is never overwritten). Never throws into the save.
    [HarmonyPatch]
    internal static class GlobalSavePatch
    {
        private static MethodBase TargetMethod() => Vanilla.GlobalStateOnSavingGame;

        private static void Prefix(GlobalState __instance)
        {
            try
            {
                if (__instance == GlobalState.instance) Records.OnSaving();
            }
            catch (Exception e) { Log.Exception("journal save", e); }
        }
    }
}
