using System;
using System.Collections.Generic;
using OS.Items;

namespace VitalSigns
{
    // "Losing health now" (spec §6): live reads mirrored on the game's own formulas, at 2 Hz. A tick is one stats update:
    // one per game minute at normal speed (SRC/PlayerStats.cs:313-325); during the game's own fast waits the extra seconds
    // are dropped, so every time here is "at normal speed".
    internal static class Forecast
    {
        internal struct Pool { internal int Ticks; internal float Hp; internal bool Capped; }

        // Mirror of the drain branch (SRC/PlayerStatsHealth.cs:57-71): the full rate every tick until the pool reaches 0.
        internal static Pool Drain(float pool, float r, float hp)
        {
            var res = new Pool();
            if (pool >= 0f || r <= 0f) return res;
            float p = pool;
            int n = 0;
            while (p < 0f && n < 100000) { p += r; if (p > 0f) p = 0f; n++; }
            res.Ticks = n;
            res.Hp = Math.Min(n * r, Math.Max(0f, hp));
            res.Capped = n >= 100000;
            return res;
        }

        // Mirror of the regen branch (:43-55): health can't pass 100.
        internal static Pool Regen(float pool, float r, float hp)
        {
            var res = new Pool();
            if (pool <= 0f || r <= 0f) return res;
            float p = pool, h = hp;
            int n = 0;
            while (p > 0f && h < 100f && n < 100000) { h += r; p -= r; if (h > 100f) h = 100f; n++; }
            res.Ticks = n;
            res.Hp = Math.Max(0f, h - hp);
            return res;
        }

        internal struct Bleed { internal bool Endless; internal float PerTick, Hp; internal int Ticks; }

        // Mirror of :72-88: below 50 it heals 2 * (1 - b/100) a tick; the cost is b * 0.1 after that step.
        internal static Bleed Bleeding(float b)
        {
            var res = new Bleed();
            if (b <= 0f) return res;
            if (b >= 50f) { res.Endless = true; res.PerTick = b * 0.1f; return res; }
            float loss = 0f;
            int n = 0;
            while (b > 0f && n < 200)
            {
                if (b < 50f) b = Math.Max(0f, b - 2f * (1f - b / 100f));
                loss += b * 0.1f;
                n++;
            }
            res.Hp = loss;
            res.Ticks = n;
            return res;
        }

        internal struct Death { internal int Ticks; internal string Causes; }

        // The stats tick's losses in code order (SRC/PlayerStats.cs:315-322): the pool and bleeding (health), starvation
        // and dehydration (hunger and thirst, only while the bar is already full and still rising, PlayerStatsHungerAndThirst
        // .cs:84-89, :96-101, :114-119, :126-131), then the radiation dose with no new exposure (PlayerStatsRadiation
        // .cs:27-36), until health reaches 0 or every term stops. Ticks -1: not fatal. Causes names the terms counted.
        internal static Death DeathIn(PlayerStats ps)
        {
            var res = new Death { Ticks = -1 };
            var h = ps.health;
            float hp = h.currentStatus, p = h.HealthToRegenerate, b = h.bleeding, r = h.HealthRegenerationRate;
            float starve = FullNeedDamage(ps, true), thirst = FullNeedDamage(ps, false);
            var rad = ps.radiation;
            float d = rad != null ? rad.Dose : 0f, t = rad != null ? rad.DoseDamageThreshold : 0f, f = rad != null ? rad.DoseDamageFull : 0f;
            float dmg = rad != null ? rad.RadiationDamage : 0f, fade = rad != null ? rad.DoseFadeout : 0f;
            bool radOn = rad != null && f > t && d - fade / 60f > t;
            if (p >= 0f && b <= 0f && starve <= 0f && thirst <= 0f && !radOn) return res;

            var causes = new List<string>();
            if (p < 0f) causes.Add(Strings.CauseHidden);
            if (b > 0f) causes.Add(Strings.CauseBleeding);
            if (starve > 0f) causes.Add(Strings.CauseStarving);
            if (thirst > 0f) causes.Add(Strings.CauseThirst);
            if (radOn) causes.Add(Strings.CauseRadiation);

            for (int n = 1; n <= 20000; n++)
            {
                if (p > 0f) { hp += r; p -= r; if (p < 0f) p = 0f; if (hp > 100f) { hp = 100f; p = 0f; } }
                else if (p < 0f) { hp -= r; p = Math.Min(0f, p + r); if (hp <= 0f) return Found(res, n, causes); }
                if (b > 0f)
                {
                    if (b < 50f) b = Math.Max(0f, b - 2f * (1f - b / 100f));
                    hp -= b * 0.1f;
                    if (hp < 0f) return Found(res, n, causes);
                }
                if (starve > 0f) { hp -= starve; if (hp < 0f) return Found(res, n, causes); }
                if (thirst > 0f) { hp -= thirst; if (hp < 0f) return Found(res, n, causes); }
                if (radOn && d > 0f)
                {
                    d -= fade / 60f;
                    if (d > t)
                    {
                        hp -= (d - t) / (f - t) * (dmg / 60f);
                        if (hp < 0f) return Found(res, n, causes);
                    }
                }
                if (p >= 0f && b <= 0f && starve <= 0f && thirst <= 0f && (!radOn || d <= t)) return res;
            }
            return res;
        }

        private static Death Found(Death res, int n, List<string> causes)
        {
            res.Ticks = n;
            res.Causes = Strings.ListAnd(causes);
            return res;
        }

        // ThristStarvationDamage / 60 a tick while the bar sits at 100 and this tick's rise is positive (the bar is clamped
        // to 100 first, so the damage needs it to pass 100 again); 0 otherwise.
        private static float FullNeedDamage(PlayerStats ps, bool hunger)
        {
            var ht = ps.hungerAndThirst;
            if (ht == null || DifficultyController.instance == null || ps.mushrooms == null || ps.addictions == null) return 0f;
            if ((hunger ? ht.Hunger : ht.Thirst) < 100f) return 0f;
            float rise;
            if (Game.Asleep) rise = (hunger ? ht.GetHungerRate() : ht.GetThirstRate()) / 60f * ht.SleepHungerThristRateFactor;
            else if (hunger) rise = ht.GetHungerRate() / 60f * (1f - ps.mushrooms.MushroomsHigh / 100f) * (1f - ps.addictions.AlcoholAddiction / 200f);
            else rise = ht.GetThirstRate() / 60f;
            return rise > 0f ? ht.ThristStarvationDamage / 60f : 0f;
        }

        internal struct Rad { internal bool Damaging; internal float PerHour; internal int StopTicks; internal float StopHp; internal bool Capped; }

        // Mirror of SRC/PlayerStatsRadiation.cs:27-41 with no new exposure.
        internal static Rad Radiation(PlayerStatsRadiation r)
        {
            var res = new Rad();
            if (r == null) return res;
            float d = r.Dose, t = r.DoseDamageThreshold, f = r.DoseDamageFull, dmg = r.RadiationDamage, fade = r.DoseFadeout;
            if (d <= t || f <= t) return res;
            res.Damaging = true;
            res.PerHour = (d - t) / (f - t) * dmg;
            float hp = 0f;
            int n = 0;
            const int cap = 60 * 24 * 60;   // 60 days of ticks
            while (d > t && n < cap)
            {
                d -= fade / 60f;
                if (d > t) hp += (d - t) / (f - t) * (dmg / 60f);
                n++;
            }
            res.StopTicks = n;
            res.StopHp = hp;
            res.Capped = n >= cap;
            return res;
        }

        // Mirror of SRC/PlayerStatsTiredness.cs:45-56: awake, tiredness after this tick's exhaustion step below -100, and
        // caffeine still to come. Pending Coffee moves into tirednessToAdd on every stats call before the tick
        // (ProcessItemSpecialEffects, SRC/PlayerStats.cs:262-304, :329), how much depends on how many calls come first,
        // so with Coffee pending the amount is a lower bound (atLeast).
        internal static float CaffeineNext(PlayerStats ps, out bool atLeast)
        {
            atLeast = false;
            var t = ps.tiredness;
            if (t == null || ps.mushrooms == null || Game.Asleep) return 0f;
            float next = t.currentStatus + t.Exhaustionrate / 60f * (1f - ps.mushrooms.MushroomsHigh / 100f);
            if (!(next < -100f) || !(t.tirednessToAdd < 0f)) return 0f;
            atLeast = ps.unprocessedSpecialEffects != null &&
                      ps.unprocessedSpecialEffects.TryGetValue(ItemConsumable.SpecialEffect.Coffee, out float pending) && pending > 0f;
            return Math.Abs(t.tirednessToAdd) * 0.5f;
        }

        internal static bool CaffeinePending(PlayerStats ps)
        {
            var t = ps.tiredness;
            return (t != null && t.tirednessToAdd < 0f) ||
                   (ps.unprocessedSpecialEffects != null && ps.unprocessedSpecialEffects.ContainsKey(ItemConsumable.SpecialEffect.Coffee));
        }

        // The header lines of the Health page (and the source of the Health tooltip's first line).
        internal static void Lines(List<UiLine> l)
        {
            var ps = PlayerStats.instance;
            var h = ps != null ? ps.health : null;
            if (h == null) { l.Add(UiLine.Dim(Strings.NoPlayer)); return; }
            float hp = h.currentStatus, pool = h.HealthToRegenerate, r = h.HealthRegenerationRate;
            l.Add(UiLine.Text(Strings.HealthNow((int)Math.Round(hp, MidpointRounding.AwayFromZero))));

            if (pool < 0f)
            {
                var d = Drain(pool, r, hp);
                l.Add(UiLine.Warn(Strings.HiddenToCome(d.Hp, d.Ticks)));
                if (PoolLedger.Debts.Count > 0)
                    foreach (var debt in PoolLedger.Debts)
                        l.Add(UiLine.Small(Strings.DebtLine(Ctx.Label(debt.LabelId), debt.Remaining, debt.Original, debt.Note), 1));
                l.Add(UiLine.Dim(Strings.AdviceHealingFood, 1));
                l.Add(UiLine.Dim(Strings.AdviceDoctor, 1));
            }
            else if (pool > 0f)
            {
                var g = Regen(pool, r, hp);
                if (g.Hp > 0f) l.Add(UiLine.Small(Strings.HiddenHealing(g.Hp, g.Ticks)));
            }

            float b = h.bleeding;
            if (b > 0f)
            {
                var bl = Bleeding(b);
                l.Add(UiLine.Warn(bl.Endless ? Strings.BleedEndless(bl.PerTick) : Strings.BleedStops(bl.Hp, bl.Ticks)));
                string causes = BleedCauses.Summary();
                if (causes != null) l.Add(UiLine.Small(Strings.BleedCausesLine(causes), 1));
            }

            var death = DeathIn(ps);
            if (death.Ticks > 0) l.Add(UiLine.Warn(Strings.DeathIn(death.Ticks, death.Causes)));

            var m = ps.mushrooms;
            if (m != null)
            {
                if (m.SMVProgressionRate != 0f) l.Add(UiLine.Text(Strings.SmvCapNow(100f - m.SMVProgression)));
                else if (m.SMVProgression > 0f) l.Add(UiLine.Small(Strings.SmvNoCap));
                if (m.SMVProgression > 40f) l.Add(UiLine.Small(Strings.SmvFood));
                if (Tracker.CancelledByCap > 0.05f) l.Add(UiLine.Dim(Strings.CapCancelled(Tracker.CancelledByCap), 1));
            }

            var rad = Radiation(ps.radiation);
            if (rad.Damaging)
            {
                l.Add(UiLine.Warn(Strings.RadiationRate(rad.PerHour)));
                l.Add(UiLine.Small(rad.Capped ? Strings.RadiationLong(rad.StopTicks) : Strings.RadiationStops(rad.StopTicks, rad.StopHp), 1));
            }

            float caf = CaffeineNext(ps, out bool atLeast);
            if (caf > 0f) l.Add(UiLine.Warn(Strings.CaffeineNextUpdate(caf, atLeast)));
            else if (CaffeinePending(ps)) l.Add(UiLine.Small(Strings.CaffeineKickingIn));
        }
    }
}
