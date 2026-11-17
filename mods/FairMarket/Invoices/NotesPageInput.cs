using System;
using UnityEngine;

namespace FairMarket
{
    // Opening the Invoices window (spec §9.4, D18, D20), adapted from Vital Signs' CharacterPageInput. The game reads
    // "Drop" only on the Inventory page (CurrentTab == 0, SRC/InventoryNavigationHandler.cs:1220), so on the Notes page
    // (tab 4) it is free: Drop (keyboard R, pad Y) opens the pause menu and the window on the next frame. Use Item isn't
    // used: pad A is also Interact, which starts editing a note there (SRC/NotesPanelNavigation.cs:113-116). Never while
    // a note is being edited or its on-screen keyboard is up (pad Y types a space there). U1 shows the key in the game's
    // own hint line. The optional OpenKey opens the window from play or over the pause menu.
    internal static class NotesPageInput
    {
        private const int NotesTab = 4;
        private static int _openFrame = -1;

        internal static bool Available;   // the list-access group is on

        internal static void Tick()
        {
            if (_openFrame >= 0 && Time.frameCount >= _openFrame)
            {
                _openFrame = -1;
                if (Game.PauseOpen && !InvoiceWindow.IsOpen) InvoiceWindow.Open();
                return;
            }
            if (InvoiceWindow.IsOpen || Game.ConsoleOpen || Game.Typing || !InvoiceWindow.Available || !Cfg.Hot.ListOn) return;

            if (Keys.Down(Cfg.InvoiceList.OpenKey))
            {
                if (Game.PauseOpen) InvoiceWindow.Open();
                else if (!Game.MenuVisible && !Game.InConversation && PlayGate.Open) OpenFromPlay();
                return;
            }

            if (!Available || !Cfg.Hot.NotesPageKey || Game.PauseOpen || !OnNotesPage()) return;
            var im = InputManager.instance;
            if (im == null || !im.GetKeyDown("Drop")) return;
            if (!PlayGate.Open || Game.InConversation || Game.NotesKeyboardVisible) return;
            OpenFromPlay();
        }

        internal static bool OnNotesPage()
        {
            try
            {
                var h = InventoryNavigationHandler.Instance;
                var d = InventoryNavigationData.Instance;
                var pages = UIPageButtons.instance;
                return h != null && h.isActiveAndEnabled && d != null && d.CurrentTab == NotesTab &&
                       pages != null && pages.currentPage == MenuPageNames.Notes &&
                       GameUIController.instance != null && GameUIController.instance.IsMenuVisible;
            }
            catch { return false; }
        }

        // PauseMenu.Pause() closes the game menu and opens the pause menu; the window opens on the next frame.
        private static void OpenFromPlay()
        {
            try
            {
                if (PauseMenu.instance == null || Game.Dead) return;
                if (InvoiceWindow.OpenBlockedBeforePause() is string why)
                {
                    Log.Verbose($"Invoices: window not opened ({why}).");
                    return;
                }
                PauseMenu.instance.Pause();
                _openFrame = Time.frameCount + 1;
            }
            catch (Exception e) { Log.Exception("open from the Notes page", e); }
        }

        // Re-runs the game's hint line (after a config change), the way Vital Signs refreshes the Character page.
        internal static void RefreshHint()
        {
            try
            {
                var h = InventoryNavigationHandler.Instance;
                var d = InventoryNavigationData.Instance;
                if (h != null && d != null && InputManager.instance != null && h.isActiveAndEnabled)
                    h.ChangeControlsInfo(InputManager.instance.inputType, d.CurrentTab, d.CurrentWindow);
            }
            catch (Exception e) { Log.Exception("notes hint", e); }
        }
    }
}
