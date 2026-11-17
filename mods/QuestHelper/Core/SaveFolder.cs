using System;
using System.IO;
using UnityEngine;

namespace QuestHelper
{
    // The newest on-disk folder of this playthrough (spec §4.6 step 4): whichever is later of
    // SaveController.LastSaveFolderPath at LoadingDone and GetCurrentSavePath() at SavingDone. That folder is the state
    // the player would get back by loading, so it is the restore point a backup must copy before an edit.
    // LastSaveFolderPath is only set by a load from disk (SRC/SaveController.cs:1622, after PlayerWillLoadGame), so it
    // is taken at LoadingDone only after such a load: a door's in-memory reload must not make an older folder "newer"
    // than the autosave the door just wrote.
    internal static class SaveFolder
    {
        private static string _loaded, _written;
        private static DateTime _loadedAt, _writtenAt;
        private static bool _fileLoad;

        internal static void OnFileLoadStarted() => _fileLoad = true;

        internal static void OnLoadingDone(string path)
        {
            if (!_fileLoad) return;
            _fileLoad = false;
            OnLoaded(path);
        }

        internal static void OnLoaded(string path)
        {
            _loaded = string.IsNullOrEmpty(path) ? null : path;
            _loadedAt = DateTime.UtcNow;
        }

        internal static void OnWritten(string path)
        {
            _written = string.IsNullOrEmpty(path) ? null : path;
            _writtenAt = DateTime.UtcNow;
        }

        // Back at the main menu: the next game may be another character.
        internal static void Reset()
        {
            _fileLoad = false;
            _loaded = _written = null;
            _loadedAt = _writtenAt = default;
        }

        internal static string Raw
        {
            get
            {
                if (_written != null && (_loaded == null || _writtenAt >= _loadedAt)) return _written;
                return _loaded;
            }
        }

        // The folder, if it exists, holds Info.tnmt and lies under persistentDataPath/Saves; otherwise null with a reason.
        internal static string Current(out string why)
        {
            why = null;
            string raw = Raw;
            if (string.IsNullOrEmpty(raw)) { why = "no save folder known yet"; return null; }
            try
            {
                string full = Path.GetFullPath(raw);
                string saves = Path.GetFullPath(Path.Combine(Application.persistentDataPath, "Saves"));
                if (!Under(full, saves)) { why = "the save folder is outside Saves"; return null; }
                if (!Directory.Exists(full)) { why = "the save folder is missing"; return null; }
                if (!File.Exists(Path.Combine(full, "Info.tnmt"))) { why = "the save folder has no Info.tnmt"; return null; }
                return full;
            }
            catch (Exception e)
            {
                why = e.Message;
                return null;
            }
        }

        internal static bool Under(string path, string root)
        {
            string p = path.Replace('\\', '/').TrimEnd('/') + "/";
            string r = root.Replace('\\', '/').TrimEnd('/') + "/";
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase) && p.Length > r.Length;
        }
    }
}
