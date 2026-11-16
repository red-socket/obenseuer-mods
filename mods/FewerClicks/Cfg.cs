using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace FewerClicks
{
    internal enum PadButton { None, DPadUp, DPadDown }

    internal enum SlotItemTypes { FoodAndDrink, Consumables }

    // Every tunable value (spec §8). Entries are read live inside every patch body and helper, so turning a feature off
    // takes effect at once. A feature that was off at launch has no patches applied; it starts at the next launch
    // (LaunchNote). All description strings are public text (humanized 2026-11-15).
    internal static class Cfg
    {
        private const string LaunchNote = " If this was off when the game started, it takes effect at the next launch.";

        // Keys our other mods or popular community mods already use (F2/F9 community mods, F3/F4/F6/F8 our own and the dev
        // starter, F7 UnityExplorer, F10, F11/F12 screenshots, Insert/PageUp/PageDown Better Tenement). Binding one of
        // these only logs a warning: the player's choice stands. F1 (the game console) is a real collision (Keys.HardCoded).
        private static readonly KeyCode[] Reserved =
        {
            KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10,
            KeyCode.F11, KeyCode.F12, KeyCode.Insert, KeyCode.PageUp, KeyCode.PageDown,
        };

        internal static ConfigEntry<bool> VerboseLogging;

        internal static class TopUp
        {
            internal static ConfigEntry<bool> Enabled, Gamepad, IncludeBackpack, AllowNewStacks;
            internal static ConfigEntry<string> Label;
            internal static ConfigEntry<int> ButtonWidth;
            internal static ConfigEntry<KeyboardShortcut> Hotkey;
        }

        internal static class AutoSwing
        {
            internal static ConfigEntry<bool> Enabled, SkipOwnedSources;
            internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        }

        internal static class QuickEat
        {
            internal static ConfigEntry<bool> Enabled, SafeFoodOnly, Notify;
            internal static ConfigEntry<KeyboardShortcut> Hotkey;
            internal static ConfigEntry<PadButton> PadButton;
            internal static ConfigEntry<float> Threshold;
            internal static ConfigEntry<int> MaxItemsPerNeed;
        }

        internal static class AutoEat
        {
            internal static ConfigEntry<bool> Enabled, Notify;
            internal static ConfigEntry<float> Threshold;
        }

        internal static class SlotKeys
        {
            internal static ConfigEntry<bool> Enabled, ShowKeyLabels;
            internal static readonly ConfigEntry<KeyboardShortcut>[] Keys = new ConfigEntry<KeyboardShortcut>[5];
            internal static ConfigEntry<SlotItemTypes> AllowedTypes;
        }

        internal static class FillAll
        {
            internal static ConfigEntry<bool> Enabled, IncludeEmptyContainers, StopWhenSeen, SummaryNotification;
            internal static ConfigEntry<string> Label, StopLabel;
            internal static ConfigEntry<KeyboardShortcut> Hotkey;
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Log every top-up, swing session, meal, slot use and fill.");

            TopUp.Enabled = c.Bind("TopUp", "Enabled", true,
                "Adds a \"Top up\" button to storage windows. It moves matching items from your inventory onto stacks the " +
                "storage already has, and never into empty slots. Hidden during trades." + LaunchNote);
            TopUp.Label = c.Bind("TopUp", "Label", "Top up", "Button text.");
            TopUp.ButtonWidth = c.Bind("TopUp", "ButtonWidth", 76,
                new ConfigDescription("Button width in pixels. The header gets crowded on powered fridges.", new AcceptableValueRange<int>(50, 110)));
            TopUp.Hotkey = c.Bind("TopUp", "Hotkey", KeyboardShortcut.Empty, "Tops up the open storage.");
            TopUp.Gamepad = c.Bind("TopUp", "Gamepad", true,
                "Controller: pressing Left from Sort (or from Better Tenement's Stash) selects Top up." + LaunchNote);
            TopUp.IncludeBackpack = c.Bind("TopUp", "IncludeBackpack", false, "Also take items from the backpack you are wearing.");
            TopUp.AllowNewStacks = c.Bind("TopUp", "AllowNewStacks", false,
                "After topping up, put what is left of item types the storage already holds into empty slots, as shift-click does.");

            AutoSwing.Enabled = c.Bind("AutoSwing", "Enabled", true,
                "While you hold Primary Use on a tree, ore, rock, limestone or fatberg, the tool keeps swinging after each normal " +
                "cooldown. Each swing takes the same time and tool wear as a click. It only starts a swing at a resource, and if " +
                "someone steps in front mid-swing, that swing misses instead of hitting them. " +
                "If this was off when the game started and ToggleKey is not bound, it takes effect at the next launch.");
            AutoSwing.ToggleKey = c.Bind("AutoSwing", "ToggleKey", KeyboardShortcut.Empty, "Turns Auto Swing on or off during normal play. The choice is saved.");
            AutoSwing.SkipOwnedSources = c.Bind("AutoSwing", "SkipOwnedSources", true,
                "Don't auto-swing at resources that belong to someone who hasn't given you permission. Manual swings work as usual.");

            QuickEat.Enabled = c.Bind("QuickEat", "Enabled", true,
                "Eat and drink from your main inventory with one key. The game's own auto-eat picks what to use." + LaunchNote);
            QuickEat.Hotkey = c.Bind("QuickEat", "Hotkey", new KeyboardShortcut(KeyCode.Alpha5),
                "Quick Eat & Drink. Does nothing in menus, dialogue, shops or trades, or while sleeping.");
            QuickEat.PadButton = c.Bind("QuickEat", "PadButton", PadButton.DPadDown,
                "Controller button for Quick Eat. It works outside menus when the game is in controller mode. In third person, " +
                "while the list of nearby objects is on screen, the D-pad scrolls that list and Quick Eat does nothing.");
            QuickEat.Threshold = c.Bind("QuickEat", "Threshold", 25f,
                new ConfigDescription("Eats or drinks while hunger or thirst is at least this high. Below 25, extra food goes to your bowels.",
                    new AcceptableValueRange<float>(10f, 95f)));
            QuickEat.SafeFoodOnly = c.Bind("QuickEat", "SafeFoodOnly", true,
                "Never falls back to alcohol, mushrooms or other food with side effects, which the game's own auto-eat does. " +
                "Also applies to Auto Eat.");
            QuickEat.MaxItemsPerNeed = c.Bind("QuickEat", "MaxItemsPerNeed", 5,
                new ConfigDescription("The most items used for each need (hunger, thirst) each time it runs. Also applies to Auto Eat.", new AcceptableValueRange<int>(1, 5)));
            QuickEat.Notify = c.Bind("QuickEat", "Notify", true, "Shows what you ate, or why nothing was eaten. Also applies to the slot keys.");

            AutoEat.Enabled = c.Bind("AutoEat", "Enabled", false,
                "Eat and drink on its own during normal play once hunger or thirst reaches the threshold.");
            AutoEat.Threshold = c.Bind("AutoEat", "Threshold", 50f,
                new ConfigDescription("Hunger or thirst level that starts Auto Eat. The game's own auto-eat uses 50.",
                    new AcceptableValueRange<float>(25f, 95f)));
            AutoEat.Notify = c.Bind("AutoEat", "Notify", true, "Shows what Auto Eat used, or that nothing safe was found.");

            SlotKeys.Enabled = c.Bind("SlotKeys", "Enabled", false, "Keys that use the items in the top row of your inventory." + LaunchNote);
            var defaults = new[] { KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.None };
            for (int i = 0; i < 5; i++)
                SlotKeys.Keys[i] = c.Bind("SlotKeys", $"Slot{i + 1}Key",
                    defaults[i] == KeyCode.None ? KeyboardShortcut.Empty : new KeyboardShortcut(defaults[i]),
                    $"Key for slot {i + 1} of the top row.");
            SlotKeys.AllowedTypes = c.Bind("SlotKeys", "AllowedTypes", SlotItemTypes.Consumables,
                "Consumables: food, drink, smokes, medicine and bandages. FoodAndDrink: food and drink only. " +
                "The keys never equip, read, place or open anything.");
            SlotKeys.ShowKeyLabels = c.Bind("SlotKeys", "ShowKeyLabels", true,
                "Shows the key on those slots, like the game's own 1, 2 and 3 (keyboard only).");

            FillAll.Enabled = c.Bind("FillAll", "Enabled", true,
                "Adds a \"Fill all\" button to sink, tap and barrel windows. It fills every container in your main inventory one " +
                "after another, the same as pressing Fill on each. It needs a mouse or keyboard: a controller can't reach the " +
                "button, but X still fills one container at a time." + LaunchNote);
            FillAll.Label = c.Bind("FillAll", "Label", "Fill all", "Button text.");
            FillAll.StopLabel = c.Bind("FillAll", "StopLabel", "Stop", "Button text while filling.");
            FillAll.Hotkey = c.Bind("FillAll", "Hotkey", KeyboardShortcut.Empty,
                "Starts or stops Fill all while a liquid window is open. A controller button (JoystickButton0-19) works too, " +
                "but every controller button already does something in the inventory (X fills or empties, View opens the " +
                "backpack, L3/R3 drag, A, B, Y and the bumpers), so that action also runs.");
            FillAll.IncludeEmptyContainers = c.Bind("FillAll", "IncludeEmptyContainers", true,
                "Also fills empty bottles and cups. They turn into filled items and no longer count as empties for recycling.");
            FillAll.StopWhenSeen = c.Bind("FillAll", "StopWhenSeen", true,
                "Stops after the current container if someone sees you taking from a source that isn't yours.");
            FillAll.SummaryNotification = c.Bind("FillAll", "SummaryNotification", false,
                "Shows a summary even when everything was filled.");

            foreach (var (e, _) in Hotkeys())
            {
                var entry = e;
                CheckReserved(entry);
                entry.SettingChanged += (s, a) => CheckReserved(entry);
            }
        }

        // Every hotkey with the feature name used in warnings and notifications.
        internal static IEnumerable<(ConfigEntry<KeyboardShortcut> entry, string feature)> Hotkeys()
        {
            yield return (TopUp.Hotkey, "Top up");
            yield return (AutoSwing.ToggleKey, "the Auto Swing toggle");
            yield return (QuickEat.Hotkey, "Quick Eat");
            for (int i = 0; i < 5; i++) yield return (SlotKeys.Keys[i], $"slot key {i + 1}");
            yield return (FillAll.Hotkey, "Fill all");
        }

        private static void CheckReserved(ConfigEntry<KeyboardShortcut> e)
        {
            var key = e.Value.MainKey;
            if (Array.IndexOf(Reserved, key) >= 0)
                Log.Warn($"{e.Definition.Section}.{e.Definition.Key} is bound to {key}, which the game or another popular mod already uses.");
        }
    }
}
