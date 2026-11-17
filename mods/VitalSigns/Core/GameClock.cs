using System;

namespace VitalSigns
{
    // Two clocks (spec §5.7). Now: monotonic game seconds summed from TimeOfDayAzure.SecondsPassed, used for merging and
    // the toast windows; it never goes back, not even when an earlier save loads. Stamp(): the game's own
    // TotalSeconds (SRC/TimeAndDay.cs:55, currentDay-based) for display. The subscription is made in the plugin's Awake,
    // so it runs before PlayerStats.DeltaSeconds (which subscribes per scene) and Now is current inside every stats tick.
    internal static class GameClock
    {
        // A backward SetTimeline wraps the delta to about 86400 (SRC/TimeOfDayAzure.cs:470); two game hours is more
        // than any frame of the game's own fast waits.
        private const int MaxStep = 7200;

        internal static double Now;
        private static ulong _lastStamp;

        internal static void OnSecondsPassed(int s)
        {
            if (s <= 0) return;
            if (s > MaxStep)
            {
                Log.WarnOnce("clock cap", $"Log: the game clock jumped {s} s in one frame (time set back?); counted as {MaxStep} s.");
                s = MaxStep;
            }
            Now += s;
        }

        // currentTimeAndDay only changes on the game minute (SRC/TimeOfDayAzure.cs:509), so stamps are minute-exact.
        internal static ulong Stamp()
        {
            try
            {
                var t = TimeOfDayAzure.instance;
                if (t != null && t.currentTimeAndDay != null) _lastStamp = t.currentTimeAndDay.TotalSeconds;
            }
            catch (Exception) { }
            return _lastStamp;
        }
    }
}
