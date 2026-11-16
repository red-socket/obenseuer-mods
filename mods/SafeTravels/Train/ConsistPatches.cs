using System;
using HarmonyLib;

namespace SafeTravels
{
    // P6 (spec §3.6): a link was released. Only marks the consists for a check in LateUpdate, so nothing runs inside
    // vanilla's call chain and nothing re-enters it.
    [HarmonyPatch(typeof(TrackTrain.TrainCoupler), nameof(TrackTrain.TrainCoupler.Release), typeof(bool))]
    internal static class CouplerReleasePatch
    {
        private static void Postfix() => Consist.LinkChanged();
    }

    // P7 (spec §3.7): a link was made. Also catches MergeTrain's silent "Wagon is not part of this train" failure
    // (SRC/TrackTrain.cs:553-556). Calls from LateStart during a load wait for TrainWatch.Settled.
    [HarmonyPatch(typeof(TrackTrain.TrainCoupler), nameof(TrackTrain.TrainCoupler.Couple), typeof(TrackTrain.TrainCoupler))]
    internal static class CouplerCouplePatch
    {
        private static void Postfix() => Consist.LinkChanged();
    }

    // P8 (spec §3.8): a train found its saved data and started OnLoadingGameDelay.
    [HarmonyPatch(typeof(TrackTrain), nameof(TrackTrain.OnLoadingGame))]
    internal static class TrainOnLoadingGamePatch
    {
        private static void Postfix(TrackTrain __instance, bool __result)
        {
            try
            {
                if (__result && __instance != null) TrainWatch.Pending[__instance] = UnityEngine.Time.realtimeSinceStartup;
            }
            catch (Exception e) { Log.Exception("train load", e); }
        }
    }

    // P9 (spec §3.9): UncoupleIfTooFarAfterLoad is the last statement of OnLoadingGameDelay (SRC/TrackTrain.cs:926), so
    // its postfix marks the end of that train's load without patching the iterator.
    [HarmonyPatch(typeof(TrackTrain), "UncoupleIfTooFarAfterLoad")]
    internal static class UncoupleAfterLoadPatch
    {
        private static void Postfix(TrackTrain __instance)
        {
            try { TrainWatch.Finished(__instance); }
            catch (Exception e) { Log.Exception("train loaded", e); }
        }
    }
}
