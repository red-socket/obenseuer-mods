using System;
using System.Reflection;
using HarmonyLib;

namespace GridSense
{
    // P1 (spec §4.4, D2): FurnitureInfoUI.UpdateFurnitureInfo (SRC/FurnitureInfoUI.cs:43-113). Vanilla sets the description
    // to furniture.Details on every call (:48) and clears it in RemoveFurnitureInfo (:122-135), so the power line is added as
    // the description's last paragraph and needs no clean-up. UpdateContentVisibility shows the description when Details was
    // empty; RebuildLayout is vanilla's ForceMeshUpdate plus LayoutRebuilder. currentOC and currentRm are never written, so
    // SetCraftingTime and the blueprint timer (SRC/ShopBaseUI.cs:833) leave the line alone.
    //
    // The only caller is ShopBaseUI.ChangeSelection(BuildingSystemShopSlot, bool) (:391), reached by mouse and by the
    // controller's d-pad and stick (SRC/ShopController.cs:285-330): furniture shop buy and sell, Blueprints craft and
    // dismantle. The TMP text is a copy; Furniture.details, TaskItem and ItemFurnitureData are never written.
    [HarmonyPatch]
    internal static class ShopLinePatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateFurnitureInfo;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(FurnitureInfoUI __instance, BuildingSystem.FurnitureInfo furnitureInfo)
        {
            if (!Cfg.Hot.ShopInfo) return;
            try
            {
                var f = furnitureInfo?.furniture;
                if (f == null) return;
                var info = PowerInfo.Get(f, furnitureInfo.meta?.currentAlternativePrefab ?? 0);
                if (info.IsEmpty) return;
                var tmp = Vanilla.CurrentDescription(__instance);
                if (tmp == null) return;
                string line = info.FullText, cur = tmp.text ?? "";
                if (string.IsNullOrEmpty(line) || cur.EndsWith(line, StringComparison.Ordinal)) return;
                tmp.text = string.IsNullOrWhiteSpace(cur) ? line : cur + "\n\n" + line;
                Vanilla.UpdateContentVisibility(__instance);
                Vanilla.RebuildLayout(__instance);
            }
            catch (Exception e) { Log.Exception("Shop: line", e); }
        }
    }
}
