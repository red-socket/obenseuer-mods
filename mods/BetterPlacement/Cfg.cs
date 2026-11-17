using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace BetterPlacement
{
    internal enum AlignMode { Hold, Toggle, Always }

    internal enum PadButton { None, DPadLeft, DPadRight }

    internal enum RememberScope { Session, Save }

    // Every tunable value (spec §8). Entries are read live inside every patch body and helper, so turning a feature off
    // takes effect at once. A feature that was off at launch has no patches applied; it starts at the next launch
    // (LaunchNote). All description strings are public text (humanized 2026-11-16).
    internal static class Cfg
    {
        private const string LaunchNote = " If this was off when the game started, it takes effect at the next launch.";

        // Keys our other mods or popular community mods already use (F2/F9 community mods, F3/F4/F6/F8/F10 and
        // Insert/PageUp/PageDown/Home/End/Delete/U our own mods, F7 UnityExplorer, F11/F12 screenshots, the middle mouse
        // button Tidy Inventory's pin key, 1-9 the hands and quick slots). Binding one of these only logs a warning: the
        // player's choice stands. Real clashes are found by Keys.CheckCollisions.
        private static readonly KeyCode[] Reserved =
        {
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6, KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10,
            KeyCode.F11, KeyCode.F12, KeyCode.Insert, KeyCode.PageUp, KeyCode.PageDown, KeyCode.Home, KeyCode.End, KeyCode.Delete,
            KeyCode.U, KeyCode.Mouse2,
        };

        internal static ConfigEntry<bool> VerboseLogging, ShowControlsHint;

        internal static class Align
        {
            internal static ConfigEntry<bool> Enabled, Centres, Edges, Flush, ScaleWithDistance, MatchRotation, WallItems, LooseItems, ShowGuides;
            internal static ConfigEntry<AlignMode> Mode;
            internal static ConfigEntry<KeyboardShortcut> Key;
            internal static ConfigEntry<PadButton> PadButton;
            internal static ConfigEntry<float> SnapDistance, ReleaseFactor, SearchRadius, FlushGap, AxisTolerance, RotationTolerance,
                MatchDistance, GuideWidth;
            internal static ConfigEntry<int> MaxNeighbours;
            internal static ConfigEntry<string> CentreColour, EdgeColour;
        }

        internal static class Rotation
        {
            internal static ConfigEntry<bool> TriggerFix, IgnoreTriggersOnKeyboard, SnapEnabled, RoundOnPress, RememberAngle;
            internal static ConfigEntry<float> TriggerDeadzone;
            internal static ConfigEntry<KeyboardShortcut> SnapKey, CoarseKey;
            internal static ConfigEntry<int> FineStep, CoarseStep;
            internal static ConfigEntry<PadButton> SnapPadButton;
            internal static ConfigEntry<RememberScope> RememberScope;
        }

        internal static class FlatItems
        {
            internal static ConfigEntry<bool> Enabled;
            internal static ConfigEntry<string> Categories, ExtraFurniture;
            internal static ConfigEntry<float> MinStickOut, LowColliderHeight;
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Log every snap, refused placement, remembered angle and flat-item decision. Useful for bug reports.");
            ShowControlsHint = c.Bind("General", "ShowControlsHint", true,
                "Add the align and rotation-step keys to the controls line at the bottom of the building menu." + LaunchNote);

            // ---- Align (U-008) ----
            Align.Enabled = c.Bind("Align", "Enabled", true,
                "In building mode, hold Key (or D-pad left) to line the piece you're placing up with furniture nearby: centres, " +
                "edges and flush sides, on the floor and on walls. It only moves the piece to spots the game already allows." + LaunchNote);
            Align.Mode = c.Bind("Align", "Mode", AlignMode.Hold,
                "Hold: aligns while the key is held. Toggle: a tap turns it on or off, and it goes off again when you close the building " +
                "menu, go through a door or switch away from the game. Always: aligns unless the key is held.");
            Align.Key = c.Bind("Align", "Key", new KeyboardShortcut(KeyCode.LeftAlt),
                "The align key. Some Linux desktops use Alt+drag to move windows; pick Toggle mode or another key if that gets in the way.");
            Align.PadButton = c.Bind("Align", "PadButton", BetterPlacement.PadButton.DPadLeft,
                "The controller button for aligning. D-pad up and down are taken by our other mods.");
            Align.Centres = c.Bind("Align", "Centres", true, "Line up the centres.");
            Align.Edges = c.Bind("Align", "Edges", true, "Line up matching edges (front with front, side with side).");
            Align.Flush = c.Bind("Align", "Flush", true, "Push pieces side by side so they touch, with FlushGap between them.");
            Align.SnapDistance = c.Bind("Align", "SnapDistance", 0.10f,
                new ConfigDescription("How close to a line the piece has to be before it snaps, in metres.", new AcceptableValueRange<float>(0.02f, 0.5f)));
            Align.ScaleWithDistance = c.Bind("Align", "ScaleWithDistance", true,
                "Snap from further away when you place things far from you (up to twice SnapDistance).");
            Align.ReleaseFactor = c.Bind("Align", "ReleaseFactor", 1.5f,
                new ConfigDescription("Once snapped, the piece lets go at this many times SnapDistance, so it doesn't flicker between lines.",
                    new AcceptableValueRange<float>(1f, 3f)));
            Align.SearchRadius = c.Bind("Align", "SearchRadius", 1.5f,
                new ConfigDescription("How far away furniture can be and still be aligned to, in metres.", new AcceptableValueRange<float>(0.5f, 4f)));
            Align.MaxNeighbours = c.Bind("Align", "MaxNeighbours", 24,
                new ConfigDescription("How many nearby pieces are considered at once (the nearest ones).", new AcceptableValueRange<int>(4, 64)));
            Align.FlushGap = c.Bind("Align", "FlushGap", 0.005f,
                new ConfigDescription("The gap left between pieces pushed side by side, in metres. Touching pieces can count as overlapping.",
                    new AcceptableValueRange<float>(0f, 0.05f)));
            Align.AxisTolerance = c.Bind("Align", "AxisTolerance", 1.5f,
                new ConfigDescription("How far, in degrees, two pieces can be turned from each other and still have their edges lined up.",
                    new AcceptableValueRange<float>(0.1f, 10f)));
            Align.MatchRotation = c.Bind("Align", "MatchRotation", true,
                "When aligning starts, turn the piece to match the nearest piece if it's already almost lined up (see RotationTolerance). " +
                "It happens at most once each time you start aligning, and not at all if you've already turned the piece yourself, " +
                "so it never fights you when you rotate.");
            Align.RotationTolerance = c.Bind("Align", "RotationTolerance", 10f,
                new ConfigDescription("MatchRotation only turns the piece if it's within this many degrees of the nearby piece.",
                    new AcceptableValueRange<float>(1f, 30f)));
            Align.MatchDistance = c.Bind("Align", "MatchDistance", 0.6f,
                new ConfigDescription("MatchRotation only looks at pieces this close, in metres.", new AcceptableValueRange<float>(0.1f, 2f)));
            Align.WallItems = c.Bind("Align", "WallItems", true, "Also align things on walls: paintings, shelves, curtains.");
            Align.LooseItems = c.Bind("Align", "LooseItems", true, "Also align loose items (bottles, plates, tools) and align to them.");
            Align.ShowGuides = c.Bind("Align", "ShowGuides", true, "Draw a thin line along each line the piece is snapped to.");
            Align.CentreColour = c.Bind("Align", "CentreColour", "#4FC3F7CC", "Guide line colour for centres (#RRGGBBAA).");
            Align.EdgeColour = c.Bind("Align", "EdgeColour", "#FFB74DCC", "Guide line colour for edges and flush sides (#RRGGBBAA).");
            Align.GuideWidth = c.Bind("Align", "GuideWidth", 0.01f,
                new ConfigDescription("Guide line width, in metres.", new AcceptableValueRange<float>(0.002f, 0.05f)));

            // ---- Rotation (S-083) ----
            Rotation.TriggerFix = c.Bind("Rotation", "TriggerFix", true,
                "Stops furniture spinning on its own in building mode when a controller is plugged in and a trigger doesn't rest at zero." + LaunchNote);
            Rotation.TriggerDeadzone = c.Bind("Rotation", "TriggerDeadzone", 0.35f,
                new ConfigDescription("How far a trigger has to be pulled before it rotates anything. The game uses 0.1. Just past this " +
                    "point the piece turns as slowly as the game's lightest pull, and a full pull is as fast as ever.",
                    new AcceptableValueRange<float>(0f, 0.9f)));
            Rotation.IgnoreTriggersOnKeyboard = c.Bind("Rotation", "IgnoreTriggersOnKeyboard", true,
                "While you're using the mouse and keyboard, a trigger only counts once it's pulled well past where it rests. " +
                "A real pull still rotates and switches to the controller.");
            Rotation.SnapEnabled = c.Bind("Rotation", "SnapEnabled", true,
                "While rotating, hold SnapKey (or D-pad right) to turn in FineStep steps, or CoarseKey for CoarseStep steps, whatever " +
                "the rotation mode is. You still rotate the usual way: hold the rotate button and move the mouse, or pull a trigger. " +
                "Let go and you're back in your own mode." + LaunchNote);
            Rotation.SnapKey = c.Bind("Rotation", "SnapKey", new KeyboardShortcut(KeyCode.Mouse3),
                "Hold while rotating to turn in FineStep steps. Mouse3 is the fourth mouse button (the game calls it Mouse 4). Shift is the game's walk " +
                "key, so it isn't used. If your mouse has no side buttons, Y is free.");
            Rotation.FineStep = c.Bind("Rotation", "FineStep", 15,
                new ConfigDescription("Degrees per step while SnapKey is held.", new AcceptableValueList<int>(5, 10, 15, 30, 45)));
            Rotation.CoarseKey = c.Bind("Rotation", "CoarseKey", new KeyboardShortcut(KeyCode.Mouse4),
                "Hold while rotating to turn in CoarseStep steps. Mouse4 is the fifth mouse button (the game calls it Mouse 5). Set to None to turn it off.");
            Rotation.CoarseStep = c.Bind("Rotation", "CoarseStep", 45,
                new ConfigDescription("Degrees per step while CoarseKey is held.", new AcceptableValueList<int>(5, 10, 15, 30, 45)));
            Rotation.SnapPadButton = c.Bind("Rotation", "SnapPadButton", PadButton.DPadRight,
                "Hold this while pulling a trigger to rotate in FineStep steps.");
            Rotation.RoundOnPress = c.Bind("Rotation", "RoundOnPress", true,
                "When you press a snap key, round the current angle to the step at once, like the game does when you change rotation mode.");
            Rotation.RememberAngle = c.Bind("Rotation", "RememberAngle", true,
                "Each kind of furniture or item starts at the angle you last placed it at." + LaunchNote);
            Rotation.RememberScope = c.Bind("Rotation", "RememberScope", BetterPlacement.RememberScope.Session,
                "Session: angles are kept until you quit the game, and nothing is written anywhere. Save: they're also kept in your " +
                "save as one line of text, which the game ignores if you remove the mod.");

            // ---- Flat items (S-083 #5848) ----
            FlatItems.Enabled = c.Bind("FlatItems", "Enabled", false,
                "Rugs can go under furniture, and curtains can hang behind it, even where the game says the spot is taken. A rug can go " +
                "under any furniture that rises clear of it, chairs and beds included. Walls, doors, other rugs, flat things and items " +
                "you can pick up still block rugs, and a curtain still can't cover anything you can use. This changes the game's " +
                "placement rules, so it's off by default." + LaunchNote);
            FlatItems.Categories = c.Bind("FlatItems", "Categories", "Rugs, Curtains",
                "Building menu categories this applies to, comma-separated, as the menu names them. Spaces and capitals don't matter.");
            FlatItems.ExtraFurniture = c.Bind("FlatItems", "ExtraFurniture", "",
                "More furniture it applies to, comma-separated: the name the building menu shows (any style's name works) or the " +
                "furniture's id. Spaces and capitals don't matter.");
            FlatItems.MinStickOut = c.Bind("FlatItems", "MinStickOut", 0.03f,
                new ConfigDescription("Furniture has to rise at least this far above a rug (or stick out this far past a curtain), in metres, " +
                    "to be allowed over it. Flat things stay blocking, so you can still pick them up.", new AcceptableValueRange<float>(0.01f, 0.2f)));
            FlatItems.LowColliderHeight = c.Bind("FlatItems", "LowColliderHeight", 0.03f,
                new ConfigDescription("Bumps in the floor (thresholds, seams) lower than this, in metres, don't block rugs.",
                    new AcceptableValueRange<float>(0.005f, 0.05f)));

            foreach (var (e, _) in Hotkeys())
            {
                var entry = e;
                CheckReserved(entry);
                entry.SettingChanged += (s, a) => { CheckReserved(entry); Keys.Recheck(); Hint.Refresh(); };
            }
            Align.Mode.SettingChanged += (s, a) => { AlignInput.ResetToggle(); Hint.Refresh(); };
            Align.PadButton.SettingChanged += (s, a) => { Keys.Recheck(); Hint.Refresh(); };
            Rotation.SnapPadButton.SettingChanged += (s, a) => { Keys.Recheck(); Hint.Refresh(); };
            Align.Enabled.SettingChanged += (s, a) => Hint.Refresh();
            Rotation.SnapEnabled.SettingChanged += (s, a) => Hint.Refresh();
            Rotation.FineStep.SettingChanged += (s, a) => Hint.Refresh();
            Rotation.CoarseStep.SettingChanged += (s, a) => Hint.Refresh();
        }

        // Every hotkey with the feature name used in warnings and notifications.
        internal static IEnumerable<(ConfigEntry<KeyboardShortcut> entry, string feature)> Hotkeys()
        {
            yield return (Align.Key, "aligning furniture");
            yield return (Rotation.SnapKey, "rotation snapping");
            yield return (Rotation.CoarseKey, "coarse rotation snapping");
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

        // #RRGGBBAA colours, parsed once per value; an unreadable value falls back to the default with one warning.
        private static readonly Dictionary<ConfigEntry<string>, (string raw, Color colour)> Colours =
            new Dictionary<ConfigEntry<string>, (string raw, Color colour)>();

        internal static Color Colour(ConfigEntry<string> e)
        {
            string raw = e.Value ?? "";
            if (Colours.TryGetValue(e, out var cached) && cached.raw == raw) return cached.colour;
            string s = raw.Trim();
            if (s.Length > 0 && s[0] != '#') s = "#" + s;
            if (!ColorUtility.TryParseHtmlString(s, out var c))
            {
                Log.WarnOnce("colour|" + e.Definition.Key + "|" + raw, $"{e.Definition.Section}.{e.Definition.Key}: \"{raw}\" is not a colour; using {e.DefaultValue}.");
                ColorUtility.TryParseHtmlString((string)e.DefaultValue, out c);
            }
            Colours[e] = (raw, c);
            return c;
        }
    }
}
