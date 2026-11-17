using OS.Items;
using UnityEngine;

namespace GreenThumb
{
    // S-061a (spec §7, D10), off by default. A ripe fruit's integrity is rewritten from its vine life on every tick
    // (GrowItem.UpdateSpoilTime, SRC/GrowItem.cs:354-394: integrity = 1 - currentSpoilTime/randomSpoilTime in the fruit's
    // own meta). Right after that write, Full sets it back to 1 and Floor raises it to FloorPercent. Done at the source,
    // so the space checks (InventoryIsNotFull with that meta, :181/:221/:296, and CanRepeatHarvest) and the add in
    // GetItem (ApplySkillQuality(CloneMeta(meta)), :276-277) see the same value; GetItem itself is never patched.
    // Vine visuals, the fruit dropping at the end of its vine life, the fruit tooltip, Spoiled() and spoiled yields stay
    // vanilla. Produce only: animal and egg yields are never touched. Saves: the vanilla float in the vanilla meta; after
    // removal the next ripe tick writes the vanilla value back.
    internal static class FreshPicking
    {
        internal static void Apply(GrowItem f)
        {
            var mode = Cfg.Hot.FreshMode;
            if (mode == FreshMode.Off || f == null) return;
            if (f.currentSpoilTime >= Vanilla.RandomSpoilTime(f)) return;   // dropped or spoiled: vanilla's branch owns it
            var m = Vanilla.GrowItemMeta(f);
            if (m == null) return;
            var it = Vanilla.GrowItemItem(f)?.item?.Item;
            if (it == null || it.Categories == null || !it.ifInCategory("Plant")) return;
            for (int i = 0; i < m.Length; i++)
                if (m[i] is ItemAnimalData) return;
            float floor = Cfg.Hot.FloorFraction;
            for (int i = 0; i < m.Length; i++)
            {
                if (!(m[i] is ItemPerishabledata d)) continue;
                d.integrity = mode == FreshMode.Full ? 1f : Mathf.Max(d.integrity, floor);
            }
        }
    }
}
