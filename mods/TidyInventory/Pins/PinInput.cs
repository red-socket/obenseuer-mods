using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TidyInventory
{
    // PinKey (spec §3.6): with the inventory open, pins or unpins the main inventory slot under the mouse. Trades may be
    // open (pinning is how you protect a stack before selling). The slot is found the way the game's GetSlotUnderMouse
    // does (SRC/Inventory.cs:1449-1455): the first raycast result whose own GameObject has a Slot.
    internal static class PinInput
    {
        internal static bool KeyPatched;   // "pin key" group (P18-P23)

        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        internal static void Tick()
        {
            if (!PinPatches.Patched || !Cfg.Pins.Enabled.Value) return;
            if (!Keys.Down(Cfg.Pins.PinKey)) return;
            if (!Game.InventoryVisible || Game.DragActive || Game.SliderOpen || !PlayGate.Open) return;
            if (PinStore.UnknownVersion || !PinStore.CanWrite)
            {
                Log.Verbose("Pins: pin key ignored (pins can't be saved right now).");
                return;
            }

            var slot = SlotUnderMouse();
            if (slot == null) return;
            if (!Reservation.SlotIndex.TryGetValue(slot, out int i))
            {
                Hints.Once("pin-storage", Strings.HintPinStorage);
                return;
            }
            var sc = Reservation.SlotAt(i);
            if (sc == null) return;

            if (PinStore.Map.ContainsKey(i))
            {
                PinStore.Map.Remove(i);
                Log.Verbose($"Pins: unpinned slot {i + 1}.");
            }
            else if (Game.NonEmpty(sc))
            {
                int id = sc.itemStack.itemId;
                PinStore.Map[i] = PinStore.Make(id);
                Log.Verbose($"Pins: pinned slot {i + 1} to {sc.itemStack.itemReference?.Item?.Title ?? id.ToString()} (id {id}, family {PinStore.Map[i].Family}).");
                Hints.Once("pin-first", Strings.HintPinFirst(Keys.Label(Cfg.Pins.PinKey), Cfg.Pins.BlockManualMoves.Value, Cfg.Pins.ReturnItemsToPin.Value && PinPatches.ReturnPatched));
            }
            else
            {
                Hints.Once("pin-empty", Strings.HintPinEmpty);
                return;
            }
            PinStore.Write("pin key");
            PinOverlay.MarkDirty();
            Reservation.Verify();
            try { sc.UpdateNow(); }
            catch (Exception e) { Log.Exception("slot refresh", e); }
        }

        private static Slot SlotUnderMouse()
        {
            var es = EventSystem.current;
            if (es == null) return null;
            Hits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = Input.mousePosition }, Hits);
            foreach (var h in Hits)
            {
                if (h.gameObject == null) continue;
                var s = h.gameObject.GetComponent<Slot>();
                if (s != null) return s;
            }
            return null;
        }

        // P18-P23 share this: a middle-button event while the pin key is the middle button.
        internal static bool Swallow(PointerEventData e)
        {
            try { return e != null && e.button == PointerEventData.InputButton.Middle && Keys.PinKeyIsMiddle; }
            catch (Exception ex) { Log.Exception("middle click check", ex); return false; }
        }
    }

    // P18-P23: the middle mouse button does nothing else on items and slots while it is the pin key. Slot.OnPointerDown
    // (:693) forwards to ItemData.OnPointerDown, so P18 also keeps middle presses out of the double-click count. Known:
    // the controller cursor still moves on a middle click (HandleNonMovementInput, :1327-1344).
    [HarmonyPatch]
    internal static class ItemPointerDownPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ItemPointerDown;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PointerEventData eventData) => !PinInput.Swallow(eventData);
    }

    [HarmonyPatch]
    internal static class ItemPointerClickPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ItemPointerClick;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PointerEventData eventData) => !PinInput.Swallow(eventData);
    }

    [HarmonyPatch]
    internal static class ItemBeginDragPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ItemBeginDrag;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PointerEventData eventData)
        {
            if (!PinInput.Swallow(eventData)) return true;
            eventData.pointerDrag = null;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ItemDragPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ItemDrag;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PointerEventData eventData) => !PinInput.Swallow(eventData);
    }

    [HarmonyPatch]
    internal static class ItemEndDragPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ItemEndDrag;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PointerEventData eventData) => !PinInput.Swallow(eventData);
    }

    [HarmonyPatch]
    internal static class SlotPointerClickPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SlotPointerClick;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PointerEventData eventData) => !PinInput.Swallow(eventData);
    }
}
