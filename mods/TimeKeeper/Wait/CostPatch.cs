using System;
using System.Reflection;
using HarmonyLib;

namespace TimeKeeper
{
    // P7: PlayerStats.DeltaSeconds(int) (SRC/PlayerStats.cs:306-324), prefix, part of the wait group. DeltaSeconds adds
    // the game seconds of a frame to LocalSeconds and runs every minute-based stat update (hunger, thirst, tiredness,
    // addictions, hygiene, mental health, health, radiation, conditions) once when LocalSeconds passes 59. Each of those
    // adds a fixed amount per call, whatever the seconds. At 1x a frame covers about a game second, so they run every
    // 60 game seconds; at the 150x of a wait a frame covers 15-110 game seconds, so they would run only every 62-112
    // game seconds and a wait would cost 55-95% of the same time spent idle, depending on the frame rate (vanilla TV
    // and chair waits have that discount). While our own wait runs, the frame's seconds are handed over one at a time,
    // exactly as idle play does, so the wait costs what idle time costs. The per-call parts (alcohol, sauna, duck
    // addiction, item special effects, the HUD bars) also run once per game second, as when idle.
    //
    // Everything else (other waits, sleep, pass-outs, idle play, a disabled or inactive stats object) runs vanilla.
    // The re-entry guard lets our own one-second calls through the patched method untouched.
    [HarmonyPatch]
    internal static class CostPatch
    {
        // Above this many seconds in one call (a time jump, not a frame), the rest is handed over in pieces that end
        // exactly on each minute tick, so the stat updates still run once per 60 game seconds.
        private const int MaxSingleSecondCalls = 600;

        private static bool _inside;

        private static MethodBase TargetMethod() => Vanilla.StatsDeltaSeconds;

        // First, so the frame's call is split before any other prefix sees it; other mods' patches then run once per
        // game second, as during idle play.
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PlayerStats __instance, int sec)
        {
            if (_inside || sec <= 1 || !Applies(__instance)) return true;

            int left = sec, calls = 0;
            _inside = true;
            try
            {
                while (left > 0)
                {
                    int piece = calls < MaxSingleSecondCalls ? 1 : Math.Max(1, Math.Min(left, 60 - __instance.LocalSeconds));
                    Vanilla.CallStatsDeltaSeconds(__instance, piece);
                    left -= piece;
                    calls++;
                }
            }
            catch (Exception e)
            {
                // The original catches its own exceptions, so only the call itself can throw here.
                Log.Exception("wait cost", e);
            }
            finally
            {
                _inside = false;
            }
            if (calls > 0) return false;
            Log.WarnOnce("cost fallback", "Wait: the per-second stat update failed; this wait's stats run the game's own way.");
            return true;   // nothing was handed over: let the original take the whole frame
        }

        // Our wait is running (WaitSession Running and the game's waiting type is Waiting), stats are live and the
        // delegate exists. The DeltaSeconds checks of Active and StatsProgressDisabled are repeated so a frozen stats
        // object keeps vanilla's single BarUpdate.
        private static bool Applies(PlayerStats stats)
        {
            try
            {
                if (WaitSession.State != WaitSession.Phase.Running || Vanilla.CallStatsDeltaSeconds == null) return false;
                if (stats == null || !stats.Active || stats.StatsProgressDisabled) return false;
                var wc = WaitingController.instance;
                return wc != null && wc.waitingType == WaitingController.Type.Waiting;
            }
            catch (Exception e)
            {
                Log.Exception("wait cost check", e);
                return false;
            }
        }
    }
}
