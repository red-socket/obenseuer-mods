using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FieldGuide
{
    // H3/H4: Sauna.OnTriggerEnter/OnTriggerExit(Collider) postfixes (spec §5, D6). Saunas with no stove of their own
    // (the Bazaar Sauna's zones, Mines Baths, Bazaar Books) get the walk-in pop-up while the player is inside. Passive
    // rooms (the hot tub) are skipped. So is a sauna contest, and any building area: there, with no menu open, the pad's
    // View button also opens the building menu (SRC/BuildingSystem.cs:627), so a pop-up would take the key.
    [HarmonyPatch]
    internal static class SaunaEnterPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SaunaTriggerEnter;

        private static void Postfix(Sauna __instance, Collider other)
        {
            if (!Cfg.Hot.PublicSaunas || !Cfg.Hot.On(HelpKind.Sauna)) return;
            try
            {
                if (other == null || !other.CompareTag("Player") || !__instance.enabled || __instance.passive) return;
                if (Classify.HasStove(__instance) || ContestController.ContestActive) return;
                if (BuildingSystem.instance != null && BuildingSystem.instance.inBuildingArea) return;
                if (HintHost.IsTracked(__instance)) return;
                HintHost.CloseKind(HelpKind.Sauna, __instance);
                HintHost.Open(__instance, HelpKind.Sauna, HintTexts.Sauna(null, __instance), station: false);
            }
            catch (Exception e)
            {
                Log.Exception("Help: sauna enter", e);
                try { HintHost.Close(__instance); HintOverlay.Hide(__instance); }
                catch { }
            }
        }
    }

    [HarmonyPatch]
    internal static class SaunaExitPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SaunaTriggerExit;

        private static void Postfix(Sauna __instance, Collider other)
        {
            try
            {
                if (other != null && other.CompareTag("Player")) HintHost.Close(__instance);
            }
            catch (Exception e) { Log.Exception("Help: sauna exit", e); }
        }
    }
}
