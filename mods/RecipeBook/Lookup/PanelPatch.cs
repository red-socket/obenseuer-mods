using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OS.Items;
using TMPro;

namespace RecipeBook
{
    // P12: ItemInfoPanel.UpdateInfo(ItemData, ChildItem) postfix, Priority.Last (spec §10.4). The inventory's item panel
    // is the only item surface a controller player sees, so the lines go under the item's description. The description
    // was just set from Item.Description (SRC/ItemInfoPanel.cs:224), so nothing accumulates across calls. It has rich
    // text off and Ellipsis overflow: the fit loop shortens our lines until nothing is cut, and the last step is exactly
    // the game's text. Only itemDescripion is touched: Fair Market edits itemValue, Tidy Inventory the buttons.
    [HarmonyPatch]
    internal static class UpdateInfoPatch
    {
        private static readonly List<string> Lines = new List<string>(3);

        // The last fit (spec §10.4): the same game text, line set and box give the same result, so it is reused without
        // another fit loop. LiquidStoragePanelUI calls UpdateInfo about every frame while a container fills
        // (SRC/LiquidStoragePanelUI.cs:143-148, :258-259); a hit costs one string compare and one text assignment.
        private static TextMeshProUGUI _lastTmp;
        private static string _lastVanilla, _lastResult;
        private static bool _lastKeptVanilla;
        private static ExtraLines.PanelSet _lastSet;
        private static float _lastWidth, _lastHeight, _lastSize;

        private static MethodBase TargetMethod() => Vanilla.UpdateInfo;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ItemInfoPanel __instance, ItemData itemData)
        {
            if (!Cfg.Hot.LinesPanel || itemData == null) return;
            TextMeshProUGUI tmp = null;
            string vanilla = null;
            try
            {
                tmp = Vanilla.ItemDescription(__instance);
                if (tmp == null) return;
                vanilla = tmp.text ?? "";
                var set = ExtraLines.ForPanel(itemData.item, itemData.meta);
                if (set == null) return;
                var rect = tmp.rectTransform.rect;
                if (ReferenceEquals(tmp, _lastTmp) && ReferenceEquals(set, _lastSet) && rect.width == _lastWidth &&
                    rect.height == _lastHeight && tmp.fontSize == _lastSize && string.Equals(vanilla, _lastVanilla, StringComparison.Ordinal))
                {
                    if (!_lastKeptVanilla) tmp.text = _lastResult;
                    return;
                }
                _lastTmp = null;
                Fit(tmp, vanilla, set);
                _lastTmp = tmp;
                _lastSet = set;
                _lastVanilla = vanilla;
                _lastResult = tmp.text;
                _lastKeptVanilla = string.Equals(_lastResult, vanilla, StringComparison.Ordinal);
                _lastWidth = rect.width;
                _lastHeight = rect.height;
                _lastSize = tmp.fontSize;
            }
            catch (Exception e)
            {
                _lastTmp = null;
                Log.Exception("Used in: item panel", e);
                try { if (tmp != null && vanilla != null) tmp.text = vanilla; }
                catch { }
            }
        }

        // Steps, fullest first (spec §10.4): names N..1, "Used in N recipes", no Used in, short Remaining, game text.
        private static void Fit(TextMeshProUGUI tmp, string vanilla, ExtraLines.PanelSet set)
        {
            for (int k = set.Names.Count; k >= 1; k--)
                if (Try(tmp, vanilla, set.RemainingFull, ExtraLines.PanelUsedIn(set, k))) return;
            if (set.NameCount > 0 && Try(tmp, vanilla, set.RemainingFull, Strings.UsedInCount(set.NameCount))) return;
            if (set.RemainingFull != null && Try(tmp, vanilla, set.RemainingFull, null)) return;
            if (set.RemainingShort != null && set.RemainingShort != set.RemainingFull && Try(tmp, vanilla, set.RemainingShort, null)) return;
            tmp.text = vanilla;
        }

        private static bool Try(TextMeshProUGUI tmp, string vanilla, string a, string b)
        {
            Lines.Clear();
            if (a != null) Lines.Add(a);
            if (b != null) Lines.Add(b);
            if (Lines.Count == 0) return false;
            tmp.text = vanilla.Length == 0 ? string.Join("\n", Lines.ToArray()) : vanilla + "\n" + string.Join("\n", Lines.ToArray());
            // ignoreActiveState: LiquidStoragePanelUI calls UpdateInfo while the panel is hidden.
            tmp.ForceMeshUpdate(true, false);
            return !tmp.isTextTruncated;
        }
    }
}
