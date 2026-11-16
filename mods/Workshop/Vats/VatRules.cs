using System;
using BepInEx.Configuration;

namespace Workshop
{
    // S-086 Bigger Vats rules (spec §6.1). A vessel is matched on its manufacturingProcess fields Recipes, capacity
    // (decilitres) and EquipmentQuality: Unity-serialized and [fsIgnore], so they hold their prefab values in a
    // Storage.Start prefix, before any save is read. 9 is the game's own count.
    //   MashingTun      Mashing,    1000 dL, EQ < 1.5   Mashing Barrel ("Mashing tun", EQ 0.75)
    //   IndustrialTun   Mashing,    1000 dL, EQ >= 1.5  mashing_tun_001_usable (EQ 2)
    //   Fermenter1000L  Fermenter, 10000 dL             fermenting_unit_001_usable
    //   Distillery      Distillery, 1000 dL             Moonshine Distillery (EQ 0.45), moonshine_still_large_001_usable (EQ 2)
    // Not matched: Mashing Kettle (300), Fermenting Barrel and Bucket, Copper Still (700), small and medium stills.
    internal static class VatRules
    {
        internal const int Vanilla9 = 9, Max = 27;

        // The configured slot count for this vessel, or 0 when no rule matches.
        internal static int SlotsFor(manufacturingProcess p, out string rule)
        {
            rule = null;
            if (p == null || p.Recipes == null) return 0;
            var entry = Match(p);
            if (entry == null) return 0;
            rule = entry.Definition.Key;
            return Math.Max(Vanilla9, Math.Min(Max, entry.Value));
        }

        private static ConfigEntry<int> Match(manufacturingProcess p)
        {
            bool Has(RecipeType t) => Array.IndexOf(p.Recipes, t) >= 0;
            if (Has(RecipeType.Mashing) && p.capacity == 1000)
                return p.EquipmentQuality < 1.5f ? Cfg.BiggerVats.MashingTun : Cfg.BiggerVats.IndustrialTun;
            if (Has(RecipeType.Fermenter) && p.capacity == 10000) return Cfg.BiggerVats.Fermenter1000L;
            if (Has(RecipeType.Distillery) && p.capacity == 1000) return Cfg.BiggerVats.Distillery;
            return null;
        }
    }
}
