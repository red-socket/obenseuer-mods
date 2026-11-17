namespace QuestHelper
{
    // The task the player follows (spec §5.3): the last one opened in the task list (P2). Kept in the journal string
    // ("t" line) so it survives doors and reloads.
    internal static class Tracking
    {
        private static string _selected;

        internal static string Selected
        {
            get => _selected;
            set
            {
                if (value == _selected) return;
                _selected = value;
                MapPin.MarkDirty();
            }
        }

        // The task the pin follows: the selected one, or with MapPin.Follows = LastUpdated the last element of allTasks
        // (MoveTaskUp moves a changed task to the end). Only an Active task counts.
        internal static TaskStatusInfo Followed()
        {
            var tc = TaskController.instance;
            if (tc == null) return null;
            TaskStatusInfo t;
            if (Cfg.MapPin.Follows.Value == PinFollow.LastUpdated)
                t = tc.allTasks.Count > 0 ? tc.allTasks[tc.allTasks.Count - 1] : null;
            else t = TaskState.Find(_selected);
            return t != null && t.task != null && t.status == CurrentTaskStatus.Active ? t : null;
        }
    }
}
