using System;
using System.Globalization;

namespace QuestHelper
{
    // Every player-facing string (spec §2). Public text: humanized 2026-11-17 (no dashes as punctuation, plain verbs).
    // Curated hint texts live in Data/curated.json.
    internal static class Strings
    {
        internal const string ModName = "Quest Helper";

        // ---- Pause entry, window frame ----
        internal static string PauseLabel(int stuck) => stuck > 0 ? $"Quest Helper ({stuck})" : "Quest Helper";
        internal const string TabStuck = "Stuck quests";
        internal const string TabBazaar = "Bazaar access";
        internal const string TabTasks = "Task list";
        internal const string Back = "Back";
        internal const string Close = "Close";
        internal const string SelectHint = "SELECT";
        internal const string BackHint = "BACK";
        internal const string PagesHint = "SWITCH PAGE";

        // ---- Hints row ----
        internal static readonly string[] TierNames = { "Off", "Nudge", "Where", "Full steps" };
        internal const string HintsLabel = "Hints";
        internal const string SpoilerWarning = "Shows names and places you may not have found yet.";
        internal const string HintsOffBazaar = "Turn hints on above to see what you need.";

        // ---- Finding states ----
        internal static string State(DiagState s)
        {
            switch (s)
            {
                case DiagState.Stuck: return "Stuck";
                case DiagState.CheckThere: return "Check there";
                case DiagState.OkForNow: return "OK for now";
                case DiagState.CannotFix: return "Can't fix";
                case DiagState.CannotCheck: return "Can't check";
                default: return "Not affected";
            }
        }
        internal const string NotCheckedYet = "Not checked yet";

        // ---- Stuck quests tab ----
        internal const string DoctorHeading = "Known stuck quests";
        internal const string NoneStuck = "No known stuck quests in this save.";
        internal static string Checked(string titles) => "Checked: " + titles + ".";
        internal static string CheckedHidden(int n) => n == 1
            ? "Quest Helper also checked 1 known problem in a quest you don't have yet. Set hints to \"Where\" to see its name."
            : $"Quest Helper also checked {n} known problems in quests you don't have yet. Set hints to \"Where\" to see their names.";
        internal const string OkForNowNeutral = "You're not stuck. The game still has a normal way forward; set hints to \"Where\" to see it.";
        internal const string AppliedHeading = "Fixes applied to this save";
        internal const string AppliedNone = "None yet.";
        internal const string WhatHappened = "What happened";
        internal const string WhatChanges = "What Quest Helper changes";
        internal const string WhatNot = "What it won't do";
        internal const string WontDo = "It gives no rewards, money or items (apart from a lost key item the quest needs) and skips no part of the story. Fees and items the quest asks for are still yours to pay and find.";
        internal const string BackupHeading = "Backup";
        internal const string NothingFound = "Nothing in your save points to this problem.";
        internal const string BackUpAndApply = "Back up and apply";
        internal static string PressAgain(int seconds) => $"Press again to back up and apply ({seconds})";
        internal const string StuckNotice = "Quest Helper found a stuck quest: pause menu > Quest Helper";
        internal const string CheckFailed = "The check stopped with an error (see the log).";
        internal static string CouldNotFind(string what) => $"Couldn't find {what} in this area.";
        internal static string CouldNotRead(string what) => $"Couldn't read {what}.";
        internal static string Requirements(string what) => $"the game's requirements for \"{what}\"";

        // ---- Gates and results ----
        internal const string GateWindow = "Open Quest Helper from the pause menu to apply a fix.";
        internal const string GateBusy = "Finish the conversation or wait for loading to end first.";
        internal const string GateSaving = "The game is saving. Try again in a moment.";
        internal const string GateSaveLock = "The game doesn't allow saving right now, so Quest Helper won't change anything. Try again once saving is possible.";
        internal const string SaveFirst = "Save your game once first, so Quest Helper has something to back up.";
        internal const string NotThatState = "This quest isn't in that state any more. Nothing was changed.";
        internal static string CouldntFindPrep(string what) => $"Couldn't find {what}. Nothing was changed.";
        internal static string BackupFailed(string why) => $"The backup failed ({why}). Nothing was changed.";
        internal static string BackupNoSpace(long needMb, long freeMb) => $"Not enough free disk space for the backup ({freeMb} MB free, {needMb} MB needed). Nothing was changed.";
        internal static string StoppedPartway(string backup) =>
            $"The fix stopped partway. Don't save now. Load your save (or the backup at {backup}) and try again.";
        internal const string ResultHeading = "Done";
        internal const string ResultOk = "The change is in place and checks out.";
        internal const string ResultUnverified = "The change was made, but the follow-up check didn't confirm it. Look at the quest before you save; the backup is there if you need it.";
        internal const string SaveToKeep = "This change is in your current game. Save to keep it.";
        internal const string KeptAtAutosave = "This change is in your current game. Saving isn't possible right now, so it will be kept at the next autosave (going through any door).";
        internal const string SaveGameButton = "Save game...";
        internal static string BackupAt(string kind, string path) =>
            kind == "saveguard" ? $"Backup: in Save Guard's restore list ({path})." : $"Backup: {path}";
        internal static string HowToRestore(string kind) => kind == "saveguard"
            ? "To undo: open Save Guard's restore window in the main menu (F6 by default) and pick the entry marked \"before a Quest Helper fix\"."
            : "To undo: quit to the main menu and copy the backup folder's files back into Saves/<character>/<slot>.";
        internal const string BackupTargetSaveGuard = "A copy of your latest save goes into Save Guard's restore list first (F6 in the main menu by default).";
        internal static string BackupTargetOwn(string root) => $"A copy of your latest save goes to {root} first.";

        // ---- Generic edits ----
        internal const string GenericBanner = "Advanced: changes any task. Finishing or abandoning runs the game's follow-up steps and can block later quests. A backup is made first.";
        internal const string GenericNeedsFirst = "The objectives this one depends on aren't done yet, so the game wouldn't count it as done. Quest Helper won't finish it. Nothing was changed.";
        internal const string OpFinish = "Finish";
        internal const string OpFail = "Fail";
        internal const string OpAbandon = "Abandon";
        internal static string GenericTaskButton(string op) => op + " task";
        internal static string GenericPressAgain(string op, int seconds) => $"Press again: back up and {op.ToLowerInvariant()} ({seconds})";
        internal const string GenericTaskWarning = "Finishing, failing or abandoning a task abandons every active objective in it, and their follow-up steps run.";

        // ---- Task list tab ----
        internal const string GroupActive = "Active";
        internal const string GroupTbc = "To be continued";
        internal const string GroupFinished = "Finished";
        internal const string GroupFailed = "Failed";
        internal const string GroupAbandoned = "Abandoned";
        internal const string Mismatch = "the game's records disagree about this objective";
        internal static string Status(ObjectiveStatus s)
        {
            switch (s)
            {
                case ObjectiveStatus.Active: return "active";
                case ObjectiveStatus.Finished: return "done";
                case ObjectiveStatus.Failed: return "failed";
                case ObjectiveStatus.Abandoned: return "abandoned";
                case ObjectiveStatus.Hidden: return "hidden";
                default: return s.ToString().ToLowerInvariant();
            }
        }

        // ---- Bazaar access tab ----
        internal const string CheckpointsHeading = "Checkpoints";
        internal const string ChainHeading = "Your chain";
        internal const string NotStarted = "not started";
        internal const string ChainActive = "active";
        internal const string ChainDone = "done";
        internal const string NoActiveObjectives = "No active objectives.";
        internal static string Needs(string list) => "Needs: " + list;

        // ---- Journal lines ----
        internal static string ReceivedFrom(string giver, string place) =>
            string.IsNullOrEmpty(giver) ? (string.IsNullOrEmpty(place) ? "" : "Received " + place)
                                        : "Received from " + giver + (string.IsNullOrEmpty(place) ? "" : ", " + place);
        internal static string Near(string landmark) => "near " + landmark;
        internal static string In(string scene) => "in " + scene;
        internal const string DoctorLine = "Quest Helper can fix a known problem here (pause menu)";
        internal static string Where(string what) => "Where: " + what;
        internal static string OnMap(string map) => $"(on the {map} map)";
        internal const string TenementMap = "Tenement";
        internal const string MinesMap = "Mines";
        internal const string OtherMap = "other";
        internal const string Have = "you have it";
        internal const string Missing = "missing";
        internal const string HaveEnough = "you have enough";
        internal const string NotEnough = "not enough";
        internal const string InInventory = "in your inventory";
        internal const string NotInInventory = "not in your inventory";
        internal static string YouHave(int n) => $"you have {n}";
        internal static string Litres(double l, string what) => l.ToString("0.##", CultureInfo.InvariantCulture) + " l of " + what;
        internal static string Rm(int n) => n.ToString(CultureInfo.InvariantCulture) + " RM";
        internal static string Oc(int n) => n.ToString(CultureInfo.InvariantCulture) + " OC";

        // ---- Map pin ----
        internal static string PinTitle(string objective) => "Quest: " + objective;
        internal const string ReceivedHere = "Received here";

        // ---- To be continued ----
        internal static string TbcHeader(bool collapsed, int n) => (collapsed ? "+ " : "- ") + "To be continued (" + n + ")";
        internal const string TbcTooltipTitle = "To be continued";
        internal const string TbcTooltip = "These stories stop here in this version of the game. Nothing is broken in your save; a later update continues them.";
        internal const string TbcLabel = "To be continued in a future update";
        internal static string TbcNotice(string task) => task + ": this story stops here for now.";
        internal const string TabHintOpen = "QUEST HELPER";
        internal const string TabHintOpenStuck = "QUEST HELPER (!)";
        internal const string TabHintTbc = "SHOW/HIDE TO BE CONTINUED";

        // ---- QD-RAT ----
        internal const string RatTitle = "Rat problem: the six rats";
        internal static string RatCounted(int n, int of) => $"{n} of {of} rats counted.";
        internal static string RatLeft(int n) => n == 1 ? "1 task rat is left in the canteen." : $"{n} task rats are left in the canteen.";
        internal static string RatLost(int n) => n == 1 ? "1 rat is gone without being counted (slaughtering a rat removes it without counting it)." : $"{n} rats are gone without being counted (slaughtering a rat removes it without counting it).";
        internal const string RatCheckThere = "Go to the Mines canteen and open Quest Helper there.";
        internal const string RatNotReleased = "The task rats were never let out: Anna's talk was cut off before she released them.";
        internal const string RatPlanRelease = "Lets the task rats out and hides the canteen's other rats, the same switch Anna's talk flips. You still catch all six.";
        internal static string RatPlanCounter(int add, int total, int left) => left > 0
            ? $"Counts the {add} lost rat{(add == 1 ? "" : "s")}, so the counter reads {total} of 6. You still catch the {left} left."
            : $"Counts the {add} lost rat{(add == 1 ? "" : "s")}, so the counter reads 6 of 6 and Anna's next step opens.";
        internal static string RatCounterChanged(int n) => $"The objective now needs {n} rats instead of 6; this game version isn't covered.";

        // ---- QD-CASINO ----
        internal const string CasinoTitle = "The Casino Coup: the boss isn't expecting you";
        internal const string CasinoEvidence = "Casper sent you away to get the money, or the talk was cut short. Either way, \"Talk to Casper\" is marked done, so Gérard won't let you back up to his office.";
        internal const string CasinoCannotFix = "Gérard sends you to security before he checks for Casper's meeting, so restoring the meeting would not help.";
        internal const string CasinoPlan = "Casper's meeting is open again, so Gérard lets you up. Paying Casper still costs 100,000 RM; nothing is given to you. The Seamus and Crazy Neighbor routes were never blocked.";

        // ---- QD-TOIVO ----
        internal const string ToivoTitle = "Collapse hazard: Toivo thinks it's not your tenement";
        internal const string ToivoEvidence = "You told Toivo Malmijoki it's not your tenement, so he never sent you to Ken Nakahara.";
        internal const string ToivoOkTalk = "Talk to Toivo.";
        internal const string ToivoOkTor = "Tor will send you back to Toivo when you show him your mining permit.";
        internal const string ToivoPlan = "Talk to Toivo Malmijoki; he'll bring up your tenement and send you to Ken Nakahara. (Quest Helper sets the flag Tor would have set.)";

        // ---- QD-PERMIT ----
        internal const string PermitTitle = "Permission to dig: Tanja's talk was cut off";
        internal const string PermitEvidence = "You handed in the permit form, but the talk with Tanja Nypan ended before she asked for the next step.";
        internal const string PermitCannotFix = "The tax was paid but the permit was never handed over; Quest Helper won't hand out the permit.";
        internal const string PermitPlanPassport = "Adds \"Show Tanja a valid passport\", the step her talk would have given you. You still need the forged passport and the 200 RM tax.";
        internal const string PermitPlanTax = "Adds \"Pay miner's tax\", the step her talk would have given you. You still pay the 200 RM tax. If the talk was cut right after you paid, the game didn't record the payment, so Tanja asks for it again.";

        // ---- QD-ELIANA ----
        internal const string ElianaTitle = "Getting a passport: Eliana's first talk was cut off";
        internal const string ElianaEvidence = "Eliana Passmore's first talk ended early, so she only says \"Not yet\".";
        internal const string ElianaPlanA = "Lets Eliana's first talk play again, so she gives you everything the normal way.";
        internal const string ElianaPlanGet = "Adds \"Get the passport from Eliana\".";
        internal const string ElianaPlanKeys = "Gives you the Deekula apartment B1 keys, only if you don't have them.";
        internal const string ElianaPlanPriest = "Starts \"Priest's debt\" with \"Go to check the priest's apartment B1\".";
        internal const string ElianaCannotFix = "Her first objective is in an unexpected state, so Quest Helper leaves it alone.";
    }
}
