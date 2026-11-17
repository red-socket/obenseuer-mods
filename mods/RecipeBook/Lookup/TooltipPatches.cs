using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RecipeBook
{
    // P10: ToolTip.ConstructDataString(ItemData, int, int, object[]) prefix + finalizer (spec §5.2, §10.3). The prefix
    // computes our block (a cached string or null) into ExtraLines.Scope; the finalizer always clears it. The finalizer
    // takes no __exception, so a vanilla exception passes through unchanged.
    [HarmonyPatch]
    internal static class ConstructDataStringPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ConstructDataString;

        private static void Prefix(ItemData itemData, object[] metas)
        {
            ExtraLines.Scope = null;
            try { ExtraLines.Scope = itemData != null ? ExtraLines.ForTooltip(itemData.item, metas ?? itemData.meta) : null; }
            catch (Exception e) { Log.Exception("Used in: tooltip", e); ExtraLines.Scope = null; }
        }

        private static void Finalizer() => ExtraLines.Scope = null;
    }

    // P11: TooltipUI.UpdateTooltip(string, string, Dictionary, List) prefix, Priority.Last (spec §5.2). Inside P10's
    // scope it appends the block to the details text, once per tooltip UI. Outside the scope it does nothing.
    [HarmonyPatch]
    internal static class UpdateTooltipPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateTooltip;

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(ref string tooltipDetails)
        {
            string add = ExtraLines.Scope;
            if (add == null) return;
            try
            {
                if (tooltipDetails == null) tooltipDetails = add;
                else if (!tooltipDetails.EndsWith(add, StringComparison.Ordinal)) tooltipDetails += add;
            }
            catch (Exception e) { Log.Exception("Used in: tooltip append", e); }
        }
    }
}
