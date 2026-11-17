using System;
using HarmonyLib;
using UnityEngine.UI;

namespace DisplayOptions
{
    // P6 (spec §3.1): a void prefix, so the Handle() inside OnEnable (UGUI/CanvasScaler.cs:221-227) already uses the
    // scaled reference. Covers canvases enabled after the scene-load sweep (CardGameUI is instantiated late at
    // CardGameTable.cs:317; shops are enabled when opened). HandleScaleWithScreenSize is not patched: that would mean a
    // write and a canvas rebuild every frame.
    [HarmonyPatch(typeof(CanvasScaler), "OnEnable")]
    internal static class ScalerOnEnablePatch
    {
        private static void Prefix(CanvasScaler __instance)
        {
            if (!Scaling.Active) return;
            try { Scaling.OnScalerEnable(__instance); }
            catch (Exception e)
            {
                Scaling.Broken = true;
                Log.Exception("Ui: scaler enable", e);
            }
        }
    }
}
