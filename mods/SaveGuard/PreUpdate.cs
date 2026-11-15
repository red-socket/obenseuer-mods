using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SaveGuard
{
    // Pre-update backup (spec §8): the first time a new game version runs (or Save Guard runs at all), copy every
    // character's saves before any menu exists. Synchronous in Awake, after startup recovery, so the copy is consistent.
    internal static class PreUpdate
    {
        private static readonly string[] Skip = { "steam_autocloud.vdf" };

        internal static void RunIfNeeded()
        {
            var state = KvFile.Read(Paths.State);
            string last = state?["lastSeenGameVersion"];
            string now = Application.version;
            string kind = null;
            if (string.IsNullOrEmpty(last)) { if (Cfg.BackupOnFirstRun.Value) kind = "firstrun"; }
            else if (last != now && Cfg.PreUpdateEnabled.Value) kind = "preupdate";

            bool hasChars = Directory.Exists(Paths.Saves) && Directory.GetDirectories(Paths.Saves).Length > 0;
            if (kind == null || !hasChars)
            {
                if (last == now) Log.Info("Game version unchanged; no pre-update backup.");
                else Log.Info(hasChars ? $"Game version {last ?? "(first run)"} -> {now}; pre-update backup is off." : "No saves yet; nothing to back up.");
                RecordVersion(state, now);
                return;
            }

            var sw = Stopwatch.StartNew();
            var (files, bytes) = FileOps.TreeBytes(Paths.Saves, Skip);
            Directory.CreateDirectory(Paths.PreUpdate);
            string from = kind == "firstrun" ? "firstrun" : last;
            if (!FileOps.HasRoomFor(Paths.PreUpdate, bytes))
            {
                // The old version string is kept, so the next launch tries again.
                Log.Warn($"Not enough disk space to back up saves before {now} ({bytes / 1048576} MB needed plus {Cfg.MinFreeSpaceMB.Value} MB reserve).");
                GuardWindow.AddNotice("preupdate", "Not enough disk space to back up saves before the update.", true);
                return;
            }

            string dir = Paths.UniqueDir(Paths.PreUpdate, $"{Paths.Stamp(DateTime.Now)}_{Sanitize(from)}_to_{Sanitize(now)}");
            string partial = dir + Paths.Partial;
            var chars = Directory.GetDirectories(Paths.Saves).Select(Path.GetFileName).ToArray();
            int slots = chars.Sum(c => Directory.GetDirectories(Path.Combine(Paths.Saves, c)).Length);
            var (copied, copiedBytes) = FileOps.CopyTree(Paths.Saves, partial, Skip);

            var e = new Kv();
            e["kind"] = kind;
            e["createdUtc"] = Paths.Iso(DateTime.UtcNow);
            e["from"] = from;
            e["to"] = now;
            e["characters"] = string.Join("|", chars);
            e["slots"] = slots.ToString();
            e["files"] = copied.ToString();
            e["bytes"] = copiedBytes.ToString();
            e["runningVersion"] = now;
            e["complete"] = "1";
            KvFile.WriteAtomic(Path.Combine(partial, Paths.EntryFile), e);
            Directory.Move(partial, dir);

            // Only after the set is complete: record the version (a kill before this reruns the backup next launch).
            RecordVersion(state, now);
            BackupStore.PrunePreUpdate();
            Log.Info($"Pre-update backup {from} -> {now}: {chars.Length} character(s), {copiedBytes / 1048576} MB, {copied} files in {sw.Elapsed.TotalSeconds:0.0} s ({BackupStore.Short(dir)})");
            string msg = kind == "firstrun" ? "Save Guard backed up all saves on its first run." : $"Save Guard backed up all saves before the first run of game version {now}.";
            GuardWindow.AddNotice("preupdate", msg, false);
            Toasts.ShowOnMenu(msg);
        }

        private static void RecordVersion(Kv state, string version)
        {
            var s = state?.Clone() ?? new Kv();
            s["formatVersion"] = "1";
            s["lastSeenGameVersion"] = version;
            KvFile.WriteAtomic(Paths.State, s);
        }

        private static string Sanitize(string v)
        {
            var chars = (v ?? "unknown").Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' ? ch : '-').ToArray();
            return new string(chars);
        }
    }
}
