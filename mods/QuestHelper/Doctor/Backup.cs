using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace QuestHelper
{
    internal sealed class BackupResult
    {
        internal bool Ok;
        internal string Kind;    // "saveguard" | "own"
        internal string Path;
        internal string Error;
    }

    // The mandatory backup before any edit (spec §4.6 step 4). Source: SaveFolder.Current, the newest on-disk state of
    // this playthrough. With Save Guard (and UseSaveGuard), its AddRecovered makes the copy and lists it in its restore
    // window; a Save Guard failure refuses the edit rather than trying again on the same disk. Otherwise Quest Helper
    // copies the slot to <root>/<character>/<stamp>_<slot>_questhelper(.partial), checks it file by file, writes
    // questhelper.txt and renames it into place. Any failure deletes the partial copy and refuses.
    internal static class Backup
    {
        internal const string Marker = "questhelper.txt";
        private const string PartialSuffix = ".partial";

        internal static bool WillUseSaveGuard => Cfg.QuestDoctor.UseSaveGuard.Value && SaveGuardBridge.Usable;

        internal static string Root
        {
            get
            {
                string custom = (Cfg.QuestDoctor.BackupFolder.Value ?? "").Trim();
                string def = Path.Combine(Application.persistentDataPath, "QuestHelper", "backups");
                if (custom.Length == 0) return def;
                try
                {
                    string full = Path.GetFullPath(custom);
                    string saves = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "Saves"));
                    if (SaveFolder.Under(full + "/x", saves) || string.Equals(full.TrimEnd('/', '\\'), saves.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase))
                    {
                        Log.WarnOnce("backup root", "Backup: BackupFolder is inside Saves, which is refused; using the default folder.");
                        return def;
                    }
                    return full;
                }
                catch (Exception e)
                {
                    Log.WarnOnce("backup root", $"Backup: BackupFolder is not a usable path ({e.Message}); using the default folder.");
                    return def;
                }
            }
        }

        // Where the backup will go, for the window's Backup section.
        internal static string TargetText()
        {
            if (WillUseSaveGuard) return Strings.BackupTargetSaveGuard;
            return Strings.BackupTargetOwn(Root);
        }

        internal static BackupResult Make(string fixId)
        {
            var r = new BackupResult();
            var sw = Stopwatch.StartNew();
            try { RevealCamera.WaitForPendingMapWrite(); }
            catch (Exception e) { Log.Exception("map write wait", e); }

            string src = SaveFolder.Current(out string why);
            if (src == null)
            {
                r.Error = Strings.SaveFirst;
                Log.Warn($"Backup: refused, {why}.");
                return r;
            }
            string slot = Path.GetFileName(src.TrimEnd('/', '\\'));
            string character = Path.GetFileName(Path.GetDirectoryName(src.TrimEnd('/', '\\')));
            var (files, bytes) = Tree(src);

            if (WillUseSaveGuard)
            {
                try
                {
                    r.Path = SaveGuardBridge.AddRecovered(src, character, slot);
                    r.Kind = "saveguard";
                    r.Ok = !string.IsNullOrEmpty(r.Path) && Directory.Exists(r.Path);
                    if (!r.Ok) r.Error = Strings.BackupFailed("Save Guard returned no folder");
                    else
                    {
                        // The same check as our own copy: every file of the slot, byte count included (Save Guard's
                        // entry.txt is not part of the slot).
                        var (gFiles, gBytes) = Tree(r.Path, SaveGuardEntry);
                        if (gFiles != files || gBytes != bytes)
                        {
                            r.Ok = false;
                            r.Error = Strings.BackupFailed($"Save Guard's copy has {gFiles} files ({gBytes} bytes) but the save has {files} ({bytes} bytes)");
                            Log.Warn($"Backup: Save Guard's copy {r.Path} has {gFiles} files and {gBytes} bytes, the save {files} and {bytes}. Nothing was changed.");
                        }
                    }
                }
                catch (Exception e)
                {
                    r.Error = Strings.BackupFailed(e.Message);
                    Log.Warn($"Backup: Save Guard could not back up {character}/{slot}: {e.Message}. Nothing was changed.");
                    return r;
                }
                if (r.Ok) Log.Info($"Backup: {src} -> {r.Path} (Save Guard, checked, {bytes / 1048576.0:0.0} MB, {sw.ElapsedMilliseconds} ms)");
                return r;
            }

            string root = Root;
            string charDir = Path.Combine(root, character);
            string name = $"{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}_{slot}_questhelper";
            string final = Path.Combine(charDir, name);
            for (int i = 2; Directory.Exists(final) || Directory.Exists(final + PartialSuffix); i++) final = Path.Combine(charDir, name + "_" + i);
            string partial = final + PartialSuffix;
            try
            {
                Directory.CreateDirectory(charDir);
                long need = bytes + (long)Cfg.QuestDoctor.MinFreeSpaceMB.Value * 1048576L;
                long free = FreeSpace(charDir);
                if (free >= 0 && free < need)
                {
                    r.Error = Strings.BackupNoSpace(need / 1048576L, free / 1048576L);
                    Log.Warn($"Backup: refused, {free / 1048576L} MB free and {need / 1048576L} MB needed in {charDir}.");
                    return r;
                }
                CopyTree(src, partial);
                var (cFiles, cBytes) = Tree(partial);
                if (cFiles != files || cBytes != bytes)
                    throw new IOException($"the copy has {cFiles} files and {cBytes} bytes, the save {files} and {bytes}");
                File.WriteAllText(Path.Combine(partial, Marker), MarkerText(fixId, src, character, slot));
                Directory.Move(partial, final);
                r.Ok = true;
                r.Kind = "own";
                r.Path = final;
                Log.Info($"Backup: {src} -> {final} ({bytes / 1048576.0:0.0} MB, {sw.ElapsedMilliseconds} ms)");
                Prune(charDir);
            }
            catch (Exception e)
            {
                try { if (Directory.Exists(partial)) Directory.Delete(partial, true); }
                catch (Exception e2) { Log.Exception("partial cleanup", e2); }
                r.Ok = false;
                r.Error = Strings.BackupFailed(e.Message);
                Log.Warn($"Backup: copy of {src} failed: {e.Message}. Nothing was changed.");
            }
            return r;
        }

        private static string MarkerText(string fixId, string src, string character, string slot) =>
            "Quest Helper backup\n" +
            $"fix: {fixId}\n" +
            $"createdUtc: {DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)}\n" +
            $"game: {Application.version}\n" +
            $"source: {src}\n" +
            $"restore: quit to the main menu, then copy every file in this folder (except {Marker}) back into Saves/{character}/{slot}, replacing what is there.\n";

        private static long FreeSpace(string dir)
        {
            try
            {
                string rootPath = Path.GetPathRoot(Path.GetFullPath(dir));
                if (string.IsNullOrEmpty(rootPath)) return -1;
                return new DriveInfo(rootPath).AvailableFreeSpace;
            }
            catch (Exception e)
            {
                Log.WarnOnce("free space", $"Backup: free disk space could not be read ({e.Message}); the copy is checked instead.");
                return -1;
            }
        }

        // Save Guard's own index file inside its backup folders (SaveGuard/Paths.cs EntryFile).
        private const string SaveGuardEntry = "entry.txt";

        // Files and bytes under dir, leaving out our marker and, at the top level only, the extra file named (if any).
        private static (int files, long bytes) Tree(string dir, string topLevelExtra = null)
        {
            int n = 0;
            long b = 0;
            string top = Path.GetFullPath(dir).TrimEnd('/', '\\');
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(f), Marker, StringComparison.OrdinalIgnoreCase)) continue;
                if (topLevelExtra != null && string.Equals(Path.GetFileName(f), topLevelExtra, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetDirectoryName(Path.GetFullPath(f))?.TrimEnd('/', '\\'), top, StringComparison.OrdinalIgnoreCase)) continue;
                n++;
                b += new FileInfo(f).Length;
            }
            return (n, b);
        }

        private static void CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), false);
            foreach (var d in Directory.GetDirectories(src)) CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        // Only folders that carry our marker file are ever pruned, newest KeepOwnBackups kept.
        private static void Prune(string charDir)
        {
            try
            {
                var ours = Directory.GetDirectories(charDir)
                    .Where(d => !d.EndsWith(PartialSuffix, StringComparison.Ordinal) && File.Exists(Path.Combine(d, Marker)))
                    .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();
                foreach (var old in ours.Skip(Math.Max(1, Cfg.QuestDoctor.KeepOwnBackups.Value)))
                {
                    Directory.Delete(old, true);
                    Log.Info($"Backup: pruned {old}");
                }
            }
            catch (Exception e) { Log.Exception("backup prune", e); }
        }
    }
}
