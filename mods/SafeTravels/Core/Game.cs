using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SafeTravels
{
    // Small read-only helpers over game state, plus the game's own notification and warning flash. Unity objects are
    // null-checked with != null.
    internal static class Game
    {
        internal const string Title = "Safe Travels";

        // The same note is not repeated within this many real seconds (a held key or repeated presses).
        private const float NotifyRepeatSeconds = 3f;

        private static string _lastNote;
        private static float _lastNoteAt = -100f;

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

        internal static bool Paused
        {
            get
            {
                try
                {
                    var gc = GameController.instance;
                    if (gc != null && gc.GameIsPaused) return true;
                    return PauseMenu.instance != null && PauseMenu.instance.PauseIsActive();
                }
                catch { return true; }
            }
        }

        // The game's own notification, null-safe. A note identical to the last one is dropped when it comes within
        // NotifyRepeatSeconds (unscaled time).
        internal static void Notify(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            float now = Time.unscaledTime;
            if (text == _lastNote && now - _lastNoteAt < NotifyRepeatSeconds) return;
            _lastNote = text;
            _lastNoteAt = now;
            Post(text);
        }

        // Key-collision notes: always shown (they explain why a key does nothing), never rate-limited.
        internal static void Post(string text)
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

        internal static string SceneName
        {
            get
            {
                try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
                catch { return "?"; }
            }
        }
    }
}
