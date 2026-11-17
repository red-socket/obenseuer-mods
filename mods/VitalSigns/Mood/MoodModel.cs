using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VitalSigns
{
    // The mental-health explainer (spec §7, S-012). The game keeps depression (C currentStatus, T mentalHealthTarget,
    // E effectiveMentalHealth); everything here is shown as 100 - depression and "points" (D14), clamped to 0-100 for
    // display. Only E is the HUD's "Mental health %"; C, T and the needs limit are labelled "underlying mood", because
    // alcohol and hygiene sit between them and the HUD (:75-76). Every number mirrors or calls the game's own code; the exact HUD breakdown
    // comes from M1's snapshot of the inputs of SRC/PlayerStatsMentalHealth.cs:75-76.
    internal static class MoodModel
    {
        internal static bool Patched;   // the mood group (M1) is on

        // ---- M1 snapshot ----

        private static bool _inTick, _recomputed;
        internal static bool LastTickRecomputed = true;
        private static bool _snapValid;
        private static float _c, _bac, _h, _e;

        internal static void TickStart()
        {
            _inTick = true;
            _recomputed = false;
        }

        // The postfix on PlayerStatsHygiene.GetMentalHealthEffect, only during the mood tick: currentStatus is after
        // the drift and the SMV factor, exactly what :75 divides.
        internal static void OnHygieneEffect(float result)
        {
            if (!_inTick) return;
            var ps = PlayerStats.instance;
            if (ps == null) return;
            _c = ps.mentalHealth.currentStatus;
            _bac = ps.alcohol.AlcoholInBlood;
            _h = result;
            _recomputed = true;
        }

        internal static void TickDone()
        {
            if (!_inTick) return;
            _inTick = false;
            LastTickRecomputed = !Patched || _recomputed;   // without the hygiene postfix there is nothing to tell
            if (_recomputed)
            {
                var ps = PlayerStats.instance;
                _e = ps != null ? ps.mentalHealth.effectiveMentalHealth : -1f;
                _snapValid = true;
            }
        }

        // A snapshot from another scene or save no longer explains the value restored from the save.
        internal static void OnSceneLoaded()
        {
            _snapValid = false;
            _inTick = false;
            LastTickRecomputed = true;
        }

        // ---- Numbers ----

        internal static float Pct(float depression) => 100f - Mathf.Clamp(depression, 0f, 100f);

        private struct Need { internal string Name; internal float Term; }

        // Mirror of MentalHealthStatsUpdate's awake branch (SRC/PlayerStatsMentalHealth.cs:131-195): same conditions,
        // same order, the game's own curve. MushroomsHigh is subtracted. Returns false when the curve can't be read.
        private static bool NeedsFloor(PlayerStats ps, List<Need> terms, out float floor)
        {
            floor = 0f;
            var curve = Vanilla.MoodCurve(ps.mentalHealth);
            if (curve == null) return false;
            var a = ps.addictions;
            var ht = ps.hungerAndThirst;
            float sum = 0f;
            void Add(string name, float v) { sum += v; terms?.Add(new Need { Name = name, Term = v }); }
            if (ps.health.currentStatus < 100f) Add(Strings.NeedHealth, curve.Evaluate(1f - ps.health.currentStatus / 100f));
            if (ps.tiredness.currentStatus > 0f) Add(Strings.NeedTiredness, curve.Evaluate(ps.tiredness.currentStatus / 100f));
            if (ht.Hunger > 0f) Add(Strings.NeedHunger, curve.Evaluate(ht.Hunger / 100f));
            if (ht.Thirst > 0f) Add(Strings.NeedThirst, curve.Evaluate(ht.Thirst / 100f));
            if (ht.Bowel > 0f) Add(Strings.NeedBowel, curve.Evaluate(ht.Bowel / 100f));
            if (ht.Bladder > 0f) Add(Strings.NeedBladder, curve.Evaluate(ht.Bladder / 100f));
            if (a.AlcoholNeed > 0f) Add(Strings.NeedAlcohol, curve.Evaluate(a.AlcoholNeed / 100f));
            if (a.MushroomNeed > 0f) Add(Strings.NeedMushroom, curve.Evaluate(a.MushroomNeed / 100f));
            if (a.SmokingNeed > 0f) Add(Strings.NeedSmokes, curve.Evaluate(a.SmokingNeed / 100f));
            if (GamblingGate.Effective(a) > 0f) Add(Strings.NeedGambling, curve.Evaluate(a.GamblingNeed / 100f));
            if (ps.mushrooms.MushroomsHighOrangeNormal > 0f) Add(Strings.NeedOrangeHigh, curve.Evaluate(ps.mushrooms.MushroomsHighOrangeNormal / 100f));
            if (ps.mushrooms.MushroomsHigh > 0f) Add(Strings.NeedHigh, -curve.Evaluate(ps.mushrooms.MushroomsHigh / 100f));
            floor = sum;
            return true;
        }

        // Y in "the next update would show Y%": the formula of :75-76 with today's inputs.
        private static float NextHud(PlayerStats ps) => HudFor(ps, ps.mentalHealth.currentStatus);

        // The HUD value for an underlying depression c, with today's alcohol and hygiene terms.
        private static float HudFor(PlayerStats ps, float c)
        {
            float bac = ps.alcohol.AlcoholInBlood;
            return 100f - Mathf.Clamp(c / (1f + 0.1f * bac) + ps.hygiene.GetMentalHealthEffect(), 0f, 100f);
        }

        private static bool SnapshotExplainsHud(PlayerStats ps) =>
            Patched && _snapValid && Mathf.Abs(_e - ps.mentalHealth.effectiveMentalHealth) < 0.01f;

        private const int MaxTicks = 200000;

        // Ticks until currentStatus stops, awake, at the game's speed (:53-70), then the awake needs limit (:195:
        // currentStatus is clamped to at least the floor after each move). With the floor above the target it stops at
        // the floor, so end = max(T, floor). -1: it doesn't move (or not within MaxTicks).
        private static int TicksToEnd(PlayerStats ps, bool hasFloor, float floor, out float factor, out float end)
        {
            var m = ps.mentalHealth;
            factor = ps.addictions.GetSmokingMentalHealthSpeedFactor();
            float step = m.mentalHealthChangeRate / 60f * factor;
            float c = m.currentStatus, t = m.mentalHealthTarget;
            end = hasFloor ? Math.Max(t, floor) : t;
            if (Mathf.Abs(c - end) < 0.005f) return 0;
            if (step <= 0f) return -1;
            for (int n = 1; n <= MaxTicks; n++)
            {
                if (t > c) { c += step; if (c > t) c = t; }
                else if (t < c) { c -= step; if (c < t) c = t; }
                if (hasFloor) c = Mathf.Clamp(c, floor, 100f);
                if (Mathf.Abs(c - end) < 0.005f) return n;
            }
            return -1;
        }

        // ---- Page (spec §7.1) ----

        internal static void Page(List<UiLine> l)
        {
            l.Add(UiLine.Heading(Strings.MoodHeading));
            if (Game.MentalHealthDisabled) { l.Add(UiLine.Text(Strings.MoodDisabled)); return; }
            var ps = PlayerStats.instance;
            if (ps == null || ps.mentalHealth == null) { l.Add(UiLine.Dim(Strings.NoPlayer)); return; }
            var m = ps.mentalHealth;
            bool asleep = Game.Asleep;

            l.Add(UiLine.Text(Strings.HudNow(Pct(m.effectiveMentalHealth))));
            if (SnapshotExplainsHud(ps))
            {
                float lift = _c - _c / (1f + 0.1f * _bac);
                float sum = _c / (1f + 0.1f * _bac) + _h;
                l.Add(UiLine.Small(Strings.Underlying(100f - _c), 1));
                if (lift > 0.005f) l.Add(UiLine.Small(Strings.AlcoholLift(lift), 1));
                l.Add(UiLine.Small(Strings.HygieneTerm(-_h), 1));
                if (sum < 0f || sum > 100f) l.Add(UiLine.Dim(Strings.Clamped, 1));
            }
            if (!LastTickRecomputed || !SnapshotExplainsHud(ps))
                l.Add(UiLine.Dim(LastTickRecomputed ? Strings.NextUpdateOnly(NextHud(ps)) : Strings.HudStale(NextHud(ps)), 1));

            // Heading toward (underlying mood; the ETA is for waking time, so the awake needs limit applies)
            var terms = new List<Need>();
            bool hasFloor = NeedsFloor(ps, terms, out float floor);
            l.Add(UiLine.Space());
            l.Add(UiLine.Text(Strings.HeadingToward(Pct(m.mentalHealthTarget))));
            if (ps.mushrooms.SMVProgression > 50f)
                l.Add(UiLine.Warn(Strings.SmvMood(ps.mushrooms.SMVProgression, (ps.mushrooms.SMVProgression - 50f) / 50f), 1));
            else
            {
                int ticks = TicksToEnd(ps, hasFloor, floor, out float factor, out float end);
                bool held = end > m.mentalHealthTarget + 0.005f;
                if (held) l.Add(UiLine.Warn(Strings.HeldByNeeds(Pct(end)), 1));
                if (ticks == 0) { if (!held) l.Add(UiLine.Small(Strings.AtTarget, 1)); }
                else if (ticks > 0) l.Add(UiLine.Small(Strings.ReachesIn(Pct(end), ticks / 60f), 1));
                if (ticks >= 0) l.Add(UiLine.Dim(Strings.HudAtEnd(HudFor(ps, end)), 1));
                if (Mathf.Approximately(factor, 0.25f)) l.Add(UiLine.Dim(Strings.SmokingSlow, 1));
                else if (factor > 1f) l.Add(UiLine.Dim(Strings.SmokingFast, 1));
            }
            l.Add(UiLine.Dim(Strings.AsleepDrift(m.mentalHealthChangeRate / 60f), 1));

            // Needs limit (awake only)
            l.Add(UiLine.Space());
            if (!hasFloor) l.Add(UiLine.Dim(Strings.NeedsUnreadable));
            else
            {
                l.Add(UiLine.Text(Strings.NeedsCap(100f - floor)));
                foreach (var t in terms)
                    if (Mathf.Abs(t.Term) >= 0.05f) l.Add(UiLine.Small(Strings.NeedTerm(t.Name, t.Term), 1));
                if (asleep) l.Add(UiLine.Dim(Strings.NeedsAsleep, 1));
            }
            if ((!GamblingGate.Patched || !Cfg.Hot.GamblingFix) && ps.addictions.SmokingNeed == 0f && ps.addictions.GamblingNeed > 0f)
                l.Add(UiLine.Warn(Strings.GamblingBug, 1));

            // Expectations
            l.Add(UiLine.Space());
            float o = PlayerStatsMentalHealth.expectationOffset;
            l.Add(UiLine.Text(Strings.Expectations(m.expectationLevel)));
            l.Add(UiLine.Small(Strings.ExpectationEffect(o), 1));

            // Strength of good things now (GetItemMentalHealt :296-304)
            l.Add(UiLine.Small(Strings.Strength(Mathf.Clamp(m.currentStatus, 0f, 100f))));

            // Fatigue
            l.Add(UiLine.Space());
            l.Add(UiLine.Heading(Strings.FatigueHeading));
            Fatigue.Lines(l, m);

            // Withdrawal and ducks
            var a = ps.addictions;
            float w = a.WithdrawalsymptomMentalhealthLoss;
            bool any = false;
            void Withdrawal(string name, float need, float level)
            {
                if (need < 100f || level <= 0f) return;
                if (!any) { l.Add(UiLine.Space()); any = true; }
                l.Add(UiLine.Warn(Strings.WithdrawalCost(name, w * level / 100f)));
            }
            Withdrawal(Strings.AddAlcohol, a.AlcoholNeed, a.AlcoholAddiction);
            Withdrawal(Strings.AddMushroom, a.MushroomNeed, a.MushroomAddiction);
            Withdrawal(Strings.AddSmoking, a.SmokingNeed, a.SmokingAddiction);
            Withdrawal(Strings.AddGambling, a.GamblingNeed, a.GamblingAddiction);
            string duck = DuckModel.MoodLine(ps);
            if (duck != null) l.Add(UiLine.Warn(duck));
        }

        // ---- Tooltip (spec §7.2), at most 4 lines ----

        internal static string Tooltip()
        {
            if (Game.MentalHealthDisabled) return Strings.MoodDisabled;
            var ps = PlayerStats.instance;
            if (ps == null || ps.mentalHealth == null) return null;
            var m = ps.mentalHealth;
            var sb = new StringBuilder();
            sb.Append(Strings.HeadingToward(Pct(m.mentalHealthTarget)));
            if (ps.mushrooms.SMVProgression <= 50f)
            {
                bool hasFloor = NeedsFloor(ps, null, out float floor);
                int ticks = TicksToEnd(ps, hasFloor, floor, out _, out float end);
                if (end > m.mentalHealthTarget + 0.005f) sb.Append(Strings.TipHeldAt(Pct(end)));
                if (ticks > 0) sb.Append(' ').Append(Strings.AboutAwake(ticks / 60f));
            }
            if (SnapshotExplainsHud(ps))
            {
                float lift = _c - _c / (1f + 0.1f * _bac);
                sb.Append('\n');
                if (lift > 0.05f) sb.Append(Strings.TipAlcohol(lift)).Append("   ");
                sb.Append(Strings.TipHygiene(-_h));
            }
            string med = Fatigue.WorstMedicine(m);
            if (med != null) sb.Append('\n').Append(med);
            else if (!Game.Asleep)
            {
                var terms = new List<Need>();
                if (NeedsFloor(ps, terms, out float floor) && floor >= 0.05f)
                {
                    terms.Sort((x, y) => y.Term.CompareTo(x.Term));
                    var names = new List<string>();
                    for (int i = 0; i < terms.Count && names.Count < 2; i++)
                        if (terms[i].Term >= 0.05f) names.Add(terms[i].Name.ToLowerInvariant());
                    sb.Append('\n').Append(Strings.TipNeeds(100f - floor, string.Join(", ", names.ToArray())));
                }
            }
            return Strings.WithMore(sb.ToString());
        }
    }
}
