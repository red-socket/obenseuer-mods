using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VitalSigns
{
    // Toast rules (spec §5.9, D6, D7). Threshold notes per hidden loss key over a tumbling game-time window, then a
    // cooldown in game minutes with a one real minute floor. With WakeSummary on, held while asleep or waiting
    // (TimeKeeper passes an hour in seconds), then one summary; with it off, shown as they happen. The death summary is built on the Update after Death.PlayerDeath, once the enclosing
    // finalizers have accounted the killing blow.
    internal static class Alerts
    {
        private sealed class Win { internal double T0, CoolUntil; internal float Sum, RealCoolUntil; }

        private static readonly Dictionary<ulong, Win> Wins = new Dictionary<ulong, Win>();
        private static readonly Dictionary<ulong, float> Held = new Dictionary<ulong, float>();
        private static readonly Dictionary<ulong, string> HeldLabel = new Dictionary<ulong, string>();
        private static bool _holding, _heldSlept;
        private static double _holdStart, _lastNow;

        internal sealed class DeathNote { internal string Title, Detail; internal ulong Stamp; }
        internal static DeathNote LastDeath;

        private sealed class Pending
        {
            internal DamageTypeNames Reason;
            internal bool InHealthTick, Asleep, Silent, Fungal, Crafting;
            internal int WaitId;
            internal string Debts, Bleeding;
        }
        private static Pending _pending;

        // ---- Threshold notes ----

        internal static void OnLoss(ulong key, string label, float hp, bool hidden)
        {
            if (!Cfg.Toasts.Enabled.Value) return;
            if (Cfg.Toasts.HiddenOnly.Value && !hidden) return;
            double now = GameClock.Now;
            if (now < _lastNow) Wins.Clear();   // can't happen (monotonic), but never toast on a stale window
            _lastNow = now;
            label = Strings.ToastLabel(key, label);
            if (_holding)
            {
                Held[key] = (Held.TryGetValue(key, out var h) ? h : 0f) + hp;
                if (!HeldLabel.ContainsKey(key)) HeldLabel[key] = label;
                return;
            }
            if (!Wins.TryGetValue(key, out var w)) Wins[key] = w = new Win { T0 = now };
            double span = Math.Max(1, Cfg.Toasts.WindowMinutes.Value) * 60.0;
            if (now - w.T0 > span) { w.T0 = now; w.Sum = 0f; }
            w.Sum += hp;
            if (w.Sum < Cfg.Toasts.Threshold.Value) return;
            if (now < w.CoolUntil || Time.realtimeSinceStartup < w.RealCoolUntil) return;
            int minutes = Math.Max(1, (int)Math.Round((now - w.T0) / 60.0));
            ToastStack.Show(Strings.ThresholdToast(label, w.Sum, minutes), Cfg.Toasts.Seconds.Value);
            Cool(w, now);
            w.T0 = now;
            w.Sum = 0f;
        }

        private static void Cool(Win w, double now)
        {
            w.CoolUntil = now + Math.Max(1, Cfg.Toasts.CooldownMinutes.Value) * 60.0;
            w.RealCoolUntil = Time.realtimeSinceStartup + 60f;
        }

        // ---- Sleep and waits, death (Plugin.Update) ----

        internal static void Tick()
        {
            bool asleep = Game.Asleep, holding = (asleep || Game.Waiting) && Cfg.Toasts.WakeSummary.Value;
            if (holding && !_holding)
            {
                _holding = true;
                _heldSlept = asleep;
                _holdStart = GameClock.Now;
                Held.Clear();
                HeldLabel.Clear();
            }
            else if (holding) _heldSlept |= asleep;
            else if (_holding)
            {
                _holding = false;
                WakeSummary();
            }
            if (_pending != null) BuildDeath();
        }

        private static void WakeSummary()
        {
            if (Held.Count == 0) return;
            float total = 0f;
            foreach (var v in Held.Values) total += v;
            var keys = new List<ulong>(Held.Keys);
            keys.Sort((a, b) => Held[b].CompareTo(Held[a]));
            if (Cfg.Toasts.Enabled.Value && total >= Cfg.Toasts.Threshold.Value)
            {
                var sb = new StringBuilder();
                double now = GameClock.Now;
                for (int i = 0; i < keys.Count && i < 3; i++)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(HeldLabel[keys[i]]).Append(' ').Append(Fmt.Hp(Held[keys[i]]));
                    if (!Wins.TryGetValue(keys[i], out var w)) Wins[keys[i]] = w = new Win { T0 = now };
                    Cool(w, now);
                    w.Sum = 0f;
                    w.T0 = now;
                }
                if (keys.Count > 3) sb.Append(", ...");
                float hours = (float)((GameClock.Now - _holdStart) / 3600.0);
                ToastStack.Show(Strings.WakeToast(_heldSlept, hours, sb.ToString()), Cfg.Toasts.Seconds.Value);
            }
            Held.Clear();
            HeldLabel.Clear();
        }

        // H8 prefix (only for a living player; godmode never gets here). Snapshots what the summary needs before the
        // finalizers close the debts.
        internal static void PendingDeath(DamageTypeNames reason)
        {
            var p = new Pending { Reason = reason, InHealthTick = Ctx.Has(K.HealthTick), Debts = PoolLedger.Summary(4), Bleeding = BleedCauses.Summary() };
            bool damageSeen = false;
            for (int i = Ctx.Depth - 1; i >= 0; i--)
            {
                var f = Ctx.At(i);
                if (f.Kind == K.Wait && p.WaitId == 0) p.WaitId = f.Id;
                else if (f.Kind == K.FungalTreatment) p.Fungal = true;
                else if (f.Kind == K.Crafting) p.Crafting = true;
                else if (f.Kind == K.Damage && !damageSeen)
                {
                    // The innermost Damage frame is the killing call: its silent flag tells the radiation dose
                    // (PlayerStatsRadiation.cs:36, silent) from a radiation area.
                    damageSeen = true;
                    p.Silent = f.Silent;
                    p.Asleep = f.Asleep;
                }
            }
            _pending = p;
        }

        private static void BuildDeath()
        {
            var p = _pending;
            _pending = null;
            string title, detail;
            if (p.Reason == DamageTypeNames.Poison && p.InHealthTick)
            {
                title = Strings.DeathHidden;
                var top = DamageLog.Recent(3600.0);
                var sb = new StringBuilder();
                for (int i = 0; i < top.Count && i < 3; i++)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(Strings.ShortLabel(top[i].Key)).Append(' ').Append(Fmt.Signed(-top[i].Value));
                }
                detail = sb.ToString();
                if (!string.IsNullOrEmpty(p.Debts)) detail = (detail.Length > 0 ? detail + ". " : "") + Strings.StillOwed(p.Debts);
            }
            else if (p.Reason == DamageTypeNames.Bleed)
            {
                title = Strings.DeathBled;
                detail = p.Bleeding ?? "";
            }
            else if (p.Reason == DamageTypeNames.SMV)
            {
                title = Strings.DeathSmv;
                detail = "";
            }
            else
            {
                int flags = (p.Fungal ? LossKey.Fungal : 0) | (p.Crafting ? LossKey.Crafting : 0) | (p.Asleep ? LossKey.Asleep : 0) |
                            (p.Silent ? LossKey.Silent : 0);
                title = Strings.DeathOther(Strings.LossLabel(LossKey.Make(Cat.Damage, (int)p.Reason, flags, p.WaitId)));
                detail = "";
            }
            LastDeath = new DeathNote { Title = title, Detail = detail, Stamp = GameClock.Stamp() };
            DamageLog.Death(title, detail);
            Log.Info($"Log: death at {Fmt.Stamp(LastDeath.Stamp)}: {title}{(detail.Length > 0 ? " (" + detail + ")" : "")}.");
            if (Cfg.Toasts.Enabled.Value && Cfg.Toasts.DeathSummary.Value)
                ToastStack.Show(detail.Length > 0 ? title + ": " + detail : title, Cfg.Toasts.DeathSeconds.Value, urgent: true);
        }

        // A fresh load: notes held for a sleep in the old save are dropped.
        internal static void OnFreshLoad()
        {
            _holding = false;
            Held.Clear();
            HeldLabel.Clear();
            _pending = null;
        }
    }
}
