using System;
using System.Reflection;
using HarmonyLib;

namespace GreenThumb
{
    // H1: Growing.RepeatHarvest(Vector3) prefix + postfix (SRC/Growing.cs:782). The prefix notes the last hand-harvest
    // stamp; the postfix runs after vanilla's own crosshair choice (unchanged) and, when that harvested nothing and the
    // hold is long enough, harvests one more plant of the same grower (HarvestAll.Step).
    [HarmonyPatch]
    internal static class RepeatHarvestPatch
    {
        private static MethodBase TargetMethod() => Vanilla.RepeatHarvest;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(out float __state)
        {
            __state = float.NaN;
            try { __state = Vanilla.LastManualHarvestTime(); }
            catch (Exception e) { Log.Exception("Harvest all: prefix", e); }
        }

        private static void Postfix(Growing __instance, float __state, bool __runOriginal)
        {
            if (!Cfg.Hot.HarvestAll || !__runOriginal || HarvestAll.ForeignSkip) return;
            try
            {
                if (float.IsNaN(__state) || !HarvestAll.Armed) return;
                if (Vanilla.LastManualHarvestTime() != __state) return;   // vanilla harvested this call
                HarvestAll.AfterVanilla(__instance);
            }
            catch (Exception e) { Log.Exception("Harvest all: step", e); }
        }
    }

    // H2: UseText.UseAndHoldText(string, string, bool, bool) prefix (SRC/UseText.cs:121, an instance method). Only
    // grow.cs:439 and GrowItem.cs:227 pass the game's "Hold - Keep harvesting", and only for ManualHarvest repeat
    // targets; the Growing.RepeatHarvestHoldText const is inlined at those sites, so it can't be patched instead. Fair
    // Justice's "Steal" prompt never matches. Someone else's plant keeps the vanilla hint while IncludeOwned is off, and
    // every plant keeps it once harvest-all has stood down for another mod (ForeignSkip).
    [HarmonyPatch]
    internal static class HoldHintPatch
    {
        private static MethodBase TargetMethod() => Vanilla.UseAndHoldText;

        private static void Prefix(ref string holdText)
        {
            if (!Cfg.Hot.HarvestAll || !Cfg.Hot.ShowHint || HarvestAll.ForeignSkip || !string.Equals(holdText, Strings.VanillaHoldHint, StringComparison.Ordinal)) return;
            try
            {
                if (!Cfg.Hot.IncludeOwned)
                {
                    var io = InteractObjects.instance;
                    var go = io != null ? io.currentGameObject : null;
                    var t = go != null ? go.GetComponentInParent<grow>() : null;
                    if (t != null && t.ownerId > 1 && !t.PermissionToTake) return;
                }
                holdText = Strings.HoldHint;
            }
            catch (Exception e) { Log.Exception("Harvest all: hint", e); }
        }
    }

    // H3: InteractObjects.IsWithinUseDistance(Interactable) postfix (SRC/InteractObjects.cs:205), patched only when
    // Reach = WholeGrower at launch (D14). Widens the reach for the scoped grower's plants, and only while
    // HarvestAll.Step is checking or harvesting one of the player's own plants. Prompts, Workshop's repeat key and every
    // other interactable keep vanilla reach.
    [HarmonyPatch]
    internal static class ReachPatch
    {
        private static MethodBase TargetMethod() => Vanilla.IsWithinUseDistance;

        private static void Postfix(Interactable target, ref bool __result)
        {
            if (__result || HarvestAll.ScopeGrowing == null) return;
            try
            {
                if (HarvestAll.InScope(target)) __result = true;
            }
            catch (Exception e) { Log.Exception("Harvest all: reach", e); }
        }
    }
}
