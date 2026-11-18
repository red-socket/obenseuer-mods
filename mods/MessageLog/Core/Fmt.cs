using System.Globalization;

namespace MessageLog
{
    // Time and number formatting (spec §4.3, D21). Invariant culture. The day is the game's day counter
    // (TimeAndDay.currentDay = TimeOfDayAzure.dayCounter, SRC/TimeAndDay.cs:130), the number the calendar and Vital Signs
    // show; the weekday is TimeAndDay.weekDay (Mon = 0), the one Time Keeper shows.
    internal static class Fmt
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // "Day 12 Tue 14:05"; "~" in front when the time was filled in later (flag 4); no clock for an untimed entry.
        internal static string Stamp(int day, int hm, int wd, bool estimated)
        {
            if (hm < 0) return Strings.DayWord + " ?";
            string s = Strings.DayWord + " " + day.ToString(C) + " " + Strings.Weekday(wd) + " " +
                       (hm / 60).ToString("00", C) + ":" + (hm % 60).ToString("00", C);
            return estimated ? "~" + s : s;
        }

        internal static string Count(int n) => n.ToString("#,0", C);
    }
}
