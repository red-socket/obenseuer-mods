using System;
using UnityEngine;

namespace VitalSigns
{
    // Opening the window (spec §9.2, D1, D3), adapted from Quest Helper's TaskTabInput. The game reads "Use Item" only
    // on the Inventory page (CurrentTab == 0, SRC/InventoryNavigationHandler.cs:1290; Drop :1220), so on the Character
    // page it is free: Use Item (keyboard X, pad A) opens the pause menu and Vital Signs on the next frame. Tab 1 is
    // shared with the Map, so the current page is checked too. A1/A2 show the key in the game's own hint line. The
    // optional OpenKey opens the window from play or over the pause menu.
    internal static class CharacterPageInput
    {
        private const int CharacterTab = 1;
        private static int _openFrame = -1;

        internal static bool Available;   // the character-access group is on

        internal static void Tick()
        {
            if (_openFrame >= 0 && Time.frameCount >= _openFrame)
            {
                _openFrame = -1;
                if (Game.PauseOpen && !VitalWindow.IsOpen) VitalWindow.Open();
                return;
            }
            if (VitalWindow.IsOpen || Game.ConsoleOpen || Game.Typing || !VitalWindow.Available) return;

            if (Keys.Down(Cfg.Access.OpenKey))
            {
                if (Game.PauseOpen) VitalWindow.Open();
                else if (!Game.MenuVisible && !Game.InConversation && PlayGate.Open) OpenFromPlay();
                return;
            }

            if (!Available || !Cfg.Hot.CharacterPageKey || Game.PauseOpen || !OnCharacterPage()) return;
            var im = InputManager.instance;
            if (im != null && im.GetKeyDown("Use Item") && PlayGate.Open && !Game.InConversation) OpenFromPlay();
        }

        internal static bool OnCharacterPage()
        {
            try
            {
                var h = InventoryNavigationHandler.Instance;
                var d = InventoryNavigationData.Instance;
                var pages = UIPageButtons.instance;
                return h != null && h.isActiveAndEnabled && d != null && d.CurrentTab == CharacterTab &&
                       pages != null && pages.currentPage == MenuPageNames.Character &&
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
                PauseMenu.instance.Pause();
                _openFrame = Time.frameCount + 1;
            }
            catch (Exception e) { Log.Exception("open from the Character page", e); }
        }

        // Re-runs the game's hint line (A2; also after a config change), the way Quest Helper refreshes the Tasks page.
        internal static void RefreshHint()
        {
            try
            {
                var h = InventoryNavigationHandler.Instance;
                var d = InventoryNavigationData.Instance;
                if (h != null && d != null && InputManager.instance != null && h.isActiveAndEnabled)
                    h.ChangeControlsInfo(InputManager.instance.inputType, d.CurrentTab, d.CurrentWindow);
            }
            catch (Exception e) { Log.Exception("character hint", e); }
        }
    }
}
