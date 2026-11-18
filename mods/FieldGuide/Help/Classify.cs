using System;
using System.Collections.Generic;
using UnityEngine;

namespace FieldGuide
{
    // Which guide a station gets (spec §6.1). Never uses ManuName, which translations change: only the station's class,
    // its RecipeType list, its RecipeCondition modifiers and its place in the prefab. Results are cached per instance id
    // and cleared on every scene load (Unity reuses ids across scenes).
    //
    //   Mashing  manufacturingProcess with RecipeType.Mashing: Mashing Barrel ("Mashing tun", fuel), Mashing Kettle
    //            (500 W), scene tuns.
    //   Corker   manufacturing with RecipeType.Bottler: Bottle Corker and its scene copies.
    //   Biogas   manufacturing with RecipeType.Compost and RecipeCondition.Advanced (the reactor's lid), or a Generator
    //            with such a lid as a direct child of one of its 3 nearest ancestors (compressor_usable/compressor).
    //   Compost  manufacturing with RecipeType.Compost and no Advanced: Compost.prefab and the NPC compost bins.
    //   Sauna    Generator whose parent has a direct child with a non-passive Sauna (the stove next to "Sauna Trigger").
    internal static class Classify
    {
        private const int NoKind = -1;
        private static readonly Dictionary<int, int> Kinds = new Dictionary<int, int>();
        private static readonly Dictionary<int, bool> Stoves = new Dictionary<int, bool>();

        internal static void Clear()
        {
            Kinds.Clear();
            Stoves.Clear();
        }

        internal static HelpKind? Of(CraftingBase cb)
        {
            if (cb == null) return null;
            int id = cb.GetInstanceID();
            if (!Kinds.TryGetValue(id, out int k))
            {
                k = Compute(cb);
                Kinds[id] = k;
                if (k != NoKind) Log.Verbose($"Help: {Path(cb.transform)} is {(HelpKind)k}.");
            }
            return k == NoKind ? (HelpKind?)null : (HelpKind)k;
        }

        private static int Compute(CraftingBase cb)
        {
            if (cb is manufacturingProcess && Has(cb.Recipes, RecipeType.Mashing)) return (int)HelpKind.Mashing;
            if (cb is manufacturing m)
            {
                if (Has(m.Recipes, RecipeType.Bottler)) return (int)HelpKind.Corker;
                if (Has(m.Recipes, RecipeType.Compost)) return (int)(IsBiogasLid(m) ? HelpKind.Biogas : HelpKind.Compost);
            }
            if (cb is Generator g)
            {
                if (BiogasLidOf(g) != null) return (int)HelpKind.Biogas;
                if (SaunaOfStove(g) != null) return (int)HelpKind.Sauna;
            }
            return NoKind;
        }

        // The reactor's lid: a compost station that runs the Advanced (biogas) recipes.
        internal static bool IsBiogasLid(manufacturing m)
        {
            if (m == null || !Has(m.Recipes, RecipeType.Compost)) return false;
            var mods = m.ModifiersWithUpgrades;
            return mods != null && mods.Contains(RecipeCondition.Advanced);
        }

        // The lid that belongs with this station: the station itself if it is the lid, otherwise a lid that is a direct
        // child of one of the 3 nearest ancestors (the compressor sits in compressor_usable under the reactor).
        internal static manufacturing BiogasLidOf(CraftingBase cb)
        {
            if (cb == null) return null;
            if (cb is manufacturing self && IsBiogasLid(self)) return self;
            var t = cb.transform.parent;
            for (int depth = 0; depth < 3 && t != null; depth++, t = t.parent)
            {
                for (int i = 0; i < t.childCount; i++)
                {
                    var lid = t.GetChild(i).GetComponent<manufacturing>();
                    if (lid != null && IsBiogasLid(lid)) return lid;
                }
            }
            return null;
        }

        // The non-passive sauna next to a stove (a direct child of the stove's parent, inactive ones included).
        internal static Sauna SaunaOfStove(Generator g)
        {
            var parent = g != null ? g.transform.parent : null;
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var s = parent.GetChild(i).GetComponent<Sauna>();
                if (s != null && !s.passive) return s;
            }
            return null;
        }

        // A sauna whose parent holds a stove (a Generator) as a direct child, inactive ones included. Those saunas get
        // the stove's pop-up instead of the walk-in one (D6).
        internal static bool HasStove(Sauna s)
        {
            if (s == null) return false;
            int id = s.GetInstanceID();
            if (Stoves.TryGetValue(id, out bool has)) return has;
            has = false;
            var parent = s.transform.parent;
            if (parent != null)
                for (int i = 0; i < parent.childCount && !has; i++)
                    has = parent.GetChild(i).GetComponent<Generator>() != null;
            Stoves[id] = has;
            return has;
        }

        private static bool Has(RecipeType[] list, RecipeType t) => list != null && Array.IndexOf(list, t) >= 0;

        internal static string Path(Transform t)
        {
            try
            {
                var parts = new List<string>();
                for (int i = 0; t != null && i < 12; i++, t = t.parent) parts.Add(t.name);
                parts.Reverse();
                return string.Join("/", parts.ToArray());
            }
            catch { return "?"; }
        }
    }
}
