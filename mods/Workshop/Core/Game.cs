using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Workshop
{
    // Small read-only helpers over game state shared by several features. Unity objects are null-checked with != null.
    internal static class Game
    {
        private static int _dragSeenFrame = -100;

        internal static bool ConsoleOpen
        {
            get
            {
                try { var c = DeveloperConsole.Instance; return c != null && c.consoleOpened; }
                catch { return false; }
            }
        }

        // A text field has the keyboard: the game's writeLock (computers, notes, the console's own input), or any
        // focused input field (editable signs only call ControlsDisabled, SRC/EditableSign.cs:127).
        internal static bool Typing
        {
            get
            {
                try
                {
                    if (GameController.instance != null && GameController.instance.writeLock) return true;
                    var es = EventSystem.current;
                    var go = es != null ? es.currentSelectedGameObject : null;
                    if (go == null) return false;
                    var tmp = go.GetComponent<TMP_InputField>();
                    if (tmp != null && tmp.isFocused) return true;
                    var ui = go.GetComponent<InputField>();
                    return ui != null && ui.isFocused;
                }
                catch { return false; }
            }
        }

        internal static bool TradeActive
        {
            get
            {
                try { return TradePanel.instance != null && TradePanel.instance.activeTrade != null; }
                catch { return true; }
            }
        }

        // The amount slider (split, drop, move or sell N) is open.
        internal static bool SliderOpen
        {
            get
            {
                try { return AmountSliderUI.instance != null && AmountSliderUI.instance.sliderIsVisible; }
                catch { return true; }
            }
        }

        // Any game menu (inventory, station panel, map, pause) is on screen. Fails closed.
        internal static bool MenuVisible
        {
            get
            {
                try { return GameUIController.instance == null || GameUIController.instance.IsMenuVisible; }
                catch { return true; }
            }
        }

        // Copy of Tidy Inventory's Game.DragActive. A drag takes its stack out of the source slot when it starts, and an
        // unsuccessful drop writes the whole original stack back over that slot two frames after the drag ends
        // (ItemData.OnBeginDrag/PostDragReturn/ReturnOriginal). Moving stacks in between would destroy or duplicate
        // items, so Fetch, the pump and Cancel refuse while a drag is active and for 3 frames after. Fails closed.
        internal static bool DragActive
        {
            get
            {
                bool drag = false;
                try
                {
                    if (Vanilla.GlobalDragField != null) drag = (Vanilla.GlobalDragField.GetValue(null) as ItemData) != null;   // Unity null: a destroyed one counts as none
                    else drag = Input.GetMouseButton(0);
                    var h = InventoryNavigationHandler.Instance;
                    if (h != null && h.Dragging) drag = true;
                }
                catch (Exception e)
                {
                    Log.Exception("drag check", e);
                    drag = true;
                }
                if (drag) _dragSeenFrame = Time.frameCount;
                return Time.frameCount - _dragSeenFrame <= 3;
            }
        }

        // Called every frame from Plugin.Update, so a drag that ended between two of our checks is still seen.
        internal static void TrackDrag()
        {
            if (Inventory.instance != null && MenuVisible) _ = DragActive;
        }

        // The player is using a controller. Fails closed (true), so the pump waits rather than moving a cursor.
        internal static bool ControllerActive
        {
            get
            {
                try { return InputManager.instance == null || InputManager.instance.inputType == InputManager.InputType.Controller; }
                catch { return true; }
            }
        }

        // An item's display name, or "?".
        internal static string Title(Item item) => item != null && !string.IsNullOrEmpty(item.Title) ? item.Title : "?";

        internal static Item ItemById(int id)
        {
            try { return ItemDatabase.instance != null ? ItemDatabase.instance.FetchItemByID(id) : null; }
            catch { return null; }
        }

        // The ItemData shown for a slot, when the slot is on screen; null for closed storage.
        internal static ItemData DataOf(SlotController sc)
        {
            try { return sc?.slot != null ? sc.slot.GetItemItemData() : null; }
            catch { return null; }
        }
    }
}
