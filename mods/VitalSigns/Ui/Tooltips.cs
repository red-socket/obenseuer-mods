using System;
using UnityEngine;
using Bar = PlayerStatsToolTipInfo.PlayerStatsBar;

namespace VitalSigns
{
    // Text T1 appends to the stat tooltips (spec §9.3, D8): Health, Mental health, the four need bars and Ducks need. Built
    // at most every 0.5 s of real time per bar, with quantised numbers; while the text is unchanged the same string
    // instance is returned, so TooltipUI's two-frame width re-fit isn't restarted every frame. Tooltips are mouse-only in
    // vanilla; everything here is also in the window.
    internal static class Tooltips
    {
        private const float RebuildSeconds = 0.5f;
        private const int Bars = 14;

        private static readonly string[] Text = new string[Bars];
        private static readonly float[] NextAt = new float[Bars];

        // T1's combined-string cache, per bar.
        private static readonly string[] CachedVanilla = new string[Bars];
        private static readonly string[] CachedExtra = new string[Bars];
        private static readonly string[] CachedCombined = new string[Bars];

        internal static string Extra(Bar bar)
        {
            int i = (int)bar;
            if (i <= 0 || i >= Bars) return null;
            float now = Time.realtimeSinceStartup;
            if (now < NextAt[i]) return Text[i];
            NextAt[i] = now + RebuildSeconds;
            string built = null;
            try { built = Build(bar); }
            catch (Exception e) { Log.Exception("tooltip " + bar, e); }
            if (built != Text[i]) Text[i] = built;   // string equality: the old instance stays while the text is the same
            return Text[i];
        }

        private static string Build(Bar bar)
        {
            switch (bar)
            {
                case Bar.HealthBar: return Cfg.Hot.HealthTooltip && Tracker.Live ? Health() : null;
                case Bar.DepressionBar: return Cfg.Hot.MoodTooltip && Cfg.Hot.MoodOn ? MoodModel.Tooltip() : null;
                case Bar.AlcoholNeedBar:
                case Bar.MushroomNeedBar:
                case Bar.SmokesNeedBar:
                case Bar.GamblingBar:
                    return Cfg.Hot.EtaTooltip ? AddictionModel.Tooltip(AddictionModel.IndexOf(bar)) : null;
                case Bar.DuckBar: return Cfg.Hot.EtaTooltip ? DuckModel.Tooltip() : null;
                default: return null;
            }
        }

        // At most 2 lines plus "More".
        private static string Health()
        {
            var h = Tracker.Health;
            if (h == null) return null;
            string s = null;
            if (h.HealthToRegenerate < 0f)
            {
                var d = Forecast.Drain(h.HealthToRegenerate, h.HealthRegenerationRate, h.currentStatus);
                s = Strings.TipHidden(d.Hp, d.Ticks);
            }
            var last = DamageLog.LastLoss;
            if (last != null && !last.Gone)
            {
                string line = Strings.TipLastLoss(last.Label, Fmt.Hp(last.Hp), Fmt.Ago(GameClock.Now - last.LastClock));
                s = s == null ? line : s + "\n" + line;
            }
            return s == null ? null : Strings.WithMore(s);
        }

        // T1: vanilla details + our text, cached on (bar, vanilla instance, extra instance).
        internal static string Combine(Bar bar, string vanilla, string extra)
        {
            int i = (int)bar;
            if (ReferenceEquals(vanilla, CachedVanilla[i]) && ReferenceEquals(extra, CachedExtra[i]) && CachedCombined[i] != null)
                return CachedCombined[i];
            CachedVanilla[i] = vanilla;
            CachedExtra[i] = extra;
            return CachedCombined[i] = (vanilla ?? "") + "\n<size=85%>" + extra + "</size>";
        }
    }
}
