using System.Collections.Generic;
using System.Linq;

namespace QuestHelper
{
    // Read-only task queries by Id strings (spec §4.3), never by asset reference. Status questions go through the
    // TaskController caches: exactly what the dialogue's Lua conditions see.
    internal static class TaskState
    {
        private static TaskController Tc => TaskController.instance;

        internal static bool Ready => Tc != null;

        internal static bool ObjActive(string t, string o) => Tc != null && Tc.ObjectiveActive(t, o);
        internal static bool ObjCompleted(string t, string o) => Tc != null && Tc.ObjectiveCompleted(t, o);
        internal static bool ObjAny(string t, string o) => Tc != null && Tc.ObjectiveAnyStatus(t, o);
        internal static bool TaskActive(string t) => Tc != null && Tc.TaskActive(t);
        internal static bool TaskFinished(string t) => Tc != null && Tc.TaskFinished(t);
        internal static bool TaskAny(string t) => Tc != null && Tc.TaskAnyStatus(t);

        internal static TaskStatusInfo Find(string taskId) =>
            Tc?.allTasks?.FirstOrDefault(x => x != null && x.task != null && x.Id == taskId);

        internal static ObjectiveStatusInfo FindObj(TaskStatusInfo tsi, string objId) =>
            tsi?.objectives?.FirstOrDefault(o => o != null && o.objective != null && o.objective.Id == objId);

        internal static string Key(TaskStatusInfo tsi, ObjectiveStatusInfo osi) => tsi.task.Id + "_" + osi.objective.Id;

        // How many times key sits in a cache list.
        internal static int Count(List<string> list, string key)
        {
            if (list == null) return 0;
            int n = 0;
            foreach (var s in list) if (s == key) n++;
            return n;
        }

        // The live status and the TaskController cache disagree (spec §4.2 Task list's amber mark).
        internal static bool Mismatch(TaskStatusInfo tsi, ObjectiveStatusInfo osi)
        {
            var tc = Tc;
            if (tc == null || tsi?.task == null || osi?.objective == null) return false;
            string key = Key(tsi, osi);
            int active = Count(tc.activeObjectives, key);
            switch (osi.status)
            {
                case ObjectiveStatus.Active: return active != 1;
                case ObjectiveStatus.Finished: return active > 0 || !tc.completedObjectives.Contains(key);
                case ObjectiveStatus.Failed: return active > 0 || !tc.failedObjectives.Contains(key);
                case ObjectiveStatus.Abandoned: return active > 0 || !tc.abandonedObjectives.Contains(key);
                default: return active > 0;
            }
        }

        // A task that reached the current end of its story (spec §7): Active, not a renovation, with at least one Active
        // objective, and every Active objective marked cannotBeCompleted.
        internal static bool IsTbc(TaskStatusInfo tsi)
        {
            if (tsi == null || tsi.task == null || tsi.status != CurrentTaskStatus.Active || tsi.renovation || tsi.objectives == null) return false;
            bool any = false;
            foreach (var o in tsi.objectives)
            {
                if (o == null || o.objective == null || o.status != ObjectiveStatus.Active) continue;
                if (!o.objective.cannotBeCompleted) return false;
                any = true;
            }
            return any;
        }

        internal static string Title(TaskStatusInfo tsi) => tsi?.task != null && !string.IsNullOrEmpty(tsi.task.title) ? tsi.task.title : tsi?.task?.Id ?? "?";

        internal static string Title(ObjectiveStatusInfo osi) => osi?.objective != null && !string.IsNullOrEmpty(osi.objective.title) ? osi.objective.title : osi?.objective?.Id ?? "?";
    }
}
