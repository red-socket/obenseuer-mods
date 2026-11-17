using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterPlacement
{
    // S-083 rotation snapping (#6208, spec §3.4). While SnapKey (or the pad button) is held, ShowPreview runs with
    // rotationMode set to FineStep; while CoarseKey is held, to CoarseStep. The override lives only inside one
    // synchronous ShowPreview call: the prefix swaps the value in and the finalizer puts the player's own mode back,
    // even if ShowPreview throws. ChangeRotationMode and UpdateMenu are never called (both rebuild the preview). V and pad
    // Y run in BuildingSystem.Update before ShowPreview, so they always cycle the player's own mode. rotationMode is saved
    // (public, no fsIgnore), but a save can't land inside the call, and Save Guard refuses saves while the menu is open.
    internal static class RotationSnap
    {
        internal static bool Patched;
        // The held step (0 when none), shown by the controls hint (H1).
        internal static int HeldStep;
        // The last frame the prefix ran; Plugin.Update clears a stale HeldStep (Take/Move slot, menu closed).
        internal static int LastFrame = -1;

        internal static void Clear()
        {
            if (HeldStep == 0) return;
            HeldStep = 0;
            Hint.Refresh();
        }
    }

    // R1: BuildingSystem.ShowPreview(RaycastHit) (SRC/BuildingSystem.cs:1192), first prefix and a finalizer.
    [HarmonyPatch]
    internal static class RotationSnapPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ShowPreview;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(BuildingSystem __instance, out int __state)
        {
            __state = int.MinValue;
            try
            {
                RotationSnap.LastFrame = Time.frameCount;
                int step = 0;
                if (Cfg.Rotation.SnapEnabled.Value && Game.BuildOpen && __instance.currentPreview != null && !Vanilla.AttachPoint(__instance))
                {
                    bool fine = Keys.Held(Cfg.Rotation.SnapKey) || (!Keys.SnapPadSuspended && Keys.PadHeld(Cfg.Rotation.SnapPadButton.Value));
                    bool coarse = Keys.Held(Cfg.Rotation.CoarseKey);
                    step = coarse ? Cfg.Rotation.CoarseStep.Value : fine ? Cfg.Rotation.FineStep.Value : 0;
                }
                if (step != RotationSnap.HeldStep)
                {
                    RotationSnap.HeldStep = step;
                    // The first step after pressing is immediate.
                    Vanilla.RotationModeCooldown(__instance) = 0f;
                    if (step > 0 && Cfg.Rotation.RoundOnPress.Value)
                    {
                        // The same rounding ChangeRotationMode does (SRC/BuildingSystem.cs:688-691), on y only.
                        ref Vector3 rot = ref Vanilla.CurrentRot(__instance);
                        rot.y = Mathf.Round(rot.y / step) * step;
                    }
                    Log.Verbose("rotation step", step, s => s > 0 ? $"Rotation: snapping in {s}° steps." : "Rotation: snap released.");
                    Hint.Refresh();
                }
                if (step > 0)
                {
                    __state = __instance.rotationMode;
                    __instance.rotationMode = step;
                }
            }
            catch (Exception e) { Log.Exception("rotation snap", e); }
        }

        private static Exception Finalizer(BuildingSystem __instance, int __state, Exception __exception)
        {
            if (__state != int.MinValue)
            {
                try { __instance.rotationMode = __state; }
                catch (Exception e) { Log.Exception("rotation snap restore", e); }
            }
            return __exception;
        }
    }
}
