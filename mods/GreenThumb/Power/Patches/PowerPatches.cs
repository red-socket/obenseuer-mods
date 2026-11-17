using System;
using System.Reflection;
using HarmonyLib;

namespace GreenThumb
{
    // P1: Growing.OnPowerOut() postfix. Never skips vanilla: the base fires onInActive, which switches off the grow
    // lamp's Sun relay on the Terrarium and the Flowerbox With Lamp. __result is true exactly when a grower that needs
    // power lost it (CraftingBase.OnPowerOut :1658-1670 returns false for AlwaysOn), which is when vanilla forgot to
    // pause. PoweredAndActive is never read here: the callback fires before the new state is stored.
    [HarmonyPatch]
    internal static class PowerOutPatch
    {
        private static MethodBase TargetMethod() => Vanilla.OnPowerOut;

        private static void Postfix(Growing __instance, bool __result)
        {
            if (!Cfg.Hot.Power) return;
            try
            {
                PowerFix.MarkDirty(__instance);
                if (__result && !PowerFix.Window) __instance.PauseGrowth();
            }
            catch (Exception e) { Log.Exception("Power: power out", e); }
        }
    }

    // P2: Growing.OnPowerOn() postfix. Vanilla already unpauses a grower that needs power; an AlwaysOn grower left paused
    // by the bug (base returns false for it) is released here.
    [HarmonyPatch]
    internal static class PowerOnPatch
    {
        private static MethodBase TargetMethod() => Vanilla.OnPowerOn;

        private static void Postfix(Growing __instance, bool __result)
        {
            if (!Cfg.Hot.Power) return;
            try
            {
                PowerFix.MarkDirty(__instance);
                if (!__result && __instance.AlwaysOn && __instance.paused && !PowerFix.Window) __instance.UnpauseGrowth();
            }
            catch (Exception e) { Log.Exception("Power: power on", e); }
        }
    }

    // P3: Growing.PauseGrowth() bool prefix. Its only callers are Growing.OnPowerOut (:733, the buggy AlwaysOn pause) and
    // our own Reconcile; no asset relay calls it. Skipped for AlwaysOn growers and while a load is settling.
    [HarmonyPatch]
    internal static class PausePatch
    {
        private static MethodBase TargetMethod() => Vanilla.PauseGrowth;

        private static bool Prefix(Growing __instance)
        {
            try { return !(Cfg.Hot.Power && (PowerFix.Window || __instance.AlwaysOn)); }
            catch (Exception e)
            {
                Log.Exception("Power: pause", e);
                return true;
            }
        }
    }

    // P4: Growing.UnpauseGrowth() bool prefix. Only caller: Growing.OnPowerOn (:749) and our own code. Held while a load
    // is settling, so the offline catch-up runs under the pause state the player left (D6).
    [HarmonyPatch]
    internal static class UnpausePatch
    {
        private static MethodBase TargetMethod() => Vanilla.UnpauseGrowth;

        private static bool Prefix()
        {
            try { return !(Cfg.Hot.Power && PowerFix.Window); }
            catch (Exception e)
            {
                Log.Exception("Power: unpause", e);
                return true;
            }
        }
    }

    // P5: grow.GetTitle(bool) postfix (D8): one extra line on plants whose grower is paused, saying whether it is switched
    // Off (CraftingBase.On, stored after the Toggle callback, so final here) or has no power. Allocates
    // one string, only on a paused plant while it is aimed at (vanilla builds this title every frame anyway).
    [HarmonyPatch]
    internal static class PausedHintPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GrowGetTitle;

        private static void Postfix(grow __instance, ref string __result)
        {
            if (!Cfg.Hot.Power || !Cfg.Hot.PausedHint || !__instance.Paused) return;
            try
            {
                var g = __instance.GetComponentInParent<Growing>();
                if (g != null && !g.AlwaysOn && g.paused) __result += "\n" + (g.On ? Strings.PausedNoPower : Strings.PausedOff);
            }
            catch (Exception e) { Log.Exception("Power: paused hint", e); }
        }
    }
}
