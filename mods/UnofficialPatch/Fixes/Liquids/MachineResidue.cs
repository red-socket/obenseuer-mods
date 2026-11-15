using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UnofficialPatch
{
    // S-051 machines (spec §8a). CraftingBase.liquidRemaining is a raw float dictionary (SRC/CraftingBase.cs:255); each
    // Fill subtracts liquidAdded (:814-820), which ItemLiquidData floors to 0.1 L steps unless it lies within 1e-4 dL of
    // a step (ItemLiquidData.cs:141-162). Float drift leaves e.g. 0.0999861 L: it floors to 0, so a Fill never drains
    // it, and ClearTraceLiquids (:1555-1572) only removes entries below 0.05 L, so the still or tun stays stuck.
    // The postfix runs where vanilla's cleanup runs (manufacturingProcess.Enter, twice per opening), then vanilla's
    // own Count == 0 check opens the lid. Per entry:
    //   v <= ResidueLitres            -> removed;
    //   ResidueLitres < v < 0.1       -> set to exactly 0.1 L, which a Fill drains (creates under 0.05 L);
    //   v >= 0.1 within 0.005 of a step -> snapped to the step (float drift only; real amounts are never rounded).
    // OpenLid is never called by the mod: in the Processing stage it fails the batch (manufacturingProcess.cs:255-258).
    // Idempotent. Healed machines stay healed after removal.
    internal static class MachineResidue
    {
        internal static bool Patched;

        private static readonly List<int> Keys = new List<int>();

        internal static void Heal(CraftingBase cb)
        {
            if (!Patched || !Cfg.ClearMachineResidue.Value || cb == null) return;
            var d = cb.liquidRemaining;
            if (d == null || d.Count == 0) return;
            float residue = Cfg.ResidueLitres.Value;
            Keys.Clear();
            Keys.AddRange(d.Keys);
            foreach (int id in Keys)
            {
                float v = d[id];
                if (v <= residue)
                {
                    d.Remove(id);
                    Log.Info($"Liquids: cleared {v:0.#######} L of trace {LiquidName(id)} from {Name(cb)}; it can be emptied again.");
                }
                else if (v < 0.1f)
                {
                    d[id] = 0.1f;
                    Log.Info($"Liquids: rounded {v:0.#######} L of {LiquidName(id)} in {Name(cb)} up to 0.1 L, so a container can drain it.");
                }
                else
                {
                    float s = Mathf.Floor(v * 10f + 0.5f) / 10f;
                    if (s != v && Mathf.Abs(s - v) <= 0.005f)
                    {
                        d[id] = s;
                        Log.Verbose($"Liquids: rounded {v:0.#######} L of {LiquidName(id)} in {Name(cb)} to {s:0.0} L.");
                    }
                }
            }
        }

        internal static string LiquidName(int id)
        {
            var item = ItemDatabase.instance != null ? ItemDatabase.instance.FetchItemByID(id) : null;
            return item != null ? item.Title : "liquid " + id;
        }

        internal static string Name(Component c) => c == null ? "?" : c.gameObject.name.Replace("(Clone)", "").Trim();
    }

    // CraftingBase.ClearTraceLiquids(), protected.
    [HarmonyPatch]
    internal static class ClearTraceLiquidsPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ClearTraceLiquids;

        private static void Postfix(CraftingBase __instance)
        {
            try { MachineResidue.Heal(__instance); }
            catch (Exception e) { Log.Exception("Liquids: machine residue", e); }
        }
    }
}
