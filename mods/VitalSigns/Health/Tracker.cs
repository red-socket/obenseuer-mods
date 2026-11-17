using System;
using OS.Items;

namespace VitalSigns
{
    // H2's snapshot: the context depth to restore and the values the health tick starts from.
    internal struct TickState
    {
        internal int Depth;   // depth + 1; 0 = nothing pushed
        internal bool Ok;
        internal float H0, P0, B0, R;
    }

    // Read-only accounting of health, the hidden-damage pool and bleeding (spec §5.2). A baseline is kept; every context
    // hook accounts the change since the last look against the innermost context before it pushes or pops, and
    // Plugin.Update accounts once a frame against "Untraced". Values are read live from PlayerStats.instance every time.
    // Nothing here ever writes a game value.
    internal static class Tracker
    {
        private const float Eps = 1e-4f;

        internal static bool CoreOn, ItemsOn;   // patch groups log-core, log-items
        internal static float CancelledByCap;           // regeneration cancelled by the fungal cap this session

        private static PlayerStats _ps;
        private static float _hp, _pool, _bleed;
        private static bool _valid, _fresh = true;
        private static float _regen;
        private static bool _regenSet;

        internal static bool Live => CoreOn && Cfg.Hot.Log;

        internal static void Invalidate() => _valid = false;

        // The next baseline starts the ledgers over (a load from disk, a new game).
        internal static void MarkFresh() => _fresh = true;

        // Values being restored or no player: no accounting; the next look rebaselines (spec §5.6).
        private static bool Ready()
        {
            var ps = PlayerStats.instance;
            if (ps == null || ps.health == null || SaveController.Loading)
            {
                _valid = false;
                return false;
            }
            if (!_valid || !ReferenceEquals(ps, _ps))
            {
                Rebaseline(ps);
                return false;
            }
            return true;
        }

        // No logging. A door keeps the ledgers and reconciles them with the live pool (should be no change); a fresh
        // load clears them and opens "From before this load" for a pool or bleeding restored from the save.
        private static void Rebaseline(PlayerStats ps)
        {
            _ps = ps;
            var h = ps.health;
            float pool = h.HealthToRegenerate, bleed = h.bleeding;
            if (_fresh)
            {
                _fresh = false;
                PoolLedger.Clear();
                BleedCauses.Clear();
                CaffeineList.Clear();
                if (pool < 0f) PoolLedger.Debit(Ctx.Intern(Strings.FromBeforeLoad), Strings.FromBeforeLoadDetail, -pool);
                if (bleed > 0f) BleedCauses.Add(Strings.FromBeforeLoad, bleed);
            }
            else
            {
                PoolLedger.Reconcile(pool);
                if (bleed <= 0f) BleedCauses.Clear();
            }
            _hp = h.currentStatus;
            _pool = pool;
            _bleed = bleed;
            _regenSet = false;
            _valid = true;
            Log.Verbose($"Log: baseline HP {_hp:0.##}, pool {_pool:0.##}, bleeding {_bleed:0.##}.");
        }

        // ---- Context hooks ----

        internal static int Enter(Frame f)
        {
            if (!Live) return 0;
            Account();
            return Ctx.Push(f) + 1;
        }

        internal static void Exit(int state)
        {
            if (state <= 0) return;
            try { Account(); }
            finally { Ctx.Truncate(state - 1); }
        }

        internal static void Account()
        {
            if (!Live)
            {
                _valid = false;   // turned off live: turning it on again starts from a new baseline, not a stale one
                return;
            }
            if (!Ready()) return;
            var h = _ps.health;
            float hp = h.currentStatus, pool = h.HealthToRegenerate, bleed = h.bleeding;
            if (hp == _hp && pool == _pool && bleed == _bleed) return;
            try { Apply(ref Ctx.Top, hp, pool, bleed); }
            catch (Exception e) { Log.Exception("log account", e); }
            _hp = hp;
            _pool = pool;
            _bleed = bleed;
        }

        private static void Apply(ref Frame f, float hp, float pool, float bleed)
        {
            float dH = hp - _hp;
            if (dH < -Eps) LossFor(ref f, -dH);
            if (pool != _pool) PoolChange(ref f, _pool, pool);
            if (bleed != _bleed) BleedChange(ref f, bleed - _bleed, bleed);
        }

        // Only decreases become losses; healing only pays the ledgers down.
        private static void LossFor(ref Frame f, float loss)
        {
            switch (f.Kind)
            {
                case K.Damage: DamageLoss(ref f, loss); break;
                case K.SmvTick: SmvCapLoss(loss); break;
                default: DamageLog.Loss(LossKey.Make(Cat.Untraced, 0, 0, 0), loss, true, false); break;
            }
        }

        private static void DamageLoss(ref Frame f, float loss)
        {
            int flags = (f.Silent ? LossKey.Silent : 0) | (f.Asleep ? LossKey.Asleep : 0), waitId = 0;
            for (int i = Ctx.Depth - 2; i >= 0; i--)
            {
                var o = Ctx.At(i);
                if (o.Kind == K.Wait && waitId == 0) waitId = o.Id;
                else if (o.Kind == K.FungalTreatment) flags |= LossKey.Fungal;
                else if (o.Kind == K.Crafting) flags |= LossKey.Crafting;
            }
            DamageLog.Loss(LossKey.Make(Cat.Damage, f.Reason, flags, waitId), loss, f.Silent, false);
        }

        // HP = 100 - SMV (SRC/PlayerStatsMushrooms.cs:72-75), netted against the regeneration it cancels in the same
        // stats tick: a positive pool at the cap would otherwise log a 1 HP loss every tick.
        private static void SmvCapLoss(float loss)
        {
            float regen = _regenSet ? _regen : 0f;
            _regenSet = false;
            _regen = 0f;
            float cancelled = Math.Min(loss, regen);
            CancelledByCap += cancelled;
            float amount = loss - cancelled;
            if (amount <= Eps) return;
            float smv = _ps != null && _ps.mushrooms != null ? _ps.mushrooms.SMVProgression : 0f;
            DamageLog.Loss(LossKey.Make(Cat.SmvCap, 0, 0, 0), amount, true, true, null, Strings.SmvCapLabel(100f - smv));
        }

        private static void PoolChange(ref Frame f, float p0, float p1)
        {
            float d0 = Math.Max(0f, -p0), d1 = Math.Max(0f, -p1);
            float r = _ps.health.HealthRegenerationRate;
            if (d1 > d0 + Eps)
            {
                float add = d1 - d0;
                switch (f.Kind)
                {
                    case K.Item: ItemDebit(ref f, d0, d1, r); break;
                    case K.TirednessTick:
                    {
                        int id = Ctx.Intern(Strings.CaffeineOverdose);
                        string detail = CaffeineList.Summary();
                        PoolLedger.Debit(id, detail, add);
                        DamageLog.Owed(id, detail, add, Marginal(d0, d1, r));
                        break;
                    }
                    case K.Wait:
                        PoolLedger.Debit(f.Id, null, add);
                        DamageLog.Owed(f.Id, null, add, Marginal(d0, d1, r));
                        break;
                    default:
                    {
                        int id = Ctx.Intern(Strings.UnknownSource);
                        PoolLedger.Debit(id, null, add);
                        DamageLog.Owed(id, null, add, Marginal(d0, d1, r));
                        break;
                    }
                }
            }
            else if (d1 < d0 - Eps) PoolLedger.Credit(d0 - d1, Strings.FrameSource(f));
            PoolLedger.Reconcile(p1);
        }

        // What raising the queue from d0 to d1 adds to the drain at the moment it is queued. The drain takes the full rate
        // from one shared pool every tick (SRC/PlayerStatsHealth.cs:57-64), so the queue costs ceil(d/r) * r in all and
        // the rounding belongs to the whole pool, not to each debt.
        private static float Marginal(float d0, float d1, float r)
        {
            if (r <= 0f) return d1 - d0;
            return ((float)Math.Ceiling(d1 / r - 1e-4f) - (float)Math.Ceiling(d0 / r - 1e-4f)) * r;
        }

        // Two debts from one item when the SMV > 40 food penalty is part of it, penalty first (spec §5.4).
        private static void ItemDebit(ref Frame f, float d0, float d1, float r)
        {
            float add = d1 - d0, rest = add, at = d0;
            if (f.Smv < 0f)
            {
                float pen = Math.Min(add, -f.Smv);
                rest -= pen;
                int pid = Ctx.Intern(Strings.FoodPenaltyLabel(Ctx.Label(f.Id)));
                float smv = _ps.mushrooms != null ? _ps.mushrooms.SMVProgression : 0f;
                string detail = Strings.FoodPenaltyDetail(smv);
                PoolLedger.Debit(pid, detail, pen);
                DamageLog.Owed(pid, detail, pen, Marginal(at, at + pen, r));
                at += pen;
            }
            if (rest > Eps)
            {
                PoolLedger.Debit(f.Id, null, rest);
                DamageLog.Owed(f.Id, null, rest, Marginal(at, d1, r));
            }
        }

        private static void BleedChange(ref Frame f, float dB, float b1)
        {
            if (dB > Eps)
            {
                string label = BleedLabel(ref f);
                BleedCauses.Add(label, dB);
                DamageLog.BleedAdded(label, dB);
            }
            if (b1 <= 0f) BleedCauses.Clear();
        }

        private static string BleedLabel(ref Frame f)
        {
            switch (f.Kind)
            {
                case K.Damage:
                case K.SelfHarm:
                    if (Ctx.Has(K.SelfHarm)) return Ctx.Has(K.MoodTick) ? Strings.SelfHarmBreakdown : Strings.SelfHarmBlackout;
                    return Strings.Wound;
                case K.Item: return Ctx.Label(f.Id);
                case K.Toilet: return Strings.CrumpledPaper;
                default: return Strings.OtherBleeding;
            }
        }

        // ---- H2: the health tick, an exact mirror of SRC/PlayerStatsHealth.cs:41-89 (spec §5.3) ----

        internal static TickState TickStart()
        {
            var s = new TickState();
            if (!Live) return s;
            Account();
            _regenSet = false;
            _regen = 0f;
            if (_valid && ReferenceEquals(PlayerStats.instance, _ps) && !SaveController.Loading)
            {
                var h = _ps.health;
                s.Ok = true;
                s.H0 = h.currentStatus;
                s.P0 = h.HealthToRegenerate;
                s.B0 = h.bleeding;
                s.R = h.HealthRegenerationRate;
            }
            s.Depth = Ctx.Push(new Frame { Kind = K.HealthTick }) + 1;
            return s;
        }

        internal static void TickDone(TickState s)
        {
            if (s.Depth <= 0) return;
            try
            {
                if (s.Ok && Live && _valid && ReferenceEquals(PlayerStats.instance, _ps) && !SaveController.Loading) Split(s);
                else Account();
            }
            catch (Exception e) { Log.Exception("health tick split", e); }
            finally { Ctx.Truncate(s.Depth - 1); }
        }

        private static void Split(TickState s)
        {
            var h = _ps.health;
            float h1 = h.currentStatus, p1 = h.HealthToRegenerate, b1 = h.bleeding;
            float h0 = s.H0, p0 = s.P0, b0 = s.B0, r = s.R;

            // A drop from above 100 to 100 is the cap, not a loss; the drain takes the full rate even when less is owed,
            // and death sets health to 0.
            float hMid = p0 > 0f ? (h0 + r > 100f ? 100f : h0 + r)
                       : p0 < 0f ? (h0 - r <= 0f ? 0f : h0 - r)
                       : h0;
            float regen = p0 > 0f ? Math.Max(0f, hMid - h0) : 0f;
            float poolLoss = p0 < 0f ? h0 - Math.Max(hMid, 0f) : 0f;
            float bleedLoss = b0 > 0f ? Math.Max(0f, hMid - h1) : 0f;   // b1 * 0.1 unless the death clamp hit
            bool poolDeath = p0 < 0f && hMid <= 0f;

            if (p0 < 0f) PoolLedger.Drain(Math.Min(r, -p0), poolLoss, poolDeath);
            if (bleedLoss > Eps)
                DamageLog.Loss(LossKey.Make(Cat.Bleeding, 0, 0, 0), bleedLoss, false, true, BleedCauses.Summary());

            float rest = (h0 - h1) - ((h0 - hMid) + bleedLoss);
            if (rest > 0.01f)
            {
                Log.WarnOnce("tick split", $"Log: a health update lost {rest:0.##} HP more than the game's own formula explains (another mod?).");
                DamageLog.Loss(LossKey.Make(Cat.Untraced, 0, 0, 0), rest, true, true);
            }

            _regen = regen;
            _regenSet = true;
            if (b1 <= 0f) BleedCauses.Clear();
            _hp = h1;
            _pool = p1;
            _bleed = b1;
            PoolLedger.Reconcile(p1);
        }

        // ---- H3/H4: items ----

        // H3, inside the consume call where c.meta is valid. The Health quality flag is linear, so this is exact; negative
        // for the SMV > 40 food penalty (SRC/OS/Items/ItemConsumable.cs:290-301). GetHealth is called here and nowhere else:
        // meta is shared with the tooltip, ItemInfoPanel and auto-consume previews.
        internal static float SmvShare(ItemConsumable c)
        {
            if (c == null) return 0f;
            return c.GetHealth() - ItemQualityData.ApplyQuality(ItemQualityData.Flags.Health, c.Health, c.meta);
        }

        // ---- Live reads for the window ----

        internal static PlayerStatsHealth Health => PlayerStats.instance != null ? PlayerStats.instance.health : null;
    }
}
