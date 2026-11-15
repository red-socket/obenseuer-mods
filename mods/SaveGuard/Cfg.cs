using System;
using BepInEx.Configuration;
using UnityEngine;

namespace SaveGuard
{
    internal enum TimedMode { Off, RealMinutes, GameHours }

    // Every tunable value (spec §10). Entries are read live wherever they are used.
    internal static class Cfg
    {
        internal static ConfigEntry<bool> VerboseLogging;
        internal static ConfigEntry<string> BackupFolder;

        internal static ConfigEntry<bool> CrashSafety;
        internal static ConfigEntry<bool> RollBackInterruptedSaves;
        internal static ConfigEntry<bool> BlockLoadingDamagedSaves;
        internal static ConfigEntry<int> RecoveredToKeepPerCharacter;
        internal static ConfigEntry<int> ReplacedToKeepPerCharacter;

        internal static ConfigEntry<bool> BackupsEnabled;
        internal static ConfigEntry<int> KeepPerCharacter;
        internal static ConfigEntry<int> MinIntervalMinutes;
        internal static ConfigEntry<bool> AlwaysBackupManualSaves;
        internal static ConfigEntry<int> MaxTotalSizeMB;
        internal static ConfigEntry<int> MinFreeSpaceMB;

        internal static ConfigEntry<bool> PreUpdateEnabled;
        internal static ConfigEntry<bool> BackupOnFirstRun;
        internal static ConfigEntry<int> KeepSets;

        internal static ConfigEntry<TimedMode> Mode;
        internal static ConfigEntry<int> RealMinutes;
        internal static ConfigEntry<int> GameHours;
        internal static ConfigEntry<bool> FollowDifficultySetting;
        internal static ConfigEntry<int> QuietSeconds;
        internal static ConfigEntry<int> GraceSecondsAfterLoad;
        internal static ConfigEntry<bool> ShowSavingOverlay;
        internal static ConfigEntry<bool> RespectNoSaveDifficulty;
        internal static ConfigEntry<bool> AllowInPrison;
        internal static ConfigEntry<bool> AllowWhileSeated;

        internal static ConfigEntry<bool> NotifyOnBackup;
        internal static ConfigEntry<bool> NotifyOnTimedAutosave;

        internal static ConfigEntry<KeyboardShortcut> ToggleWindow;
        internal static ConfigEntry<float> UiScale;

        // Keys taken by the game (F1 console, F11/F12 screenshots) or popular mods (F2, F5, F9, F7 UnityExplorer).
        private static readonly KeyCode[] Reserved = { KeyCode.F1, KeyCode.F2, KeyCode.F5, KeyCode.F7, KeyCode.F9, KeyCode.F11, KeyCode.F12 };

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false, "Log every save transaction, timed autosave check, timing and prune step.");
            BackupFolder = c.Bind("General", "BackupFolder", "",
                "Folder for backups and pre-update sets. Empty means <LocalLow>/Loiste Interactive/Obenseuer/SaveGuard. A folder inside Saves is not allowed. Takes effect at the next launch.");

            CrashSafety = c.Bind("CrashSafety", "Enabled", true,
                "Keep the previous version of a slot until its new save is fully written; roll back if the save fails.");
            RollBackInterruptedSaves = c.Bind("CrashSafety", "RollBackInterruptedSaves", true,
                "At startup, if a save was interrupted and is incomplete, put the previous version back (the incomplete one is kept in the recovered list). If false, only report it.");
            BlockLoadingDamagedSaves = c.Bind("CrashSafety", "BlockLoadingDamagedSaves", true,
                "Ask before loading a slot that looks damaged (it would fail to load, or some areas or conversations may reset). " +
                "The prompt offers \"Load anyway\" and Cancel, and from the main menu also restoring the newest good copy.");
            RecoveredToKeepPerCharacter = c.Bind("CrashSafety", "RecoveredToKeepPerCharacter", 5,
                new ConfigDescription("How many recovered entries from failed or interrupted saves (\"failed\" and \"version before the last save\") to keep per character.", new AcceptableValueRange<int>(1, 50)));
            ReplacedToKeepPerCharacter = c.Bind("CrashSafety", "ReplacedToKeepPerCharacter", 10,
                new ConfigDescription("How many slots replaced by a restore to keep per character. They are counted separately, so a restore can always be undone. The restore window warns before one is removed.", new AcceptableValueRange<int>(1, 100)));

            BackupsEnabled = c.Bind("Backups", "Enabled", true, "Copy a slot outside Saves/ after it is saved.");
            KeepPerCharacter = c.Bind("Backups", "KeepPerCharacter", 10,
                new ConfigDescription("How many rotating backups to keep per character.", new AcceptableValueRange<int>(1, 100)));
            MinIntervalMinutes = c.Bind("Backups", "MinIntervalMinutes", 10,
                new ConfigDescription("Minimum real minutes between automatic backups of one character (0 = every save).", new AcceptableValueRange<int>(0, 1440)));
            AlwaysBackupManualSaves = c.Bind("Backups", "AlwaysBackupManualSaves", true, "Back up saves into Slot1-Slot10 even within the interval.");
            MaxTotalSizeMB = c.Bind("Backups", "MaxTotalSizeMB", 4096,
                new ConfigDescription("Size limit for backups plus pre-update sets (0 = no limit). The oldest are removed first.", new AcceptableValueRange<int>(0, 1000000)));
            MinFreeSpaceMB = c.Bind("Backups", "MinFreeSpaceMB", 1024,
                new ConfigDescription("Skip a backup if less than this much disk space would stay free.", new AcceptableValueRange<int>(0, 100000)));

            PreUpdateEnabled = c.Bind("PreUpdate", "Enabled", true, "Back up all saves the first time a new game version starts.");
            BackupOnFirstRun = c.Bind("PreUpdate", "BackupOnFirstRun", true, "Also back up all saves the first time Save Guard runs.");
            KeepSets = c.Bind("PreUpdate", "KeepSets", 2,
                new ConfigDescription("How many pre-update and first-run sets to keep.", new AcceptableValueRange<int>(1, 20)));

            Mode = c.Bind("TimedAutosave", "Mode", TimedMode.Off, "Extra autosaves into the vanilla Autosave 1/2 rotation.");
            RealMinutes = c.Bind("TimedAutosave", "RealMinutes", 20,
                new ConfigDescription("RealMinutes mode: save after this many minutes of active, unpaused play without any save.", new AcceptableValueRange<int>(5, 240)));
            GameHours = c.Bind("TimedAutosave", "GameHours", 6,
                new ConfigDescription("GameHours mode: save after this many in-game hours without any save (1 game hour = 4 real minutes in 0.5.19).", new AcceptableValueRange<int>(1, 72)));
            FollowDifficultySetting = c.Bind("TimedAutosave", "FollowDifficultySetting", true,
                "If the difficulty's (normally hidden) autosave mode is Every Hour or Every Day, use it instead of Mode.");
            QuietSeconds = c.Bind("TimedAutosave", "QuietSeconds", 3,
                new ConfigDescription("A timed autosave waits until nothing has blocked it for this many seconds.", new AcceptableValueRange<int>(0, 30)));
            GraceSecondsAfterLoad = c.Bind("TimedAutosave", "GraceSecondsAfterLoad", 15,
                new ConfigDescription("No timed autosave this soon after a load or area change.", new AcceptableValueRange<int>(0, 120)));
            ShowSavingOverlay = c.Bind("TimedAutosave", "ShowSavingOverlay", true, "Show the vanilla full-screen saving overlay, like the game's own autosaves.");
            RespectNoSaveDifficulty = c.Bind("TimedAutosave", "RespectNoSaveDifficulty", true, "No timed autosaves when the difficulty disables saving.");
            AllowInPrison = c.Bind("TimedAutosave", "AllowInPrison", false, "Allow timed autosaves inside the prison.");
            AllowWhileSeated = c.Bind("TimedAutosave", "AllowWhileSeated", false, "Allow timed autosaves while sitting, on a ladder or riding the mini train.");

            NotifyOnBackup = c.Bind("Notifications", "NotifyOnBackup", false, "Show a message after each backup.");
            NotifyOnTimedAutosave = c.Bind("Notifications", "NotifyOnTimedAutosave", true, "Show \"Autosaved\" after a timed autosave. Failures are always shown.");

            ToggleWindow = c.Bind("UI", "ToggleWindow", new KeyboardShortcut(KeyCode.F6),
                "Main menu: open the Save Guard window. In game: show backup status.");
            UiScale = c.Bind("UI", "Scale", 1.0f,
                new ConfigDescription("Size of Save Guard windows.", new AcceptableValueRange<float>(0.5f, 2.5f)));

            CheckHotkey();
            ToggleWindow.SettingChanged += (s, e) => CheckHotkey();

            EventHandler reset = (s, e) => TimedAutosave.ResetScheduler("config changed");
            Mode.SettingChanged += reset;
            RealMinutes.SettingChanged += reset;
            GameHours.SettingChanged += reset;
            FollowDifficultySetting.SettingChanged += reset;
            QuietSeconds.SettingChanged += reset;
            GraceSecondsAfterLoad.SettingChanged += reset;
            ShowSavingOverlay.SettingChanged += reset;
            RespectNoSaveDifficulty.SettingChanged += reset;
            AllowInPrison.SettingChanged += reset;
            AllowWhileSeated.SettingChanged += reset;

#if SAVEGUARD_FAULTS
            Faults.Bind(c);
#endif
        }

        private static void CheckHotkey()
        {
            var key = ToggleWindow.Value.MainKey;
            if (Array.IndexOf(Reserved, key) >= 0)
                Log.Warn($"UI.ToggleWindow is bound to {key}, which the game or another popular mod already uses.");
        }
    }
}
