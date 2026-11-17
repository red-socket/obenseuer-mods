using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace QuestHelper
{
    internal enum PinFollow { SelectedTask, LastUpdated }

    // Every tunable value (spec §9). Entries are read live wherever they are used; a feature that was off at launch has no
    // patches applied and starts at the next launch (LaunchNote). All description strings are public text (humanized
    // 2026-11-17).
    internal static class Cfg
    {
        private const string LaunchNote = " Restart the game after changing this.";

        internal static ConfigEntry<bool> VerboseLog;

        internal static class QuestDoctor
        {
            internal static ConfigEntry<bool> Enabled, TaskTabKey, NotifyWhenStuck, AllowGenericEdits, UseSaveGuard;
            internal static ConfigEntry<KeyboardShortcut> OpenKey;
            internal static ConfigEntry<string> DisabledFixes, BackupFolder;
            internal static ConfigEntry<int> KeepOwnBackups, MinFreeSpaceMB;
            internal static ConfigEntry<float> ConfirmSeconds, UiScale;
        }

        internal static class Journal
        {
            internal static ConfigEntry<bool> Enabled, RecordWhereReceived, ShowWhereReceived;
            internal static ConfigEntry<float> NearRadius;
            internal static ConfigEntry<int> MaxRecords;
        }

        internal static class Hints
        {
            internal static ConfigEntry<int> SpoilerTier;
            internal static ConfigEntry<bool> ShowBlockers;
        }

        internal static class MapPin
        {
            internal static ConfigEntry<bool> Enabled, PinWhereReceived;
            internal static ConfigEntry<PinFollow> Follows;
            internal static ConfigEntry<int> PinSize;
        }

        internal static class ToBeContinued
        {
            internal static ConfigEntry<bool> Enabled, Collapsed, ClearerLabel, HideLabelWhenNotActive, Notice;
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLog = c.Bind("General", "VerboseLog", false,
                "Log every check for stuck quests, every recorded objective, map pin target and task list change.");

            QuestDoctor.Enabled = c.Bind("QuestDoctor", "Enabled", true,
                "Adds \"Quest Helper\" to the pause menu: a window that lists your tasks and fixes a few known stuck quests." + LaunchNote);
            QuestDoctor.TaskTabKey = c.Bind("QuestDoctor", "TaskTabKey", true,
                "On the Tasks page, the Use Item key (X, or A on a controller) opens Quest Helper on the selected task.");
            QuestDoctor.OpenKey = c.Bind("QuestDoctor", "OpenKey", KeyboardShortcut.Empty,
                "Optional key that opens Quest Helper during play. None by default. F1-F12, Insert, PageUp, PageDown, Home, End, " +
                "Delete, U, Left Alt, 1-9, the middle and side mouse buttons and the game's own keys are refused.");
            QuestDoctor.NotifyWhenStuck = c.Bind("QuestDoctor", "NotifyWhenStuck", true,
                "After loading, show a short note when Quest Helper finds a stuck quest (once per session).");
            QuestDoctor.DisabledFixes = c.Bind("QuestDoctor", "DisabledFixes", "",
                "Fix IDs to hide, comma-separated (QD-RAT, QD-CASINO, QD-TOIVO, QD-PERMIT, QD-ELIANA).");
            QuestDoctor.AllowGenericEdits = c.Bind("QuestDoctor", "AllowGenericEdits", false,
                "ADVANCED. Lets you finish, fail or abandon any active task or objective from the task list. That runs the game's " +
                "own follow-up steps and can block later quests. A backup is still made first.");
            QuestDoctor.UseSaveGuard = c.Bind("QuestDoctor", "UseSaveGuard", true,
                "When Save Guard is installed, put the backup in its restore list. It counts toward Save Guard's limit for " +
                "set-aside saves, so an old one may be removed.");
            QuestDoctor.BackupFolder = c.Bind("QuestDoctor", "BackupFolder", "",
                "Folder for Quest Helper's own backups. Empty means <game data>/QuestHelper/backups. A folder inside Saves is refused.");
            QuestDoctor.KeepOwnBackups = c.Bind("QuestDoctor", "KeepOwnBackups", 10,
                new ConfigDescription("How many of Quest Helper's own backups to keep per character.", new AcceptableValueRange<int>(1, 50)));
            QuestDoctor.MinFreeSpaceMB = c.Bind("QuestDoctor", "MinFreeSpaceMB", 200,
                new ConfigDescription("Don't make a backup that would leave less free disk space than this, in MB.", new AcceptableValueRange<int>(0, 10000)));
            QuestDoctor.ConfirmSeconds = c.Bind("QuestDoctor", "ConfirmSeconds", 6f,
                new ConfigDescription("How long you have to press Apply a second time.", new AcceptableValueRange<float>(2f, 15f)));
            QuestDoctor.UiScale = c.Bind("QuestDoctor", "UiScale", 1f,
                new ConfigDescription("Size of the Quest Helper window.", new AcceptableValueRange<float>(0.5f, 2f)));

            Journal.Enabled = c.Bind("Journal", "Enabled", true,
                "Extra lines under objectives in the task list: where an objective came from, and the hints below." + LaunchNote);
            Journal.RecordWhereReceived = c.Bind("Journal", "RecordWhereReceived", true,
                "Remember where, and from whom, each objective came. This is one small text entry in your save.");
            Journal.ShowWhereReceived = c.Bind("Journal", "ShowWhereReceived", true, "Show the \"Received from\" line.");
            Journal.NearRadius = c.Bind("Journal", "NearRadius", 40f,
                new ConfigDescription("Name the nearest place you've found on the map within this many metres.", new AcceptableValueRange<float>(5f, 150f)));
            Journal.MaxRecords = c.Bind("Journal", "MaxRecords", 300,
                new ConfigDescription("Most objectives remembered per save.", new AcceptableValueRange<int>(50, 2000)));

            Hints.SpoilerTier = c.Bind("Hints", "SpoilerTier", 0,
                new ConfigDescription("How much the hints tell you. 0: nothing beyond the game's own text. 1: nudges and what you're missing. " +
                    "2: who and where, with map pins. 3: step by step. You can also change this in the Quest Helper window.",
                    new AcceptableValueRange<int>(0, 3)));
            Hints.ShowBlockers = c.Bind("Hints", "ShowBlockers", true,
                "From hint level 1, list fees and items an objective needs, marked as had or missing.");

            MapPin.Enabled = c.Bind("MapPin", "Enabled", true, "Shows one map pin for the task you're following. It is never saved." + LaunchNote);
            MapPin.Follows = c.Bind("MapPin", "Follows", PinFollow.SelectedTask,
                "SelectedTask: the task you last opened in the task list. LastUpdated: the task that changed most recently.");
            MapPin.PinWhereReceived = c.Bind("MapPin", "PinWhereReceived", true,
                "When there's no hint data for an objective, pin the spot where you got it.");
            MapPin.PinSize = c.Bind("MapPin", "PinSize", 26,
                new ConfigDescription("Pin size in pixels.", new AcceptableValueRange<int>(12, 64)));

            ToBeContinued.Enabled = c.Bind("ToBeContinued", "Enabled", true,
                "Puts tasks that have reached the current end of their story into their own \"To be continued\" section of the task list." + LaunchNote);
            ToBeContinued.Collapsed = c.Bind("ToBeContinued", "Collapsed", false, "Whether that section is folded up.");
            ToBeContinued.ClearerLabel = c.Bind("ToBeContinued", "ClearerLabel", true,
                "Replace the game's \"cannot be completed\" note with \"To be continued in a future update\".");
            ToBeContinued.HideLabelWhenNotActive = c.Bind("ToBeContinued", "HideLabelWhenNotActive", true,
                "Show that note only on objectives that are still active.");
            ToBeContinued.Notice = c.Bind("ToBeContinued", "Notice", true, "Show a short note when a story reaches its current end.");

            CheckOpenKey();
            QuestDoctor.OpenKey.SettingChanged += (s, a) => { CheckOpenKey(); Keys.Recheck(); };
        }

        // Keys the brief forbids (spec §9): our other mods, UnityExplorer, the console, screenshots, popular community
        // mods, the number row, U, Left Alt and the extra mouse buttons. The D-pad is an axis in the legacy input manager,
        // so it cannot be bound here at all. A forbidden key is ignored (Keys.Suspended), not just warned about.
        internal static bool Forbidden(KeyCode k) =>
            (k >= KeyCode.F1 && k <= KeyCode.F12) || (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) ||
            k == KeyCode.Insert || k == KeyCode.PageUp || k == KeyCode.PageDown || k == KeyCode.Home || k == KeyCode.End ||
            k == KeyCode.Delete || k == KeyCode.U || k == KeyCode.LeftAlt || k == KeyCode.Mouse2 || k == KeyCode.Mouse3 || k == KeyCode.Mouse4;

        private static void CheckOpenKey()
        {
            var k = QuestDoctor.OpenKey.Value.MainKey;
            if (k != KeyCode.None && Forbidden(k))
                Log.Warn($"Keys: QuestDoctor.OpenKey is {k}, which is reserved (another mod, the console, screenshots or the game); the key is ignored.");
        }

        // Comma-separated list entries, parsed once per value.
        private static string _listRaw;
        private static HashSet<string> _list;

        internal static HashSet<string> DisabledFixes()
        {
            string raw = QuestDoctor.DisabledFixes.Value ?? "";
            if (_list != null && raw == _listRaw) return _list;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in raw.Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0) set.Add(t);
            }
            _listRaw = raw;
            _list = set;
            return set;
        }

        internal static int Tier => Math.Max(0, Math.Min(3, Hints.SpoilerTier.Value));
    }
}
