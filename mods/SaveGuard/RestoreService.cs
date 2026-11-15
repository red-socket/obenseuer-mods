using System;
using System.IO;
using System.Text;

namespace SaveGuard
{
    // Restore a restore point into a slot (spec §7.3), main thread, synchronous, main menu only.
    // Staged copy first (works for a BackupFolder on another volume), then two renames in the same call.
    internal static class RestoreService
    {
        internal static bool InProgress;

        // Returns null on success, otherwise a message for the window.
        internal static string Restore(RestorePoint point, string targetSlot, bool allowIncomplete)
        {
            if (InProgress) return "A restore is already running.";
            if (SaveTxn.Unresolved() || KvFile.Read(Paths.RestoreJournal) != null) return "Save Guard has unfinished work. Restart the game first.";
            // LoadMenu.LoadCoroutine / LoadGameWithMigration read the slot folder over several frames.
            if (Vanilla.LoadInProgress()) return "The game is loading a save. Restore when it is done.";
            InProgress = true;
            string character = point.Character;
            string target = Paths.SlotDir(character, targetSlot);
            string staging = null;
            bool swapStarted = false;
            try
            {
                Directory.CreateDirectory(Paths.Staging);
                staging = Paths.UniqueDir(Paths.Staging, $"{Paths.Stamp(DateTime.Now)}_{targetSlot}");
                string partial = staging + Paths.Partial;
                var j = new Kv();
                j["phase"] = "staging";
                j["source"] = point.Dir;
                j["character"] = character;
                j["slot"] = point.Slot;
                j["target"] = target;
                j["targetSlot"] = targetSlot;
                j["staging"] = staging;
                KvFile.WriteAtomic(Paths.RestoreJournal, j);

                // 1-2: stage and verify the copy.
                var skip = new[] { Paths.EntryFile };
                var (srcFiles, srcBytes) = FileOps.TreeBytes(point.Dir, skip);
                FileOps.CopyTree(point.Dir, partial, skip);
                var (dstFiles, dstBytes) = FileOps.TreeBytes(partial);
                if (srcFiles != dstFiles || srcBytes != dstBytes)
                    return Abort(partial, $"The copy does not match the restore point ({dstFiles}/{srcFiles} files).");

                // 3: another slot of the same character: rewrite the header SaveName that load, save and delete use.
                if (!string.Equals(targetSlot, point.Slot, StringComparison.OrdinalIgnoreCase))
                {
                    string err = RewriteSaveName(Path.Combine(partial, "Info.tnmt"), point.Slot, targetSlot)
                              ?? RewriteSaveName(Path.Combine(partial, "Globals.tnmt"), point.Slot, targetSlot);
                    if (err != null) return Abort(partial, "Can't restore into another slot: " + err);
                }

                // 4: every .tnmt must parse.
                SlotCheck.Invalidate();
                var r = SlotCheck.Full(partial, true);
                if (!r.Ok && !(allowIncomplete && r.Status == SlotStatus.Damaged))
                    return Abort(partial, "The restore point fails the check: " + r);
                Directory.Move(partial, staging);

                // 5: swap.
                j["phase"] = "swap";
                string replaced = null;
                if (Directory.Exists(target))
                {
                    string charDir = Path.Combine(Paths.Recovered, character);
                    replaced = Paths.UniqueDir(charDir, $"{Paths.Stamp(DateTime.Now)}_{targetSlot}_replaced");
                    j["replaced"] = replaced;
                }
                swapStarted = true;
                KvFile.WriteAtomic(Paths.RestoreJournal, j);
                Directory.CreateDirectory(Path.Combine(Paths.Saves, character));
                if (replaced != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(replaced));
                    Directory.Move(target, replaced);
                }
#if SAVEGUARD_FAULTS
                Faults.Kill(Fault.KillDuringRestoreSwap);
#endif
                try { Directory.Move(staging, target); }
                catch (Exception e)
                {
                    if (replaced != null && !Directory.Exists(target)) Directory.Move(replaced, target);
                    throw new IOException("Could not move the restored slot into place: " + e.Message, e);
                }
                if (replaced != null) BackupStore.WriteEntryFor(replaced, "replaced", character, targetSlot, complete: true);

                // 6.
                KvFile.Delete(Paths.RestoreJournal);
                SlotCheck.Invalidate();
                Log.Info($"Restored {character}/{targetSlot} from {BackupStore.Short(point.Dir)}" + (replaced != null ? $"; previous slot kept as {BackupStore.Short(replaced)}" : ""));
                string hint = RefreshLoadMenu();
                BackupStore.PruneCharacter(character);
                GuardWindow.ScanMainMenu();
                Toasts.Show($"{Paths.SlotDisplay(targetSlot)} restored from {SlotCheck.FormatDate(point.SaveDate, point.SaveDateFromMtime)}.");
                return hint == null ? null : "!" + hint;
            }
            catch (Exception e)
            {
                Log.Error($"Restore of {character}/{targetSlot} failed: {e}");
                try
                {
                    // Before the swap nothing in Saves/ changed. During it, a target that is back in place means the
                    // swap was undone; otherwise the journal and staging stay for StartupRecover to finish the job.
                    if (staging != null) FileOps.SafeDeleteTree(staging + Paths.Partial);
                    if (!swapStarted || Directory.Exists(target))
                    {
                        if (staging != null) FileOps.SafeDeleteTree(staging);
                        KvFile.Delete(Paths.RestoreJournal);
                    }
                }
                catch (Exception e2) { Log.Exception("restore cleanup", e2); }
                return "Restore failed: " + e.Message;
            }
            finally
            {
                InProgress = false;
                SlotCheck.Invalidate();
            }
        }

        private static string Abort(string partial, string message)
        {
            Log.Warn("Restore aborted: " + message);
            FileOps.SafeDeleteTree(partial);
            KvFile.Delete(Paths.RestoreJournal);
            return message;
        }

        // The content after the optional BOM must start with {"SaveName":"<from>" (vanilla's header layout).
        private static string RewriteSaveName(string path, string from, string to)
        {
            if (!File.Exists(path)) return Path.GetFileName(path) + " is missing";
            byte[] bytes = File.ReadAllBytes(path);
            int bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            byte[] expected = Encoding.UTF8.GetBytes("{\"SaveName\":\"" + from + "\"");
            if (bytes.Length < bom + expected.Length) return Path.GetFileName(path) + " is too short";
            for (int i = 0; i < expected.Length; i++)
                if (bytes[bom + i] != expected[i]) return Path.GetFileName(path) + " does not start with the expected SaveName";
            byte[] replacement = Encoding.UTF8.GetBytes("{\"SaveName\":\"" + to + "\"");
            var mtime = File.GetLastWriteTimeUtc(path);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                fs.Write(bytes, 0, bom);
                fs.Write(replacement, 0, replacement.Length);
                fs.Write(bytes, bom + expected.Length, bytes.Length - bom - expected.Length);
                fs.Flush(true);
            }
            File.SetLastWriteTimeUtc(path, mtime);
            return null;
        }

        // Vanilla resets currentSaveInfo the same way (:406, :503) before rebuilding its lists.
        private static string RefreshLoadMenu()
        {
            try
            {
                var lm = LoadMenu.instance;
                if (lm == null || !Vanilla.IsMainMenu(lm)) return null;
                Vanilla.SetCurrentSaveInfo(lm, null);
                lm.Init();
                return null;
            }
            catch (Exception e)
            {
                Log.Exception("LoadMenu.Init after restore", e);
                return "Return to the title screen or restart to see the restored save.";
            }
        }

        // §7.4, in Awake before SaveTxn.StartupRecover. Empties staging/ afterwards.
        internal static void StartupRecover()
        {
            var j = KvFile.Read(Paths.RestoreJournal);
            if (j != null)
            {
                string phase = j["phase"], target = j["target"], staging = j["staging"], replaced = j["replaced"];
                string character = j["character"], targetSlot = j["targetSlot"];
                string who = $"{character}/{targetSlot}";
                bool stagingExists = !string.IsNullOrEmpty(staging) && Directory.Exists(staging);
                bool targetExists = !string.IsNullOrEmpty(target) && Directory.Exists(target);
                string notice;
                if (phase == "swap" && stagingExists && !targetExists)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    Directory.Move(staging, target);
                    notice = $"A restore of {Paths.SlotDisplay(targetSlot)} ({Paths.DisplayName(character)}) was interrupted. Save Guard finished it.";
                    Log.Info($"Restore completed at startup: {who}");
                }
                else if (phase == "swap" && !stagingExists)
                {
                    notice = $"The restore of {Paths.SlotDisplay(targetSlot)} ({Paths.DisplayName(character)}) had finished before the game closed.";
                    Log.Info($"Restore of {who} had finished before the game closed");
                }
                else
                {
                    notice = $"A restore of {Paths.SlotDisplay(targetSlot)} ({Paths.DisplayName(character)}) was interrupted before it changed anything. The slot is unchanged.";
                    Log.Info($"Interrupted restore of {who} discarded (phase={phase}); Saves/ untouched");
                }
                if (!string.IsNullOrEmpty(replaced) && Directory.Exists(replaced) && KvFile.Read(Path.Combine(replaced, Paths.EntryFile)) == null)
                    BackupStore.WriteEntryFor(replaced, "replaced", character, targetSlot, complete: true);
                GuardWindow.AddNotice("restore:" + who, notice, false);
                KvFile.Delete(Paths.RestoreJournal);
            }
            if (Directory.Exists(Paths.Staging))
                foreach (var d in Directory.GetDirectories(Paths.Staging)) FileOps.SafeDeleteTree(d);
        }
    }
}
