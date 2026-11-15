using System;
using HarmonyLib;

namespace FairJustice
{
    // Jail state (spec §2.3, §3.3B, §3.4A): cache the prison, start the cell-door grace after ImprisonPlayer, and on a
    // real release reset police pursuits and start the release grace.
    internal static class PrisonEvents
    {
        internal static void Imprisoned(Prison p)
        {
            Jail.Current = p;
            Jail.ResetReleaseGrace();
            Jail.AfterLoadGraceUsed = false;
            NightRelease.ResetHold();
            // ConfiscateItems can throw partway through ImprisonPlayer, so check rather than assume.
            if (Vanilla.InPrison(p)) Jail.StartImprisonGrace("locked up");
        }

        internal static void Loaded(Prison p)
        {
            Jail.Current = p;
            NightRelease.ResetHold();
            // D-6: at most once per stay in jail per session, so saving and reloading cannot renew it. Only P7 (a new
            // imprisonment) clears the flag; a menu load does not.
            if (Cfg.AfterLoadInJail.Value && !Jail.AfterLoadGraceUsed && Vanilla.InPrison(p) && Jail.StartImprisonGrace("save loaded in jail"))
                Jail.AfterLoadGraceUsed = true;
        }

        internal static void Released()
        {
            Jail.EndImprisonGrace();
            NightRelease.ResetHold();
            Log.Info("Released from jail.");
            if (Cfg.StopPursuits.Value && Crime.instance != null && Vanilla.StopPolicePursuits != null)
            {
                // Sets every director and in-scene Act_Police to Stand, resets transitions and marks the player checked
                // (#6183). Writes only vanilla values.
                try
                {
                    Vanilla.StopPolicePursuits(Crime.instance);
                    Log.Info("Police pursuits reset.");
                }
                catch (Exception e) { Log.Exception("StopPolicePursuits", e); }
            }
            Jail.StartReleaseGrace();
        }
    }

    // P7: Prison.ImprisonPlayer(). Prefix caches the prison (ConfiscateItems runs inside); postfix starts the grace.
    [HarmonyPatch(typeof(Prison), nameof(Prison.ImprisonPlayer))]
    internal static class ImprisonPlayerPatch
    {
        private static void Prefix(Prison __instance)
        {
            if (Jail.StatePatched) Jail.Current = __instance;
        }

        private static void Postfix(Prison __instance)
        {
            if (!Jail.StatePatched) return;
            try { PrisonEvents.Imprisoned(__instance); }
            catch (Exception e) { Log.Exception("ImprisonPlayer postfix", e); }
        }
    }

    // P8: Prison.OnLoadingGame().
    [HarmonyPatch(typeof(Prison), nameof(Prison.OnLoadingGame))]
    internal static class PrisonLoadPatch
    {
        private static void Postfix(Prison __instance)
        {
            if (!Jail.StatePatched) return;
            try { PrisonEvents.Loaded(__instance); }
            catch (Exception e) { Log.Exception("Prison.OnLoadingGame postfix", e); }
        }
    }

    // P10: Prison.Release(). A separate class from P9 (night release). __state records whether a cell was active, so
    // the postfix only acts when vanilla really released; a held release (P9) leaves inPrison true and starts nothing,
    // in either prefix order.
    [HarmonyPatch(typeof(Prison), nameof(Prison.Release))]
    internal static class ReleasePatch
    {
        private static void Prefix(Prison __instance, out bool __state)
        {
            __state = false;
            try { __state = Jail.StatePatched && Vanilla.HasActiveCell && Vanilla.ActiveCell(__instance) != null; }
            catch (Exception e) { Log.Exception("Release prefix", e); }
        }

        private static void Postfix(Prison __instance, bool __state)
        {
            if (!__state) return;
            try
            {
                if (!Vanilla.InPrison(__instance)) PrisonEvents.Released();
            }
            catch (Exception e) { Log.Exception("Release postfix", e); }
        }
    }

    // P11: Prison.Escape(). Ends both graces and never starts one, but only on a real escape: vanilla Escape does
    // nothing without an active cell, and both prison scenes call it from outputs that also fire after a legitimate
    // release (the Gatehouse "EscapeTrigger" pair, the Bazaar Blastdoor's _Escape relay). Same __state test as P10.
    [HarmonyPatch(typeof(Prison), nameof(Prison.Escape))]
    internal static class EscapePatch
    {
        private static void Prefix(Prison __instance, out bool __state)
        {
            __state = false;
            try { __state = Jail.StatePatched && Vanilla.HasActiveCell && Vanilla.ActiveCell(__instance) != null; }
            catch (Exception e) { Log.Exception("Escape prefix", e); }
        }

        private static void Postfix(bool __state)
        {
            if (!__state) return;
            try
            {
                NightRelease.ResetHold();
                Jail.EndGraces("escape");
            }
            catch (Exception e) { Log.Exception("Escape postfix", e); }
        }
    }
}
