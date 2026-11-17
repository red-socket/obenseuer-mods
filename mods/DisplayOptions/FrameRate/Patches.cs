using System;
using HarmonyLib;

namespace DisplayOptions
{
    // P1: after vanilla's write (spec §3.1). Priority.Last so ours is the final word among additive patches.
    [HarmonyPatch(typeof(DisplaySave), nameof(DisplaySave.ApplyFrameRateLimit))]
    internal static class ApplyFrameRateLimitPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(int preferredRefreshRate) => FrameLimiter.Apply(preferredRefreshRate);
    }

    // P2: the fallback if P1 was inlined, and the hook for a display Apply: applyDisplaySettingsMono calls this right after
    // Screen.SetResolution (OptionsMenu.cs:1215-1216), which takes effect at the end of the frame. Two frames later the cap
    // is applied again (MatchMonitor reads the new mode) and the UI is fitted to the new aspect.
    [HarmonyPatch(typeof(OptionsMenu), nameof(OptionsMenu.ApplyVerticalSync))]
    internal static class ApplyVerticalSyncPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            try
            {
                FrameLimiter.Reapply();
                Plugin.ScheduleDisplayRefit();
            }
            catch (Exception e) { Log.Exception("Fps: vsync postfix", e); }
        }
    }
}
