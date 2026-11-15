using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UnofficialPatch
{
    // S-073 sprint and crouch toggles (spec §7a). OpenSewerCharacterController lives in each scene and keeps the toggle
    // state in private fields (walking, default true = not sprinting, :46; crouching, :52) that are never saved, so
    // every door resets them (#4106 area, Tab's Tweaks "Toggles Persist"). SaveController.PlayerWillChangeLevel
    // (door, sleep and jail transitions; never menu loads) captures both. Sprint is restored in the new controller's
    // Awake; crouch through the public Crouch() (capsule, animator and eye height together) once the player stands at
    // the arrival point. Only in toggle mode. Nothing is saved.
    internal static class MovementToggles
    {
        internal static bool SprintPatched, CrouchOn;

        private static bool? _pendingWalking, _pendingCrouch;

        private static MethodBase _awake;
        internal static MethodBase AwakeTarget => _awake ?? (_awake = AccessTools.Method(typeof(OpenSewerCharacterController), nameof(OpenSewerCharacterController.Awake)));

        private static bool ToggleSprint => ControlsSave.instance != null && ControlsSave.instance.controlsSave != null && ControlsSave.instance.controlsSave.toggleSprint;
        private static bool ToggleCrouch => ControlsSave.instance != null && ControlsSave.instance.controlsSave != null && ControlsSave.instance.controlsSave.toggleCrouch;

        internal static void Capture()
        {
            _pendingWalking = _pendingCrouch = null;
            var c = OpenSewerCharacterController.instance;
            if (c == null) return;
            if (SprintPatched && Cfg.KeepSprintToggle.Value && Vanilla.Walking != null) _pendingWalking = Vanilla.Walking(c);
            if (CrouchOn && Cfg.KeepCrouchToggle.Value && Vanilla.Crouching != null) _pendingCrouch = Vanilla.Crouching(c);
        }

        internal static void AfterAwake(OpenSewerCharacterController c)
        {
            if (!_pendingWalking.HasValue || !Cfg.KeepSprintToggle.Value || c == null) return;
            bool walking = _pendingWalking.Value;
            _pendingWalking = null;
            if (!ToggleSprint || Compat.AnyOther(AwakeTarget, "keeping the sprint toggle")) return;
            Vanilla.Walking(c) = walking;
            if (!walking) Log.Verbose("Controls: sprint toggle kept after the level change.");
        }

        // Always clears both pending values (spec §7a [B]); the crouch itself waits for the arrival teleport.
        internal static void OnLoadingDone()
        {
            bool crouch = _pendingCrouch == true;
            _pendingWalking = _pendingCrouch = null;
            if (crouch && CrouchOn && Cfg.KeepCrouchToggle.Value && Plugin.Instance != null)
                Plugin.Instance.StartCoroutine(CrouchAfterArrival());
        }

        // A door entry teleports the player 0.1 s (realtime) after LoadingDone (SaveController.cs:905-908); waiting a
        // little longer means the water check sees the arrival point, not the spot the save was made at.
        private static IEnumerator CrouchAfterArrival()
        {
            yield return new WaitForSecondsRealtime(0.3f);
            yield return null;
            try
            {
                var c = OpenSewerCharacterController.instance;
                if (c == null || Vanilla.Crouching == null || !Cfg.KeepCrouchToggle.Value || !ToggleCrouch) yield break;
                var water = WaterController.instance;
                if (water != null && water.playerIsSwimming) yield break;
                if (Vanilla.Crouching(c)) yield break;
                c.Crouch();
                Log.Verbose("Controls: crouch toggle kept after the level change.");
            }
            catch (Exception e) { Log.Exception("Controls: crouch restore", e); }
        }
    }

    // OpenSewerCharacterController.Awake() (public override, :164).
    [HarmonyPatch(typeof(OpenSewerCharacterController), nameof(OpenSewerCharacterController.Awake))]
    internal static class CharacterAwakePatch
    {
        private static void Postfix(OpenSewerCharacterController __instance)
        {
            try { MovementToggles.AfterAwake(__instance); }
            catch (Exception e) { Log.Exception("Controls: sprint restore", e); }
        }
    }
}
