using System;
using System.Reflection;
using HarmonyLib;
using TMPro;

namespace Workshop
{
    // The repeat count button in the recipe row (spec §5.3), on machines that run without the player. Idle it shows the
    // count the next Craft click will make ("×3"); while a repeat runs it reads "Stop (n)" and one click ends it.
    internal static class RepeatButton
    {
        internal static bool Available => QueuePatches.Patched && Cfg.Queue.Enabled.Value && Cfg.Queue.RepeatButton.Value;

        internal static string Label(manufacturing m)
        {
            var e = MachineStore.Get(m, false);
            if (e != null && e.Queued) return Strings.StopLabel(e.QLeft);
            return Strings.CountLabel(e != null ? e.ArmCount : 1);
        }

        // A click or A press. The controller cursor then goes back to the inventory, as after the vanilla Craft button
        // (A on a button in this window always goes through its onClick); the Right route (Step) stays on the button.
        internal static void OnClick(RecipeRow.Row row)
        {
            try
            {
                if (!Available || !PlayGate.Open) return;
                if (!(RecipeRow.StationOf(row.Info) is manufacturing m) || Stations.KindOf(m) != Kind.Plain) return;
                var e = MachineStore.Get(m, true);
                if (e.Queued)
                {
                    MachineStore.ClearQueue(m);
                    Log.Verbose($"Queue: repeat on {Stations.Name(m)} stopped by hand.");
                }
                else e.ArmCount = Next(e.ArmCount);
                UiClone.SetLabel(row.Repeat, Label(m));
                RefreshCraftLabel(m);
                if (InventoryNavigationHandler.Instance != null) InventoryNavigationHandler.Instance.ExitRecipeOpenedPanel();
            }
            catch (Exception ex) { Log.Exception("repeat button", ex); }
        }

        // The Right route: next count without leaving the button.
        internal static void Step(RecipeRow.Row row)
        {
            try
            {
                if (!(RecipeRow.StationOf(row.Info) is manufacturing m)) return;
                var e = MachineStore.Get(m, true);
                if (e.Queued) return;
                e.ArmCount = Next(e.ArmCount);
                UiClone.SetLabel(row.Repeat, Label(m));
                RefreshCraftLabel(m);
            }
            catch (Exception ex) { Log.Exception("repeat step", ex); }
        }

        private static int Next(int cur)
        {
            var steps = Cfg.Steps();
            int i = steps.IndexOf(cur);
            return i < 0 || i + 1 >= steps.Count ? steps[0] : steps[i + 1];
        }

        // Puts the craft button's text (with W9's suffix) on screen now; ShowRecipe would only pick it up later.
        internal static void RefreshCraftLabel(manufacturing m)
        {
            try
            {
                if (!CraftLabelPatch.Patched || !Vanilla.PanelOpen(m) || ManufacturingPanel.instance == null || ManufacturingPanel.instance.ActiveManu != m) return;
                string text = Vanilla.CraftLabel(m);
                ManufacturingPanel.instance.SetCreateButtonValues(text);
                var info = ManufacturingPanel.instance.manufacturingPanelUI.recipeListUI.recipeInfo;
                var tmp = info != null && info.createButton != null ? info.createButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;
                if (tmp != null) tmp.text = text;
            }
            catch (Exception e) { Log.Exception("craft label", e); }
        }
    }

    // W9: manufacturing.CraftButtonMessage() postfix (SRC/manufacturing.cs:649-656). A short suffix, because the button is
    // 236 px: " ×3" for an armed count, " (4)" while a repeat runs, " · auto" on auto-run crushers.
    [HarmonyPatch]
    internal static class CraftLabelPatch
    {
        internal static bool Patched;

        private static MethodBase TargetMethod() => Vanilla.CraftButtonMessage;

        private static void Postfix(manufacturing __instance, ref string __result)
        {
            try
            {
                if (!Cfg.Queue.Enabled.Value || __result == null) return;
                var kind = Stations.KindOf(__instance);
                if (kind == Kind.AutoRun) { __result += Strings.SuffixAuto; return; }
                if (kind != Kind.Plain) return;
                var e = MachineStore.Get(__instance, false);
                if (e == null) return;
                if (e.Queued) __result += Strings.SuffixQueued(e.QLeft);
                else if (e.ArmCount > 1 && RepeatButton.Available) __result += Strings.SuffixArmed(e.ArmCount);
            }
            catch (Exception e) { Log.Exception("craft label", e); }
        }
    }
}
