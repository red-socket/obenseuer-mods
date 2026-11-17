using System;
using System.Collections.Generic;

namespace VitalSigns
{
    // Recovery ETA per addiction (spec §8.4, S-006). c = Get{X}AddictionChange() is called live, so the saved -0.7
    // rate, the difficulty multiplier and any other mod's multiplier are all included. The loop mirrors
    // PlayerStatsAddictions.UpdateStats (one step of c/60 per awake stats tick, SRC/PlayerStatsAddictions.cs:126-297).
    internal static class AddictionModel
    {
        private const int MaxTicks = 200000;

        internal struct Info
        {
            internal string Name;
            internal float Level, Need, Change;
        }

        internal static Info Get(PlayerStatsAddictions a, int i)
        {
            switch (i)
            {
                case 0: return new Info { Name = Strings.AddAlcohol, Level = a.AlcoholAddiction, Need = a.AlcoholNeed, Change = a.GetAlcoholAddictionChange() };
                case 1: return new Info { Name = Strings.AddMushroom, Level = a.MushroomAddiction, Need = a.MushroomNeed, Change = a.GetMushroomAddictionChange() };
                case 2: return new Info { Name = Strings.AddSmoking, Level = a.SmokingAddiction, Need = a.SmokingNeed, Change = a.GetSmokingAddictionChange() };
                default: return new Info { Name = Strings.AddGambling, Level = a.GamblingAddiction, Need = a.GamblingNeed, Change = a.GetGamblingAddictionChange() };
            }
        }

        // Tooltip bar -> addiction index.
        internal static int IndexOf(PlayerStatsToolTipInfo.PlayerStatsBar bar)
        {
            switch (bar)
            {
                case PlayerStatsToolTipInfo.PlayerStatsBar.AlcoholNeedBar: return 0;
                case PlayerStatsToolTipInfo.PlayerStatsBar.MushroomNeedBar: return 1;
                case PlayerStatsToolTipInfo.PlayerStatsBar.SmokesNeedBar: return 2;
                case PlayerStatsToolTipInfo.PlayerStatsBar.GamblingBar: return 3;
                default: return -1;
            }
        }

        internal static string Eta(float level, float c)
        {
            if (level <= 0f) return Strings.NotAddicted;
            if (c == 0f) return Strings.NoRecovery;
            if (float.IsNegativeInfinity(c)) return Strings.NextUpdate;
            if (c > 0f || float.IsNaN(c)) return Strings.Rising;
            float a = level, s = c / 60f;
            int n = 0;
            while (a > 0f && n < MaxTicks) { a += s; n++; }
            if (n >= MaxTicks) return Strings.WearsOffMore(n / 60f);
            return Strings.WearsOff(n / 60f);
        }

        internal static void Page(List<UiLine> l)
        {
            l.Add(UiLine.Heading(Strings.AddictionsHeading));
            var ps = PlayerStats.instance;
            var a = ps != null ? ps.addictions : null;
            if (a == null) { l.Add(UiLine.Dim(Strings.NoPlayer)); return; }
            for (int i = 0; i < 4; i++)
            {
                Info x;
                try { x = Get(a, i); }
                catch (Exception e) { Log.Exception("addiction read", e); continue; }
                string title = Strings.AddictionRow(x.Name, x.Level, x.Need);
                var detail = Eta(x.Level, x.Change);
                if (x.Level > 0f && x.Change < 0f && !float.IsInfinity(x.Change)) detail += "   " + Strings.PerHour(-x.Change);
                bool clamped = Clamp.Patched && (i < 2 ? Cfg.Hot.Clamp : i == 2 && Cfg.Hot.ClampSmoking);
                if (x.Level > 100f) detail += "   " + (clamped ? Strings.AboveCapFixed : Strings.AboveCap);
                if (x.Need >= 100f && x.Level > 0f)
                    detail += "   " + Strings.WithdrawalCost(x.Name, a.WithdrawalsymptomMentalhealthLoss * x.Level / 100f);
                l.Add(UiLine.Row(title, detail));
            }
            l.Add(UiLine.Dim(Strings.AddictionNote));
            DuckModel.Page(l, ps);
        }

        // "Addiction 82/100, wears off in about 117 h awake (paused while asleep)".
        internal static string Tooltip(int i)
        {
            var ps = PlayerStats.instance;
            if (ps == null || ps.addictions == null) return null;
            var x = Get(ps.addictions, i);
            return Strings.WithMore(Strings.TipAddiction(x.Level, Eta(x.Level, x.Change), x.Level > 0f && x.Change < 0f));
        }
    }
}
