using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TidyInventory
{
    // The inventory's Combine button (spec §5.3): a clone of the inventory Sort button (InventoryNavigationMovement's
    // private inventorySortButton, whose persistent listener is InventoryPanelUI.SortInventory), in the same parent with
    // the same anchors and pivot, offset right of Sort. Created lazily from Update; it shows exactly when Sort is on
    // screen, and never during a trade. Its text is Combine.InventoryLabel ("Combine"), with a hover tooltip saying what it
    // does. No patches of its own.
    internal static class InventoryCombine
    {
        internal const string Name = "TI_CombineInvButton";

        private static GameObject _button, _sort;
        private static bool _failedThisScene;

        internal static GameObject Button => _button != null && _button.activeInHierarchy ? _button : null;

        internal static GameObject Sort => _sort;

        internal static void OnSceneLoaded()
        {
            _button = _sort = null;
            _failedThisScene = false;
        }

        internal static void Tick()
        {
            if (_failedThisScene) return;
            bool on = Cfg.Combine.InventoryButton.Value;
            if (_button == null && !on) return;
            if (_sort == null)
            {
                var nav = InventoryNavigationMovement.Instance;
                if (nav == null || Vanilla.InventorySortButton == null) return;
                _sort = Vanilla.InventorySortButton(nav);
                if (_sort == null) return;
            }
            bool want = on && _sort.activeInHierarchy && !Game.TradeActive;
            if (want && _button == null) Create();
            if (_button == null) return;
            if (want) Place();
            if (_button.activeSelf != want) _button.SetActive(want);
        }

        private static void Create()
        {
            var go = UiClone.CloneButton(_sort, _sort.transform.parent, Name, Cfg.Combine.InventoryLabel.Value, Combine.RunInventory, "combine-button");
            if (go == null)
            {
                _failedThisScene = true;
                return;
            }
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var text = go.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                // Sort's own label is text ("Sort") on a 40×40 button; "Combine" needs the smaller sizes.
                text.fontSizeMin = 7f;
                text.fontSizeMax = 12f;
            }
            var tip = go.AddComponent<HoverTip>();
            tip.Title = Strings.CombineTipTitle;
            tip.Text = Strings.CombineTip;
            _button = go;
            Place();
            Log.Verbose("Combine: button added next to the inventory's Sort button.");
        }

        private static void Place()
        {
            var src = _sort.transform as RectTransform;
            var rt = _button.transform as RectTransform;
            if (src == null || rt == null) return;
            rt.anchorMin = src.anchorMin;
            rt.anchorMax = src.anchorMax;
            rt.pivot = src.pivot;
            rt.sizeDelta = src.sizeDelta;
            var pos = src.anchoredPosition + new Vector2(Cfg.Combine.InventoryOffsetX.Value, Cfg.Combine.InventoryOffsetY.Value);
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
            UiClone.SetLabel(_button, Cfg.Combine.InventoryLabel.Value);
        }
    }
}
