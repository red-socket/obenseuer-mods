using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SaveGuard
{
    // Disk layout (spec §3). Everything lives under persistentDataPath/SaveGuard, a sibling of Saves/,
    // so moves between Saves/ and txn/, recovered/, staging/ are renames on the same volume.
    internal static class Paths
    {
        internal const string Partial = ".partial";
        internal const string EntryFile = "entry.txt";

        internal static string PDP;
        internal static string Saves;
        internal static string Root;
        internal static string Txn;
        internal static string SaveJournal;
        internal static string Kept;
        internal static string KeptPartial;
        internal static string RestoreJournal;
        internal static string Staging;
        internal static string Trash;
        internal static string Recovered;
        internal static string Orphans;
        internal static string State;
        internal static string BackupBase;
        internal static string Backups;
        internal static string PreUpdate;

        // The 12 slot folders vanilla knows (GetCharacters probes Slot1..Slot10 and Autosave; GetOldestAutosave uses Autosave2).
        internal static readonly string[] SlotNames =
            { "Autosave", "Autosave2", "Slot1", "Slot2", "Slot3", "Slot4", "Slot5", "Slot6", "Slot7", "Slot8", "Slot9", "Slot10" };

        private const string Readme =
@"Save Guard (Obenseuer mod) keeps its data here. The game never reads this folder, and it is
outside Saves/, so Steam Cloud does not sync it.

  backups/<Character>/   rotating copies of save slots, taken after saves
  preupdate/             copies of every character's saves, taken the first time a new game version ran
  recovered/<Character>/ slot versions Save Guard set aside: _failed (an interrupted or failed save),
                         _previous (the version before the last save, kept when that save was
                         interrupted or looked incomplete), _replaced (a slot overwritten by a restore)
  txn/                   the save in progress; normally empty
  orphans/               leftovers Save Guard could not match to a save; never deleted automatically
  staging/, trash/       temporary; emptied at every launch

Each entry is a slot folder with an entry.txt that describes it. If entry.txt says complete=0,
the entry is incomplete: it holds only some of the slot's files.
To restore a complete entry by hand while the game is closed: delete Saves/<Character>/<Slot>/,
copy the entry's folder there, and delete entry.txt from the copy.
To restore into a different slot by hand, also change the leading ""SaveName"":""..."" in Info.tnmt
and Globals.tnmt to the new slot name. The Save Guard window does this for you. Open it with the
Save Guard hotkey in the main menu (setting UI.ToggleWindow, default F6).

If the game crashed during a save and you removed the mod before starting the game again, the
previous version of that slot is in txn/kept/ (txn/save.txt names the slot as target=...):
  - If txn/kept/ holds only a few files (Info.tnmt, Globals.tnmt, Globals.dialogue, one level file,
    maybe a .png), the save went into the slot you had loaded and only those files were rewritten.
    Copy these files into Saves/<Character>/<Slot>/ and overwrite. Do not delete the slot folder:
    it holds every other area of that save.
  - Otherwise txn/kept/ is the whole previous slot: delete Saves/<Character>/<Slot>/ and copy
    txn/kept/ there under the slot's name.

After removing the mod, this folder is safe to delete.
";

        internal static void Init()
        {
            PDP = Application.persistentDataPath;
            Saves = Path.Combine(PDP, "Saves");
            Root = Path.Combine(PDP, "SaveGuard");
            Txn = Path.Combine(Root, "txn");
            SaveJournal = Path.Combine(Txn, "save.txt");
            Kept = Path.Combine(Txn, "kept");
            KeptPartial = Path.Combine(Txn, "kept" + Partial);
            RestoreJournal = Path.Combine(Txn, "restore.txt");
            Staging = Path.Combine(Root, "staging");
            Trash = Path.Combine(Root, "trash");
            Recovered = Path.Combine(Root, "recovered");
            Orphans = Path.Combine(Root, "orphans");
            State = Path.Combine(Root, "state.txt");

            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Txn);
            string readme = Path.Combine(Root, "README.txt");
            try
            {
                if (!File.Exists(readme) || File.ReadAllText(readme) != Readme) File.WriteAllText(readme, Readme);
            }
            catch (Exception e) { Log.Warn($"Could not write {readme}: {e.Message}"); }

            BackupBase = Root;
            string custom = (Cfg.BackupFolder.Value ?? "").Trim();
            if (custom.Length > 0)
            {
                try
                {
                    string full = Path.GetFullPath(custom);
                    if (IsInsideSaves(full))
                        Log.Error($"BackupFolder '{custom}' is inside Saves/; using {Root} instead.");
                    else
                    {
                        Directory.CreateDirectory(full);
                        BackupBase = full;
                    }
                }
                catch (Exception e) { Log.Error($"BackupFolder '{custom}' is not usable ({e.Message}); using {Root} instead."); }
            }
            Backups = Path.Combine(BackupBase, "backups");
            PreUpdate = Path.Combine(BackupBase, "preupdate");

            // Leftovers of a killed process: half-copied entries and staging folders never become visible.
            DeletePartials(Root);
            if (!SameDir(BackupBase, Root)) DeletePartials(BackupBase);
            // Complete staging folders are left for RestoreService.StartupRecover: a restore killed between its two
            // renames is finished from there. It empties staging/ afterwards.
            FileOps.QueueTrashSweep();
        }

        // *.partial folders at the top two levels below each store (store/<Char>/<name>.partial, preupdate/<set>.partial).
        private static void DeletePartials(string baseDir)
        {
            foreach (var store in new[] { "backups", "recovered", "preupdate", "staging", "txn" })
            {
                string s = Path.Combine(baseDir, store);
                if (!Directory.Exists(s)) continue;
                foreach (var d in Directory.GetDirectories(s))
                {
                    if (d.EndsWith(Partial, StringComparison.OrdinalIgnoreCase)) { DeletePartial(d); continue; }
                    if (store == "txn" || store == "staging") continue;
                    foreach (var dd in Directory.GetDirectories(d))
                        if (dd.EndsWith(Partial, StringComparison.OrdinalIgnoreCase)) DeletePartial(dd);
                }
            }
        }

        private static void DeletePartial(string d)
        {
            // txn/kept.partial is only ever a copy (the original stays in Saves/), so it is safe to drop too.
            Log.Info($"Removing incomplete {d}");
            FileOps.SafeDeleteTree(d);
        }

        internal static bool IsInsideSaves(string path)
        {
            string full = Path.GetFullPath(path).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
            string saves = Path.GetFullPath(Saves).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
            return full.StartsWith(saves, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool SameDir(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        internal static string SlotDir(string character, string slot) => Path.Combine(Path.Combine(Saves, character), slot);

        internal static string Stamp(DateTime local) => local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        // parent/<name> that exists neither as a folder nor as its .partial; same-second clashes get _2, _3, ...
        internal static string UniqueDir(string parent, string name)
        {
            for (int n = 1; ; n++)
            {
                string candidate = Path.Combine(parent, n == 1 ? name : name + "_" + n);
                if (!Directory.Exists(candidate) && !Directory.Exists(candidate + Partial)) return candidate;
            }
        }

        internal static string Iso(DateTime utc) => utc.ToString("o", CultureInfo.InvariantCulture);

        internal static DateTime ParseIso(string s)
        {
            if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d))
                return d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d;
            return default;
        }

        internal static string DisplayName(string folder) => (folder ?? "").Replace('_', ' ');

        internal static string SlotDisplay(string slot)
        {
            if (string.Equals(slot, "Autosave", StringComparison.OrdinalIgnoreCase)) return "Autosave 1";
            if (string.Equals(slot, "Autosave2", StringComparison.OrdinalIgnoreCase)) return "Autosave 2";
            if (slot != null && slot.StartsWith("Slot", StringComparison.Ordinal)) return "Slot " + slot.Substring(4);
            return slot;
        }
    }
}
