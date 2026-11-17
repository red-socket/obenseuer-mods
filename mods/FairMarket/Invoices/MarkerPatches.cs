using System;
using System.Reflection;
using HarmonyLib;
using OS.Items;

namespace FairMarket
{
    // I1: ToolTip.InvoiceDataToString(ItemInvoiceData) postfix (spec §9.2). The game shows this line in the letter's
    // tooltip (bold, SRC/ToolTip.cs:284) and in the hover text of the item panel's due-date chip (ItemInfoPanel.cs:637).
    [HarmonyPatch]
    internal static class InvoiceTooltipPatch
    {
        private static MethodBase TargetMethod() => Vanilla.InvoiceDataToString;

        private static void Postfix(ItemInvoiceData invoiceData, ref string __result)
        {
            if (!Cfg.Hot.Markers) return;
            try
            {
                switch (Marker.For(invoiceData))
                {
                    case Mark.Scam: __result += Strings.TipScam; break;
                    case Mark.ScamExpired: __result += Strings.TipScamExpired; break;
                    case Mark.NoDebt: __result += Strings.TipNoDebt; break;
                }
            }
            catch (Exception e) { Log.Exception("invoice tooltip", e); }
        }
    }

    // I2: ItemInfoPanel.UpdateInfo(ItemData, ChildItem) postfix (spec §9.3). A short tag after "Amount due: N", which a
    // controller player sees without a tooltip. The text sits right of the currency icon, left-aligned with an ellipsis,
    // so the icon never moves. Tidy Inventory's postfix on the same method touches only the Use and Drop buttons.
    [HarmonyPatch]
    internal static class InvoicePanelPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateInfo;

        private static void Postfix(ItemInfoPanel __instance, ItemData itemData)
        {
            if (!Cfg.Hot.Markers || itemData == null || Vanilla.ItemValue == null) return;
            try
            {
                var d = itemData.GetMetaOfType<ItemInvoiceData>();
                if (d == null) return;
                string tag;
                switch (Marker.For(d))
                {
                    case Mark.Scam:
                    case Mark.ScamExpired: tag = Strings.TagScam; break;
                    case Mark.NoDebt: tag = Strings.TagNoDebt; break;
                    default: return;
                }
                var label = Vanilla.ItemValue(__instance);
                if (label == null || label.text == null || !label.text.StartsWith("Amount due: ", StringComparison.Ordinal) || label.text.EndsWith(tag, StringComparison.Ordinal)) return;
                label.text += tag;
            }
            catch (Exception e) { Log.Exception("invoice panel", e); }
        }
    }
}
