using System;
using System.Collections.Generic;

namespace SoundSanity
{
    // Which category a station belongs to (spec §4.1), checked in this order: Machines.ExtraRules (a station name, then a
    // sound name prefix), the built-in station names, the built-in sound prefixes. Runtime clip names never carry
    // AssetRipper's "_0" suffix. A sound only counts when the station itself owns it, so soundscape loops and scene props
    // that reuse it are never affected.
    internal static class Categories
    {
        // Stations sorted by their ManuName (serialized, never set at runtime) before any sound is looked at. Their running
        // sounds are shared with stations of another kind: fire_loop_001 is the OnSound of every barrel, wood, metal and
        // tile stove (heating, not brewing), and boil_001 is the Biogas Reactor's only sound. Stoves and fireplaces fall
        // through to OtherMachines.
        private static readonly (string name, Category cat)[] BuiltInStations =
        {
            ("Mashing kettle", Category.Brewing), ("Mashing tun", Category.Brewing), ("Copper Still", Category.Brewing),
            ("Distillation Kit", Category.Brewing), ("Distillery", Category.Brewing),
            ("Biogas Reactor", Category.Generators),
        };

        private static readonly (string prefix, Category cat)[] BuiltInSounds =
        {
            ("machine_027", Category.Crushers),
            ("angle_grinder", Category.Grinders), ("meat_grinder", Category.Grinders), ("grain_mill", Category.Grinders),
            ("bandsaw", Category.Saws), ("circular_saw", Category.Saws), ("chainsaw", Category.Saws), ("handsaw", Category.Saws),
            ("boil_001", Category.Brewing), ("distil_loop", Category.Brewing),
            ("anvil_", Category.Smithing), ("powerhammer", Category.Smithing), ("machine_hydraulic", Category.Smithing),
            ("lathe_", Category.Machining), ("machining_loop", Category.Machining),
            ("diesel_generator", Category.Generators), ("generator_001", Category.Generators), ("compressor_001", Category.Generators),
            ("furnace_001", Category.Furnaces),
            ("loom_", Category.Workshop), ("old_printing_press", Category.Workshop), ("roller_001", Category.Workshop),
            ("sewing_machine", Category.Workshop), ("mixer_001", Category.Workshop), ("3d_printer", Category.Workshop),
        };

        private static (string key, Category cat)[] _extra = new (string, Category)[0];

        // Machines.ExtraRules: "key=Category;key=Category". A key is a station's in-game name (exact, any case) or the start
        // of one of its sound names. Unknown categories are ignored and noted once.
        internal static void Reparse()
        {
            var list = new List<(string, Category)>();
            foreach (var part in (Cfg.Machines.ExtraRules.Value ?? "").Split(';'))
            {
                var p = part.Trim();
                if (p.Length == 0) continue;
                int eq = p.IndexOf('=');
                string key = eq > 0 ? p.Substring(0, eq).Trim() : "";
                string name = eq > 0 ? p.Substring(eq + 1).Trim() : "";
                if (key.Length == 0 || !Enum.TryParse(name, true, out Category cat) || !Enum.IsDefined(typeof(Category), cat))
                {
                    Log.WarnOnce("rule|" + p, $"Machines.ExtraRules: ignoring '{p}' (expected name=Category, with a category from the [Machines] section).");
                    continue;
                }
                list.Add((key, cat));
            }
            _extra = list.ToArray();
        }

        internal static bool ExtraName(string name, out Category cat)
        {
            if (!string.IsNullOrEmpty(name))
                foreach (var (key, c) in _extra)
                    if (string.Equals(key, name.Trim(), StringComparison.OrdinalIgnoreCase)) { cat = c; return true; }
            cat = default;
            return false;
        }

        internal static bool ExtraSound(string clip, out Category cat) => Prefix(_extra, clip, out cat);

        internal static bool Station(string manuName, out Category cat)
        {
            if (!string.IsNullOrEmpty(manuName))
                foreach (var (name, c) in BuiltInStations)
                    if (string.Equals(name, manuName.Trim(), StringComparison.OrdinalIgnoreCase)) { cat = c; return true; }
            cat = default;
            return false;
        }

        internal static bool Sound(string clip, out Category cat) => Prefix(BuiltInSounds, clip, out cat);

        private static bool Prefix((string, Category)[] rules, string clip, out Category cat)
        {
            foreach (var (prefix, c) in rules)
                if (clip.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { cat = c; return true; }
            cat = default;
            return false;
        }
    }
}
