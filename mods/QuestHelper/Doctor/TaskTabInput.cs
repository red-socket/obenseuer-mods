using System;
using UnityEngine;

namespace QuestHelper
{
    // Keys on the Tasks page of the game menu (spec §4.1, §7, V8). The game reads "Use Item" and "Drop" only on the
    // Inventory page (CurrentTab == 0), so on the Tasks page (3) they are free: Use Item (keyboard X, pad A) opens the
    // pause menu and Quest Helper on the selected task; Drop (keyboard R, pad Y) folds the To be continued section. P7
    // shows both in the game's own controls hint line. The optional OpenKey opens the window from play.
    internal static class TaskTabInput
    {
        private const int TasksTab = 3;
        private static int _openFrame = -1;
        private static string _preselect;

        internal static void Tick()
        {
            if (_openFrame >= 0 && Time.frameCount >= _openFrame)
            {
                _openFrame = -1;
                if (Game.PauseOpen && !HelperWindow.IsOpen) HelperWindow.Open(_preselect);
                return;
            }
            if (HelperWindow.IsOpen || Game.PauseOpen || Game.ConsoleOpen || Game.Typing) return;

            if (HelperWindow.Available && Keys.Down(Cfg.QuestDoctor.OpenKey) && !Game.MenuVisible && !Game.InConversation && PlayGate.Open)
            {
                OpenFromPlay(Tracking.Selected);
                return;
            }

            if (!OnTasksPage()) return;
            var im = InputManager.instance;
            if (im == null) return;
            if (HelperWindow.Available && Cfg.QuestDoctor.TaskTabKey.Value && im.GetKeyDown("Use Item") && PlayGate.Open && !Game.InConversation)
            {
                OpenFromPlay(Tracking.Selected);
                return;
            }
            if (TbcSection.Available && TbcSection.HeaderExists && im.GetKeyDown("Drop")) TbcSection.Toggle();
        }

        internal static bool OnTasksPage()
        {
            try
            {
                var h = InventoryNavigationHandler.Instance;
                var d = InventoryNavigationData.Instance;
                return h != null && h.isActiveAndEnabled && d != null && d.CurrentTab == TasksTab &&
                       GameUIController.instance != null && GameUIController.instance.IsMenuVisible;
            }
            catch { return false; }
        }

        // PauseMenu.Pause() closes the game menu and opens the pause menu; the window opens on the next frame.
        private static void OpenFromPlay(string preselect)
        {
            try
            {
                if (PauseMenu.instance == null || Game.Dead) return;
                PauseMenu.instance.Pause();
                _preselect = preselect;
                _openFrame = Time.frameCount + 1;
            }
            catch (Exception e) { Log.Exception("open from tasks", e); }
        }
    }
}
