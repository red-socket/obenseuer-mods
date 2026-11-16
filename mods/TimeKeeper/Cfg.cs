using System;
using BepInEx.Configuration;
using UnityEngine;

namespace TimeKeeper
{
    internal enum WatchScope { WornWatchOnly, AnyWatchCarried, AnyClock }

    internal enum PadOpen { None, DPadUp, DPadDown }

    internal enum PadCancel { None, A, B, X, Y }

    // Every tunable value (spec §3). Entries are read live inside every patch body and helper, so turning a feature off
    // takes effect at once. A feature that was off at launch has no patches applied; it starts at the next launch
    // (LaunchNote). All description strings are public text (humanized 2026-11-16).
    internal static class Cfg
    {
        private const string LaunchNote = " If this was off when the game started, it takes effect at the next launch.";

        // Keys the game, our other mods or popular community mods already use (F1 console, F2/F9 community mods,
        // F3/F4/F6/F8 our own and the dev starter, F7 UnityExplorer, F10 TimeTravel, F11/F12 screenshots,
        // Insert/PageUp/PageDown Better Tenement, 1-4 the game's hand/backpack/head keys, 5-9 Fewer Clicks). Binding the
        // wait key to one of these only logs a warning: the player's choice stands. Real collisions with the game's own
        // bindings and hard-coded keys suspend the key instead (Keys.CheckCollisions).
        internal static readonly KeyCode[] Reserved =
        {
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10,
            KeyCode.F11, KeyCode.F12, KeyCode.Insert, KeyCode.PageUp, KeyCode.PageDown,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7,
            KeyCode.Alpha8, KeyCode.Alpha9,
        };

        internal static ConfigEntry<bool> VerboseLogging;

        internal static class Weekday
        {
            internal static ConfigEntry<bool> Enabled, ShareWithHudMods;
            internal static ConfigEntry<WatchScope> Scope;
        }

        internal static class Wait
        {
            internal static ConfigEntry<bool> Enabled, RequireSeated, RequireClock, AllowInJail, StopWhenHurt, Notify;
            internal static ConfigEntry<KeyboardShortcut> Hotkey, CancelKey;
            internal static ConfigEntry<PadOpen> PadButton;
            internal static ConfigEntry<PadCancel> CancelPadButton;
            internal static ConfigEntry<int> MaxHours, StepMinutes, DefaultMinutes, DangerSeconds;
            internal static ConfigEntry<float> SpeedFactor;
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Log every wait that opens, is refused, starts or ends, and every change of the watch that shows the weekday.");

            Weekday.Enabled = c.Bind("Weekday", "Enabled", true,
                "Every watch you wear shows the day of the week next to the time, not only the digital ones. Wall clocks " +
                "stay as they are, and you still need a watch to see the time." + LaunchNote);
            Weekday.Scope = c.Bind("Weekday", "Scope", WatchScope.WornWatchOnly,
                "WornWatchOnly: a watch in the watch slot. AnyWatchCarried: also a watch in your pockets while the inventory " +
                "is open. AnyClock: also an alarm clock in your pockets while the inventory is open. None of them show a " +
                "clock the game would hide.");
            Weekday.ShareWithHudMods = c.Bind("Weekday", "ShareWithHudMods", true,
                "Also tell the game that a worn watch is a calendar watch, so HUD mods that read the watch slot show the " +
                "weekday too. Turn this off if another HUD mod misbehaves." + LaunchNote);

            Wait.Enabled = c.Bind("Wait", "Enabled", true,
                "A key opens the game's sleep clock so you can pass time awake. Hunger, thirst, tiredness, timers, spoilage " +
                "and rent carry on as usual, and nothing rests or heals you the way sleep does." + LaunchNote);
            Wait.Hotkey = c.Bind("Wait", "Hotkey", new KeyboardShortcut(KeyCode.Home),
                "Opens the waiting clock. Press it again to start waiting, and again to stop.");
            Wait.CancelKey = c.Bind("Wait", "CancelKey", new KeyboardShortcut(KeyCode.Escape),
                "Stops a wait early. Time Keeper reads it only during a wait, when the game ignores this key.");
            Wait.PadButton = c.Bind("Wait", "PadButton", PadOpen.DPadUp,
                "Controller button that opens the waiting clock, starts the wait and stops it. In third person, while the " +
                "list of nearby objects is on screen, the D-pad scrolls that list instead.");
            Wait.CancelPadButton = c.Bind("Wait", "CancelPadButton", PadCancel.B,
                "Controller button that stops a wait early.");
            Wait.MaxHours = c.Bind("Wait", "MaxHours", 12,
                new ConfigDescription("Longest wait you can set, in hours.", new AcceptableValueRange<int>(1, 24)));
            Wait.StepMinutes = c.Bind("Wait", "StepMinutes", 15,
                new ConfigDescription("The duration changes in steps of this many minutes. The shortest wait is one step, " +
                    "and never less than 10 minutes, as in the game.", new AcceptableValueList<int>(5, 10, 15, 20, 30, 60)));
            Wait.DefaultMinutes = c.Bind("Wait", "DefaultMinutes", 60,
                new ConfigDescription("Duration the clock opens with. After your first wait it remembers the last one " +
                    "until you quit the game.", new AcceptableValueRange<int>(10, 1440)));
            Wait.SpeedFactor = c.Bind("Wait", "SpeedFactor", 0f,
                new ConfigDescription("How fast time runs while you wait. 0 uses the game's sleep and TV speed (150). It " +
                    "only changes how long you look at the screen, not what the wait costs. Values below 1 count as 0.",
                    new AcceptableValueRange<float>(0f, 150f)));
            Wait.RequireSeated = c.Bind("Wait", "RequireSeated", false, "Only allow waiting while you sit.");
            Wait.RequireClock = c.Bind("Wait", "RequireClock", false,
                "Only allow waiting while you wear a watch or carry a watch or alarm clock in your pockets (not the backpack).");
            Wait.AllowInJail = c.Bind("Wait", "AllowInJail", false,
                "Allow waiting in a jail cell. Your sentence runs down as time passes, as it does when you sleep there.");
            Wait.DangerSeconds = c.Bind("Wait", "DangerSeconds", 20,
                new ConfigDescription("After you are hurt by an attack, a fall, fire, gas or something similar, you can't " +
                    "start waiting for this many seconds. 0 turns this off.", new AcceptableValueRange<int>(0, 120)));
            Wait.StopWhenHurt = c.Bind("Wait", "StopWhenHurt", true,
                "Stop waiting as soon as you are hurt by an attack, a fall, fire, gas or something similar.");
            Wait.Notify = c.Bind("Wait", "Notify", true,
                "Show a short note when waiting is refused or stops early. The red warning flash always plays.");

            CheckReserved();
            Wait.Hotkey.SettingChanged += (s, a) => { CheckReserved(); Keys.Recheck(); };
            Wait.PadButton.SettingChanged += (s, a) => Keys.Recheck();
            Wait.Enabled.SettingChanged += (s, a) => Keys.Recheck();
        }

        internal static void CheckReserved()
        {
            var key = Wait.Hotkey.Value.MainKey;
            if (Array.IndexOf(Reserved, key) >= 0)
                Log.WarnOnce("reserved|" + key, $"Keys: Wait.Hotkey is bound to {key}, which the game or another popular mod already uses.");
        }
    }
}
