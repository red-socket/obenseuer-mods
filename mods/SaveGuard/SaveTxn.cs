using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SaveGuard
{
    // State of the SaveGame call in progress (opened by the P1 prefix, closed by the P1 finalizer).
    internal sealed class SaveCtx
    {
        internal string Kind;
        internal DateTime StartedUtc;
        internal int Nested;
        internal bool BeginAttempted;
        internal bool Begun;
        internal string Mode;             // move | copy | new
        internal string Target, Character, Slot, Level;
        internal Kv Journal;
    }

    // Crash-safe save writes (spec §4). The previous version of the target slot stays on disk (txn/kept)
    // until the new version has been checked; a failed save is rolled back, an interrupted one is resolved at startup.
    internal static class SaveTxn
    {
        internal static SaveCtx Ctx;
        private static readonly string[] Fixed = { "Info.tnmt", "Globals.tnmt", "Globals.dialogue" };

        internal static bool Unresolved() => KvFile.Read(Paths.SaveJournal) != null || Directory.Exists(Paths.Kept);

        // ---- Begin (§4.2), from the IsSameFolder postfix: headers are serialized, nothing on disk has changed yet ----

        internal static void Begin(SaveCtx c, string target, bool same)
        {
            var sw = Stopwatch.StartNew();
            string mode = null;
            bool moved = false;
            try
            {
                if (string.IsNullOrEmpty(target)) return;
                if (Unresolved())
                {
                    Log.Warn("Previous save transaction is unresolved; this save runs unprotected. It is resolved at the next launch.");
                    return;
                }
                if (Directory.Exists(Paths.KeptPartial)) FileOps.SafeDeleteTree(Paths.KeptPartial);
                if (Directory.Exists(Paths.KeptPartial)) { Log.Warn("txn/kept.partial could not be removed; this save runs unprotected."); return; }

                c.Target = target;
                c.Slot = Path.GetFileName(target);
                c.Character = Path.GetFileName(Path.GetDirectoryName(target));
                c.Level = Vanilla.LevelName;
                mode = !Directory.Exists(target) ? "new" : (same ? "copy" : "move");

                string last = SaveController.LastSaveFolderPath;
                if (mode != "copy" && !string.IsNullOrEmpty(last) && !Directory.Exists(last))
                    Log.Warn($"LastSaveFolderPath {last} no longer exists: the game will fail to copy from it (a vanilla bug). Save Guard rolls back if it does.");

                var j = new Kv();
                j["mode"] = mode;
                j["target"] = target;
                j["character"] = c.Character;
                j["slot"] = c.Slot;
                j["level"] = c.Level ?? "";
                j["kind"] = c.Kind;
                j["startedUtc"] = Paths.Iso(c.StartedUtc);
                j["gameVersion"] = UnityEngine.Application.version;
                j["phase"] = "preparing";
                if (mode == "copy")
                {
                    var files = RewriteSet(target, c.Level);
                    j["files"] = string.Join("|", files);
                    j["existed"] = string.Join("|", files.Select(f => File.Exists(Path.Combine(target, f)) ? "1" : "0"));
                }
                KvFile.WriteAtomic(Paths.SaveJournal, j);
                c.Journal = j;

                if (mode == "move")
                {
                    try
                    {
#if SAVEGUARD_FAULTS
                        if (Faults.Armed(Fault.FailSlotMove)) { Faults.Consume(); throw new IOException("SaveGuard fault: slot move refused"); }
#endif
                        Directory.Move(target, Paths.Kept);
                        moved = true;
                    }
                    catch (Exception e)
                    {
                        // An external program holds a file open: keep a copy instead; vanilla then deletes the original as usual.
                        Log.Warn($"Could not move {c.Character}/{c.Slot} aside ({e.Message}); copying it instead.");
                        FileOps.CopyTree(target, Paths.KeptPartial);
                        Directory.Move(Paths.KeptPartial, Paths.Kept);
                        j["moveFallback"] = "1";
                    }
                }
                else if (mode == "copy")
                {
                    Directory.CreateDirectory(Paths.KeptPartial);
                    foreach (var f in j["files"].Split('|'))
                    {
                        string src = Path.Combine(target, f);
                        if (File.Exists(src)) FileOps.CopyFile(src, Path.Combine(Paths.KeptPartial, f), false);
                    }
                    Directory.Move(Paths.KeptPartial, Paths.Kept);
                }

                // From here the previous version is safe in txn/kept: Commit or Rollback own the outcome,
                // even if the journal update below fails (StartupRecover handles a "preparing" journal with kept present).
                c.Mode = mode;
                c.Begun = true;
                j["phase"] = "kept";
                try { KvFile.WriteAtomic(Paths.SaveJournal, j); }
                catch (Exception e) { Log.Exception("journal phase=kept", e); }
                Log.Verbose($"Txn begin {c.Character}/{c.Slot} mode={mode} kind={c.Kind} ({sw.Elapsed.TotalMilliseconds:0.0} ms)");
            }
            catch (Exception e)
            {
                Log.Error($"Crash safety could not start for this save; it runs unprotected (as in vanilla): {e}");
                UndoBegin(c, mode, moved);
            }
        }

        // Info, Globals, <level>.tnmt, Globals.dialogue and every top-level *.png (the async map write).
        private static List<string> RewriteSet(string target, string level)
        {
            var files = new List<string>(Fixed);
            if (!string.IsNullOrEmpty(level)) files.Insert(2, level + ".tnmt");
            foreach (var png in Directory.GetFiles(target, "*.png"))
            {
                string name = Path.GetFileName(png);
                if (!files.Contains(name, StringComparer.OrdinalIgnoreCase)) files.Add(name);
            }
            return files;
        }

        private static void UndoBegin(SaveCtx c, string mode, bool moved)
        {
            try
            {
                if (moved && Directory.Exists(Paths.Kept) && !Directory.Exists(c.Target))
                    Directory.Move(Paths.Kept, c.Target);
                else if (!moved)
                {
                    FileOps.SafeDeleteTree(Paths.KeptPartial);
                    FileOps.SafeDeleteTree(Paths.Kept);
                }
                KvFile.Delete(Paths.SaveJournal);
            }
            catch (Exception e)
            {
                Log.Critical($"Could not undo the crash-safety step for {c.Character}/{c.Slot}: {e}. Save Guard resolves it at the next launch.");
            }
        }

        // ---- Commit (§4.3), from the P1 finalizer without exception ----

        // Returns false only when the written slot fails the check (no backup is taken of it then).
        internal static bool Commit(SaveCtx c)
        {
            var sw = Stopwatch.StartNew();
            try { RevealCamera.WaitForPendingMapWrite(); } catch (Exception e) { Log.Exception("WaitForPendingMapWrite", e); }
            SlotCheck.Invalidate();
            var r = SlotCheck.StructuralRewritten(c.Target, c.Level);
            if (r.Ok)
            {
                if (Directory.Exists(Paths.Kept)) FileOps.Discard(Paths.Kept);
                KvFile.Delete(Paths.SaveJournal);
                Log.Verbose($"Txn commit {c.Character}/{c.Slot} mode={c.Mode} ({sw.Elapsed.TotalMilliseconds:0.0} ms)");
                return true;
            }

            // Should never happen. No rollback: vanilla is about to use the new files (a door loads them right away).
            Log.Error($"The save just written to {c.Character}/{c.Slot} looks incomplete ({r}). The previous version is kept in Save Guard's recovered list.");
            if (Directory.Exists(Paths.Kept))
            {
                try { KeepPreviousAsRecovered(c.Mode, c.Target, c.Character, c.Slot, c.Journal); }
                catch (Exception e)
                {
                    // The rename into recovered/ failed. The journal stays, so the next launch resolves kept/.
                    Log.Exception("keeping the previous version after a failed check", e);
                    Toasts.Show("Save Guard: the new save looks incomplete, and the previous version could not be moved to the recovered list. See the BepInEx log.", 12f);
                    return false;
                }
                PruneRecovered(c.Character);
            }
            KvFile.Delete(Paths.SaveJournal);
            string key = GuardWindow.KeyName();
            Toasts.Show($"Save Guard: the new save looks incomplete. The previous version is kept. Restore it from the main menu ({key}).", 12f);
            GuardWindow.AddNotice("commit:" + c.Character + "/" + c.Slot,
                $"The save of {Paths.SlotDisplay(c.Slot)} ({Paths.DisplayName(c.Character)}) looked incomplete. The previous version is in the recovered list. Press {GuardWindow.KeyHole} to restore.", true);
            return false;
        }

        // kept -> recovered/<char>/<stamp>_<slot>_previous as a complete slot (§4.5), then kept is gone.
        // Copy mode needs room for a full slot copy; without it (or if the copy fails) the kept rewrite set is renamed
        // into recovered/ as an incomplete entry instead, which needs no disk space. Throws only if that rename fails.
        private static void KeepPreviousAsRecovered(string mode, string target, string character, string slot, Kv journal)
        {
            if (!Directory.Exists(Paths.Kept)) return;
            if (mode != "copy")
            {
                BackupStore.AddRecovered(Paths.Kept, character, slot, "previous", move: true, complete: true);
                return;
            }
            try
            {
                Compose(target, journal, character, slot, "previous");
            }
            catch (Exception e)
            {
                if (!Directory.Exists(Paths.Kept)) throw;
                Log.Warn($"Could not build a complete copy of the previous {character}/{slot} ({e.Message}); keeping only its rewritten files.");
                BackupStore.AddRecovered(Paths.Kept, character, slot, "previous", move: true, complete: false);
            }
        }

        // Copy-mode helper (§4.5): current target + the kept rewrite set - files that did not exist before.
        private static void Compose(string target, Kv journal, string character, string slot, string kind)
        {
            string charDir = Path.Combine(Paths.Recovered, character);
            Directory.CreateDirectory(charDir);
            var (_, bytes) = FileOps.TreeBytes(target);
            if (!FileOps.HasRoomFor(charDir, bytes))
                throw new IOException($"not enough free disk space ({bytes / 1048576.0:0.0} MB needed plus {Cfg.MinFreeSpaceMB.Value} MB reserve)");
            string dir = Paths.UniqueDir(charDir, $"{Paths.Stamp(DateTime.Now)}_{slot}_{kind}");
            string partial = dir + Paths.Partial;
            try
            {
                FileOps.CopyTree(target, partial);
                var files = (journal?["files"] ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                var existed = (journal?["existed"] ?? "").Split('|');
                for (int i = 0; i < files.Length; i++)
                {
                    string kept = Path.Combine(Paths.Kept, files[i]);
                    string dst = Path.Combine(partial, files[i]);
                    if (File.Exists(kept)) FileOps.CopyFile(kept, dst, true);
                    else if (i < existed.Length && existed[i] == "0" && File.Exists(dst)) File.Delete(dst);
                }
                BackupStore.WriteEntryFor(partial, kind, character, slot, complete: true);
                Directory.Move(partial, dir);
            }
            catch
            {
                FileOps.SafeDeleteTree(partial);
                throw;
            }
            FileOps.Discard(Paths.Kept);
            Log.Info($"Kept the previous {character}/{slot} as {BackupStore.Short(dir)}");
        }

        private static void PruneRecovered(string character)
        {
            try { BackupStore.PruneRecovered(character); }
            catch (Exception e) { Log.Exception("prune recovered", e); }
        }

        // The new version is complete when every rewritten file exists, was written by this save (mtime at or after
        // startedUtc, 2 s tolerance) and passes FullRewritten. Scene files the save only copied are not judged.
        private static bool NewVersionComplete(string target, string level, DateTime startedUtc, List<string> reasons)
        {
            if (!Directory.Exists(target)) { reasons?.Add("slot folder missing"); return false; }
            bool ok = true;
            foreach (var f in new[] { "Info.tnmt", "Globals.tnmt", (level ?? "") + ".tnmt", "Globals.dialogue" })
            {
                if (f == ".tnmt") continue;
                string p = Path.Combine(target, f);
                if (!File.Exists(p)) { ok = false; reasons?.Add(f + " missing"); }
                else if (File.GetLastWriteTimeUtc(p) < startedUtc.AddSeconds(-2)) { ok = false; reasons?.Add(f + " not rewritten"); }
            }
            if (!ok) return false;
            var r = SlotCheck.FullRewritten(target, level);
            if (!r.Ok) { reasons?.Add(r.ToString()); return false; }
            return true;
        }

        // ---- Rollback (§4.4), from the P1 finalizer with an exception ----

        internal static void Rollback(SaveCtx c, Exception error)
        {
            try { RevealCamera.WaitForPendingMapWrite(); } catch (Exception e) { Log.Exception("WaitForPendingMapWrite", e); }
            string slot = Paths.SlotDisplay(c.Slot);

            // Thrown after every file was written (a SavingDone subscriber, an OnSavingFile handler): vanilla would
            // leave a complete, loadable save, so it stays and the previous version goes to the recovered list.
            bool complete = false;
            try { SlotCheck.Invalidate(); complete = NewVersionComplete(c.Target, c.Level, c.StartedUtc, null); }
            catch (Exception e) { Log.Exception("completeness check", e); }
            if (complete)
            {
                // mode=new: the slot did not exist before this save, so there is no previous version to keep or mention.
                bool hadPrevious = c.Mode != "new";
                try
                {
                    KeepPreviousAsRecovered(c.Mode, c.Target, c.Character, c.Slot, c.Journal);
                    KvFile.Delete(Paths.SaveJournal);
                    Log.Error($"SaveGame threw {error.GetType().Name}: {error.Message} after {c.Character}/{c.Slot} was completely written; " +
                              (hadPrevious ? "the new save is kept and the previous version is in recovered/" : "the new save is kept (the slot was new)") +
                              $" (mode={c.Mode})");
                }
                catch (Exception e)
                {
                    Log.Critical($"SaveGame threw {error.GetType().Name}: {error.Message}; the new {c.Character}/{c.Slot} is complete, but keeping the previous " +
                                 $"version failed: {e}. Save Guard finishes it at the next launch; later saves in this session run unprotected.");
                }
                SlotCheck.Invalidate();
                PruneRecovered(c.Character);
                // Whole literals per case, not a spliced sentence: PT-BR translates these by literal.
                GuardWindow.ShowFailureNotice(c.Kind == "door"
                    ? (hadPrevious
                        ? $"The game hit an error while changing area, after {slot} was fully saved. Save Guard kept that new save. " +
                          "The previous version is in Save Guard's recovered list. The game is stuck on this screen. This is a game problem, not a Save Guard one. " +
                          $"Quit with Alt+F4, then load {slot} to continue."
                        : $"The game hit an error while changing area, after {slot} was fully saved. Save Guard kept that new save. " +
                          "The game is stuck on this screen. This is a game problem, not a Save Guard one. " +
                          $"Quit with Alt+F4, then load {slot} to continue.")
                    : (hadPrevious
                        ? $"The game hit an error after {slot} was fully saved. Save Guard kept the new save. The previous version is in " +
                          "Save Guard's recovered list. See the BepInEx log."
                        : $"The game hit an error after {slot} was fully saved. Save Guard kept the new save. See the BepInEx log."));
                return;
            }

            try
            {
                var j = c.Journal ?? KvFile.Read(Paths.SaveJournal) ?? new Kv();
                j["phase"] = "rollback";
                try { KvFile.WriteAtomic(Paths.SaveJournal, j); } catch (Exception e) { Log.Exception("journal phase=rollback", e); }
                UndoSave(j, c.Mode, c.Target, c.Character, c.Slot, c.StartedUtc, startup: false);
                KvFile.Delete(Paths.SaveJournal);
                SlotCheck.Invalidate();
                Log.Error($"SaveGame threw {error.GetType().Name}: {error.Message}; rolled back {c.Character}/{c.Slot} (mode={c.Mode})");
            }
            catch (Exception e)
            {
                SlotCheck.Invalidate();
                Log.Critical($"SaveGame threw {error.GetType().Name}: {error.Message}; rolling back {c.Character}/{c.Slot} failed: {e}. " +
                             "Save Guard finishes it at the next launch; later saves in this session run unprotected.");
            }
            PruneRecovered(c.Character);
            GuardWindow.ShowFailureNotice(c.Kind == "door"
                ? $"Saving failed while changing area. Save Guard put back the previous {slot}, so no save file was damaged. " +
                  "The game is stuck on this screen. This is a game problem, not a Save Guard one. Quit with Alt+F4. Progress since your last save could not be saved."
                : $"Saving failed. Save Guard put back the previous {slot}. See the BepInEx log.");
        }

        // Rollback steps 3 (§4.4), idempotent so StartupRecover can rerun them: every step whose source is gone is skipped.
        // Setting the failed version aside is best effort (it is incomplete anyway, and the most common cause of a
        // failed save, a full disk, also makes a copy fail); putting the previous version back always runs after it.
        // Returns whether the failed version is in recovered/ afterwards.
        private static bool UndoSave(Kv j, string mode, string target, string character, string slot, DateTime startedUtc, bool startup)
        {
            var extra = new Kv();
            extra["txnStartedUtc"] = Paths.Iso(startedUtc);
            bool failedExists = startup && j["phase"] == "rollback" && HasFailedEntry(character, startedUtc);
            bool kept = failedExists;
            switch (mode)
            {
                case "move":
                    if (j.Bool("moveFallback") && Directory.Exists(target) && Directory.Exists(Paths.Kept) && OriginalIntact(target, startedUtc))
                    {
                        // Vanilla never deleted the original (the reason the move failed): it is still the previous version.
                        FileOps.Discard(Paths.Kept);
                        return false;
                    }
                    // A rename never leaves both a _failed entry and the target behind, so no duplicate check is needed here.
                    if (Directory.Exists(target) && Directory.Exists(Paths.Kept))
                    {
                        try { BackupStore.AddRecovered(target, character, slot, "failed", move: true, complete: false, extra); kept = true; }
                        catch (Exception e)
                        {
                            // The incomplete version is in the way of the complete previous one: drop it.
                            Log.Warn($"Could not set the failed {character}/{slot} aside ({e.Message}); deleting it to put the previous version back.");
                            FileOps.Discard(target);
                        }
                    }
                    if (Directory.Exists(Paths.Kept) && !Directory.Exists(target)) Directory.Move(Paths.Kept, target);
                    break;

                case "copy":
                    if (!Directory.Exists(Paths.Kept)) break;
                    if (!Directory.Exists(target))
                    {
                        // Something else removed the slot: kept is only the rewrite set, so it cannot stand in for it.
                        BackupStore.AddRecovered(Paths.Kept, character, slot, "previous", move: true, complete: false, extra);
                        break;
                    }
                    if (!failedExists) kept = SetAsideFailedCopy(target, character, slot, extra);
                    RestoreRewriteSet(j, target);
                    FileOps.Discard(Paths.Kept);
                    break;

                case "new":
                    // The menu then shows "Empty Slot" rather than a slot that fails to load.
                    if (Directory.Exists(target) && !failedExists)
                    {
                        try { BackupStore.AddRecovered(target, character, slot, "failed", move: true, complete: false, extra); kept = true; }
                        catch (Exception e) { Log.Warn($"Could not move the failed new slot {character}/{slot} out of Saves ({e.Message}); it stays as the game left it."); }
                    }
                    break;
            }
            return kept;
        }

        // Copy mode: a full copy of the failed slot into recovered/, skipped when the disk has no room for it.
        private static bool SetAsideFailedCopy(string target, string character, string slot, Kv extra)
        {
            try
            {
                var (_, bytes) = FileOps.TreeBytes(target);
                Directory.CreateDirectory(Paths.Recovered);
                if (!FileOps.HasRoomFor(Paths.Recovered, bytes))
                {
                    Log.Warn($"Not enough free disk space to keep the failed {character}/{slot}; it is not kept.");
                    return false;
                }
                BackupStore.AddRecovered(target, character, slot, "failed", move: false, complete: false, extra);
                return true;
            }
            catch (Exception e) { Log.Warn($"Could not keep a copy of the failed {character}/{slot} ({e.Message}); it is not kept."); return false; }
        }

        // Puts the kept rewrite set back over target by rename (same volume, needs no disk space) and deletes the
        // rewrite-set files that did not exist before. Rerunnable: a file already moved back is no longer in kept.
        private static void RestoreRewriteSet(Kv j, string target)
        {
            var files = (j["files"] ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var existed = (j["existed"] ?? "").Split('|');
            for (int i = 0; i < files.Length; i++)
            {
                string kept = Path.Combine(Paths.Kept, files[i]);
                string dst = Path.Combine(target, files[i]);
                if (File.Exists(kept))
                {
                    try
                    {
                        if (File.Exists(dst)) File.Delete(dst);
                        File.Move(kept, dst);
                    }
                    catch (Exception e)
                    {
                        Log.Verbose($"Rename of kept {files[i]} failed ({e.Message}); copying it");
                        FileOps.CopyFile(kept, dst, true);
                    }
                }
                else if (i < existed.Length && existed[i] == "0" && File.Exists(dst)) File.Delete(dst);
            }
        }

        // The original of a move-fallback is intact when it parses, predates the save and still has every file kept has.
        private static bool OriginalIntact(string target, DateTime startedUtc)
        {
            try
            {
                if (!SlotCheck.Full(target, false).Ok) return false;
                if (File.GetLastWriteTimeUtc(Path.Combine(target, "Info.tnmt")) >= startedUtc) return false;
                var keptFiles = Directory.GetFiles(Paths.Kept, "*", SearchOption.AllDirectories).Select(f => f.Substring(Paths.Kept.Length));
                var targetFiles = new HashSet<string>(Directory.GetFiles(target, "*", SearchOption.AllDirectories).Select(f => f.Substring(target.Length)), StringComparer.OrdinalIgnoreCase);
                return keptFiles.All(targetFiles.Contains);
            }
            catch (Exception) { return false; }
        }

        private static bool HasFailedEntry(string character, DateTime startedUtc)
        {
            string iso = Paths.Iso(startedUtc);
            string dir = Path.Combine(Paths.Recovered, character);
            if (!Directory.Exists(dir)) return false;
            foreach (var d in Directory.GetDirectories(dir))
            {
                var e = KvFile.Read(Path.Combine(d, Paths.EntryFile));
                if (e != null && e["kind"] == "failed" && e["txnStartedUtc"] == iso) return true;
            }
            return false;
        }

        // ---- StartupRecover (§4.6), in Awake before any menu or load ----

        internal static void StartupRecover()
        {
            var j = KvFile.Read(Paths.SaveJournal);
            if (j == null)
            {
                FileOps.SafeDeleteTree(Paths.KeptPartial);
                if (Directory.Exists(Paths.Kept))
                {
                    Directory.CreateDirectory(Paths.Orphans);
                    string dst = Paths.UniqueDir(Paths.Orphans, Paths.Stamp(DateTime.Now));
                    Directory.Move(Paths.Kept, dst);
                    Log.Warn($"Found txn/kept without a journal; moved it to {BackupStore.Short(dst)} (not deleted).");
                }
                KvFile.Delete(Paths.SaveJournal);
                return;
            }

            string mode = j["mode"], target = j["target"], character = j["character"], slot = j["slot"], level = j["level"];
            DateTime started = Paths.ParseIso(j["startedUtc"]);
            string who = $"{character}/{slot}";
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(mode))
            {
                Log.Warn("Save journal without target; discarded.");
                KvFile.Delete(Paths.SaveJournal);
                return;
            }

            if (!Directory.Exists(Paths.Kept))
            {
                // Crashed before the keep step finished, or after Commit's rename: the slot in Saves/ is the live version.
                if (mode == "new" && Directory.Exists(target) && !NewVersionComplete(target, level, started, null))
                {
                    var extra = new Kv(); extra["txnStartedUtc"] = Paths.Iso(started);
                    BackupStore.AddRecovered(target, character, slot, "failed", move: true, complete: false, extra);
                    Notice(who, $"The game closed while saving the new slot {Paths.SlotDisplay(slot)} ({Paths.DisplayName(character)}). The incomplete save is in the recovered list.");
                    Log.Info($"Interrupted first save of {who} moved to recovered");
                }
                FileOps.SafeDeleteTree(Paths.KeptPartial);
                KvFile.Delete(Paths.SaveJournal);
                return;
            }

            // Same criterion as Commit and Rollback: only the files this save rewrote decide (§4.6, review fix).
            var reasons = new List<string>();
            bool newOk = NewVersionComplete(target, level, started, reasons);

            string prevDate = DateOfKept(mode, target);
            if (newOk)
            {
                KeepPreviousAsRecovered(mode, target, character, slot, j);
                Notice(who, $"The game closed while saving {Paths.SlotDisplay(slot)} ({Paths.DisplayName(character)}), but the save is complete. The previous version is in Save Guard's recovered list.");
                Log.Info($"Interrupted save of {who} completed: the new version is complete; the previous one is in recovered/");
            }
            else if (Cfg.RollBackInterruptedSaves.Value || !Directory.Exists(target))
            {
                // A missing target is always put back: there is nothing to compare and nothing to lose.
                bool failedKept = UndoSave(j, mode, target, character, slot, started, startup: true);
                Notice(who, $"The game closed while saving {Paths.SlotDisplay(slot)} ({Paths.DisplayName(character)}). Save Guard put back the previous version ({prevDate})." +
                            (failedKept ? " The incomplete save is in the recovered list." : ""));
                Log.Info($"Interrupted save of {who} rolled back (mode={mode}; {string.Join(", ", reasons)})");
            }
            else
            {
                KeepPreviousAsRecovered(mode, target, character, slot, j);
                Notice(who, $"The game closed while saving {Paths.SlotDisplay(slot)} ({Paths.DisplayName(character)}), so it may be damaged. Restore it with {GuardWindow.KeyHole}.");
                Log.Info($"Interrupted save of {who} left in place (RollBackInterruptedSaves=false; {string.Join(", ", reasons)})");
            }
            KvFile.Delete(Paths.SaveJournal);
            SlotCheck.Invalidate();
        }

        private static string DateOfKept(string mode, string target)
        {
            try
            {
                // Move mode: kept is the whole previous slot. Copy mode: kept holds the previous Info.tnmt.
                var h = SlotCheck.ReadHeader(Paths.Kept);
                if (h != null) return SlotCheck.FormatDate(h.Date, h.DateFromMtime);
            }
            catch { }
            return "earlier date";
        }

        private static void Notice(string who, string text)
        {
            GuardWindow.AddNotice("txn:" + who, text, false);
            Toasts.ShowOnMenu("Save Guard: " + text.Replace(GuardWindow.KeyHole, GuardWindow.KeyName()));
        }
    }
}
