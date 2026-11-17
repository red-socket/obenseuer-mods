using System;
using System.Globalization;

namespace FairMarket
{
    // Number, clock and money formatting (spec §7.3, §8.1, §9.4). Invariant culture, so a decimal comma never splits a
    // line. Clock times follow the game's HUD ("9:00", "18:00"); hour 24 reads "midnight".
    internal static class Fmt
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private static readonly string[] Days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        internal static string Day(int weekDay) => Days[((weekDay % 7) + 7) % 7];

        internal static string Clock(int hour) => hour >= 24 ? Strings.Midnight : hour.ToString(C) + ":00";

        // Short hour for the weekly line ("8-16"), "24" kept as is so a range reads "18-24".
        internal static string Hour(int hour) => hour.ToString(C);

        // One decimal: "69.3%", "<0.1%" below 0.1 %, ">99.9%" just below 100 %.
        internal static string Pct1(float p)
        {
            if (p <= 0f) return "0%";
            if (p >= 1f) return "100%";
            if (p < 0.001f) return "<0.1%";
            if (p >= 0.999f) return ">99.9%";
            return (Math.Round(p * 100.0, 1, MidpointRounding.AwayFromZero)).ToString("0.0", C) + "%";
        }

        // "2,000 OC", "150 RM", "40.5 RM".
        internal static string MoneyText(float amount, global::Money.Currency currency) =>
            currency == global::Money.Currency.OC
                ? ((int)Math.Round(amount)).ToString("#,0", C) + " OC"
                : amount.ToString("#,0.##", C) + " RM";
    }
}
