using System;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FairMarket
{
    // Live accessors over game state shared by several features, copied from Vital Signs (Quest Helper's). Unity objects
    // are null-checked with != null and never cached across scenes.
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

        // A text field has the keyboard (Workshop's Game.Typing): the game's writeLock (notes, computers) or a focused field.
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

        // Any game menu (inventory, station panel, map) is on screen. Fails closed.
        internal static bool MenuVisible
        {
            get
            {
                try { return GameUIController.instance == null || GameUIController.instance.IsMenuVisible; }
                catch { return true; }
            }
        }

        internal static bool Dead
        {
            get
            {
                try { return Death.instance != null && Death.instance.dead; }
                catch { return false; }
            }
        }

        internal static bool InConversation
        {
            get
            {
                try { return DialogueManager.isConversationActive; }
                catch { return true; }
            }
        }

        internal static bool PauseOpen
        {
            get
            {
                try { return PauseMenu.instance != null && PauseMenu.instance.PauseIsActive(); }
                catch { return false; }
            }
        }

        internal static bool Controller
        {
            get
            {
                try { return InputManager.instance != null && InputManager.instance.inputType == InputManager.InputType.Controller; }
                catch { return false; }
            }
        }

        // The notes page's on-screen keyboard is up (pad Y types a space there, SRC/NotesPanelNavigation.cs:128-137).
        // Fails closed: when it can't be read, the Notes page key does nothing.
        internal static bool NotesKeyboardVisible
        {
            get
            {
                try
                {
                    var nav = UnityEngine.Object.FindAnyObjectByType<NotesPanelNavigation>();
                    if (nav == null) return false;
                    if (Vanilla.NotesKeyboard == null) return true;
                    var kb = Vanilla.NotesKeyboard(nav);
                    return kb != null && kb.keyboardIsVisible;
                }
                catch { return true; }
            }
        }

        // Game minutes since the start of the week, for text caches (changes once a game minute).
        internal static int WeekMinute
        {
            get
            {
                try
                {
                    var t = TimeOfDayAzure.instance;
                    if (t == null || t.currentTimeAndDay == null) return -1;
                    var d = t.currentTimeAndDay;
                    return d.weekDay * 1440 + (int)d.hours * 60 + (int)d.minutes;
                }
                catch { return -1; }
            }
        }

        internal static string ItemTitle(int id)
        {
            try
            {
                var item = ItemDatabase.instance != null ? ItemDatabase.instance.FetchItemByID(id) : null;
                if (item != null && !string.IsNullOrEmpty(item.Title)) return item.Title;
            }
            catch { }
            return "#" + id;
        }

        internal static string Clean(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
