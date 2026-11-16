using System;
using BepInEx.Configuration;
using UnityEngine;

namespace SafeTravels
{
    // Every tunable value (spec §5). Entries are read live inside every patch body and helper, so turning a feature off
    // takes effect at once. A patched feature that was off at launch has no patches applied; it starts at the next launch
    // (LaunchNote). Unstuck and Rerail patch nothing, so they follow their Enabled entry live. All description strings
    // are public text (humanized 2026-11-16).
    internal static class Cfg
    {
        private const string LaunchNote = " If this was off when the game started, it takes effect at the next launch.";

        // Keys the game, our other mods or popular community mods already use (F1 console, F2/F9 community mods,
        // F3/F4/F6/F10 our own mods and F8 the dev starter, F5 listed by Save Guard and Better Tenement, F7 UnityExplorer,
        // F11/F12 screenshots, Insert/PageUp/PageDown Better Tenement, Home Time Keeper, 1-4 the game's hand/backpack/head
        // keys, 5-9 Fewer Clicks). Binding one of our keys to one of these only logs a warning: the player's choice stands.
        // Real collisions suspend the key instead (Keys.CheckCollisions).
        internal static readonly KeyCode[] Reserved =
        {
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9,
            KeyCode.F10, KeyCode.F11, KeyCode.F12, KeyCode.Insert, KeyCode.PageUp, KeyCode.PageDown, KeyCode.Home,
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7,
            KeyCode.Alpha8, KeyCode.Alpha9,
        };

        internal static ConfigEntry<bool> VerboseLogging;

        internal static class Toll
        {
            internal static ConfigEntry<bool> Enabled;
            internal static ConfigEntry<float> LockSeconds;
            internal static ConfigEntry<int> Price;
        }

        internal static class Unstuck
        {
            internal static ConfigEntry<bool> Enabled, PauseMenuButton, EntrypointFallback;
            internal static ConfigEntry<KeyboardShortcut> Hotkey;
            internal static ConfigEntry<float> CooldownSeconds, MaxAgeSeconds, MaxDistance, MinDistance, SampleSeconds, EntrypointMaxDistance;
        }

        internal static class Coupling
        {
            internal static ConfigEntry<bool> Enabled, RequireDriving;
            internal static ConfigEntry<float> MaxSpeed, CoastSeconds, LoadGraceSeconds;
        }

        internal static class Decoupling
        {
            internal static ConfigEntry<bool> Enabled;
            internal static ConfigEntry<int> GraceFrames;
            internal static ConfigEntry<float> GraceSeconds, HardBreakFactor;
        }

        internal static class Consist
        {
            internal static ConfigEntry<bool> Enabled;
        }

        internal static class Rerail
        {
            internal static ConfigEntry<bool> Enabled, PauseMenuButton, AllowWagons;
            internal static ConfigEntry<KeyboardShortcut> Hotkey;
            internal static ConfigEntry<float> MaxDistance, CooldownSeconds;
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Log refused presses, recorded spots, coupling decisions, held gaps and consist checks.");

            Toll.Enabled = c.Bind("Toll", "Enabled", true,
                "After you pay at the Tenement toll bridge, the coin slot takes no more money until you've walked through " +
                "or the gate has reset. Its prompt shows that you've paid. Other coin slots don't change." + LaunchNote);
            Toll.LockSeconds = c.Bind("Toll", "LockSeconds", 3f,
                new ConfigDescription("The slot also refuses coins for this many seconds after you pay, even if the gate " +
                    "can't be found.", new AcceptableValueRange<float>(0f, 10f)));
            Toll.Price = c.Bind("Toll", "Price", -1,
                new ConfigDescription("Toll in OC. -1 keeps the game's price (10).", new AcceptableValueRange<int>(-1, 999)));

            Unstuck.Enabled = c.Bind("Unstuck", "Enabled", true,
                "Moves you back to a spot where you recently stood safely, or to an entrance close by. It doesn't work in " +
                "jail, with the police after you, while falling, seated, on a ladder or on the minitrain, or in dialogue, " +
                "cutscenes, minigames, waits or loading screens.");
            Unstuck.Hotkey = c.Bind("Unstuck", "Hotkey", KeyboardShortcut.Empty,
                "Key for Unstuck. Empty by default, since the pause menu has a button.");
            Unstuck.PauseMenuButton = c.Bind("Unstuck", "PauseMenuButton", true,
                "Adds \"Get Unstuck\" to the pause menu. It works with a controller.");
            Unstuck.CooldownSeconds = c.Bind("Unstuck", "CooldownSeconds", 60f,
                new ConfigDescription("Seconds between two uses. Time in the pause menu doesn't count.", new AcceptableValueRange<float>(10f, 600f)));
            Unstuck.MaxAgeSeconds = c.Bind("Unstuck", "MaxAgeSeconds", 180f,
                new ConfigDescription("Spots older than this many seconds are ignored. Time in the pause menu doesn't count.",
                    new AcceptableValueRange<float>(30f, 900f)));
            Unstuck.MaxDistance = c.Bind("Unstuck", "MaxDistance", 30f,
                new ConfigDescription("Spots further away than this (in metres) are ignored.", new AcceptableValueRange<float>(5f, 100f)));
            Unstuck.MinDistance = c.Bind("Unstuck", "MinDistance", 2f,
                new ConfigDescription("Spots closer than this (in metres) are skipped, since you're probably stuck there.",
                    new AcceptableValueRange<float>(0.5f, 10f)));
            Unstuck.SampleSeconds = c.Bind("Unstuck", "SampleSeconds", 1f,
                new ConfigDescription("How often a safe spot is recorded, in seconds.", new AcceptableValueRange<float>(0.5f, 5f)));
            Unstuck.EntrypointFallback = c.Bind("Unstuck", "EntrypointFallback", true,
                "With no recent spot, use the nearest entrance within EntrypointMaxDistance. It never picks the jail, or an " +
                "entrance inside a closed shop or private area.");
            Unstuck.EntrypointMaxDistance = c.Bind("Unstuck", "EntrypointMaxDistance", 20f,
                new ConfigDescription("Entrances further away than this (in metres) are never used.", new AcceptableValueRange<float>(5f, 50f)));

            Coupling.Enabled = c.Bind("Coupling", "Enabled", true,
                "Trains only couple on slow contact that you cause: while you drive one, shortly after you step off, or " +
                "when you enable a coupler while it touches another train. Fast reversing and loading an area no longer " +
                "couple trains." + LaunchNote);
            Coupling.MaxSpeed = c.Bind("Coupling", "MaxSpeed", 0f,
                new ConfigDescription("Coupling speed limit in m/s, in both directions. 0 uses the game's limit (2.5).",
                    new AcceptableValueRange<float>(0f, 12f)));
            Coupling.RequireDriving = c.Bind("Coupling", "RequireDriving", true,
                "Turn this off to keep only the speed and loading fixes.");
            Coupling.CoastSeconds = c.Bind("Coupling", "CoastSeconds", 5f,
                new ConfigDescription("After you step off, how many seconds your train can still couple by rolling into another.",
                    new AcceptableValueRange<float>(0f, 30f)));
            Coupling.LoadGraceSeconds = c.Bind("Coupling", "LoadGraceSeconds", 3f,
                new ConfigDescription("No automatic coupling for this many seconds after an area has loaded.",
                    new AcceptableValueRange<float>(0f, 10f)));

            Decoupling.Enabled = c.Bind("Decoupling", "Enabled", true,
                "A coupling only breaks when the gap stays too wide for a moment, not on one bad frame." + LaunchNote);
            Decoupling.GraceFrames = c.Bind("Decoupling", "GraceFrames", 5,
                new ConfigDescription("How many frames in a row the gap must stay too wide.", new AcceptableValueRange<int>(1, 60)));
            Decoupling.GraceSeconds = c.Bind("Decoupling", "GraceSeconds", 0.2f,
                new ConfigDescription("The gap must also stay too wide for at least this many seconds.",
                    new AcceptableValueRange<float>(0f, 2f)));
            Decoupling.HardBreakFactor = c.Bind("Decoupling", "HardBreakFactor", 2f,
                new ConfigDescription("A gap this many times the limit breaks the coupling at once.",
                    new AcceptableValueRange<float>(1.2f, 5f)));

            Consist.Enabled = c.Bind("Consist", "Enabled", true,
                "After a load, a coupling or an uncoupling, repairs trains whose wagon list no longer matches their " +
                "couplings, using the game's own rebuild." + LaunchNote);

            Rerail.Enabled = c.Bind("Rerail", "Enabled", true,
                "A key puts a locomotive back on its rails where it stands, using the game's own reset. Its wagons are " +
                "uncoupled first and stay where they are. The train has to stand still with its engine off.");
            Rerail.Hotkey = c.Bind("Rerail", "Hotkey", new KeyboardShortcut(KeyCode.End),
                "Rerails the locomotive you're looking at, or else the last one you drove or the nearest one in range.");
            Rerail.PauseMenuButton = c.Bind("Rerail", "PauseMenuButton", false,
                "Adds \"Rerail Train\" to the pause menu, so you can rerail with a controller.");
            Rerail.AllowWagons = c.Bind("Rerail", "AllowWagons", false,
                "Also rerail wagons. Their cargo moves with them.");
            Rerail.MaxDistance = c.Bind("Rerail", "MaxDistance", 15f,
                new ConfigDescription("Range in metres for a train you're not looking at.", new AcceptableValueRange<float>(3f, 30f)));
            Rerail.CooldownSeconds = c.Bind("Rerail", "CooldownSeconds", 10f,
                new ConfigDescription("Real seconds between two rerails.", new AcceptableValueRange<float>(0f, 120f)));

            CheckReserved(Unstuck.Hotkey);
            CheckReserved(Rerail.Hotkey);
            Toll.Price.SettingChanged += (s, a) => Plugin.Step("toll price", TollSlot.ApplyPrice);
            foreach (var e in new[] { Unstuck.Hotkey, Rerail.Hotkey })
            {
                var entry = e;
                entry.SettingChanged += (s, a) => { CheckReserved(entry); Keys.Recheck(); };
            }
            foreach (var e in new[] { Unstuck.Enabled, Rerail.Enabled })
                e.SettingChanged += (s, a) => { Keys.Recheck(); PauseButtons.Schedule(); };
            foreach (var e in new[] { Unstuck.PauseMenuButton, Rerail.PauseMenuButton })
                e.SettingChanged += (s, a) => PauseButtons.Schedule();
        }

        private static void CheckReserved(ConfigEntry<KeyboardShortcut> e)
        {
            var key = e.Value.MainKey;
            if (key != KeyCode.None && Array.IndexOf(Reserved, key) >= 0)
                Log.WarnOnce($"reserved|{e.Definition.Section}|{key}",
                    $"Keys: {e.Definition.Section}.{e.Definition.Key} is bound to {key}, which the game or another popular mod already uses.");
        }
    }
}
