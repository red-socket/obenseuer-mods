using System;
using System.Collections.Generic;
using System.Text;

namespace VitalSigns
{
    // One queued amount of hidden damage (the negative part of PlayerStatsHealth.HealthToRegenerate).
    internal sealed class Debt
    {
        internal int LabelId;
        internal string Detail, Note;
        internal float Remaining, Original;
        internal double Clock;
    }

    // FIFO ledger over the hidden-damage pool (spec §5.4, D5). The game keeps one number; each queued amount here is
    // exact, and drains and credits are shared out oldest first. Invariant: the sum of Remaining equals max(0, -pool),
    // checked after every change (Reconcile). Capped at 32 debts (the two oldest merge).
    internal static class PoolLedger
    {
        private const int Cap = 32;
        internal static readonly List<Debt> Debts = new List<Debt>();
        private static bool _driftWarned;

        internal static float Total
        {
            get
            {
                float t = 0f;
                for (int i = 0; i < Debts.Count; i++) t += Debts[i].Remaining;
                return t;
            }
        }

        internal static void Clear() => Debts.Clear();

        internal static void Debit(int labelId, string detail, float amount)
        {
            if (amount <= 0f) return;
            var last = Debts.Count > 0 ? Debts[Debts.Count - 1] : null;
            // The same source twice in the same game minute is one debt (a stack eaten at once).
            if (last != null && last.LabelId == labelId && GameClock.Now - last.Clock < 60.0 && last.Note == null)
            {
                last.Remaining += amount;
                last.Original += amount;
                if (detail != null) last.Detail = detail;
                return;
            }
            Debts.Add(new Debt { LabelId = labelId, Detail = detail, Remaining = amount, Original = amount, Clock = GameClock.Now });
            if (Debts.Count > Cap)
            {
                var a = Debts[0];
                var b = Debts[1];
                b.Remaining += a.Remaining;
                b.Original += a.Original;
                b.LabelId = Ctx.Intern(Strings.Merged(Ctx.Label(a.LabelId), Ctx.Label(b.LabelId)));
                Debts.RemoveAt(0);
            }
        }

        // Healing that cancels hidden damage, oldest debt first.
        internal static void Credit(float amount, string from)
        {
            for (int i = 0; i < Debts.Count && amount > 0f; i++)
            {
                var d = Debts[i];
                float take = Math.Min(d.Remaining, amount);
                d.Remaining -= take;
                amount -= take;
                d.Note = Strings.CancelledBy(from);
            }
            Debts.RemoveAll(d => d.Remaining <= 1e-4f);
        }

        // One drain step of the health tick: consumed pool units, poolLoss HP. Each debt touched is charged
        // poolLoss * taken / consumed, so a lone 0.2 debt is charged the full HP rate, as in the game. The charges add
        // up to poolLoss exactly.
        internal static void Drain(float consumed, float poolLoss, bool death)
        {
            if (poolLoss <= 0f && consumed <= 0f) return;
            bool attributed = Debts.Count > 1;
            float left = consumed;
            if (Debts.Count == 0 || consumed <= 0f)
            {
                if (poolLoss > 0f) DamageLog.Loss(LossKey.Make(Cat.Hidden, 0, 0, Ctx.Intern(Strings.UnknownSource)), poolLoss, true, true);
                return;
            }
            for (int i = 0; i < Debts.Count && left > 1e-6f; i++)
            {
                var d = Debts[i];
                float take = Math.Min(d.Remaining, left);
                if (take <= 0f) continue;
                d.Remaining -= take;
                left -= take;
                float charge = poolLoss * (take / consumed);
                DamageLog.Loss(LossKey.Make(Cat.Hidden, 0, 0, d.LabelId), charge, true, true, d.Detail, null, attributed);
            }
            if (left > 1e-4f && poolLoss > 0f)   // the ledger was short of the pool: the rest is unknown
                DamageLog.Loss(LossKey.Make(Cat.Hidden, 0, 0, Ctx.Intern(Strings.UnknownSource)), poolLoss * (left / consumed), true, true);
            if (death)
            {
                foreach (var d in Debts)
                    if (d.Remaining > 1e-4f) Log.Verbose($"Log: debt '{Ctx.Label(d.LabelId)}' ({d.Remaining:0.##} left) cut short by death.");
                Debts.Clear();
            }
            Debts.RemoveAll(d => d.Remaining <= 1e-4f);
        }

        // Keeps the invariant: drift above 0.01 becomes an "Unknown source" debt (the pool fell) or a credit from
        // "unknown healing" (it rose). quiet: a door reconciles without a warning when nothing is off.
        internal static void Reconcile(float pool)
        {
            float owed = Math.Max(0f, -pool);
            float drift = owed - Total;
            if (Math.Abs(drift) <= 0.01f)
            {
                if (owed == 0f && Debts.Count > 0) Debts.Clear();
                return;
            }
            if (!_driftWarned)
            {
                _driftWarned = true;
                Log.Warn($"Log: the hidden-damage ledger was {drift:+0.##;-0.##} off the game's pool; corrected (further corrections are verbose only).");
            }
            else Log.Verbose($"Log: ledger corrected by {drift:+0.##;-0.##}.");
            if (drift > 0f) Debit(Ctx.Intern(Strings.UnknownSource), null, drift);
            else Credit(-drift, Strings.UnknownHealing);
        }

        // "Caffeine overdose 10, Raw Fish 4".
        internal static string Summary(int max)
        {
            if (Debts.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < Debts.Count && i < max; i++)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Ctx.Label(Debts[i].LabelId)).Append(' ').Append(Fmt.Num(Debts[i].Remaining));
            }
            if (Debts.Count > max) sb.Append(", ...");
            return sb.ToString();
        }
    }
}
