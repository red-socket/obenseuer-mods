using System;
using UnityEngine;

namespace MessageLog
{
    // Opening the window (spec §8.1, D5), adapted from Fair Market's NotesPageInput. The journal's Notes page is where
    // letters and mail live. The game reads "Move Item" only on the Inventory page (CurrentTab == 0,
    // SRC/InventoryNavigationHandler.cs:1224, :1235) and in shops (SRC/ShopController.cs:93), and no mod of ours reads it
    // on the Notes page, so there Move Item (keyboard F, pad X) opens the pause menu and the window on the next frame.
    // Fair Market's Drop (R / pad Y) on the same page stays Fair Market's. Never while a note is being edited or its
    // on-screen keyboard is up (pad X is Backspace there). H1 shows the key in the game's own hint line. The optional
    // OpenKey opens the window from play or over the pause menu.
    internal static class NotesPageInput
    {
        private const int NotesTab = 4;
        private static int _openFrame = -1;

        internal static bool Available;   // the notes-key group is on

        internal static void Tick()
        {
            if (_openFrame >= 0 && Time.frameCount >= _openFrame)
            {
                _openFrame = -1;
                if (Game.PauseOpen && !LogWindow.IsOpen) LogWindow.Open();
                return;
            }
            if (LogWindow.IsOpen || Game.ConsoleOpen || Game.Typing || !LogWindow.Available || !Cfg.Hot.WindowOn) return;

            if (Keys.Down(Cfg.Access.OpenKey))
            {
                if (Game.PauseOpen) LogWindow.Open();
                else if (!Game.MenuVisible && !Game.InConversation && PlayGate.Open) OpenFromPlay();
                else Log.Verbose("Window: not opened (a menu or a conversation is open).");
                return;
            }

            if (!Available || !Cfg.Hot.NotesPageKey || Game.PauseOpen || !OnNotesPage()) return;
            var im = InputManager.instance;
            if (im == null || !im.GetKeyDown("Move Item")) return;
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
                if (LogWindow.OpenBlockedBeforePause() is string why)
                {
                    Log.Verbose($"Window: not opened ({why}).");
                    return;
                }
                PauseMenu.instance.Pause();
                _openFrame = Time.frameCount + 1;
            }
            catch (Exception e) { Log.Exception("open from the Notes page", e); }
        }

        // Re-runs the game's hint line (after a config change), the way Fair Market refreshes the Notes page.
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
