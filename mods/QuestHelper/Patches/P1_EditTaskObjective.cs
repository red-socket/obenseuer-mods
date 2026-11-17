using System;
using System.Reflection;
using HarmonyLib;

namespace QuestHelper
{
    // P1: TaskController.EditTaskObjective(TaskStatusInfo, ObjectiveStatusInfo, ObjectiveStatus) postfix (spec §3.2).
    // Marks the TBC section and the pin dirty on every call. On a real activation outside loading and our own edits it
    // records where the objective came from (§5.1) and shows the "to be continued" note (§7). Never changes state.
    [HarmonyPatch]
    internal static class EditTaskObjectivePatch
    {
        private static MethodBase TargetMethod() => Vanilla.EditTaskObjective;

        private static void Postfix(TaskStatusInfo task, ObjectiveStatusInfo objective, ObjectiveStatus newStatus)
        {
            try
            {
                TbcSection.Dirty = true;
                MapPin.MarkDirty();
                Detector.Dirty = true;
                if (newStatus != ObjectiveStatus.Active || objective == null || objective.status != ObjectiveStatus.Active) return;
                if (SaveController.Loading || Edits.Applying || task?.task == null || objective.objective == null || task.renovation) return;
                if (Plugin.JournalOn && Cfg.Journal.RecordWhereReceived.Value) Records.Record(task, objective);
                if (objective.objective.cannotBeCompleted) TbcSection.Notice(task, objective);
            }
            catch (Exception e) { Log.Exception("objective hook", e); }
        }
    }
}
