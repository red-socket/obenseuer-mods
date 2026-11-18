using System;
using UnityEngine;

namespace MessageLog
{
    // Which save the log in memory belongs to (spec §3, §7.4). Only the game's own static events, no save-path patches.
    // Every handler is wrapped (Plugin.Guard): SavingDone and LoadingDone are invoked unguarded (SRC/SaveController.cs:553,
    // :838), and a throw from a LoadingStarted handler would skip every OnLoading phase (:810).
    //
    //   Door:      PlayerWillChangeLevel -> SavingDone (the autosave) -> PlayerWillLoadGame -> LoadingStarted -> LoadingDone
    //   File load: PlayerWillLoadGame -> (scene load) -> LoadingStarted -> LoadingDone
    //   New game:  no load events at all; the Main Menu reset is what empties the log.
    internal static class SlotTracker
    {
        private enum Mode { None, Door, File, Loaded }

        private static Mode _mode;
        private static bool _doorPending, _sawLoadingStarted;
        private static SideCar.ReadResult _pendingFix;   // a rollback time-trim waiting for LoadingDone

        // Between PlayerWillLoadGame and LoadingDone: the clock still shows the old session (Clock.Now).
        internal static bool Busy => _mode != Mode.None;

        // A load from file is starting and the log hasn't been replaced yet: what comes now is the tail of the session
        // being left, except "Save data corrupted" (D19).
        internal static bool CapturesPending => _mode == Mode.File;

        internal static void Reset()
        {
            _mode = Mode.None;
            _doorPending = false;
            _sawLoadingStarted = false;
            _pendingFix = null;
            SideCar.LastSave = (null, null);
            SideCar.Frozen.Clear();
        }

        internal static void OnPlayerWillChangeLevel()
        {
            _doorPending = true;
            LogWindow.Close();
            SearchGuard.ReleaseNow();
        }

        internal static void OnPlayerWillLoadGame()
        {
            _sawLoadingStarted = false;
            _pendingFix = null;
            if (_doorPending)
            {
                _doorPending = false;
                _mode = Mode.Door;
            }
            else
            {
                _mode = Mode.File;
                History.Pending.Clear();
            }
            LogWindow.Close();
            SearchGuard.ReleaseNow();
        }

        internal static void OnSavingDone() => SideCar.Write();

        // LastSaveFolderPath is already the folder being loaded (SRC/SaveController.cs:1622).
        internal static void OnLoadingStarted()
        {
            _sawLoadingStarted = true;
            // PlayerWillLoadGame is a multicast event inside a swallowing try (:1606-1612): a handler before ours that
            // throws means ours never ran. Decide here instead.
            if (_mode == Mode.None || _mode == Mode.Loaded)
            {
                _mode = _doorPending ? Mode.Door : Mode.File;
                _doorPending = false;
            }
            string dir = SaveController.LastSaveFolderPath;
            var last = SideCar.LastSave;
            if (_mode == Mode.Door && last.folder != null && last.info != null && SideCar.SameFolder(dir, last.folder) &&
                SideCar.ReadInfoDate(dir) == last.info)
            {
                // The folder we just saved, still at the save we saw: the log in memory is that save's log.
                _mode = Mode.Loaded;
                Log.Verbose($"Load: door, log kept ({History.Count} messages).");
                return;
            }

            string how = _mode == Mode.Door ? "door, folder changed since our save" : "file";
            if (!Cfg.Hot.SaveWithGame)
            {
                History.Load(null, null, null, 1, 0, null);
                int k = History.TakePending();
                Log.Verbose($"Load: {how}; SaveWithGame is off, so the log starts empty ({k} pending kept).");
            }
            else
            {
                var r = SideCar.ReadAndMatch(dir);
                // The gap marker goes right after the file's entries, before anything captured during the load (design
                // §16.4). It stays untimed until LoadingDone stamps it with the loaded save's time.
                if (r.Gap && Cfg.Hot.GapMarker) History.AddGap();
                int k = History.TakePending();
                if (r.Match == SideCar.Match.Rollback) _pendingFix = r;
                Log.Verbose($"Load: {how}; checkpoint {Describe(r)}; kept {r.Kept}, dropped {r.Dropped}, {k} pending kept.");
            }
            History.Pending.Clear();
            _mode = Mode.Loaded;
        }

        private static string Describe(SideCar.ReadResult r)
        {
            switch (r.Match)
            {
                case SideCar.Match.Exact: return "match";
                case SideCar.Match.Rollback: return "newer -> floor + time trim";
                case SideCar.Match.Older: return r.Gap ? "older -> gap" : "older";
                case SideCar.Match.Missing: return "missing";
                default: return r.Match.ToString().ToLowerInvariant();
            }
        }

        internal static void OnLoadingDone()
        {
            var fix = _pendingFix;
            _pendingFix = null;
            if (fix != null && fix.Match == SideCar.Match.Rollback && Clock.Raw(out int day, out short hm, out _))
            {
                int trimmed = History.TrimAfter(fix.TrimFloor, fix.TrimMax, day * 1440 + hm);
                Log.Verbose($"Load: {trimmed} messages after the loaded save's time were dropped.");
            }
            int stamped = History.StampUntimed();
            if (stamped > 0) Log.Verbose($"Load: {stamped} messages got the loaded save's time.");
            _mode = Mode.None;
        }

        // Plugin.Update, first (spec §3, VitalSigns Plugin.cs:244-248 pattern).
        internal static void Tick()
        {
            // ChangeLevelSame (no level name) raises PlayerWillChangeLevel but never loads (SRC/SaveController.cs:427).
            if (_doorPending && !SaveController.Loading) _doorPending = false;
            // A load that failed before the scene load (:1630-1668, :1806-1816): the log in memory is still the right
            // one. Only "Save data corrupted" from the pending window is kept.
            if (_mode == Mode.File && !SaveController.Loading && !_sawLoadingStarted)
            {
                _mode = Mode.None;
                int k = History.TakePending();
                History.StampUntimed();
                Log.Verbose($"Load: failed before the level loaded; the log is unchanged ({k} pending kept).");
            }
            // A load whose LoadingDone never came (an exception inside the load phases ends the coroutine after its
            // finally cleared Loading, :826-835): after two frames, finish as LoadingDone would, so the clock isn't treated
            // as frozen for the rest of the session.
            else if (_mode == Mode.Loaded && !SaveController.Loading)
            {
                if (_loadedSince < 0) _loadedSince = Time.frameCount;
                else if (Time.frameCount > _loadedSince + 2)
                {
                    _loadedSince = -1;
                    Log.Verbose("Load: no LoadingDone after the level loaded; finishing the load here.");
                    OnLoadingDone();
                }
            }
            else _loadedSince = -1;
        }

        private static int _loadedSince = -1;
    }
}
