using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NoFloorSpill
{
    internal enum Kind { Refuse, Junk }

    // Deleted: refuse over a full ash slot (Delete mode). Stored: nearby storage. Dropped: the vanilla floor drop.
    // Machine: junk put into the slot its used-up ingredient freed. Inventory: junk into the player's inventory.
    internal enum Outcome { Deleted, Stored, Dropped, Machine, Inventory }

    // Aggregated counters (spec §6). Nothing is logged per call: amounts are summed per frame and flushed on the next
    // frame (Plugin.Update), on a level change and on quit (QuitHook). The first event of each (kind, outcome) per session gets
    // one Info line; with VerboseLogging every flushed sum gets a "[v]" line; a session summary goes out on quit.
    // Main thread only.
    internal static class Tally
    {
        private struct Key : IEquatable<Key>
        {
            internal Kind Kind;
            internal Outcome Outcome;
            internal string Station, Target;
            internal int ItemId;

            public bool Equals(Key o) => Kind == o.Kind && Outcome == o.Outcome && ItemId == o.ItemId
                && string.Equals(Station, o.Station, StringComparison.Ordinal) && string.Equals(Target, o.Target, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = ((int)Kind * 31 + (int)Outcome) * 31 + ItemId;
                    h = h * 31 + (Station?.GetHashCode() ?? 0);
                    return h * 31 + (Target?.GetHashCode() ?? 0);
                }
            }
        }

        private static readonly Dictionary<Key, int> Frame = new Dictionary<Key, int>();
        private static readonly List<Key> Order = new List<Key>();
        private static readonly Dictionary<(Kind, Outcome, int), long> Session = new Dictionary<(Kind, Outcome, int), long>();
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        private static readonly HashSet<(Kind, Outcome)> Announced = new HashSet<(Kind, Outcome)>();
        private static int _frame = -1;
        private static bool _summaryDone;

        internal static void Add(Kind kind, Outcome outcome, string station, Item item, int amount, string target = null)
        {
            if (amount <= 0 || item == null) return;
            try { AddCore(kind, outcome, station, item, amount, target); }
            catch (Exception e) { Log.Exception("tally", e); }
        }

        private static void AddCore(Kind kind, Outcome outcome, string station, Item item, int amount, string target)
        {
            int now = Time.frameCount;
            if (Frame.Count > 0 && now != _frame) Flush();
            _frame = now;
            if (!Names.ContainsKey(item.ID)) Names[item.ID] = string.IsNullOrEmpty(item.Title) ? "item " + item.ID : item.Title;

            var k = new Key { Kind = kind, Outcome = outcome, Station = station ?? "?", Target = target, ItemId = item.ID };
            if (Frame.TryGetValue(k, out int sum)) Frame[k] = sum + amount;
            else
            {
                Frame[k] = amount;
                Order.Add(k);
            }
            var sk = (kind, outcome, item.ID);
            Session.TryGetValue(sk, out long total);
            Session[sk] = total + amount;
        }

        internal static void FlushIfFrameChanged()
        {
            if (Frame.Count > 0 && Time.frameCount != _frame) Flush();
        }

        internal static void Flush()
        {
            if (Frame.Count == 0) return;
            try
            {
                foreach (var k in Order)
                {
                    int n = Frame[k];
                    string item = ItemName(k.ItemId);
                    if (Announced.Add((k.Kind, k.Outcome))) Log.Info(FirstLine(k, n, item));
                    if (Log.VerboseOn)
                    {
                        Session.TryGetValue((k.Kind, k.Outcome, k.ItemId), out long total);
                        Log.Verbose($"{Prefix(k.Kind)} {k.Station}: {n} {item} {Verb(k)} (session total {total}).");
                    }
                }
            }
            catch (Exception e) { Log.Exception("tally flush", e); }
            finally
            {
                Frame.Clear();
                Order.Clear();
            }
        }

        // One Info line on quit (QuitHook, Application.quitting), only if something happened.
        internal static void SessionSummary()
        {
            Flush();
            if (_summaryDone || Session.Count == 0) return;
            _summaryDone = true;
            try
            {
                var sb = new StringBuilder("Session:");
                var refuseIds = new List<int>();
                bool junk = false;
                foreach (var key in Session.Keys)
                {
                    if (key.Item1 == Kind.Refuse && !refuseIds.Contains(key.Item3)) refuseIds.Add(key.Item3);
                    if (key.Item1 == Kind.Junk) junk = true;
                }
                refuseIds.Sort();
                string sep = " ";
                foreach (int id in refuseIds)
                {
                    sb.Append(sep).Append(ItemName(id))
                      .Append(" deleted ").Append(Sum(Kind.Refuse, Outcome.Deleted, id))
                      .Append(", stored ").Append(Sum(Kind.Refuse, Outcome.Stored, id))
                      .Append(", dropped ").Append(Sum(Kind.Refuse, Outcome.Dropped, id));
                    sep = "; ";
                }
                if (junk)
                    sb.Append(sep).Append("junk: ")
                      .Append(Sum(Kind.Junk, Outcome.Machine, null)).Append(" machine, ")
                      .Append(Sum(Kind.Junk, Outcome.Inventory, null)).Append(" inventory, ")
                      .Append(Sum(Kind.Junk, Outcome.Stored, null)).Append(" storage, ")
                      .Append(Sum(Kind.Junk, Outcome.Dropped, null)).Append(" dropped");
                Log.Info(sb.Append('.').ToString());
            }
            catch (Exception e) { Log.Exception("session summary", e); }
        }

        // CraftingBase.ManuName ("Metal stove") when the dropper is a station, else the GameObject's name.
        internal static string StationName(GameObject go)
        {
            if (go == null) return "?";
            var cb = go.GetComponent<CraftingBase>();
            if (cb != null && !string.IsNullOrEmpty(cb.ManuName)) return cb.ManuName;
            return CleanName(go.name);
        }

        internal static string CleanName(string name) => string.IsNullOrEmpty(name) ? "?" : name.Replace("(Clone)", "").Trim();

        internal static string ItemName(int id)
        {
            if (Names.TryGetValue(id, out var n)) return n;
            try
            {
                var item = ItemDatabase.instance != null ? ItemDatabase.instance.FetchItemByID(id) : null;
                if (item != null && !string.IsNullOrEmpty(item.Title)) return Names[id] = item.Title;
            }
            catch { }
            return "item " + id;
        }

        private static long Sum(Kind kind, Outcome outcome, int? id)
        {
            long s = 0;
            foreach (var e in Session)
                if (e.Key.Item1 == kind && e.Key.Item2 == outcome && (id == null || e.Key.Item3 == id.Value)) s += e.Value;
            return s;
        }

        private static string Prefix(Kind k) => k == Kind.Refuse ? "Refuse:" : "Junk:";

        private static string Verb(Key k)
        {
            switch (k.Outcome)
            {
                case Outcome.Deleted: return "deleted";
                case Outcome.Stored: return "stored in " + k.Target;
                case Outcome.Dropped: return "dropped";
                case Outcome.Machine: return "put into the freed slot";
                case Outcome.Inventory: return "put into the inventory";
                default: return k.Outcome.ToString();
            }
        }

        private const string Quiet = " Later amounts are counted quietly (VerboseLogging shows them).";

        private static string FirstLine(Key k, int n, string item)
        {
            if (k.Kind == Kind.Refuse)
            {
                switch (k.Outcome)
                {
                    case Outcome.Deleted: return $"Refuse: deleted {n} {item} that did not fit the full ash slot of {k.Station}." + Quiet;
                    case Outcome.Stored: return $"Refuse: put {n} {item} from {k.Station} into {k.Target}." + Quiet;
                    case Outcome.Dropped: return $"Refuse: {n} {item} from {k.Station} had nowhere else to go; dropped as in the game." + Quiet;
                }
            }
            else
            {
                switch (k.Outcome)
                {
                    case Outcome.Machine: return $"Junk: {n} {item} from {k.Station} went into the slot its used-up ingredient freed (the machine was full)." + Quiet;
                    case Outcome.Inventory: return $"Junk: {n} {item} from {k.Station} went into your inventory (the machine was full)." + Quiet;
                    case Outcome.Stored: return $"Junk: put {n} {item} from {k.Station} into {k.Target} (the machine was full)." + Quiet;
                    case Outcome.Dropped: return $"Junk: no room for {n} {item} from {k.Station}; dropped next to the machine as in the game." + Quiet;
                }
            }
            return $"{Prefix(k.Kind)} {k.Station}: {n} {item} {Verb(k)}.";
        }
    }
}
