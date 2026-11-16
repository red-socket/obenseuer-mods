using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace Workshop
{
    internal enum VesselTarget { FullBatch, OneMore }

    internal enum CancelScopeMode { DishwasherOnly, AllManufacturing }

    // Every tunable value (spec §8). Entries are read live inside every patch body and helper, so turning a feature off
    // takes effect at once. A feature that was off at launch has no patches applied; it starts at the next launch
    // (LaunchNote). All description strings are public text (humanized 2026-11-16).
    internal static class Cfg
    {
        private const string LaunchNote = " If this was off when the game started, it takes effect at the next launch.";

        // Keys our other mods or popular community mods already use (F2/F9 community mods, F3/F4/F6/F8 our own and the dev
        // starter, F7 UnityExplorer, F10, F11/F12 screenshots, Insert/PageUp/PageDown/Home/End/Delete our other mods, the
        // middle mouse button Tidy Inventory's pin key). Binding one of these only logs a warning: the player's choice
        // stands. Real clashes are found by Keys.CheckCollisions.
        private static readonly KeyCode[] Reserved =
        {
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10,
            KeyCode.F11, KeyCode.F12, KeyCode.Insert, KeyCode.PageUp, KeyCode.PageDown, KeyCode.Home, KeyCode.End, KeyCode.Delete,
            KeyCode.Mouse2,
        };

        internal static ConfigEntry<bool> VerboseLogging;

        internal static class Fetch
        {
            internal static ConfigEntry<bool> Enabled, UseWornBag, UseNearbyStorage, OnlyPlacedFurniture, SameAreaOnly, AllowPermittedStacks,
                OldestFirst, AllowPartial, ShowSummary;
            internal static ConfigEntry<float> Range, MaxHeightDifference;
            internal static ConfigEntry<VesselTarget> VesselTarget;
        }

        internal static class Queue
        {
            internal static ConfigEntry<bool> Enabled, RepeatButton, LookHint, AutoRun;
            internal static ConfigEntry<KeyboardShortcut> RepeatKey;
            internal static ConfigEntry<string> RepeatSteps, AutoRunMachines, AutoRunRecipes;
            internal static ConfigEntry<float> ConfirmSeconds, PollSeconds;
            internal static ConfigEntry<int> MaxStartsPerTick;
        }

        internal static class Cancel
        {
            internal static ConfigEntry<bool> Enabled, KeepFreshness;
            internal static ConfigEntry<CancelScopeMode> Scope;
            internal static ConfigEntry<float> ConfirmSeconds;
        }

        internal static class BiggerVats
        {
            internal static ConfigEntry<bool> Enabled;
            internal static ConfigEntry<int> MashingTun, IndustrialTun, Fermenter1000L, Distillery;
        }

        internal static class Interface
        {
            internal static ConfigEntry<bool> GamepadRoutes;
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Log every fetch plan, queue start and stop, and every refusal with its reason.");

            Fetch.Enabled = c.Bind("Fetch", "Enabled", true,
                "Adds a \"Fetch ingredients\" button to crafting stations. It moves what the selected recipe is missing from your " +
                "inventory, your worn bag and your own storage nearby into the station. Stolen items and other people's things are " +
                "never used." + LaunchNote);
            Fetch.UseWornBag = c.Bind("Fetch", "UseWornBag", true, "Also take items from the bag you're wearing.");
            Fetch.UseNearbyStorage = c.Bind("Fetch", "UseNearbyStorage", true,
                "Also take items from your own storage near the station (crates, shelves, fridges and so on).");
            Fetch.Range = c.Bind("Fetch", "Range", 10f,
                new ConfigDescription("How far from the station storage can be, in metres, measured across the floor.",
                    new AcceptableValueRange<float>(2f, 30f)));
            Fetch.MaxHeightDifference = c.Bind("Fetch", "MaxHeightDifference", 2.5f,
                new ConfigDescription("How far above or below the station storage can be, in metres. The default keeps it to one floor.",
                    new AcceptableValueRange<float>(0.5f, 10f)));
            Fetch.OnlyPlacedFurniture = c.Bind("Fetch", "OnlyPlacedFurniture", true,
                "Only use storage that is furniture (things you can place and pick up), not crates that are part of the level. " +
                "Level crates also need SameAreaOnly off, because only furniture belongs to a flat or area.");
            Fetch.SameAreaOnly = c.Bind("Fetch", "SameAreaOnly", true,
                "Only use storage that belongs to the same flat or area as the station.");
            Fetch.AllowPermittedStacks = c.Bind("Fetch", "AllowPermittedStacks", false,
                "Also use goods you bought that still carry the seller's name. Stolen goods are never used.");
            Fetch.OldestFirst = c.Bind("Fetch", "OldestFirst", true, "Take the least fresh food first.");
            Fetch.AllowPartial = c.Bind("Fetch", "AllowPartial", true,
                "When not everything can be found, still bring the ingredients that can be, whole ones only. " +
                "When off, nothing moves unless everything can be found.");
            Fetch.VesselTarget = c.Bind("Fetch", "VesselTarget", Workshop.VesselTarget.FullBatch,
                "Brewing vessels: FullBatch fills the vessel for the biggest batch that fits and can be found. OneMore brings enough for one more batch per click.");
            Fetch.ShowSummary = c.Bind("Fetch", "ShowSummary", true,
                "Show a note after a fetch that couldn't bring everything. Clicking a button that says Not found nearby always tells you what's missing.");

            Queue.Enabled = c.Bind("Queue", "Enabled", true,
                "Machines remember the last recipe you made on them. Press RepeatKey while looking at one to make it again, " +
                "and set a repeat count on machines that run without you." + LaunchNote);
            Queue.RepeatKey = c.Bind("Queue", "RepeatKey", new KeyboardShortcut(KeyCode.U),
                "Makes the last recipe again on the machine you're looking at, or on the open one. Press it twice to stop a running repeat.");
            Queue.RepeatButton = c.Bind("Queue", "RepeatButton", true,
                "Shows a repeat count button next to Fetch on machines that run without you." + LaunchNote);
            Queue.RepeatSteps = c.Bind("Queue", "RepeatSteps", "1,2,3,5,10,All",
                "The counts the repeat button steps through, comma-separated. Each is 1 to 99, or All.");
            Queue.LookHint = c.Bind("Queue", "LookHint", true,
                "When you look at a machine, show what the repeat key would make." + LaunchNote);
            Queue.ConfirmSeconds = c.Bind("Queue", "ConfirmSeconds", 3f,
                new ConfigDescription("How long the second key press stays armed.", new AcceptableValueRange<float>(1f, 10f)));
            Queue.PollSeconds = c.Bind("Queue", "PollSeconds", 1f,
                new ConfigDescription("How often, in real seconds, a repeat checks whether its machine has a free slot.",
                    new AcceptableValueRange<float>(0.25f, 10f)));
            Queue.MaxStartsPerTick = c.Bind("Queue", "MaxStartsPerTick", 4,
                new ConfigDescription("How many machines can start a job in the same check.", new AcceptableValueRange<int>(1, 20)));
            Queue.AutoRun = c.Bind("Queue", "AutoRun", false,
                "Machines in AutoRunMachines crush whatever is loaded into them on their own, like the mine's ore crusher. " +
                "This changes how the game plays, so it's off by default. Needs Queue.Enabled.");
            Queue.AutoRunMachines = c.Bind("Queue", "AutoRunMachines", "Jaw Crusher",
                "Crusher names that run on their own when AutoRun is on (comma-separated).");
            Queue.AutoRunRecipes = c.Bind("Queue", "AutoRunRecipes", "10513,10514,10515,10516,10533",
                "Recipe IDs auto-run may make, in order of preference (comma-separated). Sand (10517) and Glass Shards (10563) " +
                "are left out so gravel and bottles aren't crushed by accident.");

            Cancel.Enabled = c.Bind("Cancel", "Enabled", true,
                "Click a running job in a machine twice to cancel it. Scope sets which jobs can be cancelled and what you get back. " +
                "You get no skill for it." + LaunchNote);
            Cancel.Scope = c.Bind("Cancel", "Scope", CancelScopeMode.DishwasherOnly,
                "DishwasherOnly: only washes can be cancelled, and you get the item back as it was. AllManufacturing: any machine " +
                "job can be cancelled, but you only get what a failed job gives (often nothing or ash), and the ingredients are lost. " +
                "Jobs that give some of the same things when they fail (coffee, tea, planks and so on) can't be cancelled.");
            Cancel.ConfirmSeconds = c.Bind("Cancel", "ConfirmSeconds", 4f,
                new ConfigDescription("How long the second click stays armed.", new AcceptableValueRange<float>(1f, 10f)));
            Cancel.KeepFreshness = c.Bind("Cancel", "KeepFreshness", true,
                "A cancelled wash gives the item back as fresh as it went in. When off, it comes back as fresh as after a failed wash, " +
                "but never fresher than it went in.");

            BiggerVats.Enabled = c.Bind("BiggerVats", "Enabled", false,
                "Gives large brewing vessels more ingredient slots, so they can be filled to capacity. Batch size is still capped " +
                "by the vessel's size. This changes how the game plays, so it's off by default. Changes apply from the next loading screen." + LaunchNote);
            BiggerVats.MashingTun = Slots(c, "MashingTun", 18, "Slots in the 100 L mashing tun (Mashing Barrel).");
            BiggerVats.IndustrialTun = Slots(c, "IndustrialTun", 18, "Slots in the industrial mashing tun.");
            BiggerVats.Fermenter1000L = Slots(c, "Fermenter1000L", 24, "Slots in the 1000 L fermenter.");
            BiggerVats.Distillery = Slots(c, "Distillery", 12, "Slots in the distilleries (the Moonshine Distillery and the large still).");

            Interface.GamepadRoutes = c.Bind("Interface", "GamepadRoutes", true,
                "Reach the Fetch and repeat buttons with a controller: press Up from the Craft or Start button." + LaunchNote);

            foreach (var (e, _) in Hotkeys())
            {
                var entry = e;
                CheckReserved(entry);
                entry.SettingChanged += (s, a) => { CheckReserved(entry); Keys.Recheck(); };
            }
        }

        private static ConfigEntry<int> Slots(ConfigFile c, string key, int def, string what) =>
            c.Bind("BiggerVats", key, def, new ConfigDescription(what + " 9 is the game's own number.", new AcceptableValueRange<int>(9, 27)));

        // Every hotkey with the feature name used in warnings and notifications.
        internal static IEnumerable<(ConfigEntry<KeyboardShortcut> entry, string feature)> Hotkeys()
        {
            yield return (Queue.RepeatKey, "repeating the last recipe");
        }

        private static void CheckReserved(ConfigEntry<KeyboardShortcut> e)
        {
            var key = e.Value.MainKey;
            if (Array.IndexOf(Reserved, key) >= 0 || (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9))
                Log.Warn($"{e.Definition.Section}.{e.Definition.Key} is bound to {key}, which the game or another popular mod already uses.");
        }

        // Comma-separated list entries, parsed once per value.
        private static readonly Dictionary<ConfigEntry<string>, (string raw, HashSet<string> set)> Lists =
            new Dictionary<ConfigEntry<string>, (string raw, HashSet<string> set)>();

        internal static HashSet<string> List(ConfigEntry<string> e)
        {
            string raw = e.Value ?? "";
            if (Lists.TryGetValue(e, out var cached) && cached.raw == raw) return cached.set;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in raw.Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0) set.Add(t);
            }
            Lists[e] = (raw, set);
            return set;
        }

        // Ordered integer lists (AutoRunRecipes). Entries that are not numbers are skipped.
        private static readonly Dictionary<ConfigEntry<string>, (string raw, List<int> list)> IntLists =
            new Dictionary<ConfigEntry<string>, (string raw, List<int> list)>();

        internal static List<int> Ints(ConfigEntry<string> e)
        {
            string raw = e.Value ?? "";
            if (IntLists.TryGetValue(e, out var cached) && cached.raw == raw) return cached.list;
            var list = new List<int>();
            foreach (var part in raw.Split(','))
                if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && !list.Contains(v)) list.Add(v);
            IntLists[e] = (raw, list);
            return list;
        }

        // RepeatSteps: 1-99 or All (int.MaxValue), in the written order. Always has at least 1.
        private static string _stepsRaw;
        private static List<int> _steps;

        internal static List<int> Steps()
        {
            string raw = Queue.RepeatSteps.Value ?? "";
            if (_steps != null && raw == _stepsRaw) return _steps;
            var list = new List<int>();
            foreach (var part in raw.Split(','))
            {
                var t = part.Trim();
                if (t.Equals("All", StringComparison.OrdinalIgnoreCase)) { if (!list.Contains(int.MaxValue)) list.Add(int.MaxValue); continue; }
                if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= 1 && v <= 99 && !list.Contains(v)) list.Add(v);
            }
            if (!list.Contains(1)) list.Insert(0, 1);
            _stepsRaw = raw;
            _steps = list;
            return list;
        }
    }
}
