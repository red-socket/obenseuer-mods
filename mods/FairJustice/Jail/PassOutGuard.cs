using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FairJustice
{
    // #5543 (spec §3.4B). Passing out in jail can roll an arrest (WaitingController.StartSleep, SRC/WaitingController.cs:
    // 306-311) or a GetCaughtFromCrime pass-out event, and either teleports the player back to prison. P21 records the
    // event a sleep that started in jail rolled; P22 blocks an Arrest or GetCaught event while in jail, or when it is
    // that recorded event (a release can fire during the same sleep). Stolen goods are still taken, as in both vanilla
    // branches. A real GoToPrison sets arrestInProgress first and installs a new event object, so it always passes.
    internal static class PassOutGuard
    {
        internal static bool Patched;

        private static MethodBase _startSleep, _execute;
        internal static MethodBase StartSleepTarget => _startSleep ?? (_startSleep = AccessTools.Method(typeof(WaitingController), "StartSleep",
            new[] { typeof(float), typeof(bool), typeof(SleepEvent[]), typeof(bool), typeof(float) }));
        internal static MethodBase ExecuteTarget => _execute ?? (_execute = AccessTools.Method(typeof(SleepEventController), nameof(SleepEventController.ExecuteEvent),
            new[] { typeof(SleepEventController.SleepEvents), typeof(Transform) }));

        internal static bool Live => Patched && Cfg.NoPassOutReArrest.Value;

        internal static void SleepStarted(WaitingController w)
        {
            if (!Live) return;
            var crime = Crime.instance;
            Jail.FromJailEvent = Jail.InPrison && crime != null && !crime.ArrestInProgress ? Vanilla.ComingSleepEvent(w) : null;
        }

        // Returns false to block the event.
        internal static bool Execute(SleepEventController sec, SleepEventController.SleepEvents ev)
        {
            if (!Live) return true;
            if (ev != SleepEventController.SleepEvents.Arrest && ev != SleepEventController.SleepEvents.GetCaughtFromCrime) return true;
            var crime = Crime.instance;
            if (crime == null || crime.ArrestInProgress) return true;
            var w = WaitingController.instance;
            // The event is still set here: WaitingController nulls it after this call.
            bool fromJail = Jail.FromJailEvent != null && w != null && ReferenceEquals(Vanilla.ComingSleepEvent(w), Jail.FromJailEvent);
            if (!Jail.InPrison && !fromJail) return true;
            if (Vanilla.RemoveStolenGoods == null) return true;
            if (Compat.Foreign(ExecuteTarget, "the pass-out guard")) return true;

            Vanilla.RemoveStolenGoods(sec);   // runs our P13 rescue first
            if (ev == SleepEventController.SleepEvents.Arrest && w != null) w.eventDialogue = "";   // GetCaught keeps its non-arrest line
            Jail.FromJailEvent = null;
            Log.Info($"Pass-out arrest in jail blocked ({ev}).");
            return false;
        }
    }

    // P21: WaitingController.StartSleep(float, bool, SleepEvent[], bool, float) (private).
    [HarmonyPatch]
    internal static class StartSleepPatch
    {
        private static MethodBase TargetMethod() => PassOutGuard.StartSleepTarget;

        private static void Postfix(WaitingController __instance)
        {
            try { PassOutGuard.SleepStarted(__instance); }
            catch (Exception e) { Log.Exception("StartSleep postfix", e); }
        }
    }

    // P22: SleepEventController.ExecuteEvent(SleepEvents, Transform).
    [HarmonyPatch]
    internal static class ExecuteSleepEventPatch
    {
        private static MethodBase TargetMethod() => PassOutGuard.ExecuteTarget;

        private static bool Prefix(SleepEventController __instance, SleepEventController.SleepEvents Ev)
        {
            try { return PassOutGuard.Execute(__instance, Ev); }
            catch (Exception e)
            {
                Log.Exception("pass-out guard", e);
                return true;
            }
        }
    }
}
