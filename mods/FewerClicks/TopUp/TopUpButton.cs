using System;
using UnityEngine;

namespace FewerClicks
{
    // The "Top up" button in the storage panel header (spec §2 P1): a clone of Take all, created once per
    // StoragePanelUI (the panel is part of each scene), ordered left of Better Tenement's Stash and of Sort.
    internal static class TopUpButton
    {
        internal const string Name = "FC_TopUpButton";
        private const string StashName = "BT_StashButton";

        private static GameObject _button;
        private static StoragePanelUI _for;
        private static bool _failedThisScene;

        internal static GameObject Button => _button != null && _button.activeInHierarchy ? _button : null;

        internal static void OnSceneLoaded()
        {
            _button = null;
            _for = null;
            _failedThisScene = false;
        }

        internal static void Hide()
        {
            _failedThisScene = true;
            if (_button != null) _button.SetActive(false);
        }

        // Runs after every UpdateStoragePanelUI (each open, the backpack toggle and the power button).
        internal static void OnPanel(StoragePanelUI ui, Storage storage)
        {
            if (ui == null) return;
            bool want = Cfg.TopUp.Enabled.Value && !_failedThisScene && TopUp.IsTopUpStorage(ui, storage)
                        && ui.takeAllButton != null && ui.takeAllButton.activeSelf;
            if (want && (_button == null || _for != ui)) Create(ui);
            if (_button == null || _for != ui) return;
            if (want)
            {
                Order(ui);
                UiClone.SetLabel(_button, Cfg.TopUp.Label.Value);
                var rt = _button.GetComponent<RectTransform>();
                if (rt != null && !Mathf.Approximately(rt.sizeDelta.x, Cfg.TopUp.ButtonWidth.Value))
                    rt.sizeDelta = new Vector2(Cfg.TopUp.ButtonWidth.Value, rt.sizeDelta.y);
            }
            if (_button.activeSelf != want) _button.SetActive(want);
        }

        private static void Create(StoragePanelUI ui)
        {
            var take = ui.takeAllButton;
            var go = UiClone.CloneButton(take, take.transform.parent, Name, Cfg.TopUp.Label.Value, TopUp.Run, "topup-button");
            if (go == null)
            {
                _failedThisScene = true;
                return;
            }
            var rt = go.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(Cfg.TopUp.ButtonWidth.Value, rt.sizeDelta.y);
            _button = go;
            _for = ui;
            Order(ui);
            Log.Verbose("Top up: button added to the storage panel.");
        }

        // The Buttons layout group is MiddleRight. Left of Better Tenement's Stash (if present and shown) and of Sort, so
        // the row reads [Top up][Stash][Sort][Take all][Power] whichever mod created its button first.
        private static void Order(StoragePanelUI ui)
        {
            var parent = _button.transform.parent;
            int target = ui.sortButton != null && ui.sortButton.transform.parent == parent ? ui.sortButton.transform.GetSiblingIndex() : -1;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == StashName && c.gameObject.activeSelf && (target < 0 || i < target)) target = i;
            }
            if (target < 0) return;
            int mine = _button.transform.GetSiblingIndex();
            // Moving ours out of the way shifts the indices after it by one.
            if (mine < target) target--;
            if (mine != target) _button.transform.SetSiblingIndex(target);
        }
    }
}
