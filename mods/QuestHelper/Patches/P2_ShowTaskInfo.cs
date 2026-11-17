using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace QuestHelper
{
    // P2: TaskPanel.ShowTaskInfo(int) postfix (spec §3.2, V11). UpdateObjectives is only called from here and recreates
    // every objective row, so appending to objectiveTitle here is idempotent; the vanilla UpdateTaskInfo coroutine
    // rebuilds the layout at the end of the frame. Also records the selected task for the map pin, and applies the TBC
    // marker (D14).
    [HarmonyPatch]
    internal static class ShowTaskInfoPatch
    {
        private const string Open = "\n<size=80%><color=#BFBFBF><noparse>";
        private const string Close = "</noparse></color></size>";

        private static MethodBase TargetMethod() => Vanilla.ShowTaskInfo;

        private static void Postfix(TaskPanel __instance, int index)
        {
            try
            {
                var list = Vanilla.TaskList(__instance);
                if (list == null || index < 0 || index >= list.Count || list[index] == null || !Vanilla.TaskInfoPanelActive(__instance)) return;
                var panel = list[index].GetComponent<TaskUIPanel>();
                var tsi = panel != null ? panel.task : null;
                if (tsi?.task == null) return;
                if (Tracking.Selected != tsi.Id)
                {
                    Tracking.Selected = tsi.Id;
                    TbcSection.RefreshHint();   // P7's "(!)" follows the selection
                }

                var rows = Vanilla.VisibleObjectives(__instance);
                if (rows == null) return;
                foreach (var go in rows)
                {
                    var ui = go != null ? go.GetComponent<ObjectiveUI>() : null;
                    var osi = ui != null ? Vanilla.ObjectiveStatusField(ui) : null;
                    if (osi?.objective == null) continue;
                    TbcSection.Label(ui, osi);
                    if (!Plugin.JournalOn || (osi.status != ObjectiveStatus.Active && osi.status != ObjectiveStatus.Hidden)) continue;
                    var title = Vanilla.ObjectiveTitle(ui);
                    if (title == null) continue;
                    foreach (var line in Lines.For(tsi, osi))
                        title.text += Open + line.Replace("</noparse>", "") + Close;
                }
            }
            catch (Exception e) { Log.Exception("task info", e); }
        }
    }
}
