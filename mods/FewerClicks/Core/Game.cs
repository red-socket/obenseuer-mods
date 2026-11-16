using System;
using UnityEngine;

namespace FewerClicks
{
    // Small read-only helpers over game state shared by several features. Unity objects are null-checked with != null.
    internal static class Game
    {
        internal const string Title = "Fewer Clicks";

        private static int _dragSeenFrame = -100;

        internal static bool ConsoleOpen
        {
            get
            {
                try { var c = DeveloperConsole.Instance; return c != null && c.consoleOpened; }
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

        internal static bool WriteLock
        {
            get
            {
                try { return GameController.instance != null && GameController.instance.writeLock; }
                catch { return false; }
            }
        }

        // The amount slider (split, drop or move N) is open.
        internal static bool SliderOpen
        {
            get
            {
                try { return AmountSliderUI.instance != null && AmountSliderUI.instance.sliderIsVisible; }
                catch { return false; }
            }
        }

        internal static bool MenuVisible
        {
            get
            {
                try { return GameUIController.instance != null && GameUIController.instance.IsMenuVisible; }
                catch { return false; }
            }
        }

        // Called every frame from Plugin.Update while a storage or liquid window is open, so a drag that ended between
        // two of our checks is still seen.
        internal static void TrackDrag()
        {
            if (Storage.active != null || LiquidStorage.currentStorage != null) DragActive();
        }

        // Copy of Better Tenement's Stash.DragActive. A drag takes its stack out of the source slot when it starts, and an
        // unsuccessful drop writes the whole original stack back over that slot two frames after the drag ends
        // (ItemData.OnBeginDrag/PostDragReturn/ReturnOriginal). Moving stacks in between would destroy or duplicate
        // items, so moves refuse while a drag is active and for 3 frames after. Fails closed.
        internal static bool DragActive()
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

        // The game's own notification, null-safe.
        internal static void Notify(string text)
        {
            try
            {
                if (Notifications.instance != null) Notifications.instance.CreateNotification(Title, text);
            }
            catch (Exception e) { Log.Exception("notification", e); }
        }

        // The game's red "can't do that" flash and sound under the crosshair.
        internal static void Warning()
        {
            try
            {
                if (UseText.instance != null) UseText.instance.WarningUseEffect();
            }
            catch (Exception e) { Log.Exception("warning effect", e); }
        }
    }
}
