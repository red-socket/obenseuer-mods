using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnofficialPatch
{
    // S-051 tanks and sinks (spec §8b). LiquidStorage.IsEmpty counts only <= 0.001 L as empty (SRC/LiquidStorage.cs:490),
    // so a tank holding a sliver of one liquid refuses any other (ItemCanBeFilled/Unfilled) and blocks pick-up, sale and
    // dismantling (BuildingSystem.HasSaveableContent, :1722-1739). Heal empties such a tank with the game's own
    // ClearLiquid at discrete moments: after load, before a fill or unfill check, and before HasSaveableContent.
    // IsEmpty itself is never patched: Logic_liquidFiller fills from empty with SetLiquid (0.025 L per tick for the
    // lvl-1 water upgrade, Logic_liquidFiller.cs:99-108), so a wider "empty" would stop it ever filling. For the same
    // reason a tank an active filler feeds is left alone. Healed tanks stay healed after removal.
    internal static class TankResidue
    {
        internal static bool Patched;

        private static Logic_liquidFiller[] _fillers;
        private static int _fillerScene = -1;
        private static float _fillerTime = -100f;

        internal static void Heal(LiquidStorage s, string when)
        {
            if (!Patched || !Cfg.ClearTankResidue.Value || s == null) return;
            if (s.Capacity < 0f || s.Liquid == null) return;
            float amount = s.LiquidAmount;
            if (amount <= 0.001f || amount > Cfg.ResidueLitres.Value) return;
            if (BeingFilled(s)) return;
            string liquid = s.Liquid.Item != null ? s.Liquid.Item.Title : "liquid";
            s.ClearLiquid();
            Log.Info($"Liquids: emptied {amount:0.#######} L of trace {liquid} from {MachineResidue.Name(s)} ({when}).");
        }

        // An upgrade filler with a positive fill rate that targets this tank. The scene's fillers are cached for 10 s.
        private static bool BeingFilled(LiquidStorage s)
        {
            if (Vanilla.FillerStorage == null) return HasFillerNearby(s);
            int scene = s.gameObject.scene.handle;
            if (_fillers == null || scene != _fillerScene || Time.realtimeSinceStartup - _fillerTime > 10f)
            {
                _fillers = UnityEngine.Object.FindObjectsByType<Logic_liquidFiller>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                _fillerScene = scene;
                _fillerTime = Time.realtimeSinceStartup;
            }
            foreach (var f in _fillers)
                if (f != null && f.maxFillSpeed * f.fillRate > 0f && ReferenceEquals(Vanilla.FillerStorage(f), s)) return true;
            return HasFillerNearby(s);
        }

        // Spec fallback: a filler on the tank's own object tree.
        private static bool HasFillerNearby(LiquidStorage s)
        {
            foreach (var f in s.GetComponentsInParent<Logic_liquidFiller>(true))
                if (f.maxFillSpeed * f.fillRate > 0f) return true;
            foreach (var f in s.GetComponentsInChildren<Logic_liquidFiller>(true))
                if (f.maxFillSpeed * f.fillRate > 0f) return true;
            return false;
        }

        internal static void OnLoadingDone()
        {
            _fillers = null;
            _fillerScene = -1;
        }
    }

    // LiquidStorage.OnLoadingGame() (:301).
    [HarmonyPatch(typeof(LiquidStorage), nameof(LiquidStorage.OnLoadingGame))]
    internal static class LiquidStorageLoadPatch
    {
        private static void Postfix(LiquidStorage __instance, bool __result)
        {
            if (!__result) return;
            try { TankResidue.Heal(__instance, "on load"); }
            catch (Exception e) { Log.Exception("Liquids: tank on load", e); }
        }
    }

    // LiquidStorage.ItemCanBeFilled(ref ItemStack) (:846).
    [HarmonyPatch(typeof(LiquidStorage), nameof(LiquidStorage.ItemCanBeFilled))]
    internal static class ItemCanBeFilledPatch
    {
        private static void Prefix(LiquidStorage __instance)
        {
            try { TankResidue.Heal(__instance, "before filling"); }
            catch (Exception e) { Log.Exception("Liquids: tank fill check", e); }
        }
    }

    // LiquidStorage.ItemCanBeUnfilled(ref ItemStack) (:736).
    [HarmonyPatch(typeof(LiquidStorage), nameof(LiquidStorage.ItemCanBeUnfilled))]
    internal static class ItemCanBeUnfilledPatch
    {
        private static void Prefix(LiquidStorage __instance)
        {
            try { TankResidue.Heal(__instance, "before pouring"); }
            catch (Exception e) { Log.Exception("Liquids: tank unfill check", e); }
        }
    }

    // BuildingSystem.HasSaveableContent(GameObject) (:1722): picking up a tank with a trace no longer blocks Sell/Dismantle.
    [HarmonyPatch(typeof(BuildingSystem), nameof(BuildingSystem.HasSaveableContent), new[] { typeof(GameObject) })]
    internal static class HasSaveableContentPatch
    {
        private static void Prefix(GameObject gameObject)
        {
            try
            {
                if (!TankResidue.Patched || !Cfg.ClearTankResidue.Value || gameObject == null) return;
                foreach (var s in gameObject.GetComponentsInChildren<LiquidStorage>())
                    TankResidue.Heal(s, "when picked up");
            }
            catch (Exception e) { Log.Exception("Liquids: tank pick-up", e); }
        }
    }
}
