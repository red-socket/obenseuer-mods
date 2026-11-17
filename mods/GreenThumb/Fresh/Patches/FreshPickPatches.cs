using System;
using System.Reflection;
using HarmonyLib;

namespace GreenThumb
{
    // F1: GrowItem.UpdateSpoilTime(int) postfix (private, SRC/GrowItem.cs:354). Runs once per ripe fruit per grow tick
    // (every 60+ game seconds); paused growers don't tick, and each of TimeKeeper's large batches ends here too.
    [HarmonyPatch]
    internal static class SpoilTimePatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateSpoilTime;

        private static void Postfix(GrowItem __instance)
        {
            if (Cfg.Hot.FreshMode == FreshMode.Off) return;
            try { FreshPicking.Apply(__instance); }
            catch (Exception e) { Log.Exception("Fresh: fresh picking", e); }
        }
    }
}
