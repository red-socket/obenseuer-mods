using System;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TidyInventory
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
                catch { return false; }
            }
        }

        // The amount slider (split, drop, move or sell N) is open.
        internal static bool SliderOpen
        {
            get
            {
                try { return AmountSliderUI.instance != null && AmountSliderUI.instance.sliderIsVisible; }
                catch { return false; }
            }
        }

        // The main inventory grid is on screen.
        internal static bool InventoryVisible
        {
            get
            {
                try
                {
                    var inv = Inventory.instance;
                    var ui = inv != null ? inv.inventoryPanelUI : null;
                    return ui != null && ui.inventorySlotPanel != null && ui.inventorySlotPanel.activeInHierarchy;
                }
                catch { return false; }
            }
        }

        internal static bool StorageVisible
        {
            get
            {
                try { return GameUIController.instance != null && GameUIController.instance.StorageIsVisible(); }
                catch { return false; }
            }
        }

        internal static bool ConversationActive
        {
            get
            {
                try { return DialogueManager.isConversationActive; }
                catch { return true; }
            }
        }

        // Copy of Fewer Clicks' Game.DragActive. A drag takes its stack out of the source slot when it starts, and an
        // unsuccessful drop writes the whole original stack back over that slot two frames after the drag ends
        // (ItemData.OnBeginDrag/PostDragReturn/ReturnOriginal). Changing stacks in between would destroy or duplicate
        // items, so our actions refuse while a drag is active and for 3 frames after. Fails closed.
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
            if (Inventory.instance != null && (InventoryVisible || Storage.active != null)) _ = DragActive;
        }

        internal static bool NonEmpty(SlotController sc) =>
            sc != null && sc.itemStack != null && sc.itemStack.itemId > -1 && sc.itemStack.itemAmount > 0;

        // The item info panel shows a stack of one of these slots: close it before the stack changes under it.
        internal static void CloseInfoFor(Func<SlotController, bool> affected)
        {
            try
            {
                var info = ItemInfo.instance;
                if (info != null && info.ItemData != null && affected(info.ItemData.slotController)) info.Deactivate();
            }
            catch (Exception e) { Log.Exception("item panel close", e); }
        }

        // The tooltip and hover state of a removed stack (spec §4.3).
        internal static void ClearHoverFor(Func<SlotController, bool> affected)
        {
            try
            {
                var hover = ItemData.currentHoverItemData;
                if (hover != null && affected(hover.slotController))
                {
                    ItemData.currentHoverItemData = null;
                    if (ToolTip.instance != null) ToolTip.instance.Deactivate();
                }
            }
            catch (Exception e) { Log.Exception("hover clear", e); }
            CloseInfoFor(affected);
        }
    }
}
