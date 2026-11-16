using System;
using System.Reflection;
using HarmonyLib;

namespace TimeKeeper
{
    // P3: WaitingUI.StartSleep() (SRC/WaitingUI.cs:274), bool prefix, first. The clock's Sleep button, controller
    // Interact (SRC/GameController.cs:130) and our own keys all go through it. Outside our session it is vanilla (bed,
    // alarm clock, other mods). While our picker is open the original never runs, so vanilla Sleep is never reached
    // from our picker (fails closed) and tiredness.lastAlarmTime (saved) is never written. While our wait runs, a stray
    // call is swallowed: it would turn the wait into sleep (WaitingController.StartSleep, :261-267).
    [HarmonyPatch]
    internal static class StartSleepPatch
    {
        private static MethodBase TargetMethod() => Vanilla.StartSleep;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(WaitingUI __instance)
        {
            switch (WaitSession.State)
            {
                case WaitSession.Phase.Picking:
                    try { WaitSession.Start(__instance); }
                    catch (Exception e)
                    {
                        Log.Exception("wait start", e);
                        WaitSession.Abort();
                    }
                    return false;
                case WaitSession.Phase.Running:
                    return false;
                default:
                    return true;
            }
        }
    }

    // P4: WaitingUI.GetTypeString() (:154), private, postfix. Our picker reads "Waiting" instead of "Sleeping". While the
    // wait runs vanilla already returns "Waiting" for Type.Waiting.
    [HarmonyPatch]
    internal static class GetTypeStringPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GetTypeString;

        private static void Postfix(ref string __result)
        {
            try
            {
                var wc = WaitingController.instance;
                if (WaitSession.State == WaitSession.Phase.Picking && wc != null && wc.waitingType == WaitingController.Type.None)
                    __result = WaitSession.WaitLabel;
            }
            catch (Exception e) { Log.Exception("wait label", e); }
        }
    }

    // P5: WaitingUI.UpdateSleptTime(float) (:122), prefix. Our picker shows the duration snapped to StepMinutes. Only the
    // text: the slider value and the clock's set time stay raw, so the game's accelerating arrow and LB/RB input keeps
    // working (snapping the raw value every frame would stall its +10 s steps). Start snaps the same way, so the wait
    // is what the label shows. The countdown is never touched.
    [HarmonyPatch]
    internal static class UpdateSleptTimePatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateSleptTime;

        [HarmonyPriority(Priority.High)]
        private static void Prefix(ref float sleepingTimeSeconds)
        {
            try
            {
                var wc = WaitingController.instance;
                if (WaitSession.State == WaitSession.Phase.Picking && wc != null && wc.waitingType == WaitingController.Type.None
                    && sleepingTimeSeconds > 0f)
                    sleepingTimeSeconds = WaitSession.Rounded((int)sleepingTimeSeconds);
            }
            catch (Exception e) { Log.Exception("wait step", e); }
        }
    }
}
