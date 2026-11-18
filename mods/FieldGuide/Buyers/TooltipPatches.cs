using System;
using System.Reflection;
using HarmonyLib;

namespace FieldGuide
{
    // T1: ToolTip.ConstructDataString(ItemData, int, int, object[]) prefix + finalizer (spec §5), the Recipe Book P10
    // pattern. The prefix computes our block (a cached string or null) into BoughtBy.Scope; the finalizer always clears
    // it. The finalizer takes no __exception, so a vanilla exception (e.g. the NRE at SRC/ToolTip.cs:338) passes through
    // unchanged.
    [HarmonyPatch]
    internal static class ConstructDataStringPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ConstructDataString;

        private static void Prefix(ItemData itemData, int owner, object[] metas)
        {
            BoughtBy.Scope = null;
            if (!Cfg.Hot.TooltipOn) return;
            try { BoughtBy.Scope = itemData != null ? BoughtBy.TooltipBlock(itemData, owner, metas ?? itemData.meta) : null; }
            catch (Exception e) { Log.Exception("Bought by: tooltip", e); BoughtBy.Scope = null; }
        }

        private static void Finalizer() => BoughtBy.Scope = null;
    }

    // T2: TooltipUI.UpdateTooltip(string, string, Dictionary, List) prefix (spec §5). Runs after Recipe Book's own
    // prefix (Priority.Last plus HarmonyAfter), so the tooltip reads vanilla, then "Used in", then "Bought by". Vanilla
    // calls this once per registered TooltipUI (SRC/ToolTip.cs:342-345), hence the EndsWith check. Outside T1's scope it
    // does nothing. The block is the same string for the same input, so UpdateWidth is not restarted needlessly
    // (SRC/TooltipUI.cs:139-156).
    [HarmonyPatch]
    internal static class UpdateTooltipPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateTooltip;

        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(Compat.RecipeBookGuid)]
        private static void Prefix(ref string tooltipDetails)
        {
            string add = BoughtBy.Scope;
            if (add == null) return;
            try
            {
                if (tooltipDetails == null) tooltipDetails = add;
                else if (!tooltipDetails.EndsWith(add, StringComparison.Ordinal)) tooltipDetails += add;
            }
            catch (Exception e) { Log.Exception("Bought by: tooltip append", e); }
        }
    }
}
