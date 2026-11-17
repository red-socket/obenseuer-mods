using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterPlacement
{
    // S-083 trigger fix (spec §3.3). ShowPreview reads both triggers raw with a 0.1 threshold
    // (SRC/BuildingSystem.cs:1252-1259): a pad whose trigger rests above 0.1 spins the preview and flips the game to
    // controller mode, hiding the cursor. The transpiler swaps the two Input.GetAxis calls for TriggerAxis; nothing is
    // added or removed, and the five "Mouse X" reads stay untouched. A zero from TriggerAxis keeps vanilla's num at 0, so
    // drift never reaches ChangeInputType(Controller) and the controller rotation adds nothing. ShowPreview only runs in
    // building mode, so menus, the furniture shop's rotate preview and the camera zoom never see the filter.
    internal static class TriggerFilter
    {
        internal static bool Patched;
        // The transpiler found exactly the two trigger reads (set on every re-patch of ShowPreview).
        internal static bool Active;
        // The last frame a trigger read got through: MatchRotation treats it as rotation input.
        internal static int LastNonZeroFrame = -1;

        private static bool _driftLogged;

        // Vanilla's smallest accepted trigger value (SRC/BuildingSystem.cs:1253).
        private const float VanillaThreshold = 0.1f;

        // Replaces Input.GetAxis("RightTrigger"/"LeftTrigger") in ShowPreview. A deadzone above vanilla's 0.1 is rescaled:
        // a pull just past the deadzone gives vanilla's smallest accepted value (free mode turns at 10°/s, as before) and
        // a full pull still gives 1, so the slow end of free rotation survives. At or below 0.1 the raw value passes.
        internal static float TriggerAxis(string axis)
        {
            float v = Input.GetAxis(axis);
            try
            {
                if (!Active || !Cfg.Rotation.TriggerFix.Value) return v;
                Device.Tick();
                float m = Mathf.Abs(v);
                float dz = Cfg.Rotation.TriggerDeadzone.Value;
                if (m < dz)
                {
                    if (m > 0.1f && Device.Last == InputDevice.KeyboardMouse) Drift(axis, m);
                    return 0f;
                }
                if (Cfg.Rotation.IgnoreTriggersOnKeyboard.Value && Device.Last == InputDevice.KeyboardMouse && m - Device.Baseline(axis) < dz)
                {
                    Drift(axis, Device.Baseline(axis));
                    return 0f;
                }
                Device.MarkPad();
                LastNonZeroFrame = Time.frameCount;
                if (dz <= VanillaThreshold || m >= 1f) return v;
                // Just above the threshold, so vanilla's strict "> 0.1" still accepts it.
                float scaled = VanillaThreshold + 0.001f + (m - dz) / (1f - dz) * (1f - VanillaThreshold - 0.001f);
                return Mathf.Sign(v) * scaled;
            }
            catch (Exception e)
            {
                Log.Exception("trigger filter", e);
                return v;
            }
        }

        private static void Drift(string axis, float rest)
        {
            if (_driftLogged) return;
            _driftLogged = true;
            string name = axis == "LeftTrigger" ? "LT" : "RT";
            Log.Info($"Rotation: trigger drift ignored ({name} resting at {rest:0.00}).");
        }
    }

    // T1: BuildingSystem.ShowPreview(RaycastHit) (SRC/BuildingSystem.cs:1192). In the shipped DLL the two reads are
    // IL_02ce/02d3 (RightTrigger) and IL_02d8/02dd (LeftTrigger). Exactly two matches or nothing.
    [HarmonyPatch]
    internal static class TriggerReadPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ShowPreview;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            try
            {
                var getAxis = AccessTools.Method(typeof(Input), nameof(Input.GetAxis), new[] { typeof(string) });
                var ours = AccessTools.Method(typeof(TriggerFilter), nameof(TriggerFilter.TriggerAxis), new[] { typeof(string) });
                var hits = new List<int>();
                for (int i = 0; i + 1 < list.Count; i++)
                {
                    if (list[i].opcode != System.Reflection.Emit.OpCodes.Ldstr) continue;
                    var s = list[i].operand as string;
                    if (s != "RightTrigger" && s != "LeftTrigger") continue;
                    if (list[i + 1].Calls(getAxis)) hits.Add(i + 1);
                }
                if (hits.Count != 2 || getAxis == null || ours == null)
                {
                    TriggerFilter.Active = false;
                    Log.ErrorOnce("trigger count", $"Trigger fix: expected 2 trigger reads in ShowPreview, found {hits.Count}; the fix is off.");
                    return list;
                }
                foreach (int h in hits) list[h].operand = ours;
                TriggerFilter.Active = true;
                return list;
            }
            catch (Exception e)
            {
                TriggerFilter.Active = false;
                Log.Exception("trigger transpiler", e);
                return instructions;
            }
        }
    }
}
