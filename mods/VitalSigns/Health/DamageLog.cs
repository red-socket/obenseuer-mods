using System;
using System.Collections.Generic;

namespace VitalSigns
{
    // Loss keys (spec §5.7): one ulong per cause, so merging and the toast windows compare numbers, not strings.
    // Bits 0-7 category, 8-15 reason, 16-23 flags, 32-63 interned label (wait title or debt label).
    internal enum Cat : byte { None, Damage, SmvCap, Untraced, Bleeding, Hidden, Owed, BleedAdd }

    internal static class LossKey
    {
        internal const int Fungal = 1, Crafting = 2, Silent = 4, Asleep = 8;

        internal static ulong Make(Cat c, int reason, int flags, int id) =>
            (byte)c | ((ulong)(byte)reason << 8) | ((ulong)(byte)flags << 16) | ((ulong)(uint)id << 32);

        internal static Cat CatOf(ulong k) => (Cat)(byte)k;
        internal static int Reason(ulong k) => (byte)(k >> 8);
        internal static int Flags(ulong k) => (byte)(k >> 16);
        internal static int Id(ulong k) => (int)(uint)(k >> 32);
    }

    internal enum EntryKind : byte { Loss, Owed, Bleed, Death, Divider }

    internal sealed class Entry
    {
        internal EntryKind Kind;
        internal ulong Key;
        internal string Label, Detail;
        internal string Cause;   // Owed and Bleed: the source; their display line is built by the window (Strings.Line)
        internal float Hp, Cost;   // Owed: Cost is what the queued amount added to the drain when it was queued
        internal double FirstClock, LastClock;
        internal ulong FirstStamp, LastStamp;
        internal int Hits;
        internal bool Hidden, Attributed, Tick, Logged, Gone;
    }

    // The damage log (spec §5.7): entries in memory for the whole game session, newest last. A loss with the same key
    // within MergeMinutes game minutes adds in place; the last-touched entry is checked first, so a per-frame hazard
    // costs a compare and two adds. Capped at MaxEntries (oldest dropped). Nothing here is ever saved (D4).
    internal static class DamageLog
    {
        internal static readonly List<Entry> Entries = new List<Entry>();
        private static readonly Dictionary<ulong, Entry> Open = new Dictionary<ulong, Entry>();
        private static Entry _last;

        internal static Entry LastLoss;

        private static double MergeSeconds => Math.Max(1, Cfg.DamageLog.MergeMinutes.Value) * 60.0;

        // A health loss. label is built from the key unless given (fungal cap). Detail may be null.
        internal static void Loss(ulong key, float hp, bool hidden, bool tick, string detail = null, string label = null,
            bool attributed = false)
        {
            if (hp <= 0f) return;
            double now = GameClock.Now;
            var e = _last != null && _last.Key == key && _last.Kind == EntryKind.Loss ? _last : null;
            if (e == null) Open.TryGetValue(key, out e);
            if (e != null && now - e.LastClock <= MergeSeconds && !e.Gone)
            {
                e.Hp += hp;
                e.Hits++;
                e.LastClock = now;
                e.LastStamp = GameClock.Stamp();
                if (detail != null) e.Detail = detail;
                if (label != null) e.Label = label;
                e.Attributed |= attributed;
            }
            else
            {
                if (e != null) Close(e);
                e = Add(new Entry
                {
                    Kind = EntryKind.Loss, Key = key, Label = label ?? Strings.LossLabel(key), Detail = detail, Hp = hp, Hits = 1,
                    Hidden = hidden, Tick = tick, Attributed = attributed,
                });
                Open[key] = e;
                if (Log.VerboseOn) Log.Verbose($"Log: Loss opened | {Fmt.Stamp(e.FirstStamp)} | {e.Label} | -{hp:0.##} HP{(hidden ? " | hidden" : "")}");
            }
            _last = e;
            LastLoss = e;
            Alerts.OnLoss(key, e.Label, hp, hidden);
        }

        // "Raw Fish: 10 hidden damage queued". Merged per label like losses. cost: what it added to the drain (Tracker.Marginal).
        internal static void Owed(int labelId, string detail, float amount, float cost)
        {
            if (amount <= 0f) return;
            ulong key = LossKey.Make(Cat.Owed, 0, 0, labelId);
            double now = GameClock.Now;
            if (Open.TryGetValue(key, out var e) && now - e.LastClock <= MergeSeconds && !e.Gone)
            {
                e.Hp += amount;
                e.Hits++;
                e.LastClock = now;
                e.LastStamp = GameClock.Stamp();
                e.Cost += cost;
                if (detail != null) e.Detail = detail;
            }
            else
            {
                e = Add(new Entry { Kind = EntryKind.Owed, Key = key, Cause = Ctx.Label(labelId), Detail = detail, Hp = amount, Cost = cost, Hits = 1, Hidden = true });
                Open[key] = e;
            }
            if (Log.VerboseOn) Log.Verbose($"Log: Owed | {Fmt.Stamp(e.LastStamp)} | {Strings.Line(e)}");
        }

        // "Bleeding +5% (Mussel)". cause is a constant or interned string, so a per-frame source allocates nothing.
        internal static void BleedAdded(string cause, float amount)
        {
            if (amount <= 0f) return;
            ulong key = LossKey.Make(Cat.BleedAdd, 0, 0, Ctx.Intern(cause));
            double now = GameClock.Now;
            if (Open.TryGetValue(key, out var e) && now - e.LastClock <= MergeSeconds && !e.Gone)
            {
                e.Hp += amount;
                e.Hits++;
                e.LastClock = now;
                e.LastStamp = GameClock.Stamp();
            }
            else
            {
                e = Add(new Entry { Kind = EntryKind.Bleed, Key = key, Cause = cause, Hp = amount, Hits = 1 });
                Open[key] = e;
                if (Log.VerboseOn) Log.Verbose($"Log: Bleed | {Fmt.Stamp(e.LastStamp)} | {Strings.Line(e)}");
            }
        }

        internal static void Death(string title, string detail)
        {
            Add(new Entry { Kind = EntryKind.Death, Label = title, Detail = detail, Hits = 1 });
        }

        internal static void Divider(string text)
        {
            Add(new Entry { Kind = EntryKind.Divider, Label = text, Hits = 1 });
        }

        private static Entry Add(Entry e)
        {
            e.FirstClock = e.LastClock = GameClock.Now;
            e.FirstStamp = e.LastStamp = GameClock.Stamp();
            Entries.Add(e);
            int max = Math.Max(50, Cfg.DamageLog.MaxEntries.Value);
            while (Entries.Count > max)
            {
                var old = Entries[0];
                Entries.RemoveAt(0);
                old.Gone = true;
                if (Open.TryGetValue(old.Key, out var o) && o == old) Open.Remove(old.Key);
                if (_last == old) _last = null;
                if (LastLoss == old) LastLoss = null;
            }
            return e;
        }

        // Verbose: one line when a merged entry closes (2 Hz from Plugin.Update).
        internal static void Tick()
        {
            if (Open.Count == 0) return;
            double now = GameClock.Now, merge = MergeSeconds;
            List<ulong> done = null;
            foreach (var kv in Open)
                if (now - kv.Value.LastClock > merge) (done ?? (done = new List<ulong>())).Add(kv.Key);
            if (done == null) return;
            foreach (var k in done)
            {
                var e = Open[k];
                Open.Remove(k);
                Close(e);
            }
        }

        private static void Close(Entry e)
        {
            if (e.Logged || e.Kind != EntryKind.Loss || e.Hits < 2) return;
            e.Logged = true;
            if (Log.VerboseOn)
                Log.Verbose($"Log: Loss | {Fmt.Range(e.FirstStamp, e.LastStamp)} | {e.Label} | -{e.Hp:0.##} HP{(e.Hidden ? " | hidden" : "")} | hits {e.Hits}");
        }

        // Losses in the last span game seconds (since the last divider), summed per key, largest first (the death summary).
        internal static List<KeyValuePair<Entry, float>> Recent(double span)
        {
            var sums = new Dictionary<ulong, KeyValuePair<Entry, float>>();
            double from = GameClock.Now - span;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                var e = Entries[i];
                if (e.Kind == EntryKind.Divider) break;
                if (e.Kind != EntryKind.Loss || e.LastClock < from) continue;
                sums[e.Key] = sums.TryGetValue(e.Key, out var kv) ? new KeyValuePair<Entry, float>(kv.Key, kv.Value + e.Hp) : new KeyValuePair<Entry, float>(e, e.Hp);
            }
            var list = new List<KeyValuePair<Entry, float>>(sums.Values);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }
    }
}
