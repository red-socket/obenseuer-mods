using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TidyInventory
{
    // The pin badge and the ghost icon (spec §3.7), polled in LateUpdate while the inventory grid is on screen, over the
    // pinned slots only. Objects live under each slot's "Padding" (slot.slotImage's parent, SRC GameObject/Slot.prefab),
    // never under the grid layout itself, and never take raycasts. The badge uses the game's own "locked" sprite
    // (art/NOTES.md). The poll also follows in-place ID changes of the shown item (a filled bottle drunk empty). With
    // KeepReservationWhenEmpty off, PinStore.Tick unpins emptied slots, open inventory or not.
    internal static class PinOverlay
    {
        internal static bool CosmeticsPatched;   // "pin cosmetics" group (P24, P25)

        private static readonly Color Gold = new Color32(0xD9, 0xA4, 0x41, 0xFF);
        private static readonly Color Grey = new Color32(0x8C, 0x8C, 0x8C, 0xFF);

        private enum State { None, Filled, Reserved, Foreign }

        private sealed class Marks
        {
            internal Slot Slot;
            internal GameObject Badge, Ghost;
            internal Image BadgeImage, GhostImage;
            internal State State = State.None;
            internal int GhostId = -1;
            internal bool BadgeOn, GhostOn;
            internal float GhostAlpha = -1f;
        }

        private static readonly Dictionary<int, Marks> Objects = new Dictionary<int, Marks>();
        private static Sprite _badgeSprite;
        private static bool _badgeSearched;
        private static bool _dirty = true;

        internal static void MarkDirty() => _dirty = true;

        // Scene load and Inventory.Start: the old scene's objects went with its slots.
        internal static void OnInventoryStart()
        {
            Objects.Clear();
            _dirty = true;
        }

        internal static void OnSceneLoaded()
        {
            Objects.Clear();
            _badgeSprite = null;
            _badgeSearched = false;
            _dirty = true;
        }

        internal static void Poll()
        {
            if (!PinPatches.Patched || !Game.InventoryVisible) return;
            if (Objects.Count == 0 && PinStore.Map.Count == 0) return;
            bool enabled = Cfg.Pins.Enabled.Value;
            bool keep = Cfg.Pins.KeepReservationWhenEmpty.Value;
            // After a pin change, a load or a config change, every mark is applied again from scratch.
            if (_dirty)
                foreach (var m in Objects.Values) Hide(m);

            foreach (var kv in PinStore.Map)
            {
                var sc = Reservation.SlotAt(kv.Key);
                if (sc == null || sc.slot == null) continue;
                var pin = kv.Value;
                State st;
                if (!Game.NonEmpty(sc)) st = State.Reserved;
                else if (PinStore.InFamily(pin, sc.itemStack.itemId)) st = State.Filled;
                else st = State.Foreign;

                if (st == State.Reserved && !keep)
                {
                    // PinStore.Tick unpins it (not mid-drag); until then it shows no marks.
                    if (Objects.TryGetValue(kv.Key, out var gone)) Hide(gone);
                    continue;
                }
                if (st == State.Filled && sc.itemStack.itemId != pin.ShownId)
                {
                    pin.ShownId = sc.itemStack.itemId;
                    PinStore.Map[kv.Key] = pin;
                    PinStore.Dirty = true;
                    break;   // changing the map ends this enumeration; the next frame applies the marks
                }
                Apply(kv.Key, sc.slot, st, pin, enabled);
            }

            // Marks of slots that are no longer pinned.
            foreach (var kv in Objects)
                if (!PinStore.Map.ContainsKey(kv.Key) || !enabled) Hide(kv.Value);

            if (PinStore.Dirty) PinStore.Write("follow item");
            _dirty = false;
        }

        private static void Apply(int index, Slot slot, State st, Pin pin, bool enabled)
        {
            if (!Objects.TryGetValue(index, out var m) || m.Slot != slot || m.Badge == null)
            {
                m = Create(slot);
                if (m == null) return;
                Objects[index] = m;
            }
            bool badgeOn = enabled && Cfg.Pins.ShowBadge.Value;
            bool ghostOn = enabled && st == State.Reserved && Cfg.Pins.ShowGhostIcon.Value && Cfg.Pins.KeepReservationWhenEmpty.Value;

            if (m.State != st && m.BadgeImage != null)
            {
                m.BadgeImage.color = st == State.Foreign ? Grey : Gold;
                m.State = st;
            }
            if (m.BadgeOn != badgeOn)
            {
                m.Badge.SetActive(badgeOn);
                m.BadgeOn = badgeOn;
            }

            if (ghostOn && m.GhostId != pin.ShownId)
            {
                m.GhostId = pin.ShownId;
                Sprite s = null;
                try { s = ItemDatabase.instance?.FetchItemByID(pin.ShownId)?.Appearance?.GetSprite((object[])null); }
                catch (Exception e) { Log.Exception("ghost sprite", e); }
                m.GhostImage.sprite = s;
                if (s == null) ghostOn = false;
            }
            else if (ghostOn && m.GhostImage.sprite == null) ghostOn = false;

            float alpha = Cfg.Pins.GhostOpacity.Value;
            if (ghostOn && !Mathf.Approximately(m.GhostAlpha, alpha))
            {
                m.GhostImage.color = new Color(1f, 1f, 1f, alpha);
                m.GhostAlpha = alpha;
            }
            if (m.GhostOn != ghostOn)
            {
                m.Ghost.SetActive(ghostOn);
                m.GhostOn = ghostOn;
            }
        }

        private static void Hide(Marks m)
        {
            if (m.BadgeOn && m.Badge != null) m.Badge.SetActive(false);
            if (m.GhostOn && m.Ghost != null) m.Ghost.SetActive(false);
            m.BadgeOn = m.GhostOn = false;
            m.State = State.None;
        }

        private static Marks Create(Slot slot)
        {
            var padding = slot.slotImage != null ? slot.slotImage.transform.parent : null;
            if (padding == null)
            {
                Log.WarnOnce("pin-overlay-shape", "Pins: the inventory slot has an unexpected shape; the pin mark and faded picture are off.");
                return null;
            }
            var m = new Marks { Slot = slot };

            m.Badge = NewImage("TidyInventory.Pin", padding, out m.BadgeImage);
            var rt = (RectTransform)m.Badge.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(14f, 16f);
            rt.anchoredPosition = Vector2.zero;
            m.Badge.transform.SetAsLastSibling();
            m.BadgeImage.sprite = BadgeSprite();
            m.BadgeImage.preserveAspect = true;
            m.BadgeImage.color = Gold;
            m.Badge.SetActive(false);

            m.Ghost = NewImage("TidyInventory.Ghost", padding, out m.GhostImage);
            var gt = (RectTransform)m.Ghost.transform;
            gt.anchorMin = Vector2.zero;
            gt.anchorMax = Vector2.one;
            gt.sizeDelta = Vector2.zero;
            gt.anchoredPosition = Vector2.zero;
            m.Ghost.transform.SetSiblingIndex(Mathf.Min(1, padding.childCount - 1));
            m.GhostImage.type = Image.Type.Simple;
            m.GhostImage.preserveAspect = false;
            m.Ghost.SetActive(false);
            return m;
        }

        private static GameObject NewImage(string name, Transform parent, out Image image)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return go;
        }

        // The game's "locked" padlock (ASSETS/Sprite/locked.asset), else "icon_owned", else a plain tinted square.
        private static Sprite BadgeSprite()
        {
            if (_badgeSearched) return _badgeSprite;
            _badgeSearched = true;
            try
            {
                Sprite owned = null;
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    if (s == null) continue;
                    if (s.name == "locked" && s.texture != null && s.texture.name == "locked") { _badgeSprite = s; break; }
                    if (owned == null && s.name == "icon_owned") owned = s;
                }
                if (_badgeSprite == null) _badgeSprite = owned;
            }
            catch (Exception e) { Log.Exception("badge sprite", e); }
            if (_badgeSprite == null)
                Log.WarnOnce("pin-sprite", "Pins: the game's padlock sprite was not found; pinned slots show a plain mark.");
            return _badgeSprite;
        }

        // P25 helper: the item panel during a trade or for a locked pin.
        internal static void OnUpdateInfo(ItemInfoPanel panel, ItemData itemData)
        {
            if (panel == null || itemData == null || !PinStore.Active) return;
            var sc = itemData.slotController;
            if (sc == null) return;
            if (Game.TradeActive && sc.slot != null && sc.slot.playerSlot && Reservation.SellLocked(sc))
            {
                var t = Vanilla.CantUseText(panel);
                if (t != null) t.text = Strings.Pinned;
            }
            if (Reservation.MoveLocked(sc))
            {
                var drop = Vanilla.DropButton(panel);
                if (drop != null && drop.activeSelf) drop.SetActive(false);
            }
        }
    }

    // P24: Slot.OnPointerEnter(ItemData) (SRC/Slot.cs:425; the controller calls it too, InventoryNavigationHandler :1884).
    // A pinned slot shows red for items it would refuse.
    [HarmonyPatch]
    internal static class SlotPointerEnterPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SlotPointerEnter;

        private static void Postfix(Slot __instance, ItemData itemData)
        {
            try
            {
                if (__instance == null || itemData == null || itemData.item == null || itemData.item.ID <= 1) return;
                if (Reservation.Vetoes(__instance.slotcontrol, itemData.item)) __instance.SetColorToNotAllowed();
            }
            catch (Exception e) { Log.Exception("slot hover colour", e); }
        }
    }

    // P25: ItemInfoPanel.UpdateInfo(ItemData, ChildItem) (SRC/ItemInfoPanel.cs:182). During a trade the "No money" text of
    // a pinned stack reads "Pinned"; a locked pin has no Drop button. Vanilla re-enables Drop on every call (:197).
    [HarmonyPatch]
    internal static class UpdateInfoPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateInfo;

        private static void Postfix(ItemInfoPanel __instance, ItemData itemData)
        {
            try { PinOverlay.OnUpdateInfo(__instance, itemData); }
            catch (Exception e) { Log.Exception("item panel pin", e); }
        }
    }
}
