using System;
using HarmonyLib;
using UnityEngine;

namespace UnofficialPatch
{
    // S-058 cats hunt rats (spec §11a, #6213). Animal.HuntTarget (SRC/Animal.cs:667-700) aborts when
    // animalData.currentHunger <= 0.25 or Random.Range(0.25, 1) > currentHunger, but both cat prefabs ship with
    // hungerEnabled 0 and currentHunger 0 (cat_001.prefab, cat_001_player.prefab), and AnimalStats.UpdateHunger only
    // runs with hungerEnabled (and divides by zero there), so every hunt is cancelled. For a Cat with hunger disabled
    // and a target of an allowed type, the prefix sets currentHunger to 1 for the duration of the call and the
    // finalizer restores it, so the value never reaches a save. hungerEnabled is never turned on. The player is never
    // a target (glowing cats have playerHuntChance 30); Chick and Bird targets put livestock at risk, so Rat only by
    // default. The vanilla post-hunt cooldown is unchanged. Stateless.
    internal static class CatHunting
    {
        internal static bool Patched;

        internal sealed class Held
        {
            internal AnimalStats Stats;
            internal float Hunger;
        }

        internal static Held Before(Animal a, Transform target)
        {
            if (!Patched || !Cfg.HuntRats.Value || !(a is Cat) || target == null) return null;
            var stats = a.GetComponent<AnimalStats>();
            if (stats == null || stats.animalData == null || stats.animalData.hungerEnabled) return null;
            var t = target.GetComponentInChildren<Stats>();
            if (t == null || t.type == Stats.Type.Player || !Cfg.HuntTypes.Contains(t.type)) return null;
            if (Log.VerboseOn) Log.Verbose($"Cats: {a.gameObject.name} hunts a {t.type}.");
            var held = new Held { Stats = stats, Hunger = stats.animalData.currentHunger };
            stats.animalData.currentHunger = 1f;   // last statement: nothing can throw between this and the return
            return held;
        }

        internal static void After(Held h)
        {
            if (h == null || h.Stats == null || h.Stats.animalData == null) return;
            h.Stats.animalData.currentHunger = h.Hunger;
        }
    }

    // Animal.HuntTarget(Transform).
    [HarmonyPatch(typeof(Animal), nameof(Animal.HuntTarget), new[] { typeof(Transform) })]
    internal static class HuntTargetPatch
    {
        private static void Prefix(Animal __instance, Transform target, out CatHunting.Held __state)
        {
            __state = null;
            try { __state = CatHunting.Before(__instance, target); }
            catch (Exception e) { Log.Exception("Cats: hunt prefix", e); }
        }

        // Runs whatever happens in HuntTarget, so the temporary hunger never stays.
        private static void Finalizer(CatHunting.Held __state)
        {
            try { CatHunting.After(__state); }
            catch (Exception e) { Log.Exception("Cats: hunt finalizer", e); }
        }
    }
}
