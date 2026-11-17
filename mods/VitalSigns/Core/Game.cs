using System;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VitalSigns
{
    // Live accessors over game state shared by several features, copied from Quest Helper. Unity objects are null-checked
    // with != null and never cached across scenes.
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

        // A text field has the keyboard (Workshop's Game.Typing).
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

        // Asleep or passed out (SRC/WaitingController.cs:86).
        internal static bool Asleep
        {
            get
            {
                try { var w = WaitingController.instance; return w != null && w.IsSleeping; }
                catch { return false; }
            }
        }

        // Any vanilla wait that isn't sleep: TV, benches, jobs, arrest, Time Keeper (SRC/WaitingController.cs:98).
        internal static bool Waiting
        {
            get
            {
                try { var w = WaitingController.instance; return w != null && w.IsWaiting; }
                catch { return false; }
            }
        }

        internal static bool MentalHealthDisabled
        {
            get
            {
                try
                {
                    var d = DifficultyController.instance;
                    return d != null && d.currentDifficultyInfo != null && d.currentDifficultyInfo.mentalHealthDisabled;
                }
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

        // The key the game shows for an action ("X", "A"), or the fallback.
        internal static string KeyText(string action, string fallback)
        {
            try
            {
                var im = InputManager.instance;
                string s = im != null ? im.GetKeyCodeDisplayText(action) : null;
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            catch { return fallback; }
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
