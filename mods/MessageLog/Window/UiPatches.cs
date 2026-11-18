using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MessageLog
{
    // W1: PauseNavigationHandler.Update() prefix, Priority.First, copied from Fair Market (Vital Signs', Quest Helper's).
    // While the window is open (and on the frame after it closes) the pause menu doesn't read the pad or keyboard, so no
    // pause button fires underneath. Inert while the window is closed; Unofficial Patch's ConsoleGuard prefix and our
    // sibling windows' W1 copies only ever skip too.
    [HarmonyPatch]
    internal static class PauseNavUpdatePatch
    {
        private static MethodBase TargetMethod() => Vanilla.PauseNavUpdate;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return !(LogWindow.IsOpen || Time.frameCount <= LogWindow.ClosedFrame + 1);
        }
    }

    // W2: PauseNavigationHandler.HandleBackButtonClick() prefix, Priority.First. Esc, pad B and pad Menu all end here;
    // with the window open they end typing first, then close the window instead of resuming. The window also reads pad B
    // itself in Plugin.Update, which runs before GameController.Update (execution order 10400): when that press has just
    // closed the window, GameController's "Menu" path still reaches this method in the same frame, so a call on the
    // closing frame is swallowed too (Fair Market UiPatches.cs:120-130). Otherwise pad B would close the window and
    // resume the game at once.
    [HarmonyPatch]
    internal static class HandleBackButtonPatch
    {
        private static MethodBase TargetMethod() => Vanilla.HandleBackButtonClick;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            try
            {
                if (!LogWindow.IsOpen) return Time.frameCount != LogWindow.ClosedFrame;
                LogWindow.Back();
                return false;
            }
            catch (Exception e)
            {
                Log.Once("Window: back", e);
                return true;
            }
        }
    }

    // H1: InventoryNavigationHandler.GetButtonInfo(int, CurrentInventoryWindow, InputType) postfix. Vanilla returns "" for
    // tab 4, which is only ever the Notes page (SRC/InventoryNavigationHandler.cs:416-418), so no page check is needed.
    // Appends "F)MESSAGES" (pad "X)MESSAGES") in the game's own style; KeyCodeInfoToString returns only the new part,
    // with its "   " separator when the line isn't empty. Fair Market appends "R)INVOICES" to the same line; either
    // order reads fine.
    [HarmonyPatch]
    internal static class ButtonInfoPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GetButtonInfo;

        private static void Postfix(int tab, ref string __result)
        {
            if (tab != 4 || !Cfg.Hot.NotesPageKey || !Cfg.Hot.WindowOn || !LogWindow.Available || !NotesPageInput.Available) return;
            try
            {
                string s = __result ?? "";
                __result = s + InventoryNavigationHandler.KeyCodeInfoToString(s, Strings.HintLabel, "Move Item");
            }
            catch (Exception e) { Log.Once("Window: notes hint", e); }
        }
    }
}
