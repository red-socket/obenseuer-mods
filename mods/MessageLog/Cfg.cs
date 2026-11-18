using BepInEx.Configuration;
using UnityEngine;

namespace MessageLog
{
    // Every tunable value (spec §10). Entries marked LaunchNote gate a patch group at launch: turning them off works at
    // once (patch bodies read Hot), turning them on needs a restart. Hot caches the values read on hot paths and is
    // refreshed on SettingChanged. Description strings are public text: run them through the humanizer again after any
    // change.
    internal static class Cfg
    {
        private const string LaunchNote = " Restart the game after turning this on.";

        internal static ConfigEntry<bool> VerboseLogging;

        internal static class Capture
        {
            internal static ConfigEntry<bool> Notifications, FoldObjectiveProgress;
            internal static ConfigEntry<int> MergeSeconds;
        }

        internal static class Dialogue
        {
            internal static ConfigEntry<bool> ConversationLines, PlayerChoices;
        }

        internal static class Storage
        {
            internal static ConfigEntry<bool> SaveWithGame, GapMarker;
            internal static ConfigEntry<int> MaxNotifications, MaxDialogueLines, MaxFileKB;
        }

        internal static class Window
        {
            internal static ConfigEntry<bool> Enabled, Search, TranslateWithPtBr;
            internal static ConfigEntry<float> UiScale;
        }

        internal static class Access
        {
            internal static ConfigEntry<bool> NotesPageKey, PauseMenuEntry;
            internal static ConfigEntry<KeyboardShortcut> OpenKey;
        }

        // Cached values read by patch bodies and the save path (spec §10). One field read per call.
        internal static class Hot
        {
            internal static bool Notifications, Dialogue, PlayerChoices, FoldObjectives, SaveWithGame, GapMarker,
                TranslateWithPtBr, NotesPageKey, WindowOn, Search;
            internal static float MergeSeconds;
            internal static int MaxNotifications, MaxDialogueLines, MaxFileBytes;

            internal static void Refresh()
            {
                Notifications = Capture.Notifications.Value;
                MergeSeconds = Capture.MergeSeconds.Value;
                FoldObjectives = Capture.FoldObjectiveProgress.Value;
                Dialogue = Cfg.Dialogue.ConversationLines.Value;
                PlayerChoices = Cfg.Dialogue.PlayerChoices.Value;
                SaveWithGame = Storage.SaveWithGame.Value;
                GapMarker = Storage.GapMarker.Value;
                MaxNotifications = Mathf.Clamp(Storage.MaxNotifications.Value, 100, 5000);
                MaxDialogueLines = Mathf.Clamp(Storage.MaxDialogueLines.Value, 0, 3000);
                MaxFileBytes = Mathf.Clamp(Storage.MaxFileKB.Value, 128, 4096) * 1024;
                WindowOn = Window.Enabled.Value;
                Search = Window.Search.Value;
                TranslateWithPtBr = Window.TranslateWithPtBr.Value;
                NotesPageKey = Access.NotesPageKey.Value;
            }
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Write extra detail to the BepInEx log: each save and load of the log, and why the window didn't open.");

            Capture.Notifications = c.Bind("Capture", "Notifications", true,
                "Record every notification the game shows, including the ones other mods send through it." + LaunchNote);
            Capture.MergeSeconds = c.Bind("Capture", "MergeSeconds", 3,
                new ConfigDescription("When the same message comes again within this many seconds, it's counted (x2, x3) " +
                    "instead of listed again. 0 lists every one.", new AcceptableValueRange<int>(0, 30)));
            Capture.FoldObjectiveProgress = c.Bind("Capture", "FoldObjectiveProgress", true,
                "For objectives with a counter (3/10), keep only the latest step between saves.");

            Dialogue.ConversationLines = c.Bind("Dialogue", "ConversationLines", true,
                "Record what people say to you in conversations, with their names." + LaunchNote);
            Dialogue.PlayerChoices = c.Bind("Dialogue", "PlayerChoices", true,
                "Also record the reply you picked, shown as \"You\".");

            Storage.SaveWithGame = c.Bind("Storage", "SaveWithGame", true,
                "Keep the log in each save's folder (MessageLog.txt), so loading a save brings back its own log. When off, " +
                "the log only lasts until you load or quit, and files already saved are left alone.");
            Storage.MaxNotifications = c.Bind("Storage", "MaxNotifications", 1000,
                new ConfigDescription("How many notifications each save keeps. The oldest go first.", new AcceptableValueRange<int>(100, 5000)));
            Storage.MaxDialogueLines = c.Bind("Storage", "MaxDialogueLines", 500,
                new ConfigDescription("How many conversation lines each save keeps. They're counted separately from " +
                    "notifications, so a long talk never pushes notifications out.", new AcceptableValueRange<int>(0, 3000)));
            Storage.MaxFileKB = c.Bind("Storage", "MaxFileKB", 1024,
                new ConfigDescription("The largest the log file of one save can get, in KB. Above it, the oldest messages go.",
                    new AcceptableValueRange<int>(128, 4096)));
            Storage.GapMarker = c.Bind("Storage", "GapMarker", true,
                "Add a line where messages are missing, for example after playing without the mod.");

            Window.Enabled = c.Bind("Window", "Enabled", true, "The Message Log window." + LaunchNote);
            Window.Search = c.Bind("Window", "Search", true,
                "The search box (keyboard only). While you type in it, the game ignores your keys." + LaunchNote);
            Window.TranslateWithPtBr = c.Bind("Window", "TranslateWithPtBr", true,
                "When the Obenseuer PT-BR translation is running, show messages the way it shows them in the game.");
            Window.UiScale = c.Bind("Window", "UiScale", 1f,
                new ConfigDescription("Size of the window.", new AcceptableValueRange<float>(0.75f, 2f)));

            Access.NotesPageKey = c.Bind("Access", "NotesPageKey", true,
                "On the journal's Notes page, Move Item (F, or X on a controller) opens the log." + LaunchNote);
            Access.PauseMenuEntry = c.Bind("Access", "PauseMenuEntry", false,
                "Add \"Message Log\" to the pause menu. Takes effect the next time the pause menu is closed.");
            Access.OpenKey = c.Bind("Access", "OpenKey", KeyboardShortcut.Empty,
                "A key that opens the log. None by default. Message Log ignores F1-F12, Insert, PageUp, PageDown, Home, End, " +
                "Delete, U, R, Left Alt, Shift, 1-9, the middle and side mouse buttons, controller buttons, the game's own keys " +
                "and our other mods' keys.");

            Hot.Refresh();
            CheckOpenKey();
            Access.OpenKey.SettingChanged += (s, a) => { CheckOpenKey(); Keys.Recheck(); };
        }

        // Keys the brief forbids (spec §9): our other mods, UnityExplorer, the console, screenshots, popular community mods,
        // the number row, U, R (Fair Market's Notes page key), Left Alt, Shift and the extra mouse buttons. Controller
        // buttons are refused in Keys; the D-pad is an axis in the legacy input manager, so it can't be bound at all.
        internal static bool Forbidden(KeyCode k) =>
            (k >= KeyCode.F1 && k <= KeyCode.F12) || (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) ||
            (k >= KeyCode.Keypad1 && k <= KeyCode.Keypad9) ||
            k == KeyCode.Insert || k == KeyCode.PageUp || k == KeyCode.PageDown || k == KeyCode.Home || k == KeyCode.End ||
            k == KeyCode.Delete || k == KeyCode.U || k == KeyCode.R || k == KeyCode.LeftAlt || k == KeyCode.LeftShift ||
            k == KeyCode.RightShift || k == KeyCode.Mouse2 || k == KeyCode.Mouse3 || k == KeyCode.Mouse4;

        private static void CheckOpenKey()
        {
            var k = Access.OpenKey.Value.MainKey;
            if (k != KeyCode.None && Forbidden(k))
                Log.Warn($"Keys: Access.OpenKey is {k}, which is reserved (another mod, the console, screenshots or the game); the key is ignored.");
        }
    }
}
