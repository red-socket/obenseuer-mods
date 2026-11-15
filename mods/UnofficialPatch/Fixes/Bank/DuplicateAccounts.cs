using System;
using System.Collections.Generic;
using HarmonyLib;

namespace UnofficialPatch
{
    // S-033 merge (spec §3b). Before this fix, every scam payout into a frozen account appended a hidden same-name
    // account (ScamInvoiceHold.cs). After Money's global data is loaded, later same-name entries are folded into the
    // first one and removed. The first object is kept (with its Frozen, Interest and DebtInterest), so the Banks'
    // accountData, re-fetched 2 frames later (Bank.cs:113-119), and LegalServices' FirstOrDefault stay valid.
    // Idempotent: with one account per name it does nothing. A plain edit of a vanilla list; no mod data is saved.
    internal static class DuplicateAccounts
    {
        internal static bool Patched;

        private static readonly Dictionary<string, int> First = new Dictionary<string, int>(StringComparer.Ordinal);

        internal static void Merge(Money money)
        {
            if (!Patched || !Cfg.MergeDuplicateAccounts.Value || money == null) return;
            var list = money.bankAccounts;
            if (list == null || list.Count < 2) return;

            First.Clear();
            Dictionary<string, (int n, float rm, int oc)> merged = null;
            for (int i = 0; i < list.Count; i++)
            {
                var x = list[i];
                if (x == null || x.Bankname == null) continue;
                if (!First.TryGetValue(x.Bankname, out int fi))
                {
                    First[x.Bankname] = i;
                    continue;
                }
                var first = list[fi];
                first.OC += x.OC;
                first.RM += x.RM;
                first.CurrentDebt = new Money.money(first.CurrentDebt.OC + x.CurrentDebt.OC, first.CurrentDebt.RM + x.CurrentDebt.RM);
                first.MonthlyDownpayment = new Money.money(first.MonthlyDownpayment.OC + x.MonthlyDownpayment.OC,
                    first.MonthlyDownpayment.RM + x.MonthlyDownpayment.RM);
                if (merged == null) merged = new Dictionary<string, (int, float, int)>(StringComparer.Ordinal);
                merged.TryGetValue(x.Bankname, out var m);
                merged[x.Bankname] = (m.n + 1, m.rm + x.RM, m.oc + x.OC);
                list.RemoveAt(i);
                i--;
            }
            if (merged == null) return;
            foreach (var kv in merged)
                Log.Info($"Bank accounts: merged {kv.Value.n} hidden duplicate \"{kv.Key}\" account{(kv.Value.n == 1 ? "" : "s")} into the real one " +
                         $"(+{kv.Value.rm} RM, +{kv.Value.oc} OC).");
        }
    }

    // Money.OnLoadingGame() (SRC/Money.cs:217).
    [HarmonyPatch(typeof(Money), nameof(Money.OnLoadingGame))]
    internal static class MoneyLoadPatch
    {
        private static void Postfix(Money __instance, bool __result)
        {
            if (!__result) return;
            try { DuplicateAccounts.Merge(__instance); }
            catch (Exception e) { Log.Exception("Bank accounts: merge", e); }
        }
    }
}
