using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterPlacement
{
    // U-008 alignment patches (spec §3.6). The game's own ChangeColor stays the authority on validity: alignment only
    // moves the preview within what the game already allows, and the settle guard (A3) makes sure a placement never uses
    // a validity result that predates the latest snap.
    internal static class AlignPatches
    {
        internal static bool Patched;
    }

    // Physics-step bookkeeping for the settle guard. EvalStep: the step count when vanilla's ChangeColor last ran for the
    // current preview. ChangeStep: the step count when the snap last changed (engage, switch, release, MatchRotation).
    // InsideTrigger.inside, ObjectPreview.col and noBuildArea are event-driven at the physics step (autoSyncTransforms is
    // on), so until ChangeColor has run with trigger state from a step after the change, the isPlaceable that Place reads
    // (the previous frame's, SRC/BuildingSystem.cs:649-661) may describe a pose the preview jumped away from.
    internal static class Settle
    {
        internal static int EvalStep = -1;
        internal static int ChangeStep = -1;
        internal static float ChangeTime = -100f;

        internal static void Changed()
        {
            ChangeStep = Steps.Count;
            ChangeTime = Time.unscaledTime;
        }

        internal static bool Unsettled => ChangeStep >= 0 && EvalStep <= ChangeStep;
    }

    // A1: ObjectPreview.ChangeColor() (SRC/ObjectPreview.cs:126). Stamped here rather than in ShowPreview, because
    // ShowPreview only reaches ChangeColor when hit.normal is non-zero (SRC/BuildingSystem.cs:1333).
    [HarmonyPatch]
    internal static class ChangeColorPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ChangeColor;

        private static void Postfix(ObjectPreview __instance)
        {
            try
            {
                var bs = BuildingSystem.instance;
                if (bs != null && bs.currentPreview != null && __instance.transform.IsChildOf(bs.currentPreview))
                    Settle.EvalStep = Steps.Count;
            }
            catch (Exception e) { Log.Exception("settle stamp", e); }
        }
    }

    // A2: BuildingSystem.ShowPreview(RaycastHit) (SRC/BuildingSystem.cs:1192), postfix. Runs after vanilla has set
    // currentpos, the alternativeOrigin shift (:1417-1424) and the child yaw (:1433). Runs before the flat-item postfix.
    [HarmonyPatch]
    internal static class AlignPreviewPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ShowPreview;

        [HarmonyPriority(Priority.High)]
        private static void Prefix(BuildingSystem __instance)
        {
            try { Solver.BeginFrame(__instance); }
            catch (Exception e) { Log.Exception("align connection", e); }
        }

        [HarmonyPriority(Priority.High)]
        private static void Postfix(BuildingSystem __instance, RaycastHit hit2)
        {
            try { Solver.Run(__instance, hit2); }
            catch (Exception e)
            {
                Log.Exception("align", e);
                try { Solver.Release(); }
                catch (Exception) { }
            }
            try { Solver.EndFrame(__instance); }
            catch (Exception e) { Log.Exception("align connection", e); }
        }
    }

    // A4: BuildingSystem.UpdateConnectionIndicator() (SRC/BuildingSystem.cs:725), private, prefix. While aligning crafting
    // furniture, skips vanilla's call at the end of ShowPreview (:1436), which would scan the scene for the unsnapped
    // spot; A2's postfix makes the one call from the snapped spot. Optional: without the method the A2 postfix redraws
    // after vanilla, as before, and the rest of alignment is unaffected.
    [HarmonyPatch]
    internal static class ConnectionDeferPatch
    {
        private static bool Prepare() => Vanilla.UpdateConnectionIndicator != null && Vanilla.UpdateConnection != null;

        private static MethodBase TargetMethod() => Vanilla.UpdateConnectionIndicator;

        private static bool Prefix()
        {
            try { return !Solver.DeferringConnection; }
            catch (Exception) { return true; }
        }
    }

    // A3: BuildingSystem.Place() (SRC/BuildingSystem.cs:1485), first prefix. Refuses while the latest snap change has
    // not been seen by a physics step and then by vanilla's ChangeColor, with vanilla's own refusal feedback. In
    // practice that is one or two frames; holding Place fires again after 0.5 s (GetKeyWithHold). Place returns before
    // PlaceMovingItem, so moved items are covered too. On an error it lets the placement through only when no snap
    // changed in the last 0.5 s (fail closed).
    [HarmonyPatch]
    internal static class SettleGuardPatch
    {
        private static MethodBase TargetMethod() => Vanilla.Place;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BuildingSystem __instance)
        {
            try
            {
                var preview = __instance.currentPreview;
                if (preview == null || !preview.gameObject.activeInHierarchy) return true;
                if (!Settle.Unsettled) return true;
                Vanilla.Warn();
                Log.Verbose("settle", () => $"Align: placement refused: snap not settled (step {Steps.Count}, change {Settle.ChangeStep}, checked {Settle.EvalStep}).");
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("settle guard", e);
                return Time.unscaledTime - Settle.ChangeTime > 0.5f;
            }
        }
    }
}
