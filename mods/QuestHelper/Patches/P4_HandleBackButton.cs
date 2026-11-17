using System;
using System.Reflection;
using HarmonyLib;

namespace QuestHelper
{
    // P4: PauseNavigationHandler.HandleBackButtonClick() prefix, Priority.First (spec §3.2). Esc, pad B and pad Menu all
    // end here (PauseMenu.Pause -> PauseSettings); with the window open they step back in it instead of resuming.
    [HarmonyPatch]
    internal static class HandleBackButtonPatch
    {
        private static MethodBase TargetMethod() => Vanilla.HandleBackButtonClick;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            try
            {
                if (!HelperWindow.IsOpen) return true;
                HelperWindow.Back();
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("pause back", e);
                return true;
            }
        }
    }
}
