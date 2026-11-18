using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;

namespace FieldGuide
{
    // P1: ItemInfoPanel.UpdateInfo(ItemData, ChildItem) postfix (spec §5). The inventory's item panel is the only item
    // surface a controller player sees, so the Bought-by line goes under the description. Vanilla sets the description
    // from Item.Description on every call (SRC/ItemInfoPanel.cs:223), and Recipe Book's postfix (ordered first by
    // HarmonyAfter) may add its lines; that whole text is our base and is never edited. Recipe Book gets the last free
    // line (D13): we append only if the text still fits. The box has Ellipsis overflow, so each candidate is measured
    // and the first one that isn't cut wins; if none fits, the base text stays exactly as it was.
    [HarmonyPatch]
    internal static class UpdateInfoPatch
    {
        // The last fit (spec §5 P1 steps 4-5). A hit costs a few compares and one text assignment.
        // LiquidStoragePanelUI calls UpdateInfo about every frame while a container fills (SRC/LiquidStoragePanelUI.cs:259),
        // and then only the prices change: BoughtBy keeps the accepting traders cached under a key without the price and
        // re-prices only the names shown, and the step chosen last time is tried first here, so a fill costs a cache
        // hit, two or three price calls and about one ForceMeshUpdate per call instead of a full evaluation and loop.
        private static TextMeshProUGUI _tmp;
        private static string _base, _joined, _result;
        private static int _item = -1, _steps, _chosen = -1;
        private static float _w, _h, _size;

        private static MethodBase TargetMethod() => Vanilla.UpdateInfo;

        internal static void Clear()
        {
            _tmp = null;
            _base = _joined = _result = null;
            _item = -1;
            _steps = 0;
            _chosen = -1;
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(Compat.RecipeBookGuid)]
        private static void Postfix(ItemInfoPanel __instance, ItemData itemData)
        {
            if (!Cfg.Hot.PanelOn || itemData == null) return;
            TextMeshProUGUI tmp = null;
            string baseText = null;
            try
            {
                tmp = Vanilla.ItemDescription(__instance);
                if (tmp == null) return;
                baseText = tmp.text ?? "";
                var cands = BoughtBy.PanelCandidates(itemData);
                if (cands == null || cands.Count == 0) return;
                string joined = string.Join("\u0001", cands.ToArray());
                var rect = tmp.rectTransform.rect;
                int itemId = itemData.item != null ? itemData.item.ID : -1;
                bool sameBox = ReferenceEquals(tmp, _tmp) && rect.width == _w && rect.height == _h && tmp.fontSize == _size &&
                    string.Equals(baseText, _base, StringComparison.Ordinal);
                if (sameBox && string.Equals(joined, _joined, StringComparison.Ordinal))
                {
                    tmp.text = _result;
                    return;
                }
                int chosen = -1;
                // Only the prices changed (same item, same number of steps): try last time's step first.
                if (sameBox && itemId == _item && cands.Count == _steps && _chosen >= 0 && _chosen < cands.Count &&
                    Try(tmp, baseText, cands[_chosen]))
                    chosen = _chosen;
                else
                {
                    for (int i = 0; i < cands.Count; i++)
                        if (Try(tmp, baseText, cands[i])) { chosen = i; break; }
                    if (chosen < 0) tmp.text = baseText;
                }
                _tmp = tmp;
                _base = baseText;
                _joined = joined;
                _result = tmp.text;
                _item = itemId;
                _steps = cands.Count;
                _chosen = chosen;
                _w = rect.width;
                _h = rect.height;
                _size = tmp.fontSize;
            }
            catch (Exception e)
            {
                Clear();
                Log.Exception("Bought by: item panel", e);
                try { if (tmp != null && baseText != null) tmp.text = baseText; }
                catch { }
            }
        }

        private static bool Try(TextMeshProUGUI tmp, string baseText, string line)
        {
            tmp.text = baseText.Length == 0 ? line : baseText + "\n" + line;
            // ignoreActiveState: LiquidStoragePanelUI calls UpdateInfo while the panel is hidden.
            tmp.ForceMeshUpdate(true, false);
            return !tmp.isTextTruncated;
        }
    }
}
