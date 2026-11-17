using System;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QuestHelper
{
    // Live accessors over game state shared by several features (spec §2). Unity objects are null-checked with != null
    // and never cached across LoadingDone.
    internal static class Game
    {
        internal const string TenementMapId = "Assets/Content/TaskItems/Maps/Tenement Map.asset";
        internal const string MinesMapId = "map_mines_001";

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

        internal static string SceneName
        {
            get
            {
                try { return SceneManager.GetActiveScene().name ?? ""; }
                catch { return ""; }
            }
        }

        // The game blocks saving right now: the No-saves difficulty or a temporary lock (V7, SRC/PauseMenu.cs:458-477).
        internal static bool SaveLocked
        {
            get
            {
                var locks = Vanilla.SaveLocks(PauseMenu.instance);
                return locks == null || locks.Count > 0;
            }
        }

        internal static bool SavesDisabledByDifficulty
        {
            get
            {
                try
                {
                    var d = DifficultyController.instance;
                    return d != null && d.currentDifficultyInfo != null && d.currentDifficultyInfo.userSavesDisabled;
                }
                catch { return false; }
            }
        }

        // The vanilla Save button would be enabled (SRC/PauseMenu.cs:458).
        internal static bool SaveButtonEnabled => !SavesDisabledByDifficulty && !SaveLocked;

        // Replicates the private MapController.GetCurrentMapId (SRC/MapController.cs:197-211): "" when the scene has no map.
        internal static string CurrentMapId
        {
            get
            {
                try
                {
                    var im = info_map.instance;
                    if (im == null) return "";
                    if (im.requiredTaskItem != null) return im.requiredTaskItem.Id ?? "";
                    return im.mapImage != null ? im.mapImage.name ?? "" : "";
                }
                catch { return ""; }
            }
        }

        // Curated map keys (spec §6).
        internal static string MapIdOf(string key) =>
            key == "tenement" ? TenementMapId : key == "mines" ? MinesMapId : key ?? "";

        internal static string MapName(string mapId) =>
            mapId == TenementMapId ? Strings.TenementMap : mapId == MinesMapId ? Strings.MinesMap : Strings.OtherMap;

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

        // "Interior Mines Management" -> "Mines Management", "Open Sewer Mines" -> "Mines".
        internal static string ShortScene(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return "";
            if (scene.StartsWith("Interior ", StringComparison.Ordinal)) return scene.Substring(9);
            if (scene.StartsWith("Open Sewer ", StringComparison.Ordinal)) return scene.Substring(11);
            return scene;
        }

        // A person's name from a conversation title: last path segment, cut at ':', without a door or camera suffix
        // (spec §5.1). "Tunnels/Anya: Door" -> "Anya", "Bazaar/Chamber of Commerce/NO Company Doorphone" -> "NO Company".
        internal static string GiverFromConversation(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            string s = title;
            int slash = s.LastIndexOf('/');
            if (slash >= 0) s = s.Substring(slash + 1);
            int colon = s.IndexOf(':');
            if (colon >= 0) s = s.Substring(0, colon);
            foreach (var suffix in new[] { " Doorphone", " Door", " Camera", "_Door" })
                if (s.EndsWith(suffix, StringComparison.Ordinal)) { s = s.Substring(0, s.Length - suffix.Length); break; }
            return s.Trim();
        }

        internal static string Clean(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
