using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SoundSanity
{
    // S-074 machine volume (spec §4.1). Stations register their running sounds in CraftingBase.Start (M1): the power hum
    // and its on/off one-shots, the craft loop, and a vessel's start/stop sounds. Opening, closing and locked sounds are
    // interaction feedback and stay as they are. The category is resolved per station (Machines/Categories.cs) from its
    // names and the runtime clip names of those sounds, never as a global clip key, and cached until ExtraRules changes.
    internal static class Stations
    {
        private sealed class Info
        {
            internal bool Resolved, HasCategory, Logged;
            internal Category Category;
            internal string Why;
        }

        private static readonly HashSet<CraftingBase> All = new HashSet<CraftingBase>();
        private static readonly ConditionalWeakTable<CraftingBase, Info> Infos = new ConditionalWeakTable<CraftingBase, Info>();
        private static readonly List<SoundInfo> Buf = new List<SoundInfo>(6);
        private static readonly List<CraftingBase> Dead = new List<CraftingBase>();

        // The scaled sounds in category-resolution order: OnSound, ManufactureSound, StartSound, TurnOnSound, TurnOffSound,
        // StopSound. All are public and [fsIgnore]; CopyFsIgnoredFieldsTo keeps the same instances through a load.
        private static void Scaled(CraftingBase cb, List<SoundInfo> into)
        {
            into.Clear();
            var p = cb._Power;
            var proc = cb as manufacturingProcess;
            Add(into, p?.OnSound);
            Add(into, cb.ManufactureSound);
            Add(into, proc?.StartSound);
            Add(into, p?.TurnOnSound);
            Add(into, p?.TurnOffSound);
            Add(into, proc?.StopSound);
        }

        private static void Add(List<SoundInfo> l, SoundInfo si)
        {
            if (si != null && !l.Contains(si)) l.Add(si);
        }

        // M1 body. Idempotent: OnLoadingGame calls Start() again (SRC/CraftingBase.cs:401-428).
        internal static void OnStart(CraftingBase cb)
        {
            if (cb == null) return;
            All.Add(cb);
            Scaled(cb, Buf);
            foreach (var si in Buf) Registry.Register(si, Registry.Kind.Station, cb);
            // _Power.Init created the hum's source inside base.Start, before it was registered.
            Apply(cb);
        }

        internal static float Factor(CraftingBase cb)
        {
            if (cb == null || !Cfg.Machines.Enabled.Value || !Groups.Machines.On) return 1f;
            var info = InfoOf(cb);
            if (!info.HasCategory) return 1f;
            float f = Cfg.Machines.Levels[info.Category].Value / 100f;
            if (f < 0.9999f && !info.Logged)
            {
                info.Logged = true;
                Log.InfoOnce($"mach|{Title(cb)}|{info.Category}", $"Machines: '{Title(cb)}' plays at {Cfg.Machines.Levels[info.Category].Value}% ({info.Category}, {info.Why}).");
            }
            return f;
        }

        private static Info InfoOf(CraftingBase cb)
        {
            var info = Infos.GetOrCreateValue(cb);
            if (info.Resolved) return info;
            info.Resolved = true;
            info.HasCategory = Resolve(cb, out info.Category, out info.Why);
            if (Log.VerboseOn)
                Log.Verbose(info.HasCategory
                    ? $"Machines: '{Title(cb)}' (ManuName '{cb.ManuName}') is in {info.Category} ({info.Why})."
                    : $"Machines: '{Title(cb)}' (ManuName '{cb.ManuName}') has no running sound in any category; left as it is.");
            return info;
        }

        // Order (spec §4.1, Categories.cs): ExtraRules by station name, ExtraRules by sound, built-in station names,
        // built-in sounds, then OtherMachines when any scaled sound loops. Why names what decided it, for the log, so a
        // player can write an ExtraRules entry.
        private static bool Resolve(CraftingBase cb, out Category cat, out string why)
        {
            var list = new List<SoundInfo>(6);
            Scaled(cb, list);
            string title = Title(cb), manu = cb.ManuName;
            if (Categories.ExtraName(title, out cat) || Categories.ExtraName(manu, out cat)) { why = "ExtraRules, by name"; return true; }
            foreach (var si in list)
                if (si.Clip != null && Categories.ExtraSound(si.Clip.name, out cat)) { why = $"ExtraRules, sound '{si.Clip.name}'"; return true; }
            if (Categories.Station(manu, out cat)) { why = "by name"; return true; }
            foreach (var si in list)
                if (si.Clip != null && Categories.Sound(si.Clip.name, out cat)) { why = $"sound '{si.Clip.name}'"; return true; }
            foreach (var si in list)
                if (si.Looping && si.Clip != null) { cat = Category.OtherMachines; why = $"sound '{si.Clip.name}'"; return true; }
            cat = default;
            why = null;
            return false;
        }

        internal static void ForgetCategories()
        {
            foreach (var cb in All)
                if (cb != null && Infos.TryGetValue(cb, out var info)) { info.Resolved = false; info.Logged = false; }
        }

        private static string Title(CraftingBase cb)
        {
            var fp = cb.GetComponentInParent<FurniturePlaceable>();
            if (fp != null && fp.furniture != null && !string.IsNullOrEmpty(fp.furniture.title)) return fp.furniture.title;
            return !string.IsNullOrEmpty(cb.ManuName) ? cb.ManuName : cb.name;
        }

        private static void Apply(CraftingBase cb)
        {
            Scaled(cb, Buf);
            foreach (var si in Buf)
                if (Registry.TryGet(si, out var e) && e.Kind == Registry.Kind.Station) Registry.ApplyLive(si, e);
        }

        // Drops stations Unity has destroyed (every level change reloads the scene). Called on each sceneLoaded and before
        // a live pass, so the set never holds the stations of earlier levels.
        internal static void Prune()
        {
            Dead.Clear();
            foreach (var cb in All)
                if (cb == null) Dead.Add(cb);
            foreach (var cb in Dead) All.Remove(cb);
            Dead.Clear();
        }

        // Live pass: every registered running sound of every live station. One-shots already in flight are left alone.
        internal static void LivePass()
        {
            if (!Groups.Machines.On || !Groups.GainCore.On) return;
            Prune();
            foreach (var cb in All) Apply(cb);
            Log.Verbose($"Machines: live pass over {All.Count} station(s).");
        }
    }
}
