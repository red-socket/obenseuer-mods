using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace TidyInventory
{
    // Every tunable value (spec §10). Entries are read live inside every patch body and helper, so turning a feature off
    // takes effect at once. A feature that was off at launch has no patches applied; it starts at the next launch
    // (LaunchNote). All description strings are public text (humanized 2026-11-16).
    internal static class Cfg
    {
        private const string LaunchNote = " If this was off when the game started, it takes effect at the next launch.";

        // Keys our other mods or popular community mods already use (F2/F9 community mods, F3/F4/F6/F8 our own and the dev
        // starter, F7 UnityExplorer, F10, F11/F12 screenshots, Insert/PageUp/PageDown/Home/End our other mods). Binding
        // one of these only logs a warning: the player's choice stands. Real clashes are found by Keys.CheckCollisions.
        private static readonly KeyCode[] Reserved =
        {
            KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10,
            KeyCode.F11, KeyCode.F12, KeyCode.Insert, KeyCode.PageUp, KeyCode.PageDown, KeyCode.Home, KeyCode.End,
        };

        internal static ConfigEntry<bool> VerboseLogging, ShowHints;

        internal static class Pins
        {
            internal static ConfigEntry<bool> Enabled, BlockManualMoves, KeepReservationWhenEmpty, ReturnItemsToPin, CountReservedAsFull,
                ShowBadge, ShowGhostIcon;
            internal static ConfigEntry<KeyboardShortcut> PinKey;
            internal static ConfigEntry<float> GhostOpacity;
        }

        internal static class Bins
        {
            internal static ConfigEntry<bool> Enabled, PlayerPlacedOnly, SkipOwnedBins, ProtectStolen, Gamepad;
            internal static ConfigEntry<string> BinCategories, BinTitles, ProtectCategories, Label, ConfirmLabel;
            internal static ConfigEntry<int> ProtectValueAbove, ButtonWidth;
            internal static ConfigEntry<float> ConfirmSeconds;
        }

        internal static class BinDispose
        {
            internal static ConfigEntry<bool> Enabled;
            internal static ConfigEntry<KeyboardShortcut> Key;
        }

        internal static class BinAutoEmpty
        {
            internal static ConfigEntry<bool> Enabled, Notify;
            internal static ConfigEntry<string> Areas;
        }

        internal static class Despawn
        {
            internal static ConfigEntry<bool> ProtectValuables, ProtectTaskPackages, IncludeSpawnerLoot;
            internal static ConfigEntry<int> MinValue, RecheckMinutes;
        }

        internal static class Combine
        {
            internal static ConfigEntry<bool> InventoryButton, StorageButton, IncludeFlatPriceItems, StrictQuality;
            internal static ConfigEntry<float> InventoryOffsetX, InventoryOffsetY;
            internal static ConfigEntry<string> Label, InventoryLabel;
        }

        internal static class StolenStacks
        {
            internal static ConfigEntry<bool> MergeSameOwner;
        }

        internal static class Duck
        {
            internal static ConfigEntry<bool> Enabled, AllowCureAchievement;
            internal static ConfigEntry<KeyboardShortcut> DiscardKey;
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Log every pin change, redirect, deletion, combine and discard.");
            ShowHints = c.Bind("General", "ShowHints", true,
                "Show a short tip the first time something needs explaining (once per session).");

            Pins.Enabled = c.Bind("PinnedSlots", "Enabled", true,
                "Pin inventory slots with PinKey. Until you pin a slot, the only change is that middle-clicks on items are used " +
                "for pinning while PinKey is the middle mouse button." + LaunchNote);
            Pins.PinKey = c.Bind("PinnedSlots", "PinKey", new KeyboardShortcut(KeyCode.Mouse2),
                "With the inventory open, pins or unpins the slot under the mouse. Left and right click can't be used. " +
                "While this is the middle mouse button, middle-clicks on items do nothing else.");
            Pins.BlockManualMoves = c.Bind("PinnedSlots", "BlockManualMoves", true,
                "Pinned stacks can't be dragged, quick-moved or dropped, and Top up, Stash, the amount slider and the controller " +
                "can't move them either. When off, Sort and selling still skip them.");
            Pins.KeepReservationWhenEmpty = c.Bind("PinnedSlots", "KeepReservationWhenEmpty", true,
                "When a pinned stack runs out, the slot stays reserved for that item. When off, the pin goes with the last item.");
            Pins.ReturnItemsToPin = c.Bind("PinnedSlots", "ReturnItemsToPin", true,
                "New copies of a pinned item go to its slot first." + LaunchNote);
            Pins.CountReservedAsFull = c.Bind("PinnedSlots", "CountReservedAsFull", true,
                "Reserved slots don't count as free space, so shops, pickups and harvesting say your inventory is full instead " +
                "of dropping the item at your feet. Quest and dialogue rewards are still given: if only a reserved slot is free, " +
                "they drop at your feet." + LaunchNote);
            Pins.ShowBadge = c.Bind("PinnedSlots", "ShowBadge", true, "Show a pin mark on pinned slots.");
            Pins.ShowGhostIcon = c.Bind("PinnedSlots", "ShowGhostIcon", true, "Show a faded picture of the item in an empty reserved slot.");
            Pins.GhostOpacity = c.Bind("PinnedSlots", "GhostOpacity", 0.35f,
                new ConfigDescription("How visible the faded picture is.", new AcceptableValueRange<float>(0.1f, 0.8f)));

            Bins.Enabled = c.Bind("Bins", "Enabled", true,
                "Adds an \"Empty\" button to trash bins and dumpsters. Click it twice to throw away what's inside. Quest items " +
                "are always kept." + LaunchNote);
            Bins.BinCategories = c.Bind("Bins", "BinCategories", "Trashcan, Dumpster, Dumpster open, Trash Collection Silo",
                "Storage types that can be bins (comma-separated).");
            Bins.BinTitles = c.Bind("Bins", "BinTitles", "Trash bin, Trashcan, Dumpster, Trash Collection Silo",
                "Storage names that count as bins (comma-separated). Laundry baskets never count.");
            Bins.PlayerPlacedOnly = c.Bind("Bins", "PlayerPlacedOnly", false,
                "Only bins you placed yourself (Trash Bin, Green Dumpster) count.");
            Bins.SkipOwnedBins = c.Bind("Bins", "SkipOwnedBins", true, "No Empty button on bins that belong to someone else.");
            // Replaces ProtectOwned (true), which kept stolen goods out of the player's own bins (user bug report 2026-11-18).
            // A new key so existing configs get the new default; borrowed (permission) and shop items stay protected above.
            Bins.ProtectStolen = c.Bind("Bins", "ProtectStolen", false,
                "Keep stolen items when you empty a bin. When off, stolen things you put in your own bin are thrown away like " +
                "anything else. Items you're only allowed to borrow and shop goods are always kept.");
            Bins.ProtectCategories = c.Bind("Bins", "ProtectCategories", "QuestItem, Keycard",
                "Item categories that are never thrown away (comma-separated).");
            Bins.ProtectValueAbove = c.Bind("Bins", "ProtectValueAbove", 500,
                new ConfigDescription("Keep stacks whose sale value (price × amount) is at least this. 0 turns it off. Applies to " +
                                      "Empty, throwing away single stacks and daily emptying.", new AcceptableValueRange<int>(0, 1000000)));
            Bins.ConfirmSeconds = c.Bind("Bins", "ConfirmSeconds", 3f,
                new ConfigDescription("How long the second click or key press stays armed. Also used for single stacks and the duck.",
                    new AcceptableValueRange<float>(1f, 10f)));
            Bins.ButtonWidth = c.Bind("Bins", "ButtonWidth", 76,
                new ConfigDescription("Width of the Empty and storage Combine buttons in pixels.", new AcceptableValueRange<int>(50, 110)));
            Bins.Label = c.Bind("Bins", "Label", "Empty", "Button text.");
            Bins.ConfirmLabel = c.Bind("Bins", "ConfirmLabel", "Sure?", "Button text while it waits for the second click.");
            Bins.Gamepad = c.Bind("Bins", "Gamepad", true,
                "Reach the Empty and Combine buttons with a controller: press Right from Take all, or Right from the inventory's Sort." + LaunchNote);

            BinDispose.Enabled = c.Bind("BinDispose", "Enabled", false,
                "With a bin open, hover a stack in it and press Key twice to throw that stack away. Mouse only. Uses the bin and " +
                "protection settings under [Bins] and works even when the Empty button is off.");
            BinDispose.Key = c.Bind("BinDispose", "Key", new KeyboardShortcut(KeyCode.Delete), "Key for throwing away a single stack.");

            BinAutoEmpty.Enabled = c.Bind("BinAutoEmpty", "Enabled", false,
                "Once a day, empty the Trash Bins and Green Dumpsters you placed in the chosen areas, with the same protections. " +
                "Works even when the Empty button is off.");
            BinAutoEmpty.Areas = c.Bind("BinAutoEmpty", "Areas", "Stairs, Outside",
                "Building areas whose placed bins are emptied (comma-separated: Stairs, PlayerApartment, Workshop, Outside, " +
                "Greenhouse, Store, Communal, Public).");
            BinAutoEmpty.Notify = c.Bind("BinAutoEmpty", "Notify", true, "Show a note when bins were emptied overnight.");

            Despawn.ProtectValuables = c.Bind("Despawn", "ProtectValuables", false,
                "Dropped items worth MinValue or more don't disappear. Your save grows a little for each one kept." + LaunchNote);
            Despawn.MinValue = c.Bind("Despawn", "MinValue", 200,
                new ConfigDescription("Sale value (price × amount) from which a dropped item is kept. Real-money coins are always kept.",
                    new AcceptableValueRange<int>(1, 1000000)));
            Despawn.ProtectTaskPackages = c.Bind("Despawn", "ProtectTaskPackages", true, "Dropped packages holding quest items never disappear.");
            Despawn.RecheckMinutes = c.Bind("Despawn", "RecheckMinutes", 600,
                new ConfigDescription("Game minutes between checks on a kept item.", new AcceptableValueRange<int>(60, 1440)));
            Despawn.IncludeSpawnerLoot = c.Bind("Despawn", "IncludeSpawnerLoot", false, "Also keep valuable loot that spawned in the world.");

            Combine.InventoryButton = c.Bind("Combine", "InventoryButton", true,
                "Adds a Combine button (text set by InventoryLabel) next to the inventory's Sort button. It merges part-used items " +
                "(matches, pills, glue and so on) into full ones plus one part-used one.");
            Combine.StorageButton = c.Bind("Combine", "StorageButton", true, "Adds Combine to storage windows (not bins)." + LaunchNote);
            Combine.IncludeFlatPriceItems = c.Bind("Combine", "IncludeFlatPriceItems", false,
                "Also combine items that sell for full price however much is left (soap, detergent, e-cigarettes and so on). " +
                "This lowers their total sale value.");
            Combine.StrictQuality = c.Bind("Combine", "StrictQuality", true, "Only combine items of exactly the same quality.");
            Combine.InventoryOffsetX = c.Bind("Combine", "InventoryOffsetX", 44f, "Position of the inventory button relative to Sort (pixels right).");
            Combine.InventoryOffsetY = c.Bind("Combine", "InventoryOffsetY", 0f, "Position of the inventory button relative to Sort (pixels up).");
            Combine.Label = c.Bind("Combine", "Label", "Combine", "Storage button text.");
            Combine.InventoryLabel = c.Bind("Combine", "InventoryLabel", "Combine", "Inventory button text. Keep it short: the button is the same size as Sort.");

            StolenStacks.MergeSameOwner = c.Bind("StolenStacks", "MergeSameOwner", true,
                "Stolen items from the same owner stack even when only some carry the stolen mark. The stack stays marked. " +
                "Items from different owners never stack." + LaunchNote);

            Duck.Enabled = c.Bind("DuckDiscard", "Enabled", true,
                "Throw away the blue rubber duck once Mr. Duck's quest is done: hover it in your items (or select it with a " +
                "controller) and press twice." + LaunchNote);
            Duck.DiscardKey = c.Bind("DuckDiscard", "DiscardKey", new KeyboardShortcut(KeyCode.Delete),
                "Key for throwing the duck away. On a controller, press A twice.");
            Duck.AllowCureAchievement = c.Bind("DuckDiscard", "AllowCureAchievement", false,
                "Let this unlock the \"duck addiction cured\" achievement, as giving the duck up in the conversation does.");

            foreach (var (e, _) in Hotkeys())
            {
                var entry = e;
                CheckReserved(entry);
                entry.SettingChanged += (s, a) => { CheckReserved(entry); Keys.Recheck(); };
            }
        }

        // Every hotkey with the feature name used in warnings and notifications.
        internal static IEnumerable<(ConfigEntry<KeyboardShortcut> entry, string feature)> Hotkeys()
        {
            yield return (Pins.PinKey, "pinning");
            yield return (BinDispose.Key, "throwing away a single stack");
            yield return (Duck.DiscardKey, "throwing away the duck");
        }

        // PinKey refuses the left and right mouse buttons: they drag, click and quick-move items (spec §3.6).
        internal static bool PinKeyRefused
        {
            get
            {
                var k = Pins.PinKey.Value.MainKey;
                if (k != KeyCode.Mouse0 && k != KeyCode.Mouse1) return false;
                Log.WarnOnce("pinkey-mouse", $"Pins: PinnedSlots.PinKey is {k}, which the inventory already uses for dragging and clicking; pinning by key is off.");
                return true;
            }
        }

        private static void CheckReserved(ConfigEntry<KeyboardShortcut> e)
        {
            var key = e.Value.MainKey;
            if (Array.IndexOf(Reserved, key) >= 0)
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
    }
}
