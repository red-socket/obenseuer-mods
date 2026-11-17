using BepInEx.Configuration;
using UnityEngine;

namespace VitalSigns
{
    // Every tunable value (spec §10). Entries marked LaunchNote gate a patch group at launch: turning them off works at
    // once (patch bodies read Hot), turning them on needs a restart. PauseMenuEntry has no patch and works both ways
    // live. Hot caches the bools that patch bodies read on hot paths and is refreshed on SettingChanged. Description
    // strings are public text: humanized 2026-11-17; run them through the humanizer again after any change.
    internal static class Cfg
    {
        private const string LaunchNote = " Restart the game after turning this on.";

        internal static ConfigEntry<bool> VerboseLog;
        internal static ConfigEntry<float> UiScale;

        internal static class DamageLog
        {
            internal static ConfigEntry<bool> Enabled, HealthTooltip;
            internal static ConfigEntry<int> MaxEntries, MergeMinutes;
        }

        internal static class Toasts
        {
            internal static ConfigEntry<bool> Enabled, HiddenOnly, WakeSummary, DeathSummary;
            internal static ConfigEntry<float> Threshold;
            internal static ConfigEntry<int> WindowMinutes, CooldownMinutes;
            internal static ConfigEntry<float> Seconds, DeathSeconds;
        }

        internal static class MentalHealth
        {
            internal static ConfigEntry<bool> Enabled, TooltipBreakdown, AlwaysShowIcon;
        }

        internal static class Addictions
        {
            internal static ConfigEntry<bool> TooltipEta;
        }

        internal static class Fixes
        {
            internal static ConfigEntry<bool> GamblingMoodTerm, ClampAddictions, ClampSmoking, ClampOnLoad, DuckCravingByTime;
        }

        internal static class Access
        {
            internal static ConfigEntry<bool> CharacterPageKey, PauseMenuEntry;
            internal static ConfigEntry<KeyboardShortcut> OpenKey;
        }

        // Cached switches read by patch bodies (spec §3). One field read per call instead of a ConfigEntry lookup.
        internal static class Hot
        {
            internal static bool Log, MoodOn, AlwaysShowIcon, GamblingFix, Clamp, ClampSmoking, ClampOnLoad, DuckFix, CharacterPageKey;
            internal static bool HealthTooltip, MoodTooltip, EtaTooltip;

            internal static void Refresh()
            {
                Log = DamageLog.Enabled.Value;
                MoodOn = MentalHealth.Enabled.Value;
                AlwaysShowIcon = MentalHealth.AlwaysShowIcon.Value;
                GamblingFix = Fixes.GamblingMoodTerm.Value;
                Clamp = Fixes.ClampAddictions.Value;
                ClampSmoking = Fixes.ClampSmoking.Value;
                ClampOnLoad = Fixes.ClampOnLoad.Value;
                DuckFix = Fixes.DuckCravingByTime.Value;
                CharacterPageKey = Access.CharacterPageKey.Value;
                HealthTooltip = DamageLog.HealthTooltip.Value;
                MoodTooltip = MentalHealth.TooltipBreakdown.Value;
                EtaTooltip = Addictions.TooltipEta.Value;
            }
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLog = c.Bind("General", "VerboseLog", false,
                "Write each damage log entry to the BepInEx log, along with corrections to the hidden damage tally, addiction corrections and the duck fix's extra steps (summed once per game hour).");
            UiScale = c.Bind("General", "UiScale", 1f,
                new ConfigDescription("Size of the Vital Signs window and its notes.", new AcceptableValueRange<float>(0.5f, 2f)));

            DamageLog.Enabled = c.Bind("DamageLog", "Enabled", true,
                "Record every health loss and its cause, including the ones the game doesn't show." + LaunchNote);
            DamageLog.MaxEntries = c.Bind("DamageLog", "MaxEntries", 300,
                new ConfigDescription("How many log lines to keep in memory.", new AcceptableValueRange<int>(50, 2000)));
            DamageLog.MergeMinutes = c.Bind("DamageLog", "MergeMinutes", 5,
                new ConfigDescription("Losses from the same cause within this many game minutes share one line.", new AcceptableValueRange<int>(1, 60)));
            DamageLog.HealthTooltip = c.Bind("DamageLog", "HealthTooltip", true,
                "Add hidden damage and your last loss to the Health tooltip.");

            Toasts.Enabled = c.Bind("Toasts", "Enabled", true, "Show short notes at the top right of the screen.");
            Toasts.Threshold = c.Bind("Toasts", "Threshold", 5f,
                new ConfigDescription("Health lost to one cause before you get a note.", new AcceptableValueRange<float>(1f, 100f)));
            Toasts.WindowMinutes = c.Bind("Toasts", "WindowMinutes", 60,
                new ConfigDescription("Game minutes the threshold counts over.", new AcceptableValueRange<int>(5, 1440)));
            Toasts.CooldownMinutes = c.Bind("Toasts", "CooldownMinutes", 60,
                new ConfigDescription("Game minutes before the same cause gets another note (never more than once a real minute).",
                    new AcceptableValueRange<int>(5, 1440)));
            Toasts.HiddenOnly = c.Bind("Toasts", "HiddenOnly", true,
                "Only give notes for losses the game shows no warning for.");
            Toasts.WakeSummary = c.Bind("Toasts", "WakeSummary", true,
                "Hold notes while you sleep or wait, then show one summary afterwards. When this is off, notes show as they happen.");
            Toasts.DeathSummary = c.Bind("Toasts", "DeathSummary", true, "When you die, show a note with what really caused it.");
            Toasts.Seconds = c.Bind("Toasts", "Seconds", 8f,
                new ConfigDescription("How long a note stays on screen, in real seconds.", new AcceptableValueRange<float>(3f, 30f)));
            Toasts.DeathSeconds = c.Bind("Toasts", "DeathSeconds", 20f,
                new ConfigDescription("How long the note about what killed you stays on screen, in real seconds.", new AcceptableValueRange<float>(3f, 60f)));

            MentalHealth.Enabled = c.Bind("MentalHealth", "Enabled", true,
                "Show the Mental health page and the exact breakdown of the mood on the HUD." + LaunchNote);
            MentalHealth.TooltipBreakdown = c.Bind("MentalHealth", "TooltipBreakdown", true,
                "Add a short breakdown to the Mental health tooltip.");
            MentalHealth.AlwaysShowIcon = c.Bind("MentalHealth", "AlwaysShowIcon", false,
                "Keep the Mental health icon on the HUD even when your mood is good. The game then also shows its one-time " +
                "mental health hint, the LT stats label in the inventory and the icon in the waiting overlay." + LaunchNote);

            Addictions.TooltipEta = c.Bind("Addictions", "TooltipEta", true,
                "Add the addiction level and the time until it wears off to the need tooltips.");

            Fixes.GamblingMoodTerm = c.Bind("Fixes", "GamblingMoodTerm", true,
                "Fix a game bug: your gambling need only counts toward unmet needs while you also crave smokes." + LaunchNote);
            Fixes.ClampAddictions = c.Bind("Fixes", "ClampAddictions", true,
                "Keep alcohol and mushroom addiction between 0 and 100." + LaunchNote);
            Fixes.ClampSmoking = c.Bind("Fixes", "ClampSmoking", false,
                "Also keep smoking addiction at or below 100. The game caps it, then adds some items' addiction on top " +
                "(a Vape Liquid can leave it at 125)." + LaunchNote);
            Fixes.ClampOnLoad = c.Bind("Fixes", "ClampOnLoad", true,
                "Also correct addiction values that are already outside 0-100 when a save loads, for the addictions the two " +
                "settings above cover." + LaunchNote);
            Fixes.DuckCravingByTime = c.Bind("Fixes", "DuckCravingByTime", true,
                "Make duck craving follow game time while you're awake, so the game's fast waits count like normal time and your " +
                "frame rate doesn't matter. Sleep is unchanged." + LaunchNote);

            Access.CharacterPageKey = c.Bind("Access", "CharacterPageKey", true,
                "On the Character page, Use Item (X, or A on a controller) opens Vital Signs." + LaunchNote);
            Access.PauseMenuEntry = c.Bind("Access", "PauseMenuEntry", false,
                "Add \"Vital Signs\" to the pause menu. Takes effect the next time the pause menu is closed.");
            Access.OpenKey = c.Bind("Access", "OpenKey", KeyboardShortcut.Empty,
                "A key that opens Vital Signs. None by default. Vital Signs ignores F1-F12, Insert, PageUp, PageDown, Home, End, " +
                "Delete, U, Left Alt, 1-9, the middle and side mouse buttons, the game's own keys and our other mods' keys.");

            Hot.Refresh();
            c.SettingChanged += (s, a) => Hot.Refresh();
            CheckOpenKey();
            Access.OpenKey.SettingChanged += (s, a) => { CheckOpenKey(); Keys.Recheck(); };
        }

        // Keys the brief forbids (spec §1 D3): our other mods, UnityExplorer, the console, screenshots, popular community
        // mods, the number row, U, Left Alt and the extra mouse buttons. The D-pad is an axis in the legacy input manager,
        // so it can't be bound here at all. A forbidden key is ignored (Keys.Down), not just warned about.
        internal static bool Forbidden(KeyCode k) =>
            (k >= KeyCode.F1 && k <= KeyCode.F12) || (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) ||
            k == KeyCode.Insert || k == KeyCode.PageUp || k == KeyCode.PageDown || k == KeyCode.Home || k == KeyCode.End ||
            k == KeyCode.Delete || k == KeyCode.U || k == KeyCode.LeftAlt || k == KeyCode.Mouse2 || k == KeyCode.Mouse3 || k == KeyCode.Mouse4;

        private static void CheckOpenKey()
        {
            var k = Access.OpenKey.Value.MainKey;
            if (k != KeyCode.None && Forbidden(k))
                Log.Warn($"Keys: Access.OpenKey is {k}, which is reserved (another mod, the console, screenshots or the game); the key is ignored.");
        }
    }
}
