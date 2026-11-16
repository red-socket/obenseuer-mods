using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace FairJustice
{
    internal enum PanelAnchor { TopLeft, TopRight, BottomLeft, BottomRight }

    // Every tunable value (spec §5). Entries are read live inside every patch body, so turning a feature off takes
    // effect at once. A feature that was off at launch has no patches applied; it starts at the next launch.
    internal static class Cfg
    {
        private const string LaunchNote = " If this was off when the game started, it starts at the next launch.";

        // Bazaar jail areas (scene "Interior Bazaar Police and Jail") and the Gatehouse (spec §0.1).
        internal const string BazaarCell = "9684857b-eaed-4719-af1b-fc20ecd692b9";
        private const string ReleaseDefault =
            BazaarCell + ",87eb63c2-5539-4a8f-bcc9-e9524a688261,385c6280-fa95-43dd-bc83-96fa4f563500," +
            "c0513d5c-15f2-432e-9281-203d51793834,b4e9b582-a5ae-4e67-ab74-c83c575f953b,e723eb77-f9e2-4d77-a180-0cd7913d0b96";

        internal static ConfigEntry<bool> VerboseLogging;

        internal static ConfigEntry<bool> FreeInternalMoves, IncludeEquipping, FixStolenTagging, TagSourceRemainder, RepairOnLoad;
        internal static ConfigEntry<bool> FreeOwnUse, FreeAmenityUse;
        internal static ConfigEntry<string> AmenityItems;

        internal static ConfigEntry<bool> KeepOverflowInBag, KeepPermittedItems, RescueStolenBackpackContents, SafePayFine, Notify, WardenMentionsBag;

        internal static ConfigEntry<bool> SentencePanel, ShowWhileWaiting;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<PanelAnchor> Anchor;
        internal static ConfigEntry<int> OffsetX, OffsetY;
        internal static ConfigEntry<float> Scale;

        internal static ConfigEntry<bool> CellDoorGrace, AfterLoadInJail;
        internal static ConfigEntry<int> CellDoorGraceSeconds;
        internal static ConfigEntry<string> CellDoorAreaGuids;

        internal static ConfigEntry<bool> DeferNightRelease;
        internal static ConfigEntry<int> NightStartHour, MorningHour;

        internal static ConfigEntry<bool> ReleaseGrace, StopPursuits, ReleaseGraceTrespass, ReleaseGracePolice, PoliceAnyScene;
        internal static ConfigEntry<int> ReleaseGraceSeconds;
        internal static ConfigEntry<string> ReleaseAreaGuids;

        internal static ConfigEntry<bool> NoPassOutReArrest;

        internal static ConfigEntry<bool> StopChaseAtTenement, StopAtLockedDoors;
        internal static ConfigEntry<string> NoFollowScenes;
        internal static ConfigEntry<float> LockedDoorLookahead;

        // Parsed area lists, rebuilt when the entry changes.
        internal static HashSet<string> CellDoorAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static HashSet<string> ReleaseAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static HashSet<int> AmenityIds = new HashSet<int>();
        internal static HashSet<string> NoFollowSceneSet = new HashSet<string>(StringComparer.Ordinal);

        // Hand Soap, Bar Soap, Disinfectant; Lifehack-Pekka, Open Sewer Magazine, Stalburg Times, Evening Tidings,
        // Puuhari, IC and ARG magazines (Items.json, U-016).
        private const string AmenityDefault = "11020,19440,10990,15090,15100,15110,15120,16660,16670,16680";

        // Keys taken by the game (F1 console, F11/F12 screenshots), our own mods (F3/F4/Insert/PageUp/PageDown Better
        // Tenement, F6 Save Guard, F8 the dev starter) or popular mods (F2, F5, F9, F7 UnityExplorer).
        private static readonly KeyCode[] Reserved =
        {
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9,
            KeyCode.F11, KeyCode.F12, KeyCode.Insert, KeyCode.PageUp, KeyCode.PageDown
        };

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Log each skipped own-move check, stolen mark, bagging decision, and each grace start, end and skip.");

            FreeInternalMoves = c.Bind("Theft", "FreeInternalMoves", true,
                "Moving items between your inventory, worn backpack and equipment never counts as theft and makes no noise. " +
                "Putting your own items (stolen or not) into other containers also makes no noise; taking from them is unchanged." + LaunchNote);
            IncludeEquipping = c.Bind("Theft", "IncludeEquipping", true,
                "Equipping or unequipping by using the item (double-click or Use) also counts as a free move. " +
                "Dragging to or from equipment slots is always free.");
            FixStolenTagging = c.Bind("Theft", "FixStolenTagging", true,
                "Put the stolen mark on the stacks that received the items, not on inventory slot 1." + LaunchNote);
            TagSourceRemainder = c.Bind("Theft", "TagSourceRemainder", true,
                "After a free move of part of a stolen stack, also mark what stays behind so the parts stack again.");
            RepairOnLoad = c.Bind("Theft", "RepairOnLoad", true,
                "After each load, add the game's stolen mark to stolen stacks you carry that lack it. Only adds marks; " +
                "it changes the mood effect of consuming them and which copies stack together.");
            FreeOwnUse = c.Bind("Theft", "FreeOwnUse", true,
                "Using, eating, drinking or reading something you carry never counts as theft again and makes no noise. " +
                "Its leftovers (wrapper, empty bottle, paper trash, package contents) keep the stolen mark if the item was stolen, " +
                "and stay allowed if someone gave it to you. Equipping by use falls under IncludeEquipping." + LaunchNote);
            FreeAmenityUse = c.Bind("Theft", "FreeAmenityUse", true,
                "Holding Use on someone else's item where it lies isn't theft if the item is in AmenityItems (soap, disinfectant, " +
                "magazines and newspapers by default). Taking the item still is, and shop stock always counts. " +
                "Any trash it leaves in your inventory is yours." + LaunchNote);
            AmenityItems = c.Bind("Theft", "AmenityItems", AmenityDefault,
                "Comma-separated item IDs covered by FreeAmenityUse. Default: Hand Soap, Bar Soap, Disinfectant and the seven " +
                "magazines and newspapers. Whatever a listed item leaves in your inventory becomes yours, so listing food or " +
                "animals would let you keep their leftovers too.");

            KeepOverflowInBag = c.Bind("Confiscation", "KeepOverflowInBag", true,
                "Items that might not fit your cell locker go into the jail's orange bag instead of onto the floor, where they despawn. " +
                "The count is cautious: every stack counts as if it needs its own slots. " +
                "Stolen goods are still confiscated." + LaunchNote);
            KeepPermittedItems = c.Bind("Confiscation", "KeepPermittedItems", true,
                "Jail no longer destroys items that belong to someone else but you were allowed to have (given or lent to you). " +
                "An arrest puts them in your cell locker with their owner unchanged. If they might not fit, they go into the " +
                "orange bag when KeepOverflowInBag is on; otherwise the game drops them at the locker. A worn backpack you were allowed " +
                "to have goes to the cell's backpack spot, like your own. Stolen goods are still confiscated, including any inside that " +
                "backpack. Arrest searches and paid fines leave these items alone, and when a stolen backpack you wear is taken, they " +
                "move to your inventory like your own items. Off: the game treats them as stolen goods." + LaunchNote);
            RescueStolenBackpackContents = c.Bind("Confiscation", "RescueStolenBackpackContents", true,
                "When a stolen backpack you wear is taken (arrest search, fine), your own items in it move to your inventory first." + LaunchNote);
            SafePayFine = c.Bind("Confiscation", "SafePayFine", true,
                "Paying a fine takes your stolen goods only if the payment goes through." + LaunchNote);
            Notify = c.Bind("Confiscation", "Notify", true,
                "Show a notification when items go to the bag, are rescued from a stolen backpack, or a release is held.");
            WardenMentionsBag = c.Bind("Confiscation", "WardenMentionsBag", false,
                "Let the warden's \"orange bag\" line play when Fair Justice bagged items (fixes a typo in the game's own " +
                "flag). The game never resets the flag, so the warden keeps mentioning the bag afterwards.");

            SentencePanel = c.Bind("Prison", "SentencePanel", true, "Show time left and charges while jailed." + LaunchNote);
            ToggleKey = c.Bind("Prison", "ToggleKey", new KeyboardShortcut(KeyCode.F10), "Show or hide the panel.");
            ShowWhileWaiting = c.Bind("Prison", "ShowWhileWaiting", true, "Keep the panel visible while sleeping or waiting in the cell.");
            Anchor = c.Bind("Prison", "Anchor", PanelAnchor.TopRight, "Screen corner of the panel: TopLeft, TopRight, BottomLeft or BottomRight.");
            OffsetX = c.Bind("Prison", "OffsetX", 170,
                new ConfigDescription("Horizontal distance from the corner, in 1080p pixels.", new AcceptableValueRange<int>(0, 1000)));
            OffsetY = c.Bind("Prison", "OffsetY", 180,
                new ConfigDescription("Vertical distance from the corner, in 1080p pixels.", new AcceptableValueRange<int>(0, 800)));
            Scale = c.Bind("Prison", "Scale", 1.0f,
                new ConfigDescription("Panel size.", new AcceptableValueRange<float>(0.5f, 2.5f)));

            CellDoorGrace = c.Bind("CellDoorGrace", "Enabled", true,
                "Right after you're locked up, stepping through the still-open cell door isn't trespassing (#5847)." + LaunchNote);
            CellDoorGraceSeconds = c.Bind("CellDoorGrace", "Seconds", 20,
                new ConfigDescription("Seconds of play; paused, loading and sleeping time doesn't count.", new AcceptableValueRange<int>(0, 120)));
            AfterLoadInJail = c.Bind("CellDoorGrace", "AfterLoadInJail", true,
                "Also after loading a save made in jail, at most once per stay in jail per game session (reloading again " +
                "does not renew it). Needs CellDoorGrace.Enabled.");
            CellDoorAreaGuids = c.Bind("CellDoorGrace", "AreaGuids", BazaarCell,
                "Comma-separated trespass areas covered (Trespassing type only). Default: the Bazaar cell block.");

            DeferNightRelease = c.Bind("NightRelease", "Enabled", false,
                "A release due at night waits until morning." + LaunchNote);
            NightStartHour = c.Bind("NightRelease", "NightStartHour", 22,
                new ConfigDescription("Hour the night starts.", new AcceptableValueRange<int>(0, 23)));
            MorningHour = c.Bind("NightRelease", "MorningHour", 6,
                new ConfigDescription("Hour held releases happen.", new AcceptableValueRange<int>(0, 23)));

            ReleaseGrace = c.Bind("ReleaseGrace", "Enabled", true,
                "A short grace period after you serve your time; any new crime ends it at once." + LaunchNote);
            ReleaseGraceSeconds = c.Bind("ReleaseGrace", "Seconds", 60,
                new ConfigDescription("Seconds of play; paused, loading and sleeping time doesn't count.", new AcceptableValueRange<int>(0, 300)));
            StopPursuits = c.Bind("ReleaseGrace", "StopPursuits", true,
                "On release, call off police pursuits the way the game does on arrest (#6183)." + LaunchNote);
            ReleaseGraceTrespass = c.Bind("ReleaseGrace", "Trespass", true,
                "During the grace, the listed jail areas don't count as trespassing." + LaunchNote);
            ReleaseAreaGuids = c.Bind("ReleaseGrace", "AreaGuids", ReleaseDefault,
                "Comma-separated trespass areas covered. Default: Bazaar cell block, kitchen and storage; Gatehouse main area and both cells. " +
                "The Bazaar office and warden's room are left out on purpose.");
            ReleaseGracePolice = c.Bind("ReleaseGrace", "Police", true,
                "During the grace, police ignore sightings of you, unless you carry stolen goods or crimes they would " +
                "question you about. Where this applies is set by PoliceAnyScene." + LaunchNote);
            PoliceAnyScene = c.Bind("ReleaseGrace", "PoliceAnyScene", false,
                "On: every level, including outside the jail (for example Open Sewer Bazaar right after a Bazaar release). " +
                "Off: only in the prison level, which has no police in the unmodded game, so the police part does nothing.");

            NoPassOutReArrest = c.Bind("PassOut", "NoReArrest", true,
                "Passing out in jail can't get you arrested again (#5543); stolen goods are still taken." + LaunchNote);

            StopChaseAtTenement = c.Bind("PoliceLimits", "StopChaseAtTenement", false,
                "Police chasing you don't follow you into the levels in NoFollowScenes (your tenement's interior by default); " +
                "they search for you where they are. Off by default, because the game means police to follow you indoors." + LaunchNote);
            NoFollowScenes = c.Bind("PoliceLimits", "NoFollowScenes", "Interior Player Tenement",
                "Comma-separated level names for StopChaseAtTenement. Default: Interior Player Tenement.");
            StopAtLockedDoors = c.Bind("PoliceLimits", "StopAtLockedDoors", false,
                "Police chasing you, or coming to question you, stop at a closed, locked door between you and them and search " +
                "for you instead of going through. Patrols and searching police are unchanged, so an officer stopped here may still " +
                "pass the door later while he searches. Off by default, because in the game police may open locked doors.");
            LockedDoorLookahead = c.Bind("PoliceLimits", "Lookahead", 1.5f,
                new ConfigDescription("How far ahead StopAtLockedDoors looks for a locked door, in seconds of the officer's movement " +
                                      "(always at least 1.5 m). If Unofficial Patch's NPC door fix is installed, set this at least as " +
                                      "high as its Lookahead (0.8 by default) so the stop comes before that fix opens the door.",
                                      new AcceptableValueRange<float>(0.5f, 4f)));

            CheckHotkey(ToggleKey);
            ToggleKey.SettingChanged += (s, a) => CheckHotkey(ToggleKey);
            CellDoorAreas = ParseGuids(CellDoorAreaGuids.Value);
            ReleaseAreas = ParseGuids(ReleaseAreaGuids.Value);
            CellDoorAreaGuids.SettingChanged += (s, a) => CellDoorAreas = ParseGuids(CellDoorAreaGuids.Value);
            ReleaseAreaGuids.SettingChanged += (s, a) => ReleaseAreas = ParseGuids(ReleaseAreaGuids.Value);
            AmenityIds = ParseIds(AmenityItems.Value);
            NoFollowSceneSet = ParseNames(NoFollowScenes.Value);
            AmenityItems.SettingChanged += (s, a) => AmenityIds = ParseIds(AmenityItems.Value);
            NoFollowScenes.SettingChanged += (s, a) => NoFollowSceneSet = ParseNames(NoFollowScenes.Value);
        }

        // The binding is honoured; it only warns.
        private static void CheckHotkey(ConfigEntry<KeyboardShortcut> e)
        {
            var key = e.Value.MainKey;
            if (Array.IndexOf(Reserved, key) >= 0)
                Log.Warn($"{e.Definition.Section}.{e.Definition.Key} is bound to {key}, which the game or another popular mod already uses.");
        }

        private static HashSet<string> ParseGuids(string value)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (value ?? "").Split(','))
            {
                var g = part.Trim();
                if (g.Length > 0) set.Add(g);
            }
            return set;
        }

        // Item IDs; an entry that is not a number is skipped with a warning.
        private static HashSet<int> ParseIds(string value)
        {
            var set = new HashSet<int>();
            foreach (var part in (value ?? "").Split(','))
            {
                var p = part.Trim();
                if (p.Length == 0) continue;
                if (int.TryParse(p, out int id) && id > 1) set.Add(id);
                else Log.Warn($"Theft.AmenityItems: '{p}' is not an item ID; skipped.");
            }
            return set;
        }

        // Scene names may contain spaces; only the commas separate them. Matched exactly, as Scene.name is.
        private static HashSet<string> ParseNames(string value)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in (value ?? "").Split(','))
            {
                var n = part.Trim();
                if (n.Length > 0) set.Add(n);
            }
            return set;
        }

        // [NightStartHour, MorningHour), wrapping past midnight. Equal hours mean no night window.
        internal static bool InNightWindow(float hour)
        {
            float start = NightStartHour.Value, end = MorningHour.Value;
            hour = ((hour % 24f) + 24f) % 24f;
            if (start == end) return false;
            return start < end ? hour >= start && hour < end : hour >= start || hour < end;
        }

        internal static string MorningText => $"{MorningHour.Value:00}:00";
    }
}
