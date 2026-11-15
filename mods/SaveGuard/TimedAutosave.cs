using System;
using System.Collections;
using UnityEngine;

namespace SaveGuard
{
    // Timed autosaves into the vanilla Autosave/Autosave2 rotation (spec §11). Off by default.
    // Polled from Plugin.Update; no game event subscriptions.
    internal static class TimedAutosave
    {
        private enum Effective { Off, RealMinutes, GameHours, Daily }

        internal static bool Running;
        internal static float GraceUntil;

        // The Prison object of the current scene, looked up once per scene/load on the first gate check that reaches
        // the prison row (after the grace period). Its private inPrison is read on every check: it only becomes true
        // in Prison.ImprisonPlayer, which runs from the entrypoint spawn 0.1 s after Loading falls (SaveController.cs:905-909).
        private static Prison _prison;
        private static bool _prisonSearched;

        private static MonoBehaviour _host;
        private static bool _wasLoading;
        private static float _activeSeconds;
        private static bool _baselineSet;
        private static ulong _baselineHours;
        private static int _baselineDay;
        private static float _nextCheck;
        private static float _quietSince = -1f;
        private static bool _duePeriodLogged;
        private static string _blockReason;
        private static float _blockSince;
        private static bool _blockWarned;

        internal static void Init(MonoBehaviour host) => _host = host;

        // Every successful save of any kind restarts the interval.
        internal static void OnSaveSucceeded() => ResetBaseline();

        // Every scene load: quitting to the main menu and starting a new game (LoadingScreen.LoadLevel) never set
        // SaveController.Loading, so the falling edge alone would let a new character inherit the old interval.
        internal static void OnSceneLoaded(string scene)
        {
            ResetBaseline();
            GraceUntil = Time.realtimeSinceStartup + Cfg.GraceSecondsAfterLoad.Value;
            _prison = null;
            _prisonSearched = false;
            Log.Verbose($"Timed autosave scheduler reset (scene {scene} loaded)");
        }

        // Prison.InPrison() logs on every call; the private field is read instead.
        internal static bool InPrison()
        {
            if (!_prisonSearched)
            {
                _prisonSearched = true;
                _prison = UnityEngine.Object.FindAnyObjectByType<Prison>();
            }
            return _prison != null && Vanilla.GetInPrison(_prison);
        }

        internal static void ResetScheduler(string why)
        {
            ResetBaseline();
            Log.Verbose($"Timed autosave scheduler reset ({why})");
        }

        private static void ResetBaseline()
        {
            _activeSeconds = 0f;
            _baselineSet = false;      // sampled on the next gameplay frame, when the time of day is the loaded one
            ClearDueState();
        }

        private static void ClearDueState()
        {
            _quietSince = -1f;
            _duePeriodLogged = false;
            _blockReason = null;
            _blockWarned = false;
        }

        internal static void Update()
        {
            bool loading = SaveController.Loading;
            if (_wasLoading && !loading) OnLoadDone();
            _wasLoading = loading;

            var mode = EffectiveMode(out int hours);
            if (mode == Effective.Off || Running) return;
            if (!SaveGate.InGameplay()) return;

            if (!loading && Time.timeScale > 0f && !PauseActive())
                _activeSeconds += Time.unscaledDeltaTime;

            var tod = TimeOfDayAzure.instance;
            if (!_baselineSet)
            {
                if (tod == null) return;
                _baselineHours = tod.currentTimeAndDay.TotalHours;
                _baselineDay = tod.dayCounter;
                _baselineSet = true;
            }

            bool due;
            switch (mode)
            {
                case Effective.RealMinutes: due = _activeSeconds >= Cfg.RealMinutes.Value * 60f; break;
                case Effective.GameHours: due = tod != null && tod.currentTimeAndDay.TotalHours >= _baselineHours + (ulong)hours; break;
                case Effective.Daily: due = tod != null && tod.dayCounter != _baselineDay; break;
                default: due = false; break;
            }
            if (!due) { ClearDueState(); return; }

            float now = Time.realtimeSinceStartup;
            if (now < GraceUntil || now < _nextCheck) return;
            _nextCheck = now + 0.5f;

            if (SaveGate.Check(true, out string why))
            {
                if (_quietSince < 0f) _quietSince = now;
                _blockReason = null;
                if (now - _quietSince >= Cfg.QuietSeconds.Value)
                {
                    _quietSince = -1f;
                    _host.StartCoroutine(TimedSave());
                }
                return;
            }

            _quietSince = -1f;
            if (why != _blockReason)
            {
                if (!_duePeriodLogged) { _duePeriodLogged = true; Log.Info($"Timed autosave due, waiting: {why}"); }
                else Log.Verbose($"Timed autosave waiting: {why}");
                _blockReason = why;
                _blockSince = now;
                _blockWarned = false;
            }
            else if (!_blockWarned && now - _blockSince > 30f * 60f)
            {
                _blockWarned = true;
                Log.Warn($"Timed autosave has been blocked for 30 minutes by: {why}");
            }
        }

        private static void OnLoadDone()
        {
            ResetBaseline();
            GraceUntil = Time.realtimeSinceStartup + Cfg.GraceSecondsAfterLoad.Value;
            _prison = null;
            _prisonSearched = false;
        }

        private static bool PauseActive()
        {
            try { return PauseMenu.instance != null && PauseMenu.instance.PauseIsActive(); }
            catch (Exception) { return true; }
        }

        private static Effective EffectiveMode(out int hours)
        {
            hours = Cfg.GameHours.Value;
            if (Cfg.FollowDifficultySetting.Value)
            {
                try
                {
                    var dc = DifficultyController.instance;
                    if (dc != null && dc.currentDifficultyInfo != null)
                    {
                        // Read only. Every 0.5.19 preset is Default; only a hand-made Custom difficulty sets it.
                        switch (dc.currentDifficultyInfo.autosaveMode)
                        {
                            case AutosaveMode.EveryHour: hours = 1; return Effective.GameHours;
                            case AutosaveMode.EveryDay: return Effective.Daily;
                        }
                    }
                }
                catch (Exception e) { Log.Exception("difficulty autosave mode", e); }
            }
            switch (Cfg.Mode.Value)
            {
                case TimedMode.RealMinutes: return Effective.RealMinutes;
                case TimedMode.GameHours: return Effective.GameHours;
                default: return Effective.Off;
            }
        }

        // Mirrors SaveController.AutosaveDelay (:469-479), plus a gate re-check after the 3-frame delay
        // (vanilla has none, so a door could start in between) and null checks on SavingUI.
        private static IEnumerator TimedSave()
        {
            Running = true;
            bool overlay = Cfg.ShowSavingOverlay.Value && SavingUI.instance != null && GameController.instance != null;
            try { if (overlay) SavingUI.instance.ShowSavingUI(fullScreen: true); }
            catch (Exception e) { overlay = false; Log.Exception("ShowSavingUI", e); }
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();

            if (!SaveGate.Check(false, out string why))
            {
                HideOverlay(overlay);
                Log.Info($"Timed autosave cancelled: {why}");
                Running = false;
                yield break;   // stays due; the quiet period restarts
            }

            string slot = null;
            bool ok = false;
            try
            {
                slot = Vanilla.GetOldestAutosave();   // keeps the vanilla rotation; P1/P2 protect the save
                SaveController.SaveGame(slot);
                ok = true;
            }
            catch (Exception e)
            {
                // The finalizer has already rolled back and shown the failure notice when crash safety was active.
                Log.Error($"Timed autosave failed: {e}");
                Toasts.Show("Timed autosave failed. See the BepInEx log.", 10f);
            }
            // Every finished attempt restarts the interval, also one that failed or failed the commit check:
            // retrying every few seconds would only fill recovered/ (and the disk) with slot copies.
            ResetBaseline();
            yield return new WaitForEndOfFrame();
            yield return null;
            HideOverlay(overlay);
            Running = false;
            if (ok)
            {
                Log.Info($"Timed autosave into {slot}");
                if (Cfg.NotifyOnTimedAutosave.Value) Toasts.Show($"Autosaved ({Paths.SlotDisplay(slot)}, {DateTime.Now:HH:mm})");
            }
        }

        private static void HideOverlay(bool overlay)
        {
            try { if (overlay && SavingUI.instance != null) SavingUI.instance.HideSavingUI(); }
            catch (Exception e) { Log.Exception("HideSavingUI", e); }
        }
    }
}
