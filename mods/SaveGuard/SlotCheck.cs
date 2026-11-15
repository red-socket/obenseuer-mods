using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FullSerializer;

namespace SaveGuard
{
    internal enum SlotStatus { Ok, Empty, Damaged, Broken }

    internal sealed class CheckResult
    {
        internal SlotStatus Status;
        internal string Reason;

        internal CheckResult(SlotStatus status, string reason) { Status = status; Reason = reason; }
        internal bool Ok => Status == SlotStatus.Ok;
        internal static readonly CheckResult Good = new CheckResult(SlotStatus.Ok, "");
        public override string ToString() => Reason.Length == 0 ? Status.ToString() : $"{Status}: {Reason}";
    }

    internal sealed class SlotHeader
    {
        internal string LevelName, Version;
        internal DateTime Date;          // local time; default when unknown
        internal bool DateFromMtime;
    }

    // Read-only checks of a slot folder (spec §6.1). Never calls SaveController.Deserialize or GetSavedGameInfo:
    // those log errors and are bound to the main thread. fsJsonParser is plain C# and does not log.
    internal static class SlotCheck
    {
        private static readonly Dictionary<string, (string sig, CheckResult r)> StructuralCache = new Dictionary<string, (string, CheckResult)>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, (string sig, CheckResult r)> FullCache = new Dictionary<string, (string, CheckResult)>(StringComparer.OrdinalIgnoreCase);

        internal static void Invalidate()
        {
            StructuralCache.Clear();
            FullCache.Clear();
        }

        // Head and tail of every *.tnmt plus the dialogue tail. Cached by Info/Globals size and mtime.
        internal static CheckResult Structural(string dir)
        {
            string sig = Signature(dir);
            if (StructuralCache.TryGetValue(dir, out var c) && c.sig == sig) return c.r;
            var r = StructuralCore(dir, null);
            StructuralCache[dir] = (sig, r);
            return r;
        }

        // Only the four files a save rewrites (Commit check, §4.3): Info, Globals, <level>.tnmt, Globals.dialogue.
        internal static CheckResult StructuralRewritten(string dir, string level) => StructuralCore(dir, level ?? "");

        // Structural plus a full JSON parse of Info and Globals (allTnmt: every *.tnmt).
        internal static CheckResult Full(string dir, bool allTnmt)
        {
            string sig = Signature(dir) + (allTnmt ? "|all" : "");
            if (FullCache.TryGetValue(dir, out var c) && c.sig == sig) return c.r;
            var r = Structural(dir);
            if (r.Status == SlotStatus.Ok)
            {
                if (!Parses(Path.Combine(dir, "Info.tnmt"))) r = new CheckResult(SlotStatus.Broken, "Info.tnmt is unreadable");
                else if (!Parses(Path.Combine(dir, "Globals.tnmt"))) r = new CheckResult(SlotStatus.Broken, "Globals.tnmt is unreadable");
                else if (allTnmt)
                {
                    foreach (var f in Directory.GetFiles(dir, "*.tnmt"))
                    {
                        string name = Path.GetFileName(f);
                        if (IsName(name, "Info.tnmt") || IsName(name, "Globals.tnmt")) continue;
                        if (!Parses(f)) { r = new CheckResult(SlotStatus.Damaged, name + " is unreadable"); break; }
                    }
                }
            }
            FullCache[dir] = (sig, r);
            return r;
        }

        internal static CheckResult FullInfoGlobals(string dir) => Full(dir, false);

        // The files a save rewrites (Info, Globals, <level>.tnmt, Globals.dialogue): structure plus a full JSON parse
        // of the three .tnmt files. Scene files the save did not write are not judged: vanilla copies them as they
        // are and tolerates a damaged one (it resets that scene). Not cached; used after a failed or interrupted save.
        internal static CheckResult FullRewritten(string dir, string level)
        {
            var r = StructuralRewritten(dir, level);
            if (!r.Ok) return r;
            if (!Parses(Path.Combine(dir, "Info.tnmt"))) return new CheckResult(SlotStatus.Broken, "Info.tnmt is unreadable");
            if (!Parses(Path.Combine(dir, "Globals.tnmt"))) return new CheckResult(SlotStatus.Broken, "Globals.tnmt is unreadable");
            if (!string.IsNullOrEmpty(level) && !Parses(Path.Combine(dir, level + ".tnmt")))
                return new CheckResult(SlotStatus.Damaged, level + ".tnmt is unreadable");
            return CheckResult.Good;
        }

        private static CheckResult StructuralCore(string dir, string onlyLevel)
        {
            if (!Directory.Exists(dir)) return new CheckResult(SlotStatus.Empty, "no folder");
            string info = Path.Combine(dir, "Info.tnmt");
            string globals = Path.Combine(dir, "Globals.tnmt");
            bool hasInfo = File.Exists(info), hasGlobals = File.Exists(globals);
            if (!hasInfo && !hasGlobals) return new CheckResult(SlotStatus.Empty, "no save files");
            if (!hasInfo) return new CheckResult(SlotStatus.Broken, "Info.tnmt is missing");
            if (!JsonShape(info)) return new CheckResult(SlotStatus.Broken, "Info.tnmt is truncated");
            if (!hasGlobals) return new CheckResult(SlotStatus.Broken, "Globals.tnmt is missing");
            if (!JsonShape(globals)) return new CheckResult(SlotStatus.Broken, "Globals.tnmt is truncated");

            if (onlyLevel != null)
            {
                if (onlyLevel.Length > 0)
                {
                    string level = Path.Combine(dir, onlyLevel + ".tnmt");
                    if (!File.Exists(level)) return new CheckResult(SlotStatus.Damaged, onlyLevel + ".tnmt is missing");
                    if (!JsonShape(level)) return new CheckResult(SlotStatus.Damaged, onlyLevel + ".tnmt is truncated");
                }
            }
            else
            {
                string first = null; int bad = 0;
                foreach (var f in Directory.GetFiles(dir, "*.tnmt"))
                {
                    string name = Path.GetFileName(f);
                    if (IsName(name, "Info.tnmt") || IsName(name, "Globals.tnmt")) continue;
                    if (!JsonShape(f)) { bad++; if (first == null) first = name; }
                }
                if (bad > 0)
                    return new CheckResult(SlotStatus.Damaged, first + " is truncated" + (bad > 1 ? $" (+{bad - 1} more)" : ""));
            }

            string dialogue = Path.Combine(dir, "Globals.dialogue");
            if (!File.Exists(dialogue)) return new CheckResult(SlotStatus.Damaged, "Globals.dialogue is missing");
            if (!DialogueComplete(dialogue)) return new CheckResult(SlotStatus.Damaged, "Globals.dialogue is incomplete");
            return CheckResult.Good;
        }

        // Optional UTF-8 BOM, then '{'; last non-whitespace byte '}'. Catches truncation at most points;
        // the full parse catches the rest.
        private static bool JsonShape(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    if (len <= 4) return false;
                    var head = new byte[4];
                    if (ReadFully(fs, head, 4) < 4) return false;
                    int i = head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
                    if (head[i] != (byte)'{') return false;
                    int n = (int)Math.Min(len, 256);
                    var tail = new byte[n];
                    fs.Seek(len - n, SeekOrigin.Begin);
                    if (ReadFully(fs, tail, n) < n) return false;
                    for (int k = n - 1; k >= 0; k--)
                    {
                        byte b = tail[k];
                        if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n') continue;
                        return b == (byte)'}';
                    }
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Verbose($"Check of {path} failed: {e.Message}");
                return false;
            }
        }

        // The dump ends with the relationship table (PersistentDataManager.AppendRelationshipAndStatusTables,
        // written last): ...RelationshipTable = "<entries>"; . The table is capped at 64K chars, so reading the
        // last 256 KB always contains its start.
        private static bool DialogueComplete(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    if (len <= 0) return false;
                    int n = (int)Math.Min(len, 256 * 1024);
                    var buf = new byte[n];
                    fs.Seek(len - n, SeekOrigin.Begin);
                    if (ReadFully(fs, buf, n) < n) return false;
                    string tail = Encoding.UTF8.GetString(buf).TrimEnd(' ', '\t', '\r', '\n');
                    if (!tail.EndsWith("\";", StringComparison.Ordinal)) return false;
                    return tail.LastIndexOf("RelationshipTable = \"", StringComparison.Ordinal) >= 0;
                }
            }
            catch (Exception e)
            {
                Log.Verbose($"Check of {path} failed: {e.Message}");
                return false;
            }
        }

        private static bool Parses(string path)
        {
            try
            {
                return fsJsonParser.Parse(File.ReadAllText(path, Encoding.UTF8), out _).Succeeded;
            }
            catch (Exception e)
            {
                Log.Verbose($"Parse of {path} failed: {e.Message}");
                return false;
            }
        }

        // Info header fields as strings (§6.2). Date falls back to Info.tnmt's mtime, never the folder or other files:
        // copied scene files keep their old mtimes.
        internal static SlotHeader ReadHeader(string dir)
        {
            string info = Path.Combine(dir, "Info.tnmt");
            if (!File.Exists(info)) return null;
            var h = new SlotHeader();
            try
            {
                if (fsJsonParser.Parse(File.ReadAllText(info, Encoding.UTF8), out var data).Succeeded && data != null && data.IsDictionary)
                {
                    var d = data.AsDictionary;
                    h.LevelName = Str(d, "LevelName");
                    h.Version = Str(d, "ApplicationVersion");
                    string raw = Str(d, "Date");
                    if (!string.IsNullOrEmpty(raw) && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) && date != default)
                        h.Date = date.Kind == DateTimeKind.Utc ? date.ToLocalTime() : date;
                }
            }
            catch (Exception e) { Log.Verbose($"Header of {dir} unreadable: {e.Message}"); }
            if (h.Date == default)
            {
                try { h.Date = File.GetLastWriteTime(info); h.DateFromMtime = true; } catch { }
            }
            return h;
        }

        internal static string FormatDate(DateTime local, bool fromMtime) =>
            local == default ? "?" : local.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + (fromMtime ? " (file time)" : "");

        private static string Str(Dictionary<string, fsData> d, string key) =>
            d.TryGetValue(key, out var v) && v != null && v.IsString ? v.AsString : null;

        private static string Signature(string dir)
        {
            try
            {
                var i = new FileInfo(Path.Combine(dir, "Info.tnmt"));
                var g = new FileInfo(Path.Combine(dir, "Globals.tnmt"));
                return (i.Exists ? i.Length + ":" + i.LastWriteTimeUtc.Ticks : "-") + "|" + (g.Exists ? g.Length + ":" + g.LastWriteTimeUtc.Ticks : "-");
            }
            catch { return Guid.NewGuid().ToString(); }
        }

        private static bool IsName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static int ReadFully(Stream s, byte[] buf, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buf, total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }
    }
}
