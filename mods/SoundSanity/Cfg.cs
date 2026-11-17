using System.Collections.Generic;
using BepInEx.Configuration;

namespace SoundSanity
{
    // Machine categories (spec §4.1). The order is the config order.
    internal enum Category { Crushers, Grinders, Saws, Brewing, Smithing, Machining, Generators, Furnaces, Workshop, OtherMachines }

    // Every tunable value (spec §6). Entries are read live inside every patch body; SettingChanged only marks the matching
    // live pass dirty (Core/Live.cs), so a slider drag in a config manager costs one pass per frame. A feature that was off
    // at launch has no patches applied; it starts at the next launch (LaunchNote). All description strings are public
    // text and go through the humanizer skill before release.
    internal static class Cfg
    {
        private const string LaunchNote = " If this was off when the game started, it takes effect at the next launch.";

        internal const string DefaultMusicPattern = @"^radio_(\d|welho|rob|casino|internet|behind|hilla|nain_on_marjat|kurahaara|general_store)|music";
        internal const string DefaultExcludePattern = @"^radio_(static|on|bathroom_)";

        internal static ConfigEntry<bool> VerboseLogging;

        internal static class Routing
        {
            internal static ConfigEntry<bool> Enabled, FixMainGroup, FixCharacterSounds;
        }

        internal static class Machines
        {
            internal static ConfigEntry<bool> Enabled;
            internal static readonly Dictionary<Category, ConfigEntry<int>> Levels = new Dictionary<Category, ConfigEntry<int>>();
            internal static ConfigEntry<string> ExtraRules;
        }

        internal static class Radio
        {
            internal static ConfigEntry<bool> Enabled, FollowMusicSlider, MuteInteractiveRadios, MuteAmbientRadios;
            internal static ConfigEntry<int> RadioMusicVolume;
            internal static ConfigEntry<string> MusicClipPattern, ExcludeClipPattern;
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Writes extra detail to the BepInEx log. Leave it off unless you are reporting a bug.");
            VerboseLogging.SettingChanged += (s, a) => Log.Info($"Verbose logging {(VerboseLogging.Value ? "on" : "off")}.");

            Routing.Enabled = c.Bind("Routing", "Enabled", true,
                "Fix world sounds that skip the volume sliders (room upgrades, lifts, doors, flickering lights, shop previews and similar). " +
                "They then follow the Master and Sound sliders, go quiet while you sleep, and earmuffs muffle them. Turning this off puts them back on their original mixer at once." + LaunchNote);
            Routing.FixMainGroup = c.Bind("Routing", "FixMainGroup", false,
                "Also fix the few lifts, keypads and props that follow only the Master slider.");
            Routing.FixCharacterSounds = c.Bind("Routing", "FixCharacterSounds", false,
                "Also fix voices and footsteps of residents and other people that ignore the sliders.");
            foreach (var e in new[] { Routing.Enabled, Routing.FixMainGroup, Routing.FixCharacterSounds })
                e.SettingChanged += (s, a) => Live.MarkDirty(Live.Pass.Routing);

            Machines.Enabled = c.Bind("Machines", "Enabled", true,
                "Gives loud crafting machines their own volume settings. Changes apply at once, even to running machines." + LaunchNote);
            Machines.Enabled.SettingChanged += (s, a) => Live.MarkDirty(Live.Pass.Machines);
            Level(c, Category.Crushers, "the Small Jaw Crusher and the mine's Ore Crusher");
            Level(c, Category.Grinders, "the Angle Grinder, the Meat Grinder, the Electric Meat Grinder and the Grain Mill");
            Level(c, Category.Saws, "the Bandsaw, the Table Saw and the sawbucks");
            Level(c, Category.Brewing, "the Mashing Kettle, the Mashing Barrel, the Copper Moonshine Still, the Distillation Kit and the moonshine distilleries");
            Level(c, Category.Smithing, "the Anvil, the Power Hammer and the Hydraulic Press");
            Level(c, Category.Machining, "the Metalworking Lathe and the Geared Head Drill");
            Level(c, Category.Generators, "the generators, the Compressor and the Biogas Reactor");
            Level(c, Category.Furnaces, "the Brick Furnace and the Gas Furnace");
            Level(c, Category.Workshop, "the Loom, the Printing Press, the Rolling Machine, the Sewing Machine, the chemical reactors and the 3D Printer");
            Level(c, Category.OtherMachines, "any other machine with a running sound, such as dishwashers, the Smoker, the Malter, the Electric Kiln, stoves and fireplaces");
            Machines.ExtraRules = c.Bind("Machines", "ExtraRules", "",
                "Advanced: moves machines to another group. Write name=Category pairs separated by ';', for example Electric Kiln=Furnaces. " +
                "A name is a machine's in-game name or the start of a sound name. A sound rule moves every machine that uses that sound. " +
                "These rules take priority over the built-in groups. With VerboseLogging on, the log shows each machine's group and the sound that decided it. " +
                "Unknown categories are ignored and noted in the log.");
            Machines.ExtraRules.SettingChanged += (s, a) =>
            {
                Categories.Reparse();
                Stations.ForgetCategories();
                Live.MarkDirty(Live.Pass.Machines);
            };

            Radio.Enabled = c.Bind("Radio", "Enabled", true,
                "Music volume and mute settings for radios, record players and background radio music." + LaunchNote);
            Radio.RadioMusicVolume = c.Bind("Radio", "RadioMusicVolume", 100,
                new ConfigDescription("Radio music volume, in percent. With FollowMusicSlider on, it applies on top of the Music slider: " +
                    "Music at 50% and this at 50 play radio music at 25%.", new AcceptableValueRange<int>(0, 100)));
            Radio.FollowMusicSlider = c.Bind("Radio", "FollowMusicSlider", true,
                "Radio music follows the game's Music slider. Radios the game already plays through its music channel follow the slider anyway.");
            Radio.MuteInteractiveRadios = c.Bind("Radio", "MuteInteractiveRadios", false,
                "Silence the music of radios and record players you can switch on. They stay switched on.");
            Radio.MuteAmbientRadios = c.Bind("Radio", "MuteAmbientRadios", false,
                "Silence background radio and bar music that has no switch.");
            Radio.MusicClipPattern = c.Bind("Radio", "MusicClipPattern", DefaultMusicPattern,
                "Advanced: a regular expression for the sound names that count as music.");
            Radio.ExcludeClipPattern = c.Bind("Radio", "ExcludeClipPattern", DefaultExcludePattern,
                "Advanced: a regular expression for sound names that never count as music (static, switch clicks).");
            foreach (var e in new[] { Radio.Enabled, Radio.FollowMusicSlider, Radio.MuteInteractiveRadios, Radio.MuteAmbientRadios })
                e.SettingChanged += (s, a) => Live.MarkDirty(Live.Pass.Radios | Live.Pass.Loops);
            Radio.RadioMusicVolume.SettingChanged += (s, a) => Live.MarkDirty(Live.Pass.Radios | Live.Pass.Loops);
            foreach (var e in new[] { Radio.MusicClipPattern, Radio.ExcludeClipPattern })
                e.SettingChanged += (s, a) =>
                {
                    Clips.Recompile();
                    Live.MarkDirty(Live.Pass.Radios | Live.Pass.Loops);
                };
        }

        private static void Level(ConfigFile c, Category cat, string stations)
        {
            var e = c.Bind("Machines", cat.ToString(), 100,
                new ConfigDescription($"Volume of {stations}, in percent of the game's own volume. 0 silences them.",
                    new AcceptableValueRange<int>(0, 100)));
            e.SettingChanged += (s, a) => Live.MarkDirty(Live.Pass.Machines);
            Machines.Levels[cat] = e;
        }
    }
}
