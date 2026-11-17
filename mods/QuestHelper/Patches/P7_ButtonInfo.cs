using System;
using System.Reflection;
using HarmonyLib;

namespace QuestHelper
{
    // P7: InventoryNavigationHandler.GetButtonInfo(int, CurrentInventoryWindow, InputType) postfix (spec §3.2). Vanilla
    // returns "" for the Tasks page (tab 3); we add our two keys in the game's own "KEY)TEXT" style. Other tabs are
    // untouched (Tidy Inventory patches GetButtonInfoTab0).
    [HarmonyPatch]
    internal static class ButtonInfoPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GetButtonInfo;

        private static void Postfix(int tab, ref string __result)
        {
            if (tab != 3) return;
            try
            {
                string s = __result ?? "";
                if (HelperWindow.Available && Cfg.QuestDoctor.TaskTabKey.Value)
                    s += InventoryNavigationHandler.KeyCodeInfoToString(s, Detector.StuckFor(Tracking.Selected) ? Strings.TabHintOpenStuck : Strings.TabHintOpen, "Use Item");
                if (TbcSection.Available && TbcSection.HeaderExists)
                    s += InventoryNavigationHandler.KeyCodeInfoToString(s, Strings.TabHintTbc, "Drop");
                __result = s;
            }
            catch (Exception e) { Log.Exception("tasks hint", e); }
        }
    }
}
