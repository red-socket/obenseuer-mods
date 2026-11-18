using System;
using System.Reflection;
using HarmonyLib;

namespace FieldGuide
{
    // H1: CraftingBase.Enter() postfix (spec §5). One patch covers manufacturing.Enter (SRC/manufacturing.cs:673),
    // manufacturingProcess.Enter (:652) and Generator.Enter (SRC/Generator.cs:64), which all call base.Enter(); the
    // overrides themselves are not patched, which would fire twice. Enter returns early on a disabled station before it
    // sets currentCraftingBase (SRC/CraftingBase.cs:1775-1779), so the guard skips those.
    [HarmonyPatch]
    internal static class EnterPatch
    {
        private static MethodBase TargetMethod() => Vanilla.CraftingEnter;

        private static void Postfix(CraftingBase __instance)
        {
            if (!Cfg.Hot.PopupsOn) return;
            try
            {
                if (CraftingBase.currentCraftingBase != __instance) return;
                var kind = Classify.Of(__instance);
                if (kind == null || !Cfg.Hot.On(kind.Value)) return;
                HintHost.Open(__instance, kind.Value, HintTexts.Build(kind.Value, __instance), station: true);
            }
            catch (Exception e)
            {
                Log.Exception("Help: enter", e);
                try { HintHost.Close(__instance); HintOverlay.Hide(__instance); }
                catch { }
            }
        }
    }

    // H2: CraftingBase.Exit() postfix. Covers real closes, the Exit inside a re-Enter (SRC/CraftingBase.cs:1771-1774) and
    // the backpack round trip (SRC/BackpackStorage.cs:100-104, :164); HintHost tells them apart a frame later (D25).
    // Hide is idempotent (SRC/HintOverlay.cs:246).
    [HarmonyPatch]
    internal static class ExitPatch
    {
        private static MethodBase TargetMethod() => Vanilla.CraftingExit;

        private static void Postfix(CraftingBase __instance)
        {
            try { HintHost.OnStationExit(__instance); }
            catch (Exception e) { Log.Exception("Help: exit", e); }
        }
    }
}
