using System;
using HarmonyLib;
using System.Reflection;

namespace UnofficialPatch
{
    // Player-facing text of this fix (spec §0.5), humanized. {0} is the amount ("120 RM"), {1} the bank name.
    internal static class Strings
    {
        internal const string HeldTitle = "Account frozen";
        internal const string HeldBody = "{1} is holding {0} until your account is unfrozen";
    }

    // S-033 hold (spec §3a). FakeInvoiceReceiver.HandleMail (SRC/FakeInvoiceReceiver.cs:80-131) treats "found but
    // Frozen" as "not found" (:106): it appends a second BankAccountData with the same name, which FindBankAccount
    // (Money.cs:572, first match) never returns, and says "Money added to bank account!". The prefix notes the frozen
    // account and the list length; the postfix folds every same-name entry vanilla appended back into the frozen
    // account; while vanilla runs, the notification is rewritten. The fraud roll, crime, freeze roll, the payout amount
    // and the "no account yet" branch are vanilla. The lawyer's UnfreezeAccount (LegalServicesController.cs:76-133)
    // releases the held money.
    internal static class ScamHold
    {
        internal static bool Patched;

        // Set only between HandleMail's prefix and finalizer.
        internal static bool Active;
        internal static string Bank;

        internal sealed class Hold
        {
            internal Money.BankAccountData Account;
            internal int Count;
            internal string Bank;
        }

        internal static Hold Before(FakeInvoiceReceiver r)
        {
            if (!Patched || !Cfg.HoldPayoutWhenFrozen.Value || r == null || string.IsNullOrEmpty(r.targetBank)) return null;
            var money = Money.instance;
            if (money == null || money.bankAccounts == null) return null;
            var account = money.FindBankAccount(r.targetBank);
            if (account == null || !account.Frozen) return null;
            Compat.Overlap(Vanilla.HandleMail, "the frozen-account hold");
            Active = true;
            Bank = r.targetBank;
            return new Hold { Account = account, Count = money.bankAccounts.Count, Bank = r.targetBank };
        }

        internal static void After(Hold h)
        {
            if (h == null) return;
            var list = Money.instance?.bankAccounts;
            if (list == null) return;
            int oc = 0;
            float rm = 0f;
            for (int i = list.Count - 1; i >= h.Count; i--)
            {
                var x = list[i];
                if (x == null || x == h.Account || !string.Equals(x.Bankname, h.Bank, StringComparison.Ordinal)) continue;
                h.Account.OC += x.OC;
                h.Account.RM += x.RM;
                oc += x.OC;
                rm += x.RM;
                list.RemoveAt(i);
            }
            if (oc != 0 || rm != 0f)
            {
                string held = oc != 0 ? (rm != 0f ? $"{rm} RM and {oc} OC" : $"{oc} OC") : $"{rm} RM";
                Log.Info($"Scam payout: {h.Bank} is frozen; held {held} in the frozen account instead of opening a second one " +
                         $"(balance now {h.Account.RM} RM; a lawyer can unfreeze it).");
            }
        }

        internal static void End()
        {
            Active = false;
            Bank = null;
        }

        // Notifications.CreateNotification while HandleMail runs for a frozen account.
        internal static void Rewrite(ref string type, ref string text)
        {
            if (!Active) return;
            if (!string.Equals(type, "Money added to bank account!", StringComparison.Ordinal)) return;
            type = Strings.HeldTitle;
            text = string.Format(Strings.HeldBody, text, Bank);   // text is already "<amount> RM"
        }
    }

    // FakeInvoiceReceiver.HandleMail(MailController.Mail), private.
    [HarmonyPatch]
    internal static class HandleMailPatch
    {
        private static MethodBase TargetMethod() => Vanilla.HandleMail;

        private static void Prefix(FakeInvoiceReceiver __instance, out ScamHold.Hold __state)
        {
            __state = null;
            try { __state = ScamHold.Before(__instance); }
            catch (Exception e)
            {
                ScamHold.End();
                Log.Exception("Scam payout: prefix", e);
            }
        }

        private static void Postfix(ScamHold.Hold __state)
        {
            try { ScamHold.After(__state); }
            catch (Exception e) { Log.Exception("Scam payout: postfix", e); }
        }

        // Always runs, so the rewrite never reaches another notification. A void finalizer passes any exception on.
        private static void Finalizer()
        {
            ScamHold.End();
        }
    }

    // Notifications.CreateNotification(string, string, bool). Never skips the original.
    [HarmonyPatch(typeof(Notifications), nameof(Notifications.CreateNotification), new[] { typeof(string), typeof(string), typeof(bool) })]
    internal static class CreateNotificationPatch
    {
        private static void Prefix(ref string type, ref string text)
        {
            if (!ScamHold.Active) return;
            try { ScamHold.Rewrite(ref type, ref text); }
            catch (Exception e) { Log.Exception("Scam payout: notification", e); }
        }
    }
}
