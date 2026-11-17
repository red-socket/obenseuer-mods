using System;
using System.Collections.Generic;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace QuestHelper
{
    // The only code that changes game state (spec §4.3). Every method checks that a game is running, nothing is loading
    // and the pause menu is open (the window sits on it), and logs before and after. Applying is set for the whole fix so
    // P1 does not record our own activations and the TBC notice stays quiet. Vanilla methods only, with one exception
    // (ReactivateObjective's cache clean-up, D3). Never GetTaskAndObjective or the console's set_objective (their
    // :116 side effect finishes the objective), never TaskItem.GiveItem, never SaveController.SaveGame.
    internal static class Edits
    {
        internal static bool Applying;

        private static TaskController Tc
        {
            get
            {
                var tc = TaskController.instance;
                if (tc == null || SaveController.Loading || !Game.PauseOpen)
                    throw new InvalidOperationException("no game running, a load in progress, or the pause menu is closed");
                return tc;
            }
        }

        // ---- Asset loading (Prepare only; no side effects) ----

        internal static TaskObjective LoadObjective(string path, string expectedId)
        {
            try
            {
                var so = SavableScriptableObject.LoadFromPath(path) as TaskObjective;
                if (so == null || so.Id != expectedId)
                {
                    Log.Warn($"Doctor: {path} did not load as objective {expectedId}.");
                    return null;
                }
                return so;
            }
            catch (Exception e) { Log.Exception("load " + path, e); return null; }
        }

        internal static TaskItem LoadTaskItem(string path)
        {
            try
            {
                var item = SavableScriptableObject.LoadFromPath(path) as TaskItem;
                if (item == null) Log.Warn($"Doctor: {path} did not load as a task item.");
                return item;
            }
            catch (Exception e) { Log.Exception("load " + path, e); return null; }
        }

        // The requirement checks EditTask runs first (SRC/TaskController.cs:146, :170-172, :183), read-only, so a fix
        // that would be refused is refused before the backup.
        internal static bool CanActivate(TaskObjective so, TaskInfo task, TaskStatusInfo tsi)
        {
            try
            {
                var tc = TaskController.instance;
                if (tc == null || so == null || task == null) return false;
                if (!task.CheckActivateRequirements(tc.allTasks)) return false;
                return so.CheckActivateRequirements(tsi != null ? tsi.objectives : new List<ObjectiveStatusInfo>());
            }
            catch (Exception e) { Log.Exception("activate check", e); return false; }
        }

        // ---- Edits ----

        internal static void Counter(TaskStatusInfo tsi, ObjectiveStatusInfo osi, int delta, List<string> log)
        {
            var tc = Tc;
            log.Add($"counter {Key(tsi, osi)}: {osi.currentAmount}/{osi.counterAmount} {osi.status}, +{delta}");
            tc.EditTaskCounter(tsi.task, osi.objective, delta, false);
            log.Add($"counter {Key(tsi, osi)}: now {osi.currentAmount}/{osi.counterAmount} {osi.status}");
        }

        // QD-CASINO only (D3). EditTaskObjective(Active) never clears the other caches (SRC/TaskController.cs:306-322,
        // :672), so the key is removed from completed/failed/abandoned first, then kept exactly once in activeObjectives.
        internal static void ReactivateObjective(TaskStatusInfo tsi, ObjectiveStatusInfo osi, List<string> log)
        {
            var tc = Tc;
            string key = Key(tsi, osi);
            log.Add($"reactivate {key}: {osi.status}, {Caches(tc, key)}");
            tc.completedObjectives.RemoveAll(k => k == key);
            tc.failedObjectives.RemoveAll(k => k == key);
            tc.abandonedObjectives.RemoveAll(k => k == key);
            tc.EditTaskObjective(tsi, osi, ObjectiveStatus.Active);   // the vanilla "New Objective" note
            tc.activeObjectives.RemoveAll(k => k == key);
            tc.activeObjectives.Add(key);
            if (!tc.anyStatusObjectives.Contains(key)) tc.anyStatusObjectives.Add(key);
            if (TaskPanel.instance != null) TaskPanel.instance.UpdateCurrentTaskInfo();
            log.Add($"reactivate {key}: now {osi.status}, {Caches(tc, key)}");
        }

        // A new objective through the vanilla EditTask (spec §4.3). task is the live tsi.task, or for a new task the
        // objective's own task asset.
        internal static void ActivateNew(TaskObjective so, TaskInfo task, bool createTask, List<string> log)
        {
            var tc = Tc;
            if (TaskState.ObjAny(task.Id, so.Id)) throw new InvalidOperationException(task.Id + "_" + so.Id + " already has a status");
            log.Add($"activate {task.Id}_{so.Id} (new task: {createTask})");
            bool ok = tc.EditTask(task, so, ObjectiveStatus.Active, false, createTask, CurrentTaskStatus.Active, false, true, false);
            if (!ok) throw new InvalidOperationException("the game refused to activate " + task.Id + "_" + so.Id);
            log.Add($"activate {task.Id}_{so.Id}: active={TaskState.ObjActive(task.Id, so.Id)}");
        }

        internal static void SetActorField(string actor, string field, object value, List<string> log)
        {
            _ = Tc;
            var before = DialogueLua.GetActorField(actor, field);
            log.Add($"lua {actor}.{field}: {Show(before)} -> {value}");
            DialogueLua.SetActorField(actor, field, value);
            var after = DialogueLua.GetActorField(actor, field);
            bool same = value is bool b ? after.isBool && after.asBool == b
                      : value is string s ? after.isString && after.asString == s
                      : after.isNumber && Math.Abs(after.asFloat - Convert.ToSingle(value)) < 0.0001f;
            if (!same) throw new InvalidOperationException($"{actor}.{field} reads back as {Show(after)}");
        }

        internal static void SetSceneSaver(GameObjectStateSave s, bool active, List<string> log)
        {
            _ = Tc;
            if (s == null) throw new InvalidOperationException("scene switch missing");
            log.Add($"scene {s.gameObject.name}: {s.gameObjectActive} -> {active}");
            s.ChangeState(active);
        }

        // Re-gives the lost key item a quest needs, only when it is not held. Never TaskItem.GiveItem.
        internal static void GiveTaskItemIfMissing(TaskItem item, List<string> log)
        {
            _ = Tc;
            var tim = TaskItemsManager.instance;
            if (tim == null) throw new InvalidOperationException("no task items");
            if (tim.PlayerHasTaskItemId(item.Id))
            {
                log.Add($"item {item.Id}: already held");
                return;
            }
            bool ok = tim.AddTaskItem(item, 0, showNotification: true);
            log.Add($"item {item.Id}: given={ok}");
            if (!ok || !tim.PlayerHasTaskItemId(item.Id)) throw new InvalidOperationException("the game refused to give " + item.itemName);
        }

        // Generic edits (spec §4.5): an Active objective to Finished/Failed/Abandoned, the console's set_objective_status
        // path.
        internal static void GenericObjective(TaskStatusInfo tsi, ObjectiveStatusInfo osi, ObjectiveStatus s, List<string> log)
        {
            var tc = Tc;
            if (osi.status != ObjectiveStatus.Active || s == ObjectiveStatus.Active || s == ObjectiveStatus.Hidden)
                throw new InvalidOperationException("only an active objective can be finished, failed or abandoned");
            if (s == ObjectiveStatus.Finished && !CanFinish(tsi, osi))
                throw new InvalidOperationException("the objective's required objectives are not done");
            log.Add($"generic objective {Key(tsi, osi)}: {osi.status} -> {s}");
            tc.EditTaskObjective(tsi, osi, s);
            tc.FinishIfNoActiveObjectives(tsi);
            if (TaskPanel.instance != null) TaskPanel.instance.UpdateCurrentTaskInfo();
            log.Add($"generic objective {Key(tsi, osi)}: now {osi.status}, task {tsi.status}");
        }

        // The game's own completion check (read-only: no TaskObjective overrides OtherCompleteRequirements in 0.5.19).
        internal static bool CanFinish(TaskStatusInfo tsi, ObjectiveStatusInfo osi) =>
            osi?.objective != null && tsi?.objectives != null && osi.objective.CheckCompleteRequirements(tsi.objectives);

        // An Active task to Finished/Failed/Abandoned: every Active objective is abandoned and its follow-up runs.
        internal static void GenericTask(TaskStatusInfo tsi, CurrentTaskStatus s, List<string> log)
        {
            var tc = Tc;
            if (tsi.status != CurrentTaskStatus.Active || s == CurrentTaskStatus.Active)
                throw new InvalidOperationException("only an active task can be finished, failed or abandoned");
            log.Add($"generic task {tsi.Id}: {tsi.status} -> {s}");
            tc.EditTaskStatus(tsi, s);
            if (TaskPanel.instance != null) TaskPanel.instance.UpdateCurrentTaskInfo();
            log.Add($"generic task {tsi.Id}: now {tsi.status}");
        }

        private static string Key(TaskStatusInfo tsi, ObjectiveStatusInfo osi) => TaskState.Key(tsi, osi);

        private static string Caches(TaskController tc, string key) =>
            $"active x{TaskState.Count(tc.activeObjectives, key)}, completed x{TaskState.Count(tc.completedObjectives, key)}, " +
            $"failed x{TaskState.Count(tc.failedObjectives, key)}, abandoned x{TaskState.Count(tc.abandonedObjectives, key)}";

        private static string Show(Lua.Result r) =>
            r.isBool ? (r.asBool ? "true" : "false") : r.isNumber ? r.asFloat.ToString(System.Globalization.CultureInfo.InvariantCulture) : r.isString ? "\"" + r.asString + "\"" : "nil";
    }
}
