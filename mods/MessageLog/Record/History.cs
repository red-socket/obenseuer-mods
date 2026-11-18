using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MessageLog
{
    // The log in memory (spec §6). Two rings ordered by Seq: Notes (notifications and gap markers) and Talk (conversation
    // lines and choices), so a long conversation never pushes notifications out. Seq is the order key; SavedSeq is the
    // last Seq the current save contains. An entry with Seq <= SavedSeq is never changed again (merge and fold touch only
    // newer ones), so cutting the log back to a checkpoint restores exactly what that save knew. Main thread only.
    internal static class History
    {
        internal const int MaxCheckpoints = 64;
        private const int CapSlack = 32;          // amortized trimming: cut back to the cap once it is exceeded by this
        private const int FoldScan = 50;          // D13: how far back an earlier step of the same objective is looked for
        private const int MaxPending = 64;
        internal const string ObjectiveUpdated = "Objective Updated";
        internal const string SaveCorrupted = "Save data corrupted";

        internal static readonly List<Entry> Notes = new List<Entry>();
        internal static readonly List<Entry> Talk = new List<Entry>();
        internal static readonly List<Checkpoint> Cps = new List<Checkpoint>();
        // Captured between PlayerWillLoadGame and LoadingStarted of a load from file: the tail of the session being left.
        internal static readonly List<Entry> Pending = new List<Entry>();

        internal static long NextSeq = 1, SavedSeq;
        internal static string Character;
        internal static int Version;              // bumped on every change; the window rebuilds when it moves

        private static int _merges;
        private static readonly Regex ObjectiveCounter = new Regex(@"\s\d+/\d+(\s\(Optional\))?$", RegexOptions.CultureInvariant);

        internal static int Count => Notes.Count + Talk.Count;

        // ---- Capture ----

        internal static void AddNotification(string type, string text, bool silent)
        {
            var e = new Entry { Kind = 'n', Head = Text.Cap(type), Text = text, Flags = silent ? Entry.FlagSilent : (byte)0 };
            Add(e);
        }

        internal static void AddDialogue(char kind, string who, string text)
        {
            if (Cfg.Hot.MaxDialogueLines <= 0) return;
            Add(new Entry { Kind = kind, Head = Text.Cap(who ?? ""), Text = text });
        }

        private static void Add(Entry e)
        {
            Clock.Now(out e.Day, out e.Hm, out e.Wd);
            if (SlotTracker.CapturesPending)
            {
                if (Pending.Count < MaxPending) Pending.Add(e);
                return;
            }
            var ring = e.IsTalk ? Talk : Notes;
            if (TryMerge(ring, e)) { Version++; return; }
            if (e.Kind == 'n' && Cfg.Hot.FoldObjectives && e.Head == ObjectiveUpdated) Fold(e);
            Append(ring, e);
            Version++;
        }

        // D12: the same message again within MergeSeconds (real time) of its last repeat becomes "xN" on the newest
        // unsaved entry. The first time is kept. Real time isn't saved, so nothing merges across a load.
        private static bool TryMerge(List<Entry> ring, Entry e)
        {
            float window = Cfg.Hot.MergeSeconds;
            if (window <= 0f || ring.Count == 0) return false;
            var last = ring[ring.Count - 1];
            if (last.Seq <= SavedSeq || last.Kind != e.Kind || last.Head != e.Head || last.Text != e.Text) return false;
            float now = Time.realtimeSinceStartup;
            if (now - last.Real > window) return false;
            last.Count++;
            last.Real = now;
            SideCar.Format(last);
            if (++_merges % 100 == 0) Log.Verbose($"Capture: {_merges} repeats merged so far.");
            return true;
        }

        // D13: "Objective Updated" for the same objective (its title without the "3/10" counter) replaces the earlier
        // unsaved step; the new step gets a new Seq, so it sorts as the newest message.
        private static void Fold(Entry e)
        {
            string stem = ObjectiveCounter.Replace(e.Text ?? "", "");
            int scanned = 0;
            for (int i = Notes.Count - 1; i >= 0 && scanned < FoldScan; i--, scanned++)
            {
                var old = Notes[i];
                if (old.Seq <= SavedSeq) return;
                if (old.Kind != 'n' || old.Head != ObjectiveUpdated) continue;
                if (ObjectiveCounter.Replace(old.Text ?? "", "") != stem) continue;
                Notes.RemoveAt(i);
                return;
            }
        }

        internal static void Append(List<Entry> ring, Entry e)
        {
            e.Seq = NextSeq++;
            e.Real = Time.realtimeSinceStartup;
            SideCar.Format(e);
            ring.Add(e);
            EnforceCap(ring, CapSlack);
        }

        private static void EnforceCap(List<Entry> ring, int slack)
        {
            int cap = ReferenceEquals(ring, Talk) ? Cfg.Hot.MaxDialogueLines : Cfg.Hot.MaxNotifications;
            if (ring.Count > cap + slack) ring.RemoveRange(0, ring.Count - cap);
        }

        internal static void ApplyCaps()
        {
            EnforceCap(Notes, 0);
            EnforceCap(Talk, 0);
            Version++;
        }

        // ---- Load and save support ----

        // Messages left untimed by a load (or captured while one ran) get the current game time, marked as estimated.
        // Gap markers take it as their exact time: they mark the moment the save was loaded.
        internal static int StampUntimed()
        {
            if (!Clock.Raw(out int day, out short hm, out sbyte wd)) return 0;
            int n = 0;
            n += Stamp(Notes, day, hm, wd);
            n += Stamp(Talk, day, hm, wd);
            if (n > 0) Version++;
            return n;
        }

        private static int Stamp(List<Entry> ring, int day, short hm, sbyte wd)
        {
            int n = 0;
            foreach (var e in ring)
            {
                if (e.Timed) continue;
                e.Day = day; e.Hm = hm; e.Wd = wd;
                if (e.Kind != 'g') e.Flags |= Entry.FlagEstimated;
                SideCar.Format(e);
                n++;
            }
            return n;
        }

        // A load from file keeps only "Save data corrupted" from the abandoned session's tail (spec §0.3, D19).
        internal static int TakePending()
        {
            int kept = 0;
            foreach (var e in Pending)
            {
                if (e.Kind != 'n' || e.Head != SaveCorrupted) continue;
                e.Hm = -1;
                Append(Notes, e);
                kept++;
            }
            int dropped = Pending.Count - kept;
            Pending.Clear();
            if (kept > 0) Version++;
            if (dropped > 0) Log.Verbose($"Load: {dropped} messages from the session being left were dropped.");
            return kept;
        }

        // Replaces the whole log with what was read from a side-car (or with nothing).
        internal static void Load(List<Entry> notes, List<Entry> talk, List<Checkpoint> cps, long next, long saved, string character)
        {
            Notes.Clear();
            Talk.Clear();
            Cps.Clear();
            if (notes != null) Notes.AddRange(notes);
            if (talk != null) Talk.AddRange(talk);
            if (cps != null) Cps.AddRange(cps);
            long max = 0;
            if (Notes.Count > 0) max = Math.Max(max, Notes[Notes.Count - 1].Seq);
            if (Talk.Count > 0) max = Math.Max(max, Talk[Talk.Count - 1].Seq);
            NextSeq = Math.Max(Math.Max(next, max + 1), 1);
            SavedSeq = saved;
            Character = character;
            EnforceCap(Notes, 0);
            EnforceCap(Talk, 0);
            Version++;
        }

        // The "newer" rule (spec §7.5): entries above the floor checkpoint whose game time is after the loaded save's time
        // belong to a future the save never saw.
        internal static int TrimAfter(long floorSeq, long maxSeq, int saveMinute)
        {
            int n = Notes.RemoveAll(e => e.Seq > floorSeq && e.Seq <= maxSeq && e.Timed && e.GameMinute > saveMinute);
            n += Talk.RemoveAll(e => e.Seq > floorSeq && e.Seq <= maxSeq && e.Timed && e.GameMinute > saveMinute);
            if (n > 0) Version++;
            return n;
        }

        // D17: where messages are missing (the mod was off, removed, or a write failed). Untimed until LoadingDone.
        internal static void AddGap()
        {
            Append(Notes, new Entry { Kind = 'g' });
            Version++;
        }

        // Called with the save's Info Date right after a successful read of Info.tnmt in SavingDone (spec §7.3).
        internal static void Checkpoint(string info)
        {
            Clock.Raw(out int day, out short hm, out _);
            Cps.Add(new Checkpoint { Info = info, Seq = NextSeq - 1, Day = day, Hm = hm });
            if (Cps.Count > MaxCheckpoints) Cps.RemoveRange(0, Cps.Count - MaxCheckpoints);
            long oldest = OldestSeq();
            if (oldest > 0)
                for (int i = Cps.Count - 2; i >= 0; i--)
                    if (Cps[i].Seq < oldest - 1) { Cps.RemoveRange(0, i + 1); break; }
            SavedSeq = NextSeq - 1;
        }

        internal static long OldestSeq()
        {
            long a = Notes.Count > 0 ? Notes[0].Seq : long.MaxValue;
            long b = Talk.Count > 0 ? Talk[0].Seq : long.MaxValue;
            long m = Math.Min(a, b);
            return m == long.MaxValue ? 0 : m;
        }

        // Drops the oldest entries of both rings together (by Seq) until the side-car body fits (spec §7.3 step 5).
        internal static int DropOldest(long bytesOver)
        {
            int n = 0, i = 0, j = 0;
            long freed = 0;
            while (freed < bytesOver && (i < Notes.Count || j < Talk.Count))
            {
                bool takeNote = j >= Talk.Count || (i < Notes.Count && Notes[i].Seq < Talk[j].Seq);
                freed += takeNote ? Notes[i++].Bytes : Talk[j++].Bytes;
                n++;
            }
            if (i > 0) Notes.RemoveRange(0, i);
            if (j > 0) Talk.RemoveRange(0, j);
            if (n > 0) Version++;
            return n;
        }

        // Main Menu (spec §3): the only reset before a new game, which fires no load events.
        internal static void Reset()
        {
            Notes.Clear();
            Talk.Clear();
            Cps.Clear();
            Pending.Clear();
            NextSeq = 1;
            SavedSeq = 0;
            Character = null;
            Version++;
        }
    }
}
