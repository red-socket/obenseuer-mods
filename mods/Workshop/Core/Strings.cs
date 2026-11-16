using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Workshop
{
    // Every string players see in game (spec §2), humanized 2026-11-16.
    internal static class Strings
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---- Refusals (gates) ----
        internal const string Unavailable = "Can't start it right now";
        internal const string Locked = "Locked";
        internal const string Owned = "Someone else's";
        internal const string NotSupported = "Not on this machine";
        internal const string NotMadeHere = "This machine doesn't make that any more";
        internal const string RecipeUnknown = "You don't know that recipe";
        internal const string SkillTooLow = "Your skill is too low";
        internal const string NeedsUpgrade = "Needs an upgrade";
        internal static string Missing(string name) => name == null ? "Something is missing" : "Out of " + name;
        internal const string NoPower = "No power or fuel";
        internal const string GridTooWeak = "The grid is too weak";
        internal const string SlotsBusy = "All slots are busy";
        internal const string OpenTheBench = "Open the bench to repeat here";
        internal const string NoOutputRoom = "No room for the output";
        internal const string AlreadyRunning = "Already running";
        internal const string EmptyItFirst = "Empty it first";

        // ---- Fetch ----
        internal const string FetchAll = "Fetch ingredients";
        internal static string FetchForBatch(int k) => "Fetch for ×" + k.ToString(Inv);
        internal const string FetchPartial = "Fetch what's nearby";
        internal const string FetchNotFound = "Not found nearby";
        internal const string FetchNothingMissing = "Nothing missing";
        internal const string FetchVesselFull = "Vessel full for this recipe";
        internal const string FinishFirst = "Finish moving that item first.";

        internal static string NotFoundNote(List<string> missing, string variant, bool noRoom)
        {
            if (noRoom) return "There's no room in the station for what it needs.";
            string s = missing.Count > 0 ? "Not found nearby: " + string.Join(", ", missing.ToArray()) + "." : "Nothing to fetch.";
            return variant != null ? s + " " + ByHand(variant) : s;
        }

        internal static string FetchSummary(List<string> fetched, List<string> still, string variant, bool stationFull)
        {
            var parts = new List<string>();
            if (fetched.Count > 0) parts.Add("Fetched " + string.Join(", ", fetched.ToArray()) + ".");
            if (stationFull) parts.Add("The station is full.");
            if (still.Count > 0) parts.Add("Still missing: " + string.Join(", ", still.ToArray()) + ".");
            if (variant != null) parts.Add(ByHand(variant));
            return string.Join(" ", parts.ToArray());
        }

        private static string ByHand(string variant) => $"It also needs {Article(variant)}, which you'll have to add by hand.";

        private static string Article(string name) =>
            name.Length > 0 && "AEIOUaeiou".IndexOf(name[0]) >= 0 ? "an " + name.ToLowerInvariant() : "a " + name.ToLowerInvariant();

        // "Wood Log ×3" or "2.5 L of Water".
        internal static string Amount(Item item, float amount)
        {
            string name = Game.Title(item);
            bool liquid = item?.Categories != null && Array.IndexOf(item.Categories, "Liquid") >= 0;
            if (liquid) return amount.ToString("0.#", Inv) + " L of " + name;
            return name + " ×" + Mathf.CeilToInt(amount - 1e-3f).ToString(Inv);
        }

        // ---- Queue ----
        internal static string CountLabel(int count) => count == int.MaxValue ? "×All" : "×" + count.ToString(Inv);
        internal static string StopLabel(int left) => "Stop (" + Left(left) + ")";
        internal const string SuffixAuto = " · auto";
        internal static string SuffixQueued(int left) => " (" + Left(left) + ")";
        internal static string SuffixArmed(int count) => " " + CountLabel(count);
        private static string Left(int left) => left == int.MaxValue ? "all" : left.ToString(Inv);

        internal static string HintRepeat(string key, string name, int count) =>
            $" · {key}: repeat {name}" + (count > 1 ? " " + CountLabel(count) : "");
        internal static string HintQueued(string name, int left, string key) =>
            left == int.MaxValue ? $" · {name}, repeating ({key}: stop)" : $" · {name}, {left} left ({key}: stop)";
        internal const string HintAuto = " · auto";
        internal static string HintFlash(string text) => " · " + text;

        internal static string Started(string name) => "Started " + name;
        internal static string StopArmed(string name, int left, string key) =>
            (left == int.MaxValue ? $"{name}, repeating." : $"{name}, {left} left.") + $" Press {key} again to stop";
        internal const string QueueCleared = "Repeat stopped";
        internal static string ShortBatch(int now, int last, string key) => $"Only ×{now} this time (last ×{last}). Press {key} again";

        internal static string QueueStopped(string machine, string reason, int left) =>
            left == int.MaxValue ? $"{machine}: done repeating. {reason}." : $"{machine}: repeat stopped, {left} not made. {reason}.";

        // ---- Cancel ----
        internal static string CancelArmed(bool dishwasher, List<string> returns, int queueLeft)
        {
            string list = returns.Count > 0 ? string.Join(", ", returns.ToArray()) : "nothing";
            string s = dishwasher
                ? $"Click again to cancel. You get back {list} (still not yours). The powder is used up."
                : $"Click again to cancel. You get {list}. The ingredients are lost.";
            if (queueLeft > 0) s += queueLeft == int.MaxValue ? " The repeat stops." : $" The repeat stops ({queueLeft} left).";
            return s;
        }
        internal const string CancelAutocraft = "Jobs on machines that run on their own can't be cancelled.";
        internal const string CancelJustLoaded = "This job was just loaded. Try again in a moment.";
        internal const string CancelAlmostDone = "This job is almost done.";
        internal const string CancelNoOwner = "This wash can't be cancelled: the game lost track of whose item it is.";
        internal const string CancelLiquid = "Washes of filled bottles and cans can't be cancelled.";
        internal const string CancelPackage = "Jobs with a package can't be cancelled: what's inside would be lost.";
        internal const string CancelSameResult = "This job gives some of the same things when it fails, so it can't be cancelled.";
        internal const string CancelNoRoom = "Make room in the machine first, so what comes back has somewhere to go.";

        // ---- Keys ----
        internal static string KeyClash(string feature, string key, string clash) =>
            $"The key for {feature} ({key}) is also bound to {clash}, so it's off for now. Change either one. " +
            "Changes to the game's key settings take effect at the next loading screen.";
    }
}
