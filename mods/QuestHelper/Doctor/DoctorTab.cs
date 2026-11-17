using System;
using System.Linq;

namespace QuestHelper
{
    // "Stuck quests" (spec §4.2): the list of curated fixes with their state, a detail page per fix with the evidence,
    // the exact change, what it won't do and where the backup goes, and the result page after an edit.
    internal static class DoctorTab
    {
        private static IFix _detail;
        private static Diagnosis _shown;
        private static Outcome _result;
        private static string _message;

        internal static void Reset()
        {
            _detail = null;
            _shown = null;
            _result = null;
            _message = null;
        }

        internal static void OpenDetail(IFix f)
        {
            _detail = f;
            _shown = Detector.Run(f);
            _result = null;
            _message = null;
            HelperWindow.FocusOn(_shown.State == DiagState.Stuck ? "apply" : "back");
        }

        internal static bool Back()
        {
            if (_result != null || _detail != null)
            {
                string key = _detail != null ? "fix:" + _detail.Id : null;
                Reset();
                if (key != null) HelperWindow.FocusOn(key);
                return true;
            }
            return false;
        }

        internal static void Build(UiList l)
        {
            if (_result != null) BuildResult(l);
            else if (_detail != null) BuildDetail(l);
            else BuildList(l);
        }

        private static string StateText(Diagnosis d) => d == null ? Strings.NotCheckedYet : Strings.State(d.State);

        private static void BuildList(UiList l)
        {
            l.Heading(Strings.DoctorHeading);
            var all = Detector.Fixes.ToList();
            var fixes = all.Where(Visible).ToList();
            int hidden = all.Count - fixes.Count;
            bool any = fixes.Any(f => { var s = Detector.Get(f)?.State; return s == DiagState.Stuck || s == DiagState.CheckThere; });
            if (!any)
            {
                l.Text(Strings.NoneStuck);
                if (fixes.Count > 0) l.Small(Strings.Checked(string.Join(", ", fixes.Select(f => f.Title).ToArray())));
            }
            if (hidden > 0) l.Small(Strings.CheckedHidden(hidden));
            foreach (var f in fixes)
            {
                var d = Detector.Get(f);
                string state = StateText(d);
                if (d?.State == DiagState.Stuck) state = Skin.Color(state, Skin.Amber);
                else if (d == null || d.State == DiagState.NotAffected) state = Skin.Color(state, Skin.Dim);
                var fix = f;
                l.Row("fix:" + f.Id, Skin.Esc(f.Title) + ":  " + state, () => OpenDetail(fix));
            }
            l.Heading(Strings.AppliedHeading);
            var applied = Records.DoctorLines();
            if (applied.Count == 0) l.Dim(Strings.AppliedNone);
            foreach (var line in applied) l.Small(Skin.Esc(line));
        }

        // Below hint tier 2 the list names only the problems of quests the player has (or a state that needs them to
        // act); the titles of the others would name places, people and choices from later in the story.
        private static bool Visible(IFix f)
        {
            if (Cfg.Tier >= 2) return true;
            var s = Detector.Get(f)?.State;
            return s == DiagState.Stuck || s == DiagState.CheckThere || TaskState.Find(f.TaskId) != null;
        }

        private static void BuildDetail(UiList l)
        {
            var f = _detail;
            var d = _shown ?? Detector.Get(f) ?? Diagnosis.Of(DiagState.CannotCheck);
            l.Heading(Skin.Esc(f.Title) + ":  " + StateText(d));
            l.Heading(Strings.WhatHappened);
            if (d.State == DiagState.OkForNow && Cfg.Tier < 2)
            {
                // "OK for now" texts name the people and the route back; the player isn't stuck, so they stay hints.
                l.Text(Strings.OkForNowNeutral);
            }
            else
            {
                if (d.Evidence.Count == 0) l.Text(d.State == DiagState.NotAffected ? Strings.NothingFound : "");
                foreach (var e in d.Evidence) l.Text(Skin.Esc(e));
                if (!string.IsNullOrEmpty(d.Why)) l.Text(Skin.Esc(d.Why));
            }

            string key = "fix:" + f.Id;
            if (d.State == DiagState.Stuck)
            {
                l.Heading(Strings.WhatChanges);
                foreach (var p in d.Plan) l.Text(Skin.Esc(p));
                l.Heading(Strings.WhatNot);
                l.Text(Strings.WontDo);
                l.Heading(Strings.BackupHeading);
                l.Text(Skin.Esc(Backup.TargetText()));
                string gate = Pipeline.Gates();
                if (gate != null) l.Warn(gate);
                if (_message != null) l.Warn(Skin.Esc(_message));
                l.Space();
                bool armed = Confirm.IsArmed(key, f);
                l.Button("apply", armed ? Strings.PressAgain(Confirm.SecondsLeft(key)) : Strings.BackUpAndApply, () => Apply(f), gate == null);
            }
            else if (_message != null) l.Warn(Skin.Esc(_message));
            l.Button("back", Strings.Back, () => { Back(); });
        }

        private static void Apply(IFix f)
        {
            string key = "fix:" + f.Id;
            if (!Confirm.IsArmed(key, f))
            {
                string gate = Pipeline.Gates();
                if (gate != null) { _message = gate; return; }
                _message = null;
                Confirm.Arm(key, f, Cfg.QuestDoctor.ConfirmSeconds.Value);
                return;
            }
            Confirm.Reset(key);
            Outcome o;
            try { o = Pipeline.RunFix(f, _shown); }
            catch (Exception e)
            {
                Log.Exception("apply " + f.Id, e);
                o = new Outcome { Message = Strings.CheckFailed };
            }
            if (o.Changed) { _result = o; HelperWindow.FocusOn(Game.SaveButtonEnabled && !o.Broke ? "save" : "done"); }
            else
            {
                _message = o.Message;
                _shown = Detector.Get(f) ?? _shown;
            }
        }

        // Shared by the generic edits (TaskListTab).
        internal static void ShowResult(Outcome o)
        {
            _detail = null;
            _result = o;
            HelperWindow.Tab = 0;
            HelperWindow.FocusOn(Game.SaveButtonEnabled && !o.Broke ? "save" : "done");
        }

        private static void BuildResult(UiList l)
        {
            var o = _result;
            l.Heading(Skin.Esc(o.Title ?? Strings.ResultHeading));
            if (o.Broke) l.Warn(Skin.Esc(o.Message));
            else l.Text(o.Verified ? Strings.ResultOk : Strings.ResultUnverified);
            if (!string.IsNullOrEmpty(o.BackupPath)) l.Small(Skin.Esc(Strings.BackupAt(o.BackupKind, o.BackupPath)));
            if (!o.Broke)
            {
                bool canSave = Game.SaveButtonEnabled;
                l.Text(canSave ? Strings.SaveToKeep : Strings.KeptAtAutosave);
                l.Space();
                if (canSave) l.Button("save", Strings.SaveGameButton, OpenSaveMenu);
            }
            l.Button("done", Strings.Close, () => { Reset(); HelperWindow.Rebuild(); });
            l.Small(Strings.HowToRestore(o.BackupKind));
        }

        // D1: close the window and open the vanilla save submenu; the save itself is the game's (and Save Guard's).
        private static void OpenSaveMenu()
        {
            Reset();
            HelperWindow.Close();
            try
            {
                var pm = PauseMenu.instance;
                if (pm == null || !Game.SaveButtonEnabled) return;
                pm.SaveGame();
                var nav = PauseNavigationHandler.Instance;
                if (nav != null && Vanilla.ChangeMenuState != null) Vanilla.ChangeMenuState.Invoke(nav, null);
            }
            catch (Exception e) { Log.Exception("open save menu", e); }
        }
    }
}
