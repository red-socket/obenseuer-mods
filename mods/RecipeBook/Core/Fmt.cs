using System;
using System.Globalization;

namespace RecipeBook
{
    // Number formatting for badges (spec §9). Invariant culture, so a decimal comma never splits a badge.
    internal static class Fmt
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // The recipe panel's own number (SRC/RecipeInfoUI.cs:275): truncated, 100 at or above 1 (D9). 0 means "no badge".
        internal static int Pct(float p) => p >= 1f ? 100 : p <= 0f ? 0 : (int)(p * 100f);

        // Fair Market's one-decimal format (FairMarket/Core/Fmt.cs Pct1), so the badge matches its dishwasher line (D12):
        // "69.3%", "<0.1%" below 0.1 %, ">99.9%" just below 100 %.
        internal static string Pct1(float p)
        {
            if (p <= 0f) return "0%";
            if (p >= 1f) return "100%";
            if (p < 0.001f) return "<0.1%";
            if (p >= 0.999f) return ">99.9%";
            return Math.Round(p * 100.0, 1, MidpointRounding.AwayFromZero).ToString("0.0", C) + "%";
        }

        internal static string Int(int n) => n.ToString(C);
    }
}
