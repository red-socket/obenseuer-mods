using System.Collections.Generic;
using System.Linq;

namespace TidyInventory
{
    // Every string players see in game (spec §1), humanized 2026-11-16. Button labels live in the config (Cfg.cs).
    internal static class Strings
    {
        // ---- Pins ----
        internal const string Pinned = "Pinned";
        // Built from the live config: the lock sentence only with BlockManualMoves, the redirect only with ReturnItemsToPin.
        internal static string HintPinFirst(string key, bool blockMoves, bool returnToPin) =>
            "Slot pinned. " +
            (blockMoves ? "Sort skips it, and it can't be sold, moved or dropped until you unpin it." : "Sort skips it, and it can't be sold.") +
            (returnToPin ? " New copies of this item go here first." : "") +
            $" Press {key} again to unpin.";
        internal const string HintPinEmpty = "Put an item in this slot first, then pin it.";
        internal const string HintPinStorage = "Only slots in your main inventory can be pinned. Storage, backpack and character slots can't.";

        // ---- Bins ----
        internal const string NothingToThrow = "Nothing to throw away.";
        internal static string NothingToThrowKept(IDictionary<Bins.Keep, int> kept) => $"Nothing to throw away. {Kept(kept)}";
        // n counts items; kept counts stacks, by the reason each was kept.
        internal static string ThrewAway(int n, IDictionary<Bins.Keep, int> kept = null)
        {
            string s = n == 1 ? "Threw away 1 item." : $"Threw away {n} items.";
            return kept != null && kept.Values.Sum() > 0 ? s + " " + Kept(kept) : s;
        }
        private static readonly Bins.Keep[] KeepOrder = { Bins.Keep.Quest, Bins.Keep.Category, Bins.Keep.Owned, Bins.Keep.Valuable };
        private static string Kept(IDictionary<Bins.Keep, int> kept)
        {
            int n = kept.Values.Sum();
            if (n == 1) return "Kept 1 stack: " + Reason(kept.First(kv => kv.Value > 0).Key, false) + ".";
            var parts = KeepOrder.Where(k => kept.TryGetValue(k, out int c) && c > 0).Select(k => Reason(k, true)).ToList();
            return $"Kept {n} stacks: {string.Join(", ", parts.ToArray())}.";
        }
        private static string Reason(Bins.Keep k, bool plural)
        {
            switch (k)
            {
                case Bins.Keep.Owned: return plural ? "other people's things" : "someone else's";
                case Bins.Keep.Category: return plural ? "keycards or other protected items" : "a keycard or other protected item";
                case Bins.Keep.Valuable: return plural ? "valuable items" : "a valuable item";
                default: return plural ? "quest items" : "a quest item";
            }
        }
        internal static string DisposeArmed(string key) => $"Press {key} again to throw this away.";
        internal static string DisposeProtected(Bins.Keep why, int valueAbove)
        {
            switch (why)
            {
                case Bins.Keep.Owned: return "This stack stays: it's someone else's.";
                case Bins.Keep.Category: return "This stack stays: it's a keycard or other protected item.";
                case Bins.Keep.Valuable: return $"This stack stays: it's worth {valueAbove} or more.";
                default: return "This stack stays: it's a quest item.";
            }
        }
        internal static string EmptiedOvernight(int bins) => bins == 1 ? "Emptied 1 bin overnight." : $"Emptied {bins} bins overnight.";

        // ---- Combine ----
        internal const string NothingToCombine = "Nothing to combine.";
        internal static string Combined(string list) => "Combined: " + list + ".";
        internal static string CombinedEntry(string title, int before, int after) => $"{title} {before} → {after}";
        internal static string ToppedUpEntry(string title) => $"{title} topped up";
        internal const string CombineTipTitle = "Combine";
        internal const string CombineTip =
            "Merges part-used items of the same kind (matches, pills, glue and so on) into full ones plus one part-used one.";
        internal const string SkipNoRoom = "no room for what's left over";
        internal const string SkipShared = "can't be split safely";
        internal static string Skipped(string title, string reason) => $"{title}: {reason}.";
        internal const string DropFirst = "Drop the item you're dragging first.";

        // ---- Duck ----
        internal const string DuckNotYet = "Mr. Duck still needs this duck.";
        internal static string DuckArmed(string key) =>
            $"Press {key} (or A) again to throw away the blue rubber duck. This can't be undone.";
        internal const string DuckHint = "THROW AWAY";
        internal const string DuckHintArmed = "CONFIRM";

        // ---- Keys ----
        internal static string KeyClash(string feature, string key, string clash) =>
            $"The key for {feature} ({key}) is also bound to {clash}, so it's off for now. Change either one. " +
            "Changes to the game's key settings take effect at the next loading screen.";
    }
}
