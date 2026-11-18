using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MessageLog
{
    // The per-slot side-car file (spec §7): <slot folder>/MessageLog.txt, written in SavingDone and read in LoadingStarted.
    // Plain text, one record per line, tab-separated, versioned (D14). Never a *.tnmt (Save Guard's SlotCheck parses
    // those) or *.png (Save Guard's rewrite set). Nothing of ours ever goes into the game's own files.
    //
    //   MessageLog<TAB>1
    //   character<TAB>Luchik_Abaza
    //   next<TAB>1844
    //   cp<TAB><Info.tnmt Date><TAB><last Seq><TAB><day><TAB><hm>          (oldest first)
    //   <kind><TAB><seq><TAB><day><TAB><hm><TAB><wd><TAB><flags><TAB><count><TAB><head><TAB><text>
    //
    // Head and text escape \\, \t, \n and \r. Unknown line kinds, short lines and bad numbers are skipped, and extra
    // trailing fields are ignored, so a torn line costs that line only and a later version can add fields.
    internal static class SideCar
    {
        internal const string FileName = "MessageLog.txt";
        internal const string TmpName = FileName + ".tmp";
        private const string Magic = "MessageLog";
        internal const int FormatVersion = 1;
        private const long MaxReadBytes = 8L * 1024 * 1024;
        private const int MaxInfoBytes = 64 * 1024;
        private const double SlowWriteMs = 20.0;

        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly Regex InfoDate = new Regex("\"Date\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant);
        private static readonly Regex InfoCharacter = new Regex("\"CharacterName\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant);
        private static readonly StringBuilder Sb = new StringBuilder(256);

        // Folders whose side-car has a newer format version: never overwritten this session (QuestHelper Records.cs precedent).
        internal static readonly HashSet<string> Frozen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- Format ----

        // Pre-serializes an entry's line at capture (and on merge or stamp), so the save only concatenates.
        internal static void Format(Entry e)
        {
            Sb.Length = 0;
            Sb.Append(e.Kind).Append('\t').Append(e.Seq.ToString(C)).Append('\t').Append(e.Day.ToString(C)).Append('\t')
              .Append(e.Hm.ToString(C)).Append('\t').Append(e.Wd.ToString(C)).Append('\t').Append(e.Flags.ToString(C)).Append('\t')
              .Append(e.Count.ToString(C)).Append('\t');
            Escape(Sb, e.Head);
            Sb.Append('\t');
            Escape(Sb, e.Text);
            e.Line = Sb.ToString();
            e.Bytes = Utf8.GetByteCount(e.Line) + 1;
        }

        private static void Escape(StringBuilder sb, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    default: sb.Append(ch); break;
                }
            }
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch != '\\' || i + 1 >= s.Length) { sb.Append(ch); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 't': sb.Append('\t'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        // ---- Paths and Info.tnmt ----

        internal static string Normalize(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try { return Path.GetFullPath(p).TrimEnd('/', '\\'); }
            catch (Exception) { return p.TrimEnd('/', '\\'); }
        }

        internal static bool SameFolder(string a, string b) =>
            !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        // The raw "Date" and "CharacterName" of a save's Info.tnmt (one small JSON line, BOM-tolerant). GetSavedGameInfo
        // truncates the date (SRC/SaveController.cs:779), so the raw string is the only exact per-save token.
        internal static bool ReadInfo(string dir, out string date, out string character)
        {
            date = null;
            character = null;
            try
            {
                string path = Path.Combine(dir, "Info.tnmt");
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > MaxInfoBytes) return false;
                string s = File.ReadAllText(path, Encoding.UTF8);
                var m = InfoDate.Match(s);
                if (!m.Success || m.Groups[1].Value.Length == 0) return false;
                date = m.Groups[1].Value;
                var c = InfoCharacter.Match(s);
                character = c.Success ? c.Groups[1].Value : null;
                return true;
            }
            catch (Exception) { return false; }
        }

        internal static string ReadInfoDate(string dir) => ReadInfo(dir, out string d, out _) ? d : null;

        private static bool TryDate(string s, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(s)) return false;
            if (!DateTimeOffset.TryParse(s, C, DateTimeStyles.AssumeLocal, out var v)) return false;
            utc = v.UtcDateTime;
            return true;
        }

        private static string Slot(string dir)
        {
            try
            {
                string d = Normalize(dir);
                return Path.GetFileName(Path.GetDirectoryName(d)) + "/" + Path.GetFileName(d);
            }
            catch (Exception) { return dir; }
        }

        // ---- Write (SavingDone, spec §7.3) ----

        internal static (string folder, string info) LastSave;

        // Synchronous and capped. Never throws: a failure costs this save's log, never the save. Not off the hot path:
        // an open handle would race vanilla's Directory.Delete and CopyDirectory (SRC/SaveController.cs:535-547) and
        // RemoveCharacter (:1393), Save Guard backs up the whole tree on the next Update, and quitting is Process.Kill.
        internal static void Write()
        {
            string dir = null, tmp = null;
            try
            {
                var sc = SaveController.instance;
                if (sc == null) return;
                dir = sc.GetCurrentSavePath();
                bool infoOk = ReadInfo(dir, out string date, out string character);
                LastSave = (Normalize(dir), infoOk ? date : null);   // before any write: the door keep-check needs it
                if (!Cfg.Hot.SaveWithGame) { Log.Verbose($"Storage: not saved to {Slot(dir)} (SaveWithGame is off)."); return; }
                if (Frozen.Contains(Normalize(dir))) { Log.Verbose($"Storage: not saved to {Slot(dir)} (its log has a newer format)."); return; }
                if (!infoOk) { Log.Verbose($"Storage: not saved to {Slot(dir)} (Info.tnmt unreadable)."); return; }

                var sw = Stopwatch.StartNew();
                History.StampUntimed();
                History.Checkpoint(date);
                History.Character = character;

                string body = Body(out long bytes);
                if (bytes > Cfg.Hot.MaxFileBytes)
                {
                    int dropped = History.DropOldest(bytes - Cfg.Hot.MaxFileBytes);
                    Log.WarnOnce("cap", $"Storage: the log reached MaxFileKB ({Cfg.Hot.MaxFileBytes / 1024} KB); the oldest messages are dropped ({dropped} this time).");
                    body = Body(out bytes);
                }

                string dst = Path.Combine(dir, FileName);
                tmp = Path.Combine(dir, TmpName);
                if (File.Exists(tmp)) File.Delete(tmp);
                byte[] data = Utf8.GetBytes(body);
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    fs.Write(data, 0, data.Length);
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(tmp, dst);
                tmp = null;
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                Log.Verbose($"Storage: saved {History.Count} messages to {Slot(dir)} ({data.Length / 1024} KB, {ms:0.0} ms).");
                if (ms > SlowWriteMs) Log.WarnOnce("slow", $"Storage: writing the log took {ms:0} ms. Lower MaxFileKB if saving feels slow.");
            }
            catch (Exception e)
            {
                // The tmp goes only while the side-car is still there. After a delete that worked and a move that didn't
                // (a scanner or sync client holding the old file), the tmp is the slot's only copy: ReadAndMatch falls back
                // to it, and the next Write replaces it. A half-written tmp next to a missing side-car is still the best
                // copy there is (a torn line costs that line only).
                try { if (tmp != null && File.Exists(tmp) && File.Exists(Path.Combine(dir, FileName))) File.Delete(tmp); } catch { }
                Log.WarnOnce("write|" + Normalize(dir ?? ""), $"Storage: could not write the message log to {Slot(dir ?? "?")}: {e.Message}. The save itself is fine.");
            }
        }

        private static string Body(out long bytes)
        {
            var sb = new StringBuilder(64 * 1024);
            sb.Append(Magic).Append('\t').Append(FormatVersion.ToString(C)).Append('\n');
            sb.Append("character\t");
            Escape(sb, History.Character ?? "");
            sb.Append('\n');
            sb.Append("next\t").Append(History.NextSeq.ToString(C)).Append('\n');
            foreach (var cp in History.Cps)
            {
                sb.Append("cp\t");
                Escape(sb, cp.Info);
                sb.Append('\t').Append(cp.Seq.ToString(C)).Append('\t').Append(cp.Day.ToString(C)).Append('\t').Append(cp.Hm.ToString(C)).Append('\n');
            }
            var notes = History.Notes;
            var talk = History.Talk;
            int i = 0, j = 0;
            while (i < notes.Count || j < talk.Count)
            {
                bool takeNote = j >= talk.Count || (i < notes.Count && notes[i].Seq < talk[j].Seq);
                var e = takeNote ? notes[i++] : talk[j++];
                if (e.Line == null) Format(e);
                sb.Append(e.Line).Append('\n');
            }
            string s = sb.ToString();
            bytes = Utf8.GetByteCount(s);
            return s;
        }

        // ---- Read (LoadingStarted, spec §7.4, §7.5) ----

        internal enum Match { Missing, Unreadable, OtherCharacter, Newer, Exact, Rollback, Older }

        internal sealed class ReadResult
        {
            internal Match Match;
            internal int Kept, Dropped, Skipped;
            internal long TrimFloor = -1, TrimMax;   // Rollback: entries in (TrimFloor, TrimMax] are time-trimmed at LoadingDone
            internal bool Gap;                        // Older: add a gap marker
        }

        internal static ReadResult ReadAndMatch(string dir)
        {
            var r = new ReadResult();
            string slot = Slot(dir);
            string path = Path.Combine(dir, FileName);
            if (!File.Exists(path))
            {
                string tmp = Path.Combine(dir, TmpName);
                if (!File.Exists(tmp)) { History.Load(null, null, null, 1, 0, null); r.Match = Match.Missing; return r; }
                path = tmp;   // the write stopped between the tmp and the move
            }

            string[] lines;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length > MaxReadBytes) throw new InvalidDataException("the file is over 8 MB");
                lines = File.ReadAllText(path, Utf8).Split('\n');
            }
            catch (Exception e)
            {
                Log.WarnOnce("read|" + slot, $"Storage: the message log of {slot} can't be read ({e.Message}); this save starts a new log.");
                History.Load(null, null, null, 1, 0, null);
                r.Match = Match.Unreadable;
                return r;
            }

            var head = lines.Length > 0 ? lines[0].TrimEnd('\r').TrimStart('﻿').Split('\t') : new string[0];
            if (head.Length < 2 || head[0] != Magic || !int.TryParse(head[1], NumberStyles.Integer, C, out int version))
            {
                Log.WarnOnce("read|" + slot, $"Storage: the message log of {slot} has no valid header; this save starts a new log.");
                History.Load(null, null, null, 1, 0, null);
                r.Match = Match.Unreadable;
                return r;
            }
            if (version > FormatVersion)
            {
                Frozen.Add(Normalize(dir));
                Log.WarnOnce("newer|" + slot, $"Storage: the message log of {slot} was written by a newer Message Log (format {version}); it is left untouched this session.");
                History.Load(null, null, null, 1, 0, null);
                r.Match = Match.Newer;
                return r;
            }

            string character = null;
            long next = 1;
            var cps = new List<Checkpoint>();
            var notes = new List<Entry>();
            var talk = new List<Entry>();
            long last = 0;
            for (int li = 1; li < lines.Length; li++)
            {
                string line = lines[li].TrimEnd('\r');
                if (line.Length == 0) continue;
                var f = line.Split('\t');
                try
                {
                    switch (f[0])
                    {
                        case "character":
                            if (f.Length >= 2) character = Unescape(f[1]); else r.Skipped++;
                            break;
                        case "next":
                            if (f.Length >= 2 && long.TryParse(f[1], NumberStyles.Integer, C, out long nx)) next = nx; else r.Skipped++;
                            break;
                        case "cp":
                            if (f.Length >= 5 && long.TryParse(f[2], NumberStyles.Integer, C, out long cs) && int.TryParse(f[3], NumberStyles.Integer, C, out int cd) &&
                                short.TryParse(f[4], NumberStyles.Integer, C, out short ch))
                                cps.Add(new Checkpoint { Info = Unescape(f[1]), Seq = cs, Day = cd, Hm = ch });
                            else r.Skipped++;
                            break;
                        case "n": case "g": case "d": case "c":
                            var e = ParseEntry(f);
                            if (e == null || e.Seq <= last) { r.Skipped++; break; }   // Seq must keep rising
                            last = e.Seq;
                            (e.IsTalk ? talk : notes).Add(e);
                            break;
                        default:
                            r.Skipped++;
                            break;
                    }
                }
                catch (Exception) { r.Skipped++; }
            }
            if (r.Skipped > 0) Log.Verbose($"Load: skipped {r.Skipped} unreadable lines in the log of {slot}.");

            ReadInfo(dir, out string date, out string saveCharacter);
            if (!string.IsNullOrEmpty(character) && !string.IsNullOrEmpty(saveCharacter) && !string.Equals(character, saveCharacter, StringComparison.Ordinal))
            {
                Log.WarnOnce("char|" + slot, $"Storage: the message log in {slot} belongs to {character}, not {saveCharacter}; it is ignored and replaced at the next save.");
                History.Load(null, null, null, 1, 0, null);
                r.Match = Match.OtherCharacter;
                return r;
            }

            int total = notes.Count + talk.Count;
            long fileMax = last;
            next = Math.Max(next, fileMax + 1);

            // The normal case: this save's own checkpoint. Cut back to it.
            int hit = date == null ? -1 : cps.FindLastIndex(c => c.Info == date);
            if (hit >= 0)
            {
                long cut = cps[hit].Seq;
                notes.RemoveAll(e => e.Seq > cut);
                talk.RemoveAll(e => e.Seq > cut);
                cps.RemoveRange(hit + 1, cps.Count - hit - 1);
                History.Load(notes, talk, cps, Math.Max(next, cut + 1), cut, character ?? saveCharacter);
                r.Match = Match.Exact;
                r.Kept = notes.Count + talk.Count;
                r.Dropped = total - r.Kept;
                return r;
            }

            // No checkpoint for this save. Newer checkpoints mean the folder's Info went back in time after our write
            // (Save Guard's copy-mode rollback, a .tnmt-only restore, a Steam Cloud mix): keep what the newest older
            // checkpoint knew, and time-trim the rest at LoadingDone. Game time is only the fallback here, because it can
            // go backwards.
            bool dOk = TryDate(date, out DateTime d);
            Checkpoint newest = cps.Count > 0 ? cps[cps.Count - 1] : null;
            if (dOk && newest != null && TryDate(newest.Info, out DateTime nd) && nd > d)
            {
                int floor = -1;
                for (int k = cps.Count - 1; k >= 0; k--)
                    if (TryDate(cps[k].Info, out DateTime kd) && kd < d) { floor = k; break; }
                long floorSeq = floor >= 0 ? cps[floor].Seq : 0;
                cps.RemoveRange(floor + 1, cps.Count - floor - 1);
                History.Load(notes, talk, cps, next, fileMax, character ?? saveCharacter);
                r.Match = Match.Rollback;
                r.TrimFloor = floorSeq;
                r.TrimMax = fileMax;
                r.Kept = total;
                return r;
            }

            // Older checkpoints only (or dates that don't parse): the save is newer than the log, so messages are
            // missing between them (D17). Keep everything.
            History.Load(notes, talk, cps, next, fileMax, character ?? saveCharacter);
            r.Match = Match.Older;
            r.Kept = total;
            r.Gap = total > 0 && dOk;
            return r;
        }

        private static Entry ParseEntry(string[] f)
        {
            if (f.Length < 9 || f[0].Length != 1) return null;
            if (!long.TryParse(f[1], NumberStyles.Integer, C, out long seq) || seq <= 0) return null;
            if (!int.TryParse(f[2], NumberStyles.Integer, C, out int day)) return null;
            if (!short.TryParse(f[3], NumberStyles.Integer, C, out short hm) || hm < -1 || hm >= 1440) return null;
            if (!sbyte.TryParse(f[4], NumberStyles.Integer, C, out sbyte wd)) return null;
            if (!byte.TryParse(f[5], NumberStyles.Integer, C, out byte flags)) return null;
            if (!int.TryParse(f[6], NumberStyles.Integer, C, out int count) || count < 1) return null;
            var e = new Entry
            {
                Kind = f[0][0], Seq = seq, Day = day, Hm = hm, Wd = wd, Flags = flags, Count = count,
                Head = Text.Cap(Unescape(f[7])), Text = Text.Cap(Unescape(f[8])),
            };
            Format(e);
            return e;
        }
    }
}
