using System;

namespace MessageLog
{
    // The in-game time of a message (spec §4.3). TimeOfDayAzure.currentTimeAndDay follows the clock one game minute at a
    // time (SRC/TimeOfDayAzure.cs:503) and is frozen while a save loads (updateTimeDisabled, :334-368). Between
    // PlayerWillLoadGame and LoadingDone it still holds the old session's time, so messages from that window are left
    // untimed and stamped later from the loaded save's own time (History.StampUntimed). Never TimeAndDay.GetWeekday()
    // (one day off) or GetMonthNameShort (wrong table).
    internal static class Clock
    {
        internal static bool Now(out int day, out short hm, out sbyte wd)
        {
            if (!SlotTracker.Busy) return Raw(out day, out hm, out wd);
            day = 0; hm = -1; wd = 0;
            return false;
        }

        // The current time whatever the load state: LoadingDone and SavingDone use it, when currentTimeAndDay is the
        // loaded (or saved) game's exact time (SRC/TimeOfDayAzure.cs:348-369).
        internal static bool Raw(out int day, out short hm, out sbyte wd)
        {
            day = 0; hm = -1; wd = 0;
            try
            {
                var tod = TimeOfDayAzure.instance;
                var t = tod != null ? tod.currentTimeAndDay : null;
                if (t == null) return false;
                day = t.currentDay;
                int h = (int)t.hours, m = (int)t.minutes;
                if (h < 0 || h > 23 || m < 0 || m > 59) return false;
                hm = (short)(h * 60 + m);
                wd = (sbyte)(((t.weekDay % 7) + 7) % 7);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
