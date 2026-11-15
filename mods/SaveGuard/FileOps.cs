using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace SaveGuard
{
    internal static class FileOps
    {
        // Copies src into dst (created), recursively, in parallel like vanilla's CopyDirectory, and waits for it.
        // skipTop: file names skipped at the top level only (case-insensitive). Keeps each file's last-write time,
        // which SlotCheck and the restore list rely on. Returns (files, bytes).
        internal static (int files, long bytes) CopyTree(string src, string dst, ICollection<string> skipTop = null)
        {
            var srcInfo = new DirectoryInfo(src);
            if (!srcInfo.Exists) throw new DirectoryNotFoundException("Source directory not found: " + src);
            var jobs = new List<(FileInfo file, string dest)>();
            Directory.CreateDirectory(dst);
            foreach (var f in srcInfo.GetFiles())
                if (skipTop == null || !Contains(skipTop, f.Name)) jobs.Add((f, Path.Combine(dst, f.Name)));
            CollectSub(srcInfo, dst, jobs);

            long bytes = 0;
            foreach (var j in jobs) bytes += j.file.Length;
#if SAVEGUARD_FAULTS
            int done = 0;
            bool killHalfway = Faults.Armed(Fault.KillDuringBackup) && dst.IndexOf(Path.DirectorySeparatorChar + "backups" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0;
#endif
            try
            {
                Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) }, j =>
                {
                    j.file.CopyTo(j.dest, false);
                    File.SetLastWriteTimeUtc(j.dest, j.file.LastWriteTimeUtc);
#if SAVEGUARD_FAULTS
                    if (killHalfway && Interlocked.Increment(ref done) == jobs.Count / 2) Faults.Kill(Fault.KillDuringBackup);
#endif
                });
            }
            catch (AggregateException ex)
            {
                ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();
            }
            return (jobs.Count, bytes);
        }

        private static void CollectSub(DirectoryInfo src, string dst, List<(FileInfo, string)> jobs)
        {
            foreach (var d in src.GetDirectories())
            {
                string sub = Path.Combine(dst, d.Name);
                Directory.CreateDirectory(sub);
                foreach (var f in d.GetFiles()) jobs.Add((f, Path.Combine(sub, f.Name)));
                CollectSub(d, sub, jobs);
            }
        }

        private static bool Contains(ICollection<string> names, string name)
        {
            foreach (var n in names) if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static (int files, long bytes) TreeBytes(string dir, ICollection<string> skipTop = null)
        {
            int files = 0; long bytes = 0;
            var info = new DirectoryInfo(dir);
            if (!info.Exists) return (0, 0);
            string top = info.FullName.TrimEnd('/', '\\');
            foreach (var f in info.GetFiles("*", SearchOption.AllDirectories))
            {
                if (skipTop != null && Contains(skipTop, f.Name) && string.Equals(f.DirectoryName, top, StringComparison.OrdinalIgnoreCase)) continue;
                files++; bytes += f.Length;
            }
            return (files, bytes);
        }

        // Free bytes on the volume holding path. Unknown (DriveInfo unsupported or throwing under Wine) = enough.
        internal static long FreeBytes(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                long free = new DriveInfo(root).AvailableFreeSpace;
                return free < 0 ? long.MaxValue : free;
            }
            catch (Exception e)
            {
                Log.WarnOnce("freebytes", $"Free disk space is unknown ({e.GetType().Name}); backups are not limited by it.");
                return long.MaxValue;
            }
        }

        internal static bool HasRoomFor(string path, long bytes)
        {
            long free = FreeBytes(path);
            if (free == long.MaxValue) return true;
            double need = bytes * 1.1 + Cfg.MinFreeSpaceMB.Value * 1024.0 * 1024.0;
            return free >= need;
        }

        // Rename into trash/<guid> (same volume as Root) and delete there on a ThreadPool thread.
        // Falls back to a synchronous delete when the rename is impossible (BackupFolder on another volume).
        internal static void Discard(string dir)
        {
            if (!Directory.Exists(dir)) return;
            string dst = null;
            try
            {
                Directory.CreateDirectory(Paths.Trash);
                dst = Path.Combine(Paths.Trash, Guid.NewGuid().ToString("N"));
                Directory.Move(dir, dst);
            }
            catch (Exception e)
            {
                Log.Verbose($"Rename to trash failed for {dir} ({e.Message}); deleting in place");
                SafeDeleteTree(dir);
                return;
            }
            QueueDelete(dst);
        }

        internal static void QueueTrashSweep()
        {
            if (!Directory.Exists(Paths.Trash)) return;
            foreach (var d in Directory.GetDirectories(Paths.Trash)) QueueDelete(d);
        }

        // The worker only ever touches folders under SaveGuard/trash/.
        private static void QueueDelete(string dir)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { SafeDeleteTree(dir); }
                catch (Exception e) { Log.Exception("trash delete", e); }
            });
        }

        // Deletes a tree file by file; a file that cannot be deleted is logged and left for the next attempt.
        internal static bool SafeDeleteTree(string dir)
        {
            if (!Directory.Exists(dir)) return true;
            bool ok = true;
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var attr = File.GetAttributes(f);
                        if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, attr & ~FileAttributes.ReadOnly);
                        File.Delete(f);
                    }
                    catch (Exception e) { ok = false; Log.Warn($"Could not delete {f}: {e.Message}"); }
                }
                if (ok) Directory.Delete(dir, true);
            }
            catch (Exception e) { ok = false; Log.Warn($"Could not delete {dir}: {e.Message}"); }
            return ok;
        }

        // Copies one file over another and keeps the source's last-write time.
        internal static void CopyFile(string src, string dst, bool overwrite)
        {
            File.Copy(src, dst, overwrite);
            File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src));
        }
    }
}
