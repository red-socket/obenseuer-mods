using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace GreenThumb
{
    internal enum NoMoreFruit { ShowPlantLife, ZeroPercent }
    internal enum FreshMode { Off, Full, Floor }
    internal enum StackScope { Produce, Food }
    internal enum HarvestReach { Vanilla, WholeGrower }
    internal enum GhostMode { Vanilla, HideNearReady, HideModel, CrouchOnly, Off }

    // Every tunable value (spec §10). Entries marked LaunchNote gate a patch group at launch: turning them off works at
    // once (patch bodies read Hot), turning them on needs a restart. There are no hotkeys: harvest-all rides on the
    // game's own Use hold and CrouchOnly on the game's own crouch (D13, D21). Hot caches every value a patch body reads
    // and is refreshed on SettingChanged. Description strings are public text: run them through the humanizer again after
    // any change (CLAUDE.md).
    internal static class Cfg
    {
        private const string LaunchNote = " Restart the game after turning this on.";

        internal static ConfigEntry<bool> VerboseLog;

        internal static class Growth
        {
            internal static ConfigEntry<bool> FixFruitPercent;
            internal static ConfigEntry<NoMoreFruit> WhenNoMoreFruit;
        }

        internal static class Sunlight
        {
            internal static ConfigEntry<bool> Refresh;
            internal static ConfigEntry<float> IntervalSeconds;
            internal static ConfigEntry<int> ChecksPerFrame;
        }

        internal static class Power
        {
            internal static ConfigEntry<bool> PauseWithoutPower, PausedHint;
        }

        internal static class FreshPicking
        {
            internal static ConfigEntry<FreshMode> Mode;
            internal static ConfigEntry<int> FloorPercent;
        }

        internal static class FreshnessStacking
        {
            internal static ConfigEntry<bool> Enabled;
            internal static ConfigEntry<int> BandWidthPercent;
            internal static ConfigEntry<StackScope> Scope;
        }

        internal static class HarvestAll
        {
            internal static ConfigEntry<bool> Enabled, IncludeOwned, IncludeDead, ShowHint;
            internal static ConfigEntry<float> HoldSeconds, MaxDistance;
            internal static ConfigEntry<HarvestReach> Reach;
        }

        internal static class QuickPlant
        {
            internal static ConfigEntry<GhostMode> GhostMode;
            internal static ConfigEntry<float> NearReadyMeters;
        }

        // Cached values read by patch bodies (spec §10). One field read per call instead of a ConfigEntry lookup.
        internal static class Hot
        {
            internal static bool FruitPercent, ShowPlantLife, Power, PausedHint, Bands, Weight;
            internal static bool HarvestAll, IncludeOwned, IncludeDead, ShowHint;
            internal static FreshMode FreshMode;
            internal static float FloorFraction, HoldSeconds, MaxDistance, NearReadySqr, IntervalSeconds;
            internal static int ChecksPerFrame;
            internal static StackScope Scope;
            internal static HarvestReach Reach;
            internal static GhostMode GhostMode;
            // Band edges (spec §8.2), ascending. Never empty: 0.3 and 0.7 are always kept.
            internal static float[] Edges = { 0.3f, 0.7f };

            internal static void Refresh()
            {
                FruitPercent = Growth.FixFruitPercent.Value;
                ShowPlantLife = Growth.WhenNoMoreFruit.Value == NoMoreFruit.ShowPlantLife;
                IntervalSeconds = Sunlight.IntervalSeconds.Value;
                ChecksPerFrame = Sunlight.ChecksPerFrame.Value;
                Power = Cfg.Power.PauseWithoutPower.Value;
                PausedHint = Cfg.Power.PausedHint.Value;
                FreshMode = FreshPicking.Mode.Value;
                FloorFraction = FreshPicking.FloorPercent.Value / 100f;
                Bands = Weight = FreshnessStacking.Enabled.Value;
                Edges = BuildEdges(FreshnessStacking.BandWidthPercent.Value);
                if (Scope != FreshnessStacking.Scope.Value) Eligible.Clear();
                Scope = FreshnessStacking.Scope.Value;
                HarvestAll = Cfg.HarvestAll.Enabled.Value;
                HoldSeconds = Cfg.HarvestAll.HoldSeconds.Value;
                Reach = Cfg.HarvestAll.Reach.Value;
                MaxDistance = Cfg.HarvestAll.MaxDistance.Value;
                IncludeOwned = Cfg.HarvestAll.IncludeOwned.Value;
                IncludeDead = Cfg.HarvestAll.IncludeDead.Value;
                ShowHint = Cfg.HarvestAll.ShowHint.Value;
                GhostMode = QuickPlant.GhostMode.Value;
                float r = QuickPlant.NearReadyMeters.Value;
                NearReadySqr = r * r;
            }

            // {k·W/100 : k ≥ 1, k·W < 100} ∪ {0.30, 0.70}, sorted, near-duplicates dropped. W = 0 gives the three
            // vanilla-meaningful bands (below 30, 30-70, 70 and up).
            private static float[] BuildEdges(int w)
            {
                var l = new List<float> { 0.3f, 0.7f };
                if (w > 0)
                    for (int k = 1; k * w < 100; k++) l.Add(k * w / 100f);
                l.Sort();
                var o = new List<float>();
                foreach (var e in l)
                    if (o.Count == 0 || Math.Abs(e - o[o.Count - 1]) > 1e-4f) o.Add(e);
                return o.ToArray();
            }
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLog = c.Bind("General", "VerboseLog", false,
                "Write extra lines to the BepInEx log: sunlight re-checks, power changes and harvest-all sweeps.");

            Growth.FixFruitPercent = c.Bind("Growth", "FixFruitPercent", true,
                "Fix a game bug: \"Growing fruits\" counts up to the next ripe fruit instead of down." + LaunchNote);
            Growth.WhenNoMoreFruit = c.Bind("Growth", "WhenNoMoreFruit", NoMoreFruit.ShowPlantLife,
                "For plants that won't grow any more fruit: ShowPlantLife shows \"Spoiling\" with the time the plant has left, ZeroPercent shows \"Growing fruits 0%\".");

            Sunlight.Refresh = c.Bind("Sunlight", "Refresh", true,
                "Fix a game bug: planters check their sunlight again every few seconds, so moving furniture or walls changes Sun and Moist without a reload." + LaunchNote);
            Sunlight.IntervalSeconds = c.Bind("Sunlight", "IntervalSeconds", 10f,
                new ConfigDescription("Real seconds between checks.", new AcceptableValueRange<float>(2f, 600f)));
            Sunlight.ChecksPerFrame = c.Bind("Sunlight", "ChecksPerFrame", 16,
                new ConfigDescription("Planters checked per frame during a pass.", new AcceptableValueRange<int>(1, 64)));

            Power.PauseWithoutPower = c.Bind("Power", "PauseWithoutPower", true,
                "Fix a game bug: growers that run on power (terrariums, aquariums, hydroponic, aquatic and vertical farms, the mushroom growing cabinet, the flowerbox with lamp) stop growing while they have no power or are switched off. Growers that don't use power never stop." + LaunchNote);
            Power.PausedHint = c.Bind("Power", "PausedHint", true,
                "Show a line on plants in a stopped grower that says whether it has no power or is switched off." + LaunchNote);

            FreshPicking.Mode = c.Bind("FreshPicking", "Mode", FreshMode.Off,
                "Fruit on the plant doesn't lose freshness before you pick it. Full: always 100%. Floor: never below FloorPercent. Off: as in the game. This changes the game's balance." + LaunchNote);
            FreshPicking.FloorPercent = c.Bind("FreshPicking", "FloorPercent", 70,
                new ConfigDescription("Lowest freshness of fruit on the plant in Floor mode.", new AcceptableValueRange<int>(1, 100)));

            FreshnessStacking.Enabled = c.Bind("FreshnessStacking", "Enabled", true,
                "Produce of similar freshness goes in one stack, which takes the average freshness of everything in it. Bands break at 30% and 70%, but as in the game, stacks less than 2.5% apart still join." + LaunchNote);
            FreshnessStacking.BandWidthPercent = c.Bind("FreshnessStacking", "BandWidthPercent", 10,
                new ConfigDescription("Width of a freshness band in percent. 0 means three bands: below 30, 30 to 70, and 70 and up.", new AcceptableValueRange<int>(0, 50)));
            FreshnessStacking.Scope = c.Bind("FreshnessStacking", "Scope", StackScope.Produce,
                "Produce: farm vegetables, fruit, berries, mushrooms and algae. Food: all stackable food that spoils.");

            HarvestAll.Enabled = c.Bind("HarvestAll", "Enabled", true,
                "Keep holding Use on a planter, rack or field to harvest every ready plant within reach." + LaunchNote);
            HarvestAll.HoldSeconds = c.Bind("HarvestAll", "HoldSeconds", 1.2f,
                new ConfigDescription("Seconds you hold Use before everything within reach is harvested.", new AcceptableValueRange<float>(0.6f, 5f)));
            HarvestAll.Reach = c.Bind("HarvestAll", "Reach", HarvestReach.Vanilla,
                "Vanilla: plants you could reach by hand. WholeGrower: all your own plants in the grower you aim at, up to MaxDistance away." + LaunchNote);
            HarvestAll.MaxDistance = c.Bind("HarvestAll", "MaxDistance", 12f,
                new ConfigDescription("Metres. Only used with WholeGrower.", new AcceptableValueRange<float>(2f, 30f)));
            HarvestAll.IncludeOwned = c.Bind("HarvestAll", "IncludeOwned", false,
                "Also harvest plants that belong to someone else, within normal reach. Each one counts as theft, the same as picking it by hand.");
            HarvestAll.IncludeDead = c.Bind("HarvestAll", "IncludeDead", true,
                "Also clear dead plants, as holding Use already does.");
            HarvestAll.ShowHint = c.Bind("HarvestAll", "ShowHint", true,
                "Mention the longer hold in the harvest prompt." + LaunchNote);

            QuickPlant.GhostMode = c.Bind("QuickPlant", "GhostMode", GhostMode.HideNearReady,
                "When the quick-plant bar and plant preview show. HideNearReady: not next to plants that are ready to harvest. HideModel: the bar without the preview. CrouchOnly: only while you crouch. Off: never (plant from the panel). Vanilla: as in the game." + LaunchNote);
            QuickPlant.NearReadyMeters = c.Bind("QuickPlant", "NearReadyMeters", 1f,
                new ConfigDescription("Distance from a ready plant, in metres, within which HideNearReady hides the preview.", new AcceptableValueRange<float>(0.25f, 3f)));

            Hot.Refresh();
            c.SettingChanged += (s, a) =>
            {
                Hot.Refresh();
                if (a?.ChangedSetting == Power.PauseWithoutPower) Plugin.Step("power switch", PowerFix.OnSwitchChanged);
            };
        }
    }
}
