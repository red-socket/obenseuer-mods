using System;
using BepInEx.Configuration;
using UnityEngine;

namespace BetterTenement
{
    internal enum PanelMode { Scroll, Pages }
    internal enum ForceClearMode { Take, TakeOrDelete }
    internal enum LedgerSide { Left, Right }

    // Every tunable value (spec §5). Entries are read live inside every patch body, so turning a feature off takes
    // effect at once. A feature that was off at launch has no patches applied; it starts at the next launch.
    internal static class Cfg
    {
        private const string LaunchNote = " If this was off when the game started, it starts at the next launch.";

        internal static ConfigEntry<bool> VerboseLogging;
        internal static ConfigEntry<float> UiScale;

        internal static ConfigEntry<bool> FixRemainder, StolenFirst, StolenFirstIncludesInventory, PermissionWithoutUseCountsAsStolen,
            FallbackSortWhenConflict, ConsumptionNotification;

        internal static ConfigEntry<bool> Stash, StashIncludeBackpack, StashGamepad;
        internal static ConfigEntry<string> StashLabel, ExtraItemIds, ExcludeItemIds;
        internal static ConfigEntry<KeyboardShortcut> StashHotkey;
        internal static ConfigEntry<int> StashButtonWidth;

        internal static ConfigEntry<bool> RefreshSnapshot, CardStockMarkers;

        internal static ConfigEntry<bool> Chests, PrepareForRemoval, ScrollAnyLargeStorage, ShowFillCounter, WarnAboveVanilla;
        internal static ConfigEntry<int> ResourceChestSlots, RowsPerWheelNotch;
        internal static ConfigEntry<PanelMode> Mode;
        internal static ConfigEntry<KeyboardShortcut> PrevPage, NextPage;

        internal static ConfigEntry<bool> TimerFix;

        internal static ConfigEntry<bool> Doctor, DoctorPanelDetails, DoctorOutlines, AllowForceClear;
        internal static ConfigEntry<KeyboardShortcut> DoctorHotkey;
        internal static ConfigEntry<string> OutlineColor;
        internal static ConfigEntry<int> MaxOutlines;
        internal static ConfigEntry<ForceClearMode> ClearMode;

        internal static ConfigEntry<bool> LedgerCardLine, LedgerCategoryTooltip, LedgerShowWithTenementTab, LedgerShoppingList;
        internal static ConfigEntry<string> LedgerCardLineOffset;
        internal static ConfigEntry<KeyboardShortcut> LedgerHotkey;
        internal static ConfigEntry<LedgerSide> Side;
        internal static ConfigEntry<int> LedgerWidth;

        // Keys taken by the game (F1 console, F11/F12 screenshots), our own Save Guard (F6), the dev starter (F8)
        // or popular mods (F2, F5, F9, F7 UnityExplorer).
        private static readonly KeyCode[] Reserved =
            { KeyCode.F1, KeyCode.F2, KeyCode.F5, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F11, KeyCode.F12 };

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Log every removal, stash, snapshot, resize and Doctor action.");
            UiScale = c.Bind("UI", "Scale", 1.0f,
                new ConfigDescription("Size of Better Tenement panels.", new AcceptableValueRange<float>(0.5f, 2.5f)));

            FixRemainder = c.Bind("Materials", "FixRemainder", true,
                "Charge each resource chest only what is still missing, then your inventory only the rest (fixes over-charging when mods add chests). " +
                "Off: chests and inventory are charged the game's way, also while StolenFirst is on." + LaunchNote);
            StolenFirst = c.Bind("Materials", "StolenFirst", true,
                "Contractor renovations use stolen materials before clean ones." + LaunchNote);
            StolenFirstIncludesInventory = c.Bind("Materials", "StolenFirstIncludesInventory", true,
                "After stolen chest stacks, use stolen stacks you carry before clean chest stacks.");
            PermissionWithoutUseCountsAsStolen = c.Bind("Materials", "PermissionWithoutUseCountsAsStolen", false,
                "Treat items you hold with permission but may not use as stolen.");
            FallbackSortWhenConflict = c.Bind("Materials", "FallbackSortWhenConflict", true,
                "If another mod replaces material removal, move stolen stacks to the front of their material's slots before paying (changes slot order). " +
                "Stolen stacks you carry cannot be preferred in this mode.");
            ConsumptionNotification = c.Bind("Materials", "ConsumptionNotification", true,
                "Show how many stolen materials a renovation used.");

            Stash = c.Bind("Stash", "Enabled", true, "\"Stash\" button on resource chests." + LaunchNote);
            StashLabel = c.Bind("Stash", "Label", "Stash", "Button text.");
            StashButtonWidth = c.Bind("Stash", "ButtonWidth", 66,
                new ConfigDescription("Button width in pixels. A wider button can cover the chest title when other mods also add buttons to the header.",
                    new AcceptableValueRange<int>(50, 110)));
            StashHotkey = c.Bind("Stash", "Hotkey", new KeyboardShortcut(KeyCode.Insert), "Stash while a resource chest is open.");
            StashIncludeBackpack = c.Bind("Stash", "IncludeBackpack", true, "Also stash from the worn backpack (never from hands or equipment).");
            StashGamepad = c.Bind("Stash", "Gamepad", true, "Controller: Left from Sort reaches Stash.");
            ExtraItemIds = c.Bind("Stash", "ExtraItemIds", "", "Comma-separated item IDs to treat as materials.");
            ExcludeItemIds = c.Bind("Stash", "ExcludeItemIds", "",
                "Comma-separated item IDs never to stash (e.g. 10530 Firewood, 35140 Dynamite).");

            RefreshSnapshot = c.Bind("Stock", "RefreshSnapshot", true,
                "Keep other contractors' chest counts current (on chest close and on save)." + LaunchNote);
            CardStockMarkers = c.Bind("Stock", "CardStockMarkers", true,
                "Upgrade cards: \"~\" marks a last-known count from another contractor's chest; amber means carried items will be used." + LaunchNote);

            Chests = c.Bind("ResourceChests", "Enabled", true,
                "Enlarge the five contractor resource chests. Turning this off works like removing the mod for these chests: one holding more than " +
                "88 stacks keeps every item but no longer fits the game's panel. Use PrepareForRemoval first (it works with this off) and move " +
                "stacks out until each chest holds 88 or fewer." + LaunchNote);
            ResourceChestSlots = c.Bind("ResourceChests", "ResourceChestSlots", 176,
                new ConfigDescription("88-352, rounded up to a multiple of 8. Applies at the next area load. Lowering it never deletes items.",
                    new AcceptableValueRange<int>(88, 352)));
            PrepareForRemoval = c.Bind("ResourceChests", "PrepareForRemoval", false,
                "Shrink resource chests back to 88 at the next area load, keeping the scroll view so overflow can be moved out. " +
                "Works even with Enabled off (then from the next launch, if both were off when the game started). " +
                "Nothing is ever deleted: a chest with more stacks keeps one slot per stack.");
            Mode = c.Bind("ResourceChests", "PanelMode", PanelMode.Scroll, "Scroll or Pages, for storages over 88 slots.");
            ScrollAnyLargeStorage = c.Bind("ResourceChests", "ScrollAnyLargeStorage", true,
                "Use the scroll view for any storage over 88 slots, not only resource chests.");
            RowsPerWheelNotch = c.Bind("ResourceChests", "RowsPerWheelNotch", 1,
                new ConfigDescription("Rows per mouse wheel notch (Scroll mode).", new AcceptableValueRange<int>(1, 5)));
            PrevPage = c.Bind("ResourceChests", "PrevPage", new KeyboardShortcut(KeyCode.PageUp), "Page or 11 rows up in a large storage.");
            NextPage = c.Bind("ResourceChests", "NextPage", new KeyboardShortcut(KeyCode.PageDown), "Page or 11 rows down in a large storage.");
            ShowFillCounter = c.Bind("ResourceChests", "ShowFillCounter", true, "Show how full the chest is (e.g. \"97/176\") under its title.");
            WarnAboveVanilla = c.Bind("ResourceChests", "WarnAboveVanilla", false,
                "Amber counter when a chest holds more stacks than the game's own 88 slots (PrepareForRemoval always shows it).");

            TimerFix = c.Bind("Timer", "Enabled", true,
                "Renovation timers keep fractional progress (0.25-0.75x work; 1.25x is faster than 1x). Turns itself off if the game's timer code changes." + LaunchNote);

            Doctor = c.Bind("Doctor", "Enabled", true, "Tenement Doctor window.");
            DoctorHotkey = c.Bind("Doctor", "Hotkey", new KeyboardShortcut(KeyCode.F4), "Open or close the Doctor.");
            DoctorPanelDetails = c.Bind("Doctor", "PanelDetails", true,
                "Apartment panel: group cleared % against the 90%/80% requirement." + LaunchNote);
            DoctorOutlines = c.Bind("Doctor", "Outlines", true, "Outline leftover junk of the selected apartment (through walls).");
            OutlineColor = c.Bind("Doctor", "OutlineColor", "#FFAB00", "Outline colour (#RRGGBB).");
            MaxOutlines = c.Bind("Doctor", "MaxOutlines", 100,
                new ConfigDescription("Most outlines drawn at once.", new AcceptableValueRange<int>(1, 500)));
            AllowForceClear = c.Bind("Doctor", "AllowForceClear", false,
                "Allow the Doctor to pick up leftovers for you. Permanent; vacant flats only.");
            ClearMode = c.Bind("Doctor", "ForceClearMode", ForceClearMode.Take,
                "Take: picked-up items go to your inventory. TakeOrDelete: also offers to delete objects the game marks as not takeable " +
                "(after a confirmation, never one holding items). Deleted objects are gone for good; they are not added to your inventory.");

            LedgerCardLine = c.Bind("Ledger", "CardLine", true,
                "Upgrade cards: value and rent change per payday, and which contractors do the tier." + LaunchNote);
            LedgerCardLineOffset = c.Bind("Ledger", "CardLineOffset", "20,-80", "Where the card line starts: x,y of its left end from the card's top-left corner. It always ends at the card's right edge, so a larger x makes it shorter.");
            LedgerCategoryTooltip = c.Bind("Ledger", "CategoryTooltip", true,
                "Floor plan tooltips: next tier, its contractor and its effect. During a renovation they show the tier being built." + LaunchNote);
            LedgerShowWithTenementTab = c.Bind("Ledger", "ShowWithTenementTab", true,
                "Show the Ledger panel (or its collapsed tab) beside the Tenement tab. Off: no panel at all, and the hotkey does nothing.");
            LedgerHotkey = c.Bind("Ledger", "Hotkey", new KeyboardShortcut(KeyCode.F3),
                "On the Tenement tab: collapse or expand the Ledger panel. Needs ShowWithTenementTab.");
            Side = c.Bind("Ledger", "Side", LedgerSide.Right, "Which side of the screen the Ledger panel is on: Left or Right.");
            LedgerWidth = c.Bind("Ledger", "Width", 380,
                new ConfigDescription("Panel width in pixels before scale.", new AcceptableValueRange<int>(240, 800)));
            LedgerShoppingList = c.Bind("Ledger", "ShoppingList", true, "Materials for starred upgrades.");

            foreach (var e in new[] { StashHotkey, PrevPage, NextPage, DoctorHotkey, LedgerHotkey })
            {
                var entry = e;
                CheckHotkey(entry);
                entry.SettingChanged += (s, a) => CheckHotkey(entry);
            }
            ExtraItemIds.SettingChanged += (s, a) => Materials.Invalidate();
            ExcludeItemIds.SettingChanged += (s, a) => Materials.Invalidate();
        }

        private static void CheckHotkey(ConfigEntry<KeyboardShortcut> e)
        {
            var key = e.Value.MainKey;
            if (Array.IndexOf(Reserved, key) >= 0)
                Log.Warn($"{e.Definition.Section}.{e.Definition.Key} is bound to {key}, which the game or another popular mod already uses.");
        }

        // "20,-80" -> (20,-80); anything unreadable falls back to the default.
        internal static Vector2 CardOffset()
        {
            var parts = (LedgerCardLineOffset.Value ?? "").Split(',');
            if (parts.Length == 2 &&
                float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                return new Vector2(x, y);
            return new Vector2(20f, -80f);
        }

        internal static Color Outline()
        {
            return ColorUtility.TryParseHtmlString(OutlineColor.Value, out var c) ? c : new Color(1f, 0.67f, 0f);
        }
    }
}
