using System;
using System.Globalization;

namespace FairMarket
{
    // Every player-facing string (spec §2). Humanized 2026-11-17; run through the humanizer again after any change
    // (CLAUDE.md). Text that goes into the game's own TMP fields (door prompts, the dishwasher panel, the item panel) uses
    // ASCII plus the middle dot Workshop already shows in the same fonts; ranges use "-" (the fonts may lack an en dash).
    // Tooltip lines are ASCII only.
    internal static class Strings
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // ---- Opening hours (spec §7.3) ----
        internal const string Midnight = "midnight";
        internal static string OpenUntil(string clock) => ", open until " + clock;
        internal const string HoursSeparator = " ·";
        internal const string OpenAllDay = ", open all day";
        internal static string ClosedOpens(string when) => "(Closed, opens " + when + ")";
        internal static string ClosedTodayOpens(string when) => "(Closed today, opens " + when + ")";
        internal const string ClosedNoHours = "(Closed)";
        internal const string Daily = "Daily";
        internal const string DayClosed = "closed";
        internal const string ClosedAllWeek = "Closed all week";

        // ---- Dishwasher (spec §8.1, §8.2) ----
        internal static string FailLine(string pct, string basePct, int machinePct) =>
            "\nFail chance: " + pct + " (base " + basePct + ", rolled when the wash finishes)\nMachine efficiency: " + machinePct.ToString(C) + "%";
        internal const string NotWashed = "Not washed:";
        internal const string NotStolen = "not stolen, nothing to wash";
        internal static string CantWash(string token) => "can't be washed: " + token;
        internal const string NotOnList = "not on this machine's list";
        internal const string DoesntTake = "this machine doesn't take it";
        internal const string OneStack = "one stack per item at a time";
        internal static string SkipLine(string item, string reason) => item + ": " + reason;
        internal static string More(int n) => "+" + n.ToString(C) + " more";

        // ---- Invoice markers (spec §9.2, §9.3) ----
        internal const string TipScam = "Scam, no penalty: nothing happens if you don't pay.\n";
        internal const string TipScamExpired = "Scam, no penalty (past its due date).\n";
        internal const string TipNoDebt = "Unpayable: no open debt. It was paid or expired (an unpaid real invoice that expires becomes a crime), or this letter is a copy. The invoice machine won't take it.\n";
        internal const string TagScam = " <color=#ffab00>· scam</color>";
        internal const string TagNoDebt = " <color=#ffab00>· paid/expired</color>";

        // ---- Invoices window (spec §9.4) ----
        internal const string WindowTitle = "Invoices";
        internal const string NotesHint = "INVOICES";
        internal static string Owe(string owed, string have) => "You owe " + owed + " · you have " + have;
        internal static string OweNothing(string have) => "You owe nothing · you have " + have;
        internal static string And(string a, string b) => a + " and " + b;
        internal const string RecordNote = "Debts the game has on record. Some letters may still be in the post.";
        internal const string CrimeNote = "Unpaid real invoices become a crime the day after they're due.";
        internal const string Empty = "No unpaid invoices.";
        internal static string DueIn(int days) => "in " + days.ToString(C) + " days";
        internal const string DueTomorrow = "tomorrow";
        internal const string DueToday = "last day today";
        internal const string DueOverdue = "overdue";
        internal const string DueExpired = "expired";
        internal const string ScamTag = "Scam, no penalty";
        internal static string ScamShort(string due) => "scam, " + due;
        internal static string ShowOld(int n) => n == 1 ? "Show 1 old scam letter" : "Show " + n.ToString(C) + " old scam letters";
        internal static string HideOld(int n) => n == 1 ? "Hide 1 old scam letter" : "Hide " + n.ToString(C) + " old scam letters";
        internal const string Pay = "Pay";
        internal const string Dismiss = "Dismiss";
        internal static string ConfirmButton(int seconds) => "Confirm (" + seconds.ToString(C) + ")";
        internal const string NotEnoughMoney = "Not enough money";
        internal const string Expired = "Expired";
        internal const string AlreadyGone = "Already paid or expired.";
        internal static string PayPrompt(string amount, string debtor) => "Pay " + amount + " to " + debtor + "? Press again.";
        internal const string ScamPromptSuffix = " It's a scam: paying gets you nothing.";
        internal static string DismissPrompt(string debtor) => "Remove the 0 invoice from " + debtor + " for good? Press again.";
        internal const string Paid = "Paid. Its letter can't be paid again.";
        internal const string Dismissed = "Removed.";
        internal const string NotNow = "Can't pay right now.";
        internal const string NotificationTitle = "Invoice paid";
        internal static string NotificationText(string debtor, int amount, Money.Currency currency) =>
            debtor + ": " + amount.ToString(C) + " " + currency;
        internal const string PayHint = "PAY";
        internal const string DismissHint = "DISMISS";
        internal const string ShowHint = "SHOW";
        internal const string HideHint = "HIDE";
        internal const string BackHint = "BACK";
        internal const string Close = "Close";
        internal const string ListFailed = "The list couldn't be built. The log has the details.";
    }
}
