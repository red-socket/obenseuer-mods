using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FairMarket
{
    // W1: PauseNavigationHandler.Update() prefix, Priority.First, copied from Vital Signs (Quest Helper's P3). While the
    // window is open (and on the frame it closes) the pause menu doesn't read the pad or keyboard, so no pause button
    // fires underneath. Inert while the window is closed; Unofficial Patch's ConsoleGuard prefix is unaffected.
    [HarmonyPatch]
    internal static class PauseNavUpdatePatch
    {
        private static MethodBase TargetMethod() => Vanilla.PauseNavUpdate;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            if (InvoiceWindow.IsOpen) return false;
            return Time.frameCount > InvoiceWindow.ClosedFrame + 1;
        }
    }

    // W2: PauseNavigationHandler.HandleBackButtonClick() prefix, Priority.First. Esc, pad B and pad Menu all end here;
    // with the window open they disarm a pending Pay first, then close the window instead of resuming. The window also
    // reads pad B itself in Plugin.Update, which runs before GameController.Update (execution order 10400): when that
    // press has just closed the window, GameController's "Menu" path still reaches this method in the same frame, so a
    // call on the closing frame is swallowed too. Otherwise pad B would close the window and resume the game at once.
    [HarmonyPatch]
    internal static class HandleBackButtonPatch
    {
        private static MethodBase TargetMethod() => Vanilla.HandleBackButtonClick;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            try
            {
                if (!InvoiceWindow.IsOpen) return Time.frameCount != InvoiceWindow.ClosedFrame;
                InvoiceWindow.Back();
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("pause back", e);
                return true;
            }
        }
    }

    // U1: InventoryNavigationHandler.GetButtonInfo(int, CurrentInventoryWindow, InputType) postfix. Vanilla returns ""
    // for tab 4, which is only ever the Notes page (GetTabIndex, SRC/InventoryNavigationHandler.cs:1963-1981), so no
    // page check is needed. Appends "R)INVOICES" (pad "Y)INVOICES") in the game's own style. Vital Signs uses tab 1,
    // Quest Helper tab 3 and Tidy Inventory GetButtonInfoTab0.
    [HarmonyPatch]
    internal static class ButtonInfoPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GetButtonInfo;

        private static void Postfix(int tab, ref string __result)
        {
            if (tab != 4 || !Cfg.Hot.NotesPageKey || !Cfg.Hot.ListOn || !NotesPageInput.Available) return;
            try
            {
                string s = __result ?? "";
                __result = s + InventoryNavigationHandler.KeyCodeInfoToString(s, Strings.NotesHint, "Drop");
            }
            catch (Exception e) { Log.Exception("notes hint", e); }
        }
    }
}
