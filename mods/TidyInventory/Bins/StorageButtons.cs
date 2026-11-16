using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TidyInventory
{
    // The storage header row (spec §4.3): "Empty" on bins, "Combine" on other storage-panel storages, never both. Clones
    // of Take all, created once per StoragePanelUI (the panel is part of each scene) and placed right after Take all, so
    // the row reads [Top up][Stash][Modifier][Sort][Take all][ours][Power] in any combination of our mods. An overlap
    // guard hides only our button when the row would run into the storage title; vanilla layout is never changed.
    internal static class StorageButtons
    {
        internal static bool Patched;

        internal const string EmptyName = "TI_EmptyButton";
        internal const string CombineName = "TI_CombineButton";
        private const float TitleMargin = 8f;

        private static GameObject _empty, _combine;
        private static StoragePanelUI _for;
        private static bool _failedThisScene;
        private static Storage _shownFor;
        private static bool _guardHid;

        // The button the controller route reaches (P27): ours, shown and not hidden by the guard.
        internal static GameObject Active
        {
            get
            {
                if (_guardHid) return null;
                if (_empty != null && _empty.activeInHierarchy) return _empty;
                if (_combine != null && _combine.activeInHierarchy) return _combine;
                return null;
            }
        }

        internal static void OnSceneLoaded()
        {
            _empty = _combine = null;
            _for = null;
            _failedThisScene = false;
            _shownFor = null;
            _guardHid = false;
        }

        internal static void Hide()
        {
            _failedThisScene = true;
            if (_empty != null) _empty.SetActive(false);
            if (_combine != null) _combine.SetActive(false);
        }

        // Runs after every UpdateStoragePanelUI (each open, the backpack toggle and the power button).
        internal static void OnPanel(StoragePanelUI ui, Storage storage)
        {
            if (ui == null) return;
            if (_shownFor != storage) Confirm.Reset("empty");
            _shownFor = storage;

            bool eligible = !_failedThisScene && Eligible(ui, storage);
            bool wantEmpty = eligible && Cfg.Bins.Enabled.Value && Bins.IsBin(storage);
            // Bins never get Combine, with or without Empty (PlayerPlacedOnly or Bins.Enabled off).
            bool wantCombine = eligible && !wantEmpty && Cfg.Combine.StorageButton.Value && !Bins.IsBin(storage, ignorePlayerPlacedOnly: true)
                               && !(storage.StorageOwner != null && storage.StorageOwner.ID > 1 && !storage.PermissionToTake);

            if (_for != ui)
            {
                _empty = _combine = null;
                _for = ui;
            }
            if (wantEmpty && _empty == null) _empty = Create(ui, EmptyName, Cfg.Bins.Label.Value, EmptyAction.Click);
            if (wantCombine && _combine == null) _combine = Create(ui, CombineName, Cfg.Combine.Label.Value, Combine.RunStorage);

            Show(_empty, wantEmpty, ui, Confirm.IsArmed("empty", storage) ? Cfg.Bins.ConfirmLabel.Value : Cfg.Bins.Label.Value);
            Show(_combine, wantCombine, ui, Cfg.Combine.Label.Value);

            _guardHid = false;
            var ours = wantEmpty ? _empty : wantCombine ? _combine : null;
            if (ours != null && Overlaps(ui))
            {
                ours.SetActive(false);
                _guardHid = true;
                LayoutRebuild(ui);
                Log.WarnOnce("overlap|" + storage.title, $"Bins: the storage title \"{storage.title}\" leaves no room for our button; it is hidden there.");
            }
        }

        // A storage whose slots live in the storage panel (not crafting, vending or put-item storages), with Take all
        // shown and no trade.
        private static bool Eligible(StoragePanelUI ui, Storage storage)
        {
            if (storage == null || ui.storageSlotPanel == null || Vanilla.SlotPanel == null) return false;
            if (Vanilla.SlotPanel(storage) != ui.storageSlotPanel.transform) return false;
            if (ui.takeAllButton == null || !ui.takeAllButton.activeSelf) return false;
            return !Game.TradeActive;
        }

        private static GameObject Create(StoragePanelUI ui, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            var take = ui.takeAllButton;
            var go = UiClone.CloneButton(take, take.transform.parent, name, label, onClick, "storage-button");
            if (go == null)
            {
                _failedThisScene = true;
                return null;
            }
            if (name == EmptyName) go.AddComponent<ArmGuard>();
            SetWidth(go);
            Log.Verbose($"Bins: {name} added to the storage panel.");
            return go;
        }

        private static void Show(GameObject go, bool want, StoragePanelUI ui, string label)
        {
            if (go == null) return;
            if (want)
            {
                Order(go, ui);
                UiClone.SetLabel(go, label);
                SetWidth(go);
            }
            if (go.activeSelf != want) go.SetActive(want);
        }

        private static void SetWidth(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            int w = Cfg.Bins.ButtonWidth.Value;
            if (rt != null && !Mathf.Approximately(rt.sizeDelta.x, w)) rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
        }

        // Right after Take all (before the Power button when it shares the row).
        private static void Order(GameObject go, StoragePanelUI ui)
        {
            var take = ui.takeAllButton.transform;
            if (go.transform.parent != take.parent) return;
            int target = take.GetSiblingIndex() + 1;
            int mine = go.transform.GetSiblingIndex();
            if (mine < target) target--;   // moving ours out of the way shifts the indices after it by one
            if (mine != target) go.transform.SetSiblingIndex(target);
        }

        private static void LayoutRebuild(StoragePanelUI ui)
        {
            var row = ui.takeAllButton.transform.parent as RectTransform;
            if (row != null) LayoutRebuilder.ForceRebuildLayoutImmediate(row);
        }

        // The row's leftmost active button reaches the end of the title text (world space, so the two rects may sit in
        // different parents).
        private static bool Overlaps(StoragePanelUI ui)
        {
            try
            {
                var title = Vanilla.StorageName != null ? Vanilla.StorageName(ui) : null;
                var row = ui.takeAllButton.transform.parent as RectTransform;
                if (title == null || row == null) return false;
                LayoutRebuilder.ForceRebuildLayoutImmediate(row);
                float left = float.PositiveInfinity;
                var corners = new Vector3[4];
                for (int i = 0; i < row.childCount; i++)
                {
                    var c = row.GetChild(i) as RectTransform;
                    if (c == null || !c.gameObject.activeSelf) continue;
                    c.GetWorldCorners(corners);
                    left = Mathf.Min(left, corners[0].x);
                }
                if (float.IsInfinity(left)) return false;
                var trt = title.rectTransform;
                trt.GetWorldCorners(corners);
                float scale = trt.lossyScale.x;
                float textWidth = title.GetPreferredValues(title.text).x;
                float titleRight = corners[0].x + (TitleMargin + textWidth) * scale;
                return left < titleRight;
            }
            catch (Exception e)
            {
                Log.Exception("overlap guard", e);
                return false;
            }
        }

        // Disarms Empty when the panel closes, and restores its label.
        private sealed class ArmGuard : MonoBehaviour
        {
            private void OnDisable()
            {
                try { Confirm.Reset("empty"); }
                catch (Exception e) { Log.Exception("arm guard", e); }
            }
        }

        internal static void SetEmptyLabel(bool armed)
        {
            if (_empty != null) UiClone.SetLabel(_empty, armed ? Cfg.Bins.ConfirmLabel.Value : Cfg.Bins.Label.Value);
        }

        internal static void PlayTakeAllSound()
        {
            try
            {
                var ui = Inventory.instance != null ? Inventory.instance.storagePanelUI : null;
                var snd = ui != null && Vanilla.TakeAllSound != null ? Vanilla.TakeAllSound(ui) : null;
                if (snd != null) MiscAudioPlayer.PlaySound(snd);
            }
            catch (Exception e) { Log.Exception("take all sound", e); }
        }
    }

    // P26: StoragePanelUI.UpdateStoragePanelUI(Storage) (SRC/StoragePanelUI.cs:159). Last, after Fewer Clicks (Low) and
    // Better Tenement (Normal), so their buttons are already in the row when the guard measures it.
    [HarmonyPatch]
    internal static class UpdateStoragePanelUIPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateStoragePanelUI;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(StoragePanelUI __instance, Storage storage)
        {
            try { StorageButtons.OnPanel(__instance, storage); }
            catch (Exception e)
            {
                Log.Exception("storage buttons", e);
                try { StorageButtons.Hide(); } catch (Exception) { }
            }
        }
    }
}
