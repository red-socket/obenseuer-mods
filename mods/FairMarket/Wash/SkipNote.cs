using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FairMarket
{
    // The "Not washed:" note on the dishwasher panel (spec §8.2). Dishwasher.CheckItems silently skips some stacks
    // (SRC/Dishwasher.cs:45-55); this lists them with the reason, in the same slot order. One TMP under the panel's
    // slot area, with the title's font and colour; no raycasts, no layout, no Selectable, so mouse and controller
    // navigation are untouched. Hidden whenever the dishwasher isn't the open machine; destroyed on scene load.
    internal static class SkipNote
    {
        private const int MaxLines = 4;
        private static TextMeshProUGUI _text;
        private static Dishwasher _owner;

        internal static void OnSceneLoaded()
        {
            if (_text != null)
            {
                try { Object.Destroy(_text.gameObject); }
                catch { }
            }
            _text = null;
            _owner = null;
        }

        // Builds the reasons for the stacks the game skipped. Empty when nothing was skipped.
        internal static List<string> Reasons(Dishwasher dw)
        {
            var lines = new List<string>();
            var st = dw.InputOutputStorage;
            if (st == null || st.storageSlots == null) return lines;
            var powder = dw.washingpowder;
            int powderId = powder != null ? powder.ID : -1;
            bool hasPowder = !(powder != null && powder.Item != null && powder.ID > 0 && !st.CheckForItem(powder.ID, out _, 1f, checkAmmo: true));
            string[] white = dw.categoryWhitelist != null ? dw.categoryWhitelist.ToArray() : new string[0];
            string[] black = dw.categoryBlacklist != null ? dw.categoryBlacklist.ToArray() : new string[0];
            var accepted = new HashSet<int>();
            foreach (var slot in st.storageSlots)
            {
                if (slot == null || slot.itemAmount < 1 || slot.itemId <= 0 || slot.itemId == powderId || slot.itemReference == null) continue;
                var item = slot.itemReference.Item;
                if (item == null) continue;
                string reason = null;
                if (slot.ownerId < 1) reason = Strings.NotStolen;
                else if (!SlotController.CheckIfInAllowedCategoy(item, white, black, slot.Meta))
                {
                    foreach (var b in black)
                        if (!SlotController.CheckIfInAllowedCategoy(item, new string[0], new[] { b }, slot.Meta))
                        {
                            reason = Strings.CantWash(b);
                            break;
                        }
                    if (reason == null) reason = white.Length > 0 ? Strings.NotOnList : Strings.DoesntTake;
                }
                else if (hasPowder && !accepted.Add(item.ID)) reason = Strings.OneStack;
                if (reason != null) lines.Add(Strings.SkipLine(Title(item, slot.Meta), reason));
            }
            return lines;
        }

        private static string Title(Item item, object[] meta)
        {
            try
            {
                string t = item.GetSkinTitle(meta);
                if (!string.IsNullOrEmpty(t)) return t;
            }
            catch { }
            return item.Title ?? ("#" + item.ID);
        }

        internal static void Show(Dishwasher dw, List<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                Hide();
                _owner = dw;
                return;
            }
            if (!Ensure()) return;
            var sb = new System.Text.StringBuilder(Strings.NotWashed);
            for (int i = 0; i < lines.Count && i < MaxLines; i++) sb.Append('\n').Append(lines[i]);
            if (lines.Count > MaxLines) sb.Append('\n').Append(Strings.More(lines.Count - MaxLines));
            _text.text = sb.ToString();
            _owner = dw;
            Place();
            _text.gameObject.SetActive(true);
        }

        internal static void Hide()
        {
            if (_text != null && _text.gameObject.activeSelf) _text.gameObject.SetActive(false);
        }

        // Plugin.Update: hidden unless the dishwasher it describes is the open machine.
        internal static void Tick()
        {
            if (_text == null || !_text.gameObject.activeSelf) return;
            var mp = ManufacturingPanel.instance;
            bool open = Cfg.Hot.SkipNote && mp != null && _owner != null && ReferenceEquals(mp.ActiveManu, _owner) &&
                        mp.manufacturingPanelUI != null && mp.manufacturingPanelUI.gameObject.activeInHierarchy;
            if (!open) Hide();
        }

        private static bool Ensure()
        {
            if (_text != null) return true;
            var ui = ManufacturingPanel.instance != null ? ManufacturingPanel.instance.manufacturingPanelUI : null;
            Transform parent = null;
            if (ui != null && ui.inventorySlotPanel != null && ui.inventorySlotPanel.transform.parent != null)
                parent = ui.inventorySlotPanel.transform.parent;
            else if (ui != null && ui.recipeListUI != null) parent = ui.recipeListUI.transform;
            if (parent == null)
            {
                Log.WarnOnce("skip anchor", "Wash: the dishwasher panel has an unexpected shape; the \"Not washed\" note is off.");
                return false;
            }
            var go = new GameObject("FairMarket SkipNote", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            _text = go.AddComponent<TextMeshProUGUI>();
            _text.raycastTarget = false;
            var title = ui.title;
            if (title != null)
            {
                _text.font = title.font;
                _text.fontSharedMaterial = title.fontSharedMaterial;
                _text.color = title.color;
                _text.fontSize = Mathf.Max(14f, title.fontSize * 0.6f);
            }
            else _text.fontSize = 18f;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.overflowMode = TextOverflowModes.Overflow;
            return true;
        }

        // Just below the slot area (or the fallback parent), as wide as it.
        private static void Place()
        {
            var ui = ManufacturingPanel.instance != null ? ManufacturingPanel.instance.manufacturingPanelUI : null;
            var rt = (RectTransform)_text.transform;
            var parent = rt.parent as RectTransform;
            RectTransform anchor = ui != null && ui.inventorySlotPanel != null && ui.inventorySlotPanel.transform.parent == rt.parent
                ? ui.inventorySlotPanel.transform as RectTransform
                : parent;
            if (anchor == null || parent == null) return;
            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);   // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
            Vector3 bl = parent.InverseTransformPoint(corners[0]);
            Vector3 br = parent.InverseTransformPoint(corners[3]);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(Mathf.Max(200f, br.x - bl.x), 160f);
            rt.localPosition = new Vector3(bl.x, bl.y - 6f, 0f);
        }
    }
}
