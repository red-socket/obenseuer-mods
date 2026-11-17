using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SoundSanity
{
    // Runtime sources no scene sweep sees (spec §3.3, R4/R5/R7). Each target is optional and patched on its own: a missing
    // one logs a warning and drops only itself.
    internal static class SpawnPatches
    {
        internal static void Fix(AudioSource s, string where)
        {
            if (s == null || !Groups.Spawns.On) return;
            try { Router.Handle(s, Router.Ctx.HookPath, where); }
            catch (Exception e) { Groups.Spawns.Fail(e); }
        }

        internal static void Root(GameObject go, string where)
        {
            if (go == null || !Groups.Spawns.On || !Router.Active) return;
            try
            {
                Sweeps.SweepRoot(go, false, where);
                Scheduler.QueueRoot(go, where);
            }
            catch (Exception e) { Groups.Spawns.Fail(e); }
        }
    }

    // R4: the AddSound helpers. LightsON passes its prefab's (copy) group or none; LightFlicker, Bell and UseTriggerMoney
    // create sources with no group (SRC/LightsON.cs:99, LightFlicker.cs:76, Bell.cs:87, UseTriggerMoney.cs:162).
    [HarmonyPatch]
    internal static class LightsOnAddSoundPatch
    {
        private static MethodBase TargetMethod() => Vanilla.LightsOnAddSound;
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(AudioSource __result) => SpawnPatches.Fix(__result, "LightsON");
    }

    [HarmonyPatch]
    internal static class LightFlickerAddSoundPatch
    {
        private static MethodBase TargetMethod() => Vanilla.LightFlickerAddSound;
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(AudioSource __result) => SpawnPatches.Fix(__result, "LightFlicker");
    }

    [HarmonyPatch]
    internal static class BellAddSoundPatch
    {
        private static MethodBase TargetMethod() => Vanilla.BellAddSound;
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(AudioSource __result) => SpawnPatches.Fix(__result, "Bell");
    }

    [HarmonyPatch]
    internal static class MoneyAddSoundPatch
    {
        private static MethodBase TargetMethod() => Vanilla.MoneyAddSound;
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(AudioSource __result) => SpawnPatches.Fix(__result, "UseTriggerMoney");
    }

    // R5: tenant upgrade prefabs instantiated from Addressables (fluorescent_lights_001, aquarium_loop_001 and others on
    // the mixer copy). ProcessGUIDs runs for apartment upgrades; UpdateInstantiatedPrefab after every building upgrade
    // swap plus the facade and basement blocks.
    [HarmonyPatch]
    internal static class ProcessGuidsPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ProcessGuids;
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(GameObject go) => SpawnPatches.Root(go, "apartment upgrade");
    }

    [HarmonyPatch]
    internal static class UpdateInstantiatedPrefabPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateInstantiatedPrefab;
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(GameObject gameObject) => SpawnPatches.Root(gameObject, "building upgrade");
    }

    // R7: ShopBaseUI.ChangeSelection(BuildingSystemShopSlot, bool) (SRC/ShopBaseUI.cs:339): the furniture preview in a
    // shop is a plain Instantiate of the preview prefab, which vanilla never resolves.
    [HarmonyPatch]
    internal static class ShopPreviewPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ChangeSelection;

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(ShopBaseUI __instance)
        {
            if (__instance == null || Vanilla.CurrentPreview == null || !Groups.Spawns.On || !Router.Active) return;
            try
            {
                var go = Vanilla.CurrentPreview(__instance);
                if (go != null) Sweeps.SweepRoot(go, false, "shop preview");
            }
            catch (Exception e) { Groups.Spawns.Fail(e); }
        }
    }
}
