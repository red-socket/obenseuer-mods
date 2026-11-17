using System;
using System.Collections.Generic;
using System.Linq;

namespace QuestHelper
{
    // "Task list" (spec §4.2): every task grouped as Active / To be continued / Finished / Failed / Abandoned, each
    // objective with its status, counter and journal lines, and an amber mark where the live status and the
    // TaskController caches disagree. Read-only unless QuestDoctor.AllowGenericEdits is on (spec §4.5).
    internal static class TaskListTab
    {
        private static readonly string[] Groups = { "active", "tbc", "finished", "failed", "abandoned" };
        private static readonly HashSet<string> OpenGroups = new HashSet<string> { "active", "tbc" };
        private static readonly HashSet<string> OpenTasks = new HashSet<string>();
        private static string _message;

        internal static void Reset()
        {
            OpenTasks.Clear();
            OpenGroups.Clear();
            OpenGroups.Add("active");
            OpenGroups.Add("tbc");
            _message = null;
        }

        internal static void Focus(string taskId)
        {
            var tsi = TaskState.Find(taskId);
            if (tsi == null) return;
            OpenGroups.Add(GroupOf(tsi));
            OpenTasks.Add(taskId);
            HelperWindow.FocusOn("task:" + taskId);
        }

        internal static bool Back()
        {
            if (_message == null) return false;
            _message = null;
            return true;
        }

        private static string GroupOf(TaskStatusInfo t)
        {
            switch (t.status)
            {
                case CurrentTaskStatus.Active: return TaskState.IsTbc(t) ? "tbc" : "active";
                case CurrentTaskStatus.Finished: return "finished";
                case CurrentTaskStatus.Failed: return "failed";
                default: return "abandoned";
            }
        }

        private static string GroupTitle(string g)
        {
            switch (g)
            {
                case "active": return Strings.GroupActive;
                case "tbc": return Strings.GroupTbc;
                case "finished": return Strings.GroupFinished;
                case "failed": return Strings.GroupFailed;
                default: return Strings.GroupAbandoned;
            }
        }

        private static bool Generic => Cfg.QuestDoctor.AllowGenericEdits.Value;

        internal static void Build(UiList l)
        {
            HelperWindow.HintsRow(l);
            if (Generic) l.Banner(Strings.GenericBanner);
            if (_message != null) l.Warn(Skin.Esc(_message));
            var tc = TaskController.instance;
            if (tc == null) return;
            // allTasks keeps the most recently changed task last (MoveTaskUp); show newest first.
            var tasks = tc.allTasks.Where(t => t != null && t.task != null).Reverse().ToList();
            foreach (var g in Groups)
            {
                var inGroup = tasks.Where(t => GroupOf(t) == g).ToList();
                if (inGroup.Count == 0) continue;
                bool open = OpenGroups.Contains(g);
                string group = g;
                l.Row("group:" + g, (open ? "- " : "+ ") + GroupTitle(g) + " (" + inGroup.Count + ")",
                    () => { if (!OpenGroups.Remove(group)) OpenGroups.Add(group); });
                if (!open) continue;
                foreach (var t in inGroup) TaskRows(l, t);
            }
        }

        private static void TaskRows(UiList l, TaskStatusInfo t)
        {
            string id = t.Id;
            bool open = OpenTasks.Contains(id);
            bool mismatch = t.objectives != null && t.objectives.Any(o => o?.objective != null && TaskState.Mismatch(t, o));
            string text = (open ? "- " : "+ ") + Skin.Esc(TaskState.Title(t));
            if (mismatch) text += "  " + Skin.Color("!", Skin.Amber);
            if (Detector.StuckFor(id)) text += "  " + Skin.Color(Strings.DoctorLine, Skin.Amber);
            l.Row("task:" + id, text, () => { if (!OpenTasks.Remove(id)) OpenTasks.Add(id); }, 1);
            if (!open || t.objectives == null) return;

            var objectives = t.objectives.Where(o => o?.objective != null)
                .OrderBy(o => o.status == ObjectiveStatus.Active ? 0 : 1).ToList();
            foreach (var o in objectives)
            {
                string line = "• " + Skin.Esc(TaskState.Title(o)) + "   " + Skin.Color(Strings.Status(o.status) +
                    (o.counterAmount > 0 ? $" {o.currentAmount}/{o.counterAmount}" : "") + (o.optional ? ", optional" : ""), Skin.Dim);
                l.Text(line, 2);
                if (TaskState.Mismatch(t, o)) l.Warn("! " + Strings.Mismatch, 3);
                if (o.status == ObjectiveStatus.Active || o.status == ObjectiveStatus.Hidden)
                    foreach (var extra in Lines.For(t, o, forWindow: true)) l.Small(Skin.Esc(extra), 3);
                if (Generic && HelperWindow.Available && t.status == CurrentTaskStatus.Active && o.status == ObjectiveStatus.Active)
                {
                    GenericObjectiveButton(l, t, o, Strings.OpFinish, ObjectiveStatus.Finished);
                    GenericObjectiveButton(l, t, o, Strings.OpFail, ObjectiveStatus.Failed);
                    GenericObjectiveButton(l, t, o, Strings.OpAbandon, ObjectiveStatus.Abandoned);
                }
            }
            if (Generic && HelperWindow.Available && t.status == CurrentTaskStatus.Active)
            {
                l.Dim(Strings.GenericTaskWarning, 2);
                GenericTaskButton(l, t, Strings.OpFinish, CurrentTaskStatus.Finished);
                GenericTaskButton(l, t, Strings.OpFail, CurrentTaskStatus.Failed);
                GenericTaskButton(l, t, Strings.OpAbandon, CurrentTaskStatus.Abandoned);
            }
        }

        // ---- Generic edits (spec §4.5): same gates, backup and two-press confirm as the curated fixes ----

        private static void GenericObjectiveButton(UiList l, TaskStatusInfo t, ObjectiveStatusInfo o, string op, ObjectiveStatus s)
        {
            string key = "gen:o:" + TaskState.Key(t, o) + ":" + s;
            bool armed = Confirm.IsArmed(key, o);
            l.Button(key, armed ? Strings.GenericPressAgain(op, Confirm.SecondsLeft(key)) : op + ": " + Skin.Esc(TaskState.Title(o)), () =>
            {
                // EditTaskObjective(Finished) sets the status but leaves the objective in activeObjectives (Lua still
                // sees it as active) when its requiredCompleteObjectives aren't met (SRC/TaskController.cs:330). All or
                // nothing: refuse instead of leaving the records half changed.
                if (s == ObjectiveStatus.Finished && !Edits.CanFinish(t, o))
                {
                    Confirm.ResetAll();
                    _message = Strings.GenericNeedsFirst;
                    return;
                }
                if (!Arm(key, o)) return;
                string okey = TaskState.Key(t, o);
                var outcome = Pipeline.RunGeneric(TaskState.Title(t) + ": " + TaskState.Title(o), op.ToLowerInvariant() + "-objective " + okey,
                    () => o.status == ObjectiveStatus.Active && t.status == CurrentTaskStatus.Active && (s != ObjectiveStatus.Finished || Edits.CanFinish(t, o)),
                    log => Edits.GenericObjective(t, o, s, log),
                    () => o.status == s && !TaskState.Mismatch(t, o),
                    () => $"{okey} {o.status}, task {t.status}");
                Done(outcome);
            }, true, 3);
        }

        private static void GenericTaskButton(UiList l, TaskStatusInfo t, string op, CurrentTaskStatus s)
        {
            string key = "gen:t:" + t.Id + ":" + s;
            bool armed = Confirm.IsArmed(key, t);
            l.Button(key, armed ? Strings.GenericPressAgain(op, Confirm.SecondsLeft(key)) : Strings.GenericTaskButton(op), () =>
            {
                if (!Arm(key, t)) return;
                var outcome = Pipeline.RunGeneric(TaskState.Title(t), op.ToLowerInvariant() + "-task " + t.Id,
                    () => t.status == CurrentTaskStatus.Active,
                    log => Edits.GenericTask(t, s, log),
                    () => t.status == s,
                    () => $"{t.Id} {t.status}");
                Done(outcome);
            }, true, 2);
        }

        // First press arms (after the gates), second press runs.
        private static bool Arm(string key, object target)
        {
            if (Confirm.IsArmed(key, target))
            {
                Confirm.Reset(key);
                return true;
            }
            Confirm.ResetAll();
            string gate = Pipeline.Gates();
            if (gate != null) { _message = gate; return false; }
            _message = null;
            Confirm.Arm(key, target, Cfg.QuestDoctor.ConfirmSeconds.Value);
            return false;
        }

        private static void Done(Outcome o)
        {
            if (o.Changed) DoctorTab.ShowResult(o);
            else _message = o.Message;
        }
    }
}
