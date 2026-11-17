using System;
using System.Reflection;
using HarmonyLib;

namespace QuestHelper
{
    // P6: GlobalState.OnLoadingGame() postfix (spec §3.2): read the journal string once the globals are deserialized.
    [HarmonyPatch]
    internal static class GlobalLoadPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GlobalStateOnLoadingGame;

        private static void Postfix(GlobalState __instance, bool __result)
        {
            try
            {
                if (__result && __instance == GlobalState.instance) Records.OnGlobalsLoaded();
            }
            catch (Exception e) { Log.Exception("journal load", e); }
        }
    }
}
