using System;
using UnityEngine;

namespace VitalSigns
{
    // The game seconds of the PlayerStats.DeltaSeconds call that is running (D1). Set and restored only, never summed:
    // Time Keeper's First-priority prefix splits a long frame into 1-second pieces that re-enter DeltaSeconds, so the
    // pieces see 1 and the skipped outer call sees -1. 0 = no call running (unknown).
    internal static class SecScope
    {
        internal static int Sec;
    }

    // Duck craving by elapsed game time (spec §8.3, D13). Vanilla calls PlayerStatsDuck.UpdateStats once per
    // DeltaSeconds call (SRC/PlayerStats.cs:327) with fixed per-call steps (SRC/PlayerStatsDuck.cs:50-70), so craving
    // follows the frame rate: at >= 15 fps and 1x speed that is about one step a game second, but the game's own fast
    // waits (TV, benches, jobs, jail) run about 37x slower at 60 fps. Vanilla still runs once per call (achievements,
    // the cure branch, TidyInventory's live achievementEnabled and the inventory count are untouched); the postfix adds
    // sec - 1 extra steps, a float-for-float copy of :57-73, reusing the count vanilla just took. While asleep or passed
    // out nothing is added: sleep stays exactly as the game has it (D13).
    internal static class DuckTime
    {
        internal static bool Patched;
        private static bool _disabled;
        private static long _extraSteps;
        private static double _nextSummary;

        // D2 prefix: how many extra steps the postfix adds. Vanilla always runs.
        internal static int Before()
        {
            if (_disabled || !Cfg.Hot.DuckFix || SecScope.Sec < 2) return 0;   // unknown or 1 s: pure vanilla
            if (Game.Asleep) return 0;                                           // sleep: vanilla (D13)
            int sec = SecScope.Sec;
            if (sec > 3600)
            {
                Log.WarnOnce("duck cap", $"Duck: one stats call covered {sec} game seconds (time set back?); counted as 3600.");
                sec = 3600;
            }
            return sec - 1;
        }

        // D2 postfix.
        internal static void After(PlayerStatsDuck d, int extra)
        {
            if (extra <= 0 || _disabled) return;
            try
            {
                if (Inventory.instance == null || !d.HasDuckAddiction()) return;
                var ps = PlayerStats.instance;
                if (ps == null || ps.mentalHealth == null) return;
                int count = Vanilla.DuckCount(d);
                float rate = d.GetDuckNeedBaserate();
                var mh = ps.mentalHealth;
                float need = d.currentDucksNeedAmount, status = d.currentStatus;
                if (need < 0.5f) return;   // vanilla raised it to 0.5 this call; anything else means it didn't run
                for (int i = 0; i < extra; i++)
                {
                    if ((float)count >= need)
                    {
                        status = (float)count / need * rate;
                        mh.currentStatus -= status / 10000f * ((float)count / need);
                        continue;
                    }
                    status += need / (float)((count == 0) ? 1 : count) * 0.005f;
                    status = Mathf.Clamp(status, 0f, 100f);
                    need += 1E-05f;
                    if (need >= 20f) need = 20f;
                    if (status >= d.negativeEffects) mh.currentStatus += status / 10000f;
                }
                d.currentStatus = status;
                d.currentDucksNeedAmount = need;
                _extraSteps += extra;
                if (Log.VerboseOn && GameClock.Now >= _nextSummary)
                {
                    _nextSummary = GameClock.Now + 3600.0;
                    Log.Verbose($"Duck: {_extraSteps} extra steps so far; craving {status:0.##}, need {need:0.#####}, ducks {count}.");
                }
            }
            catch (Exception e)
            {
                _disabled = true;
                Log.Error($"Duck: the craving fix failed and is off for this session; the game's own behaviour resumes. {e}");
            }
        }
    }
}
