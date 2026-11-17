using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SaveGuard
{
    // One restorable slot folder: an entry of backups/ or recovered/, or one slot of a preupdate/ set.
    internal sealed class RestorePoint
    {
        internal string Dir;              // the slot folder to copy
        internal string Kind;             // door|manual|script|timed|failed|previous|replaced|firstrun|preupdate
        internal string Character, Slot;
        internal DateTime CreatedUtc;
        internal DateTime SaveDate;       // local
        internal bool SaveDateFromMtime;
        internal string SaveVersion, LevelName, FromVersion, ToVersion;
        internal long Bytes;
        internal bool Complete;

        internal string KindLabel()
        {
            switch (Kind)
            {
                case "door": return "area change";
                case "manual": return "manual save";
                case "script": return "game autosave";
                case "timed": return "timed autosave";
                case "failed": return "failed (incomplete)";
                case "previous": return "version before the last save";
                case "replaced": return "replaced by restore";
                case "firstrun": return "first run of Save Guard";
                case "questhelper": return "before a Quest Helper fix";
                case "preupdate": return $"before update {FromVersion} -> {ToVersion}";
                default: return Kind;
            }
        }
    }

    internal sealed class PendingBackup
    {
        internal string Target, Character, Slot, Kind;
    }

    // Rotating backups (spec §5) and the listing/pruning of every store (§9).
    internal static class BackupStore
    {
        internal static PendingBackup Pending;
        private static readonly Dictionary<string, DateTime> LastBackupUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static bool _lowSpaceToasted;

        // Called on a successful save. A save that is not due is not queued; the next save past the interval is.
        internal static void Queue(string target, string character, string slot, string kind)
        {
            if (!Cfg.BackupsEnabled.Value || string.IsNullOrEmpty(target) || string.IsNullOrEmpty(character)) return;
            bool manual = kind == "manual" && Cfg.AlwaysBackupManualSaves.Value;
            var last = GetLastBackupUtc(character);
            double minutes = (DateTime.UtcNow - last).TotalMinutes;
            if (!manual && minutes < Cfg.MinIntervalMinutes.Value)
            {
                Log.Verbose($"No backup of {character}/{slot} ({kind}): last one {minutes:0.0} min ago");
                return;
            }
            Pending = new PendingBackup { Target = target, Character = character, Slot = slot, Kind = kind };
        }

        // Next Update, or the Prefix of the next SaveGame (so the backup copies the state just committed).
        internal static void FlushPending()
        {
            var p = Pending;
            if (p == null) return;
            Pending = null;
            try { Create(p); }
            catch (Exception e) { Log.Error($"Backup of {p.Character}/{p.Slot} failed: {e}"); }
        }

        private static void Create(PendingBackup p)
        {
            var started = DateTime.UtcNow;
            try { RevealCamera.WaitForPendingMapWrite(); } catch (Exception e) { Log.Exception("WaitForPendingMapWrite", e); }
            if (!Directory.Exists(p.Target)) { Log.Warn($"Backup skipped: {p.Target} no longer exists"); return; }
            var (_, bytes) = FileOps.TreeBytes(p.Target);
            string charDir = Path.Combine(Paths.Backups, p.Character);
            Directory.CreateDirectory(charDir);
            if (!FileOps.HasRoomFor(charDir, bytes))
            {
                Log.Warn($"Backup of {p.Character}/{p.Slot} skipped: not enough free disk space ({bytes / 1048576.0:0.0} MB needed plus {Cfg.MinFreeSpaceMB.Value} MB reserve).");
                if (!_lowSpaceToasted) { _lowSpaceToasted = true; Toasts.Show("Save Guard: not enough disk space for backups."); }
                return;
            }
            var header = SlotCheck.ReadHeader(p.Target);
            string dir = Paths.UniqueDir(charDir, $"{Paths.Stamp(DateTime.Now)}_{p.Slot}_{p.Kind}");
            string partial = dir + Paths.Partial;
            var (files, copied) = FileOps.CopyTree(p.Target, partial);
            var entry = NewEntry(p.Kind, p.Character, p.Slot, header, files, copied, complete: true);
            KvFile.WriteAtomic(Path.Combine(partial, Paths.EntryFile), entry);
            Directory.Move(partial, dir);
            LastBackupUtc[p.Character] = DateTime.UtcNow;
            Log.Info($"Backup {p.Character}/{p.Slot} -> {Short(dir)} ({copied / 1048576.0:0.0} MB, {files} files, {(DateTime.UtcNow - started).TotalMilliseconds:0} ms)");
            if (Cfg.NotifyOnBackup.Value) Toasts.Show($"Backed up {Paths.SlotDisplay(p.Slot)}");
            Prune(p.Character);
        }

        internal static Kv NewEntry(string kind, string character, string slot, SlotHeader header, int files, long bytes, bool complete)
        {
            var e = new Kv();
            e["kind"] = kind;
            e["character"] = character;
            e["slot"] = slot;
            e["createdUtc"] = Paths.Iso(DateTime.UtcNow);
            if (header != null && header.Date != default)
            {
                e["saveDate"] = header.Date.ToString("o");
                e["saveDateSource"] = header.DateFromMtime ? "mtime" : "info";
            }
            e["saveVersion"] = header?.Version ?? "";
            e["runningVersion"] = Application.version;
            e["levelName"] = header?.LevelName ?? "";
            e["files"] = files.ToString();
            e["bytes"] = bytes.ToString();
            e["complete"] = complete ? "1" : "0";
            return e;
        }

        // Moves (rename) or copies a slot folder into recovered/<char>/<stamp>_<slot>_<kind> with its entry.txt.
        // move=true renames src away (same volume); otherwise src is copied and left in place.
        internal static string AddRecovered(string src, string character, string slot, string kind, bool move, bool complete, Kv extra = null)
        {
            string charDir = Path.Combine(Paths.Recovered, character);
            Directory.CreateDirectory(charDir);
            string dir = Paths.UniqueDir(charDir, $"{Paths.Stamp(DateTime.Now)}_{slot}_{kind}");
            string partial = dir + Paths.Partial;
            if (move)
            {
                // A rename keeps the slot on disk at every instant; entry.txt is written into the final folder
                // (a crash in between leaves a folder the UI ignores but never deletes). A failed entry.txt write
                // (a full disk) must not stop the caller's next step, which usually puts the previous version back.
                Directory.Move(src, dir);
                try { WriteEntryFor(dir, kind, character, slot, complete, extra); }
                catch (Exception e) { Log.Warn($"Could not write entry.txt for {Short(dir)} ({e.Message}); the folder is kept but not listed."); }
            }
            else
            {
                try
                {
                    FileOps.CopyTree(src, partial);
                    WriteEntryFor(partial, kind, character, slot, complete, extra);
                    Directory.Move(partial, dir);
                }
                catch
                {
                    FileOps.SafeDeleteTree(partial);
                    throw;
                }
            }
            Log.Info($"Set aside {character}/{slot} as {Short(dir)}");
            return dir;
        }

        internal static void WriteEntryFor(string dir, string kind, string character, string slot, bool complete, Kv extra = null)
        {
            var (files, bytes) = FileOps.TreeBytes(dir, new[] { Paths.EntryFile });
            var entry = NewEntry(kind, character, slot, SlotCheck.ReadHeader(dir), files, bytes, complete);
            if (extra != null)
                if (extra.Has("txnStartedUtc")) entry["txnStartedUtc"] = extra["txnStartedUtc"];
            KvFile.WriteAtomic(Path.Combine(dir, Paths.EntryFile), entry);
        }

        private static DateTime GetLastBackupUtc(string character)
        {
            if (LastBackupUtc.TryGetValue(character, out var t)) return t;
            t = default;
            foreach (var p in ListEntries(Path.Combine(Paths.Backups, character)))
                if (p.CreatedUtc > t) t = p.CreatedUtc;
            LastBackupUtc[character] = t;
            return t;
        }

        internal static string Short(string dir)
        {
            try
            {
                string root = Path.GetFullPath(Paths.BackupBase).TrimEnd('/', '\\');
                string full = Path.GetFullPath(dir);
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return full.Substring(root.Length + 1).Replace('\\', '/');
                root = Path.GetFullPath(Paths.Root).TrimEnd('/', '\\');
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return full.Substring(root.Length + 1).Replace('\\', '/');
            }
            catch { }
            return dir;
        }

        // ---- listing ----

        private static RestorePoint FromEntry(string dir, Kv e)
        {
            var p = new RestorePoint
            {
                Dir = dir,
                Kind = e["kind"] ?? "", Character = e["character"] ?? "", Slot = e["slot"] ?? "",
                CreatedUtc = Paths.ParseIso(e["createdUtc"]),
                SaveVersion = e["saveVersion"] ?? "", LevelName = e["levelName"] ?? "",
                Bytes = e.Long("bytes"), Complete = e.Bool("complete"),
                SaveDateFromMtime = e["saveDateSource"] == "mtime",
            };
            var sd = Paths.ParseIso(e["saveDate"]);
            p.SaveDate = sd == default ? default : sd.ToLocalTime();
            return p;
        }

        // Entries with a readable entry.txt directly below dir (backups/<char>, recovered/<char>).
        internal static List<RestorePoint> ListEntries(string dir)
        {
            var list = new List<RestorePoint>();
            if (!Directory.Exists(dir)) return list;
            foreach (var d in Directory.GetDirectories(dir))
            {
                if (d.EndsWith(Paths.Partial, StringComparison.OrdinalIgnoreCase)) continue;
                var e = KvFile.Read(Path.Combine(d, Paths.EntryFile));
                if (e == null) continue;
                list.Add(FromEntry(d, e));
            }
            return list;
        }

        internal sealed class PreUpdateSet
        {
            internal string Dir;
            internal Kv Entry;
            internal DateTime CreatedUtc;
        }

        internal static List<PreUpdateSet> ListPreUpdateSets()
        {
            var list = new List<PreUpdateSet>();
            if (!Directory.Exists(Paths.PreUpdate)) return list;
            foreach (var d in Directory.GetDirectories(Paths.PreUpdate))
            {
                if (d.EndsWith(Paths.Partial, StringComparison.OrdinalIgnoreCase)) continue;
                var e = KvFile.Read(Path.Combine(d, Paths.EntryFile));
                if (e == null) continue;
                list.Add(new PreUpdateSet { Dir = d, Entry = e, CreatedUtc = Paths.ParseIso(e["createdUtc"]) });
            }
            return list;
        }

        private static readonly Dictionary<string, (int files, long bytes)> SizeCache = new Dictionary<string, (int, long)>(StringComparer.OrdinalIgnoreCase);

        // All restore points of one character, newest first.
        internal static List<RestorePoint> ListPoints(string character)
        {
            var list = new List<RestorePoint>();
            list.AddRange(ListEntries(Path.Combine(Paths.Backups, character)));
            list.AddRange(ListEntries(Path.Combine(Paths.Recovered, character)));
            foreach (var set in ListPreUpdateSets())
            {
                string charDir = Path.Combine(set.Dir, character);
                if (!Directory.Exists(charDir)) continue;
                foreach (var slotDir in Directory.GetDirectories(charDir))
                {
                    var h = SlotCheck.ReadHeader(slotDir);
                    if (h == null) continue;   // not a slot (no Info.tnmt)
                    if (!SizeCache.TryGetValue(slotDir, out var size)) SizeCache[slotDir] = size = FileOps.TreeBytes(slotDir);
                    list.Add(new RestorePoint
                    {
                        Dir = slotDir,
                        Kind = set.Entry["kind"] ?? "preupdate", Character = character, Slot = Path.GetFileName(slotDir),
                        CreatedUtc = set.CreatedUtc, SaveDate = h.Date, SaveDateFromMtime = h.DateFromMtime,
                        SaveVersion = h.Version ?? "", LevelName = h.LevelName ?? "",
                        FromVersion = set.Entry["from"], ToVersion = set.Entry["to"],
                        Bytes = size.bytes, Complete = true,
                    });
                }
            }
            list.Sort((a, b) => b.CreatedUtc.CompareTo(a.CreatedUtc));
            return list;
        }

        // Character folder names from Saves/* and every store, so deleted characters and characters
        // whose slots are all unreadable (GetCharacters skips those) are reachable.
        internal static List<string> ListCharacters()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddDirs(string dir)
            {
                if (!Directory.Exists(dir)) return;
                foreach (var d in Directory.GetDirectories(dir)) set.Add(Path.GetFileName(d));
            }
            AddDirs(Paths.Saves);
            AddDirs(Paths.Backups);
            AddDirs(Paths.Recovered);
            foreach (var s in ListPreUpdateSets()) AddDirs(s.Dir);
            return set.ToList();
        }

        // ---- pruning (§9) ----

        internal static void PruneAll()
        {
            var chars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(Paths.Backups)) foreach (var d in Directory.GetDirectories(Paths.Backups)) chars.Add(Path.GetFileName(d));
            if (Directory.Exists(Paths.Recovered)) foreach (var d in Directory.GetDirectories(Paths.Recovered)) chars.Add(Path.GetFileName(d));
            foreach (var c in chars) PruneCharacter(c);
            PrunePreUpdate();
            PruneTotalSize();
        }

        internal static void Prune(string character)
        {
            PruneCharacter(character);
            PruneTotalSize();
        }

        internal static void PruneCharacter(string character)
        {
            KeepNewest(ListEntries(Path.Combine(Paths.Backups, character)), Cfg.KeepPerCharacter.Value, "backup");
            PruneRecovered(character);
        }

        // Slots replaced by a restore have their own, larger budget: they may hold the player's newest progress,
        // and only the player's own restores create them. Failed and previous entries share the other budget.
        internal static void PruneRecovered(string character)
        {
            var all = ListEntries(Path.Combine(Paths.Recovered, character));
            KeepNewest(all.Where(p => p.Kind == "replaced").ToList(), Cfg.ReplacedToKeepPerCharacter.Value, "replaced slot");
            KeepNewest(all.Where(p => p.Kind != "replaced").ToList(), Cfg.RecoveredToKeepPerCharacter.Value, "recovered entry");
        }

        private static void KeepNewest(List<RestorePoint> list, int keep, string what)
        {
            list.Sort((a, b) => b.CreatedUtc.CompareTo(a.CreatedUtc));
            for (int i = keep; i < list.Count; i++) Delete(list[i].Dir, what);
        }

        internal static void PrunePreUpdate()
        {
            var sets = ListPreUpdateSets();
            sets.Sort((a, b) => b.CreatedUtc.CompareTo(a.CreatedUtc));
            for (int i = Cfg.KeepSets.Value; i < sets.Count; i++) Delete(sets[i].Dir, "pre-update set");
        }

        // Oldest backups first (keeping the newest 2 per character), then the oldest pre-update sets (keeping 1).
        private static void PruneTotalSize()
        {
            long cap = Cfg.MaxTotalSizeMB.Value * 1048576L;
            if (cap <= 0) return;
            var backups = new List<RestorePoint>();
            if (Directory.Exists(Paths.Backups))
                foreach (var d in Directory.GetDirectories(Paths.Backups)) backups.AddRange(ListEntries(d));
            var sets = ListPreUpdateSets();
            long total = backups.Sum(b => b.Bytes) + sets.Sum(s => s.Entry.Long("bytes"));
            if (total <= cap) return;

            var perChar = backups.GroupBy(b => b.Character, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var b in backups.OrderBy(b => b.CreatedUtc))
            {
                if (total <= cap) return;
                if (perChar[b.Character] <= 2) continue;
                if (Delete(b.Dir, "backup (size cap)")) { total -= b.Bytes; perChar[b.Character]--; }
            }
            sets.Sort((a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));
            for (int i = 0; i < sets.Count - 1 && total > cap; i++)
                if (Delete(sets[i].Dir, "pre-update set (size cap)")) total -= sets[i].Entry.Long("bytes");
            if (total > cap) Log.Verbose($"Backups use {total / 1048576} MB, over MaxTotalSizeMB; the newest copies are always kept.");
        }

        // Only folders that carry a valid entry.txt reach here (listing filters the rest); unknown names are never deleted.
        private static bool Delete(string dir, string what)
        {
            Log.Info($"Pruning {what} {Short(dir)}");
            SizeCache.Remove(dir);
            try { FileOps.Discard(dir); return !Directory.Exists(dir); }
            catch (Exception e) { Log.Warn($"Could not prune {dir}: {e.Message}"); return false; }
        }

        internal static (int count, DateTime lastUtc) Summary(string character)
        {
            var list = ListEntries(Path.Combine(Paths.Backups, character));
            var last = list.Count == 0 ? default : list.Max(p => p.CreatedUtc);
            return (list.Count, last);
        }
    }
}
