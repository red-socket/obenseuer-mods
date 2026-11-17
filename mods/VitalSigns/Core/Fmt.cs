using System;
using System.Globalization;

namespace VitalSigns
{
    // Quantised number and time formatting (spec §9.3): whole %, HP to 0.1 (0.01 below 0.05, so a real loss never
    // shows as 0), hours to 0.1 below 10 and whole above, minutes whole below an hour. Invariant culture, so a decimal comma never splits a line. ASCII only ('-' for minus): the game's
    // tooltip font may lack other dashes.
    internal static class Fmt
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        internal static string Num(float v)
        {
            float a = Math.Abs(v);
            if (a >= 10f) return v.ToString("0", C);
            if (a >= 0.05f || a == 0f) return v.ToString("0.#", C);
            return a >= 0.005f ? v.ToString("0.##", C) : (v < 0f ? "-" : "") + "<0.01";
        }

        internal static string Pct(float v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", C) + "%";

        // "-18 HP", "-0.3 HP".
        internal static string Hp(float lost) => "-" + Num(Math.Abs(lost)) + " HP";

        internal static string Signed(float v) => (v < 0f ? "-" : "+") + Num(Math.Abs(v));

        // A duration that fits inside a sentence ("in about 45 min", "for another 2.5 h"): minutes below an hour,
        // rounded up so a real wait never reads as 0.
        internal static string Hours(float h)
        {
            if (h < 1f) return Minutes(Math.Max(1, (int)Math.Ceiling(h * 60f - 1e-3f)));
            return (h < 10f ? h.ToString("0.#", C) : h.ToString("0", C)) + " h";
        }

        internal static string Minutes(int n) => n == 1 ? "1 min" : n.ToString(C) + " min";

        // Ticks are game minutes at normal speed: minutes below 2 h, hours above.
        internal static string Ticks(int n) => n < 120 ? Minutes(n) : Hours(n / 60f);

        // "Day 12 14:05". The day is TimeOfDayAzure.dayCounter, the number the game's calendar shows ("Days in
        // Obenseuer", SRC/InteractableCalendar.cs:5; currentDay = dayCounter, SRC/TimeAndDay.cs:130).
        internal static string Stamp(ulong ts)
        {
            ulong day = ts / 86400UL, s = ts % 86400UL;
            return "Day " + day.ToString(C) + " " + Clock(s);
        }

        private static string Clock(ulong secOfDay) => (secOfDay / 3600UL).ToString("00", C) + ":" + (secOfDay / 60UL % 60UL).ToString("00", C);

        // "Day 12 14:05-14:23", or both days when the span crosses midnight.
        internal static string Range(ulong a, ulong b)
        {
            if (b <= a || b / 60UL == a / 60UL) return Stamp(a);
            if (a / 86400UL == b / 86400UL) return Stamp(a) + "-" + Clock(b % 86400UL);
            return Stamp(a) + " - " + Stamp(b);
        }

        // "2 min ago", "3 h ago" in game time.
        internal static string Ago(double seconds)
        {
            if (seconds < 60.0) return Strings.JustNow;
            int min = (int)(seconds / 60.0);
            return Ticks(min) + " " + Strings.AgoWord;
        }
    }
}
