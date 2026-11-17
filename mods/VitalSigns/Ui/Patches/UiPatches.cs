using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VitalSigns
{
    // T1: PlayerStatsToolTipInfo.GetStatToolTipInfo() postfix, Priority.Last (spec §4, §9.3). Appends our text to the
    // details only. StatTitle is never changed: it feeds the controller LT labels (SRC/PlayerStatUI.cs:117-118).
    [HarmonyPatch]
    internal static class TooltipPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GetStatToolTipInfo;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PlayerStatsToolTipInfo __instance)
        {
            try
            {
                var bar = __instance.statBar;
                string extra = Tooltips.Extra(bar);
                if (string.IsNullOrEmpty(extra)) return;
                string combined = Tooltips.Combine(bar, __instance.toolTipInfoDetails, extra);
                __instance.UpdateToolTipInfo(__instance.StatTitle, combined, update: false);
            }
            catch (Exception e) { Log.Exception("stat tooltip", e); }
        }
    }

    // W1: PauseNavigationHandler.Update() prefix, Priority.First, copied from Quest Helper's P3. While the window is open
    // (and on the frame it closes) the pause menu doesn't read the pad or keyboard, so no pause button fires underneath.
    [HarmonyPatch]
    internal static class PauseNavUpdatePatch
    {
        private static MethodBase TargetMethod() => Vanilla.PauseNavUpdate;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            if (VitalWindow.IsOpen) return false;
            return Time.frameCount > VitalWindow.ClosedFrame + 1;
        }
    }

    // W2: PauseNavigationHandler.HandleBackButtonClick() prefix, Priority.First, copied from Quest Helper's P4. Esc, pad B
    // and pad Menu all end here; with the window open they close it instead of resuming.
    [HarmonyPatch]
    internal static class HandleBackButtonPatch
    {
        private static MethodBase TargetMethod() => Vanilla.HandleBackButtonClick;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            try
            {
                if (!VitalWindow.IsOpen) return true;
                VitalWindow.Back();
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("pause back", e);
                return true;
            }
        }
    }

    // A1: InventoryNavigationHandler.GetButtonInfo(int, CurrentInventoryWindow, InputType) postfix. Vanilla returns "" for
    // tab 1 (Character and Map); on the Character page we append "X)VITAL SIGNS" in the game's own style. Quest Helper
    // uses tab 3 and Tidy Inventory patches GetButtonInfoTab0.
    [HarmonyPatch]
    internal static class ButtonInfoPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GetButtonInfo;

        private static void Postfix(int tab, ref string __result)
        {
            if (tab != 1 || !Cfg.Hot.CharacterPageKey) return;
            try
            {
                var pages = UIPageButtons.instance;
                if (pages == null || pages.currentPage != MenuPageNames.Character) return;
                string s = __result ?? "";
                __result = s + InventoryNavigationHandler.KeyCodeInfoToString(s, Strings.HintLabel, "Use Item");
            }
            catch (Exception e) { Log.Exception("character hint", e); }
        }
    }

    // A2: UIPageButtons.Select(MenuPageNames) postfix. Select refreshes the hint line before it sets currentPage
    // (SRC/UIPageButtons.cs:91 vs :100), so the line is built again with the new page.
    [HarmonyPatch]
    internal static class PageSelectPatch
    {
        private static MethodBase TargetMethod() => Vanilla.PageSelect;

        private static void Postfix(MenuPageNames pageName)
        {
            if (pageName != MenuPageNames.Character && pageName != MenuPageNames.Map && pageName != MenuPageNames.None) return;
            CharacterPageInput.RefreshHint();
        }
    }
}
