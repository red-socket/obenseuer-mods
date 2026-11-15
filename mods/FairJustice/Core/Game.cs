using System;
using PixelCrushers.DialogueSystem;

namespace FairJustice
{
    // Small read-only helpers over game state (from Better Tenement's Core/Game.cs, plus HasControl).
    internal static class Game
    {
        internal static bool ConsoleOpen
        {
            get
            {
                try { var c = DeveloperConsole.Instance; return c != null && c.consoleOpened; }
                catch { return false; }
            }
        }

        internal static bool Paused
        {
            get
            {
                try { return PauseMenu.instance != null && PauseMenu.instance.PauseIsActive(); }
                catch { return false; }
            }
        }

        internal static bool DialogueActive
        {
            get
            {
                try { return DialogueManager.IsConversationActive; }
                catch { return false; }
            }
        }

        // The player is walking around: not loading, changing level, sleeping, waiting, in a sleep event or paused.
        // Grace timers only count down while this holds (spec §2.3).
        internal static bool HasControl
        {
            get
            {
                try
                {
                    if (SaveController.Loading || Changelevel.ChangeLevelActive) return false;
                    var w = WaitingController.instance;
                    if (w != null && w.waitingType != WaitingController.Type.None) return false;
                    var s = SleepEventController.instance;
                    if (s != null && s.eventInProgress) return false;
                    return !Paused;
                }
                catch (Exception) { return false; }
            }
        }
    }
}
