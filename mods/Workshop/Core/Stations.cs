using System;
using System.Collections.Generic;

namespace Workshop
{
    // What kind of station a CraftingBase is for our features (spec §5.2 gate 4, §5.5, §7.1). Read live: AutoRun
    // depends on the config, and a crusher becomes Plain again the moment AutoRun is turned off.
    internal enum Kind
    {
        None,               // Growing, Generator and anything else
        Plain,              // manufacturing that runs without the player
        RequirePlayer,      // manufacturing that pauses when the player leaves (kitchen, sewing machine, anvil)
        Dishwasher,
        VanillaAutocraft,   // the game's own Autocraft flag (the mine's Ore Crusher)
        AutoRun,            // a crusher Workshop runs on its own (Queue.AutoRun)
        Vessel,             // manufacturingProcess: mashing tuns, fermenters, stills
    }

    internal static class Stations
    {
        internal static Kind KindOf(CraftingBase cb)
        {
            if (cb == null) return Kind.None;
            if (cb is Dishwasher) return Kind.Dishwasher;
            if (cb is manufacturing m)
            {
                if (m.Autocraft) return Kind.VanillaAutocraft;
                if (AutoRunEligible(m)) return Kind.AutoRun;
                return m.RequirePlayer ? Kind.RequirePlayer : Kind.Plain;
            }
            if (cb is manufacturingProcess) return Kind.Vessel;
            return Kind.None;
        }

        // The S-040 crusher allowlist (spec §5.8): a named crusher with the game's Autocraft off that runs without the player.
        // Auto-run is driven by the queue pump, so it needs the queue (on, and patched at launch); without it the crusher
        // stays Plain and keeps its Fetch button.
        internal static bool AutoRunEligible(manufacturing m)
        {
            try
            {
                if (m == null || !Cfg.Queue.AutoRun.Value || !Cfg.Queue.Enabled.Value || !QueuePatches.Patched || m.Autocraft || m.RequirePlayer || m is Dishwasher) return false;
                if (!Cfg.List(Cfg.Queue.AutoRunMachines).Contains((m.ManuName ?? "").Trim())) return false;
                return m.Recipes != null && Array.IndexOf(m.Recipes, RecipeType.Crusher) >= 0;
            }
            catch (Exception e) { Log.Exception("auto-run check", e); return false; }
        }

        // The game's own "Owned" rule (SRC/CraftingBase.cs:582).
        internal static bool IsOwned(CraftingBase cb) => cb.owner != null && cb.owner.ID > 1 && !cb.PermissionToTake;

        internal static bool IsLocked(CraftingBase cb) => cb._Lock != null && cb._Lock.Locked;

        internal static bool Usable(CraftingBase cb) => cb != null && cb.enabled && !cb.IsInteractDisabled();

        internal static string Name(CraftingBase cb)
        {
            if (cb == null) return "?";
            if (!string.IsNullOrEmpty(cb.ManuName)) return cb.ManuName;
            return string.IsNullOrEmpty(cb.title) ? "Machine" : cb.title;
        }

        // A recipe's display name; the dishwasher's are the washed item's name.
        internal static string RecipeName(Recipe r) => r == null ? "?" : (string.IsNullOrEmpty(r.Name) ? "recipe " + r.ID : r.Name);

        internal static char KindChar(CraftingBase cb) => cb is manufacturingProcess ? 'p' : 'm';

        // The station's save key (spec §5.7): scene name and GUID, as the game keyed it on load. Empty when the
        // station has no GUID yet (a fresh placement) or is global.
        internal static string SaveKey(CraftingBase cb)
        {
            try
            {
                if (cb == null || cb.global || string.IsNullOrEmpty(cb.GUID)) return null;
                return cb.gameObject.scene.name + "|" + cb.GUID;
            }
            catch { return null; }
        }

        internal static List<Recipe> Recipes(CraftingBase cb) => Vanilla.AllRecipes != null ? Vanilla.AllRecipes(cb) : null;
    }
}
