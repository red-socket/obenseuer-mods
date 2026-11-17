using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace QuestHelper
{
    // P3: PauseNavigationHandler.Update() prefix, Priority.First (spec §3.2). While the window is open (and on the frame
    // it closes) the pause menu does not read the pad or keyboard, so no pause button fires underneath. Unofficial
    // Patch's ConsoleGuard prefix on the same method is unaffected.
    [HarmonyPatch]
    internal static class PauseNavUpdatePatch
    {
        private static MethodBase TargetMethod() => Vanilla.PauseNavUpdate;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            if (HelperWindow.IsOpen) return false;
            return Time.frameCount > HelperWindow.ClosedFrame + 1;
        }
    }
}
