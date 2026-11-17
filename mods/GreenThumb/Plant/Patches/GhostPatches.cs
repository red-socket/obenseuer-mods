using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GreenThumb
{
    // Q1: Growing.UpdateQuickSlot(RaycastHit) bool prefix (SRC/Growing.cs:760; its only caller is
    // InteractObjects.GetInteractable :399-406, once per frame on the aimed grower). Skips the call in Off, in CrouchOnly
    // while standing, and in HideNearReady near a ready plant. Stands down if another mod can skip the method.
    [HarmonyPatch]
    internal static class QuickSlotPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateQuickSlot;

        private static bool Prefix(Growing __instance, RaycastHit hit)
        {
            if (Cfg.Hot.GhostMode == GhostMode.Vanilla || Cfg.Hot.GhostMode == GhostMode.HideModel || Ghost.ForeignSkip) return true;
            try { return !Ghost.Hide(__instance, hit.point); }
            catch (Exception e)
            {
                Log.Exception("Ghost: quick slot", e);
                return true;
            }
        }
    }

    // Q2: Growing.UpdateQuickPlantPreview() bool prefix (private, :970). HideModel keeps the bar and drops the preview.
    [HarmonyPatch]
    internal static class QuickPreviewPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateQuickPlantPreview;

        private static bool Prefix() => Cfg.Hot.GhostMode != GhostMode.HideModel;
    }
}
