using System.Linq;

namespace QuestHelper
{
    // "Bazaar access" (spec §4.2, §6.3, S-046): opt-in and spoiler-tiered. Tier 0 shows nothing beyond a pointer to the
    // hints row. From tier 1: the checkpoints between the tenement, the Bazaar and the mines with what each needs, and
    // "Your chain" (Getting a passport, Permission to dig, Elevator to surface) with each active objective's needs and
    // hint. A checkpoint shows once the player has the task it belongs to; tier 3 shows all of them. Read-only.
    internal static class BazaarTab
    {
        internal static void Build(UiList l)
        {
            HelperWindow.HintsRow(l);
            int tier = Cfg.Tier;
            if (tier == 0)
            {
                l.Text(Strings.HintsOffBazaar);
                return;
            }
            if (!Curated.Ok)
            {
                l.Warn(Strings.CheckFailed);
                return;
            }

            l.Heading(Strings.CheckpointsHeading);
            foreach (var cp in Curated.Checkpoints)
            {
                if (tier < 3 && !cp.Shows()) continue;
                l.Text(Skin.Esc(cp.Title));
                string needs = Blockers.Line(cp.Needs);
                if (needs != null) l.Small(Skin.Esc(needs), 1);
                if (!string.IsNullOrEmpty(cp.Note)) l.Dim(Skin.Esc(cp.Note), 1);
                if (tier >= 2 && !string.IsNullOrEmpty(cp.Where)) l.Dim(Skin.Esc(Strings.Where(cp.Where)), 1);
            }

            l.Heading(Strings.ChainHeading);
            foreach (var c in Curated.Chain)
            {
                var tsi = TaskState.Find(c.Task);
                if (tsi == null && tier < 3) continue;
                string status = tsi == null ? Strings.NotStarted : tsi.status == CurrentTaskStatus.Active ? Strings.ChainActive : Strings.ChainDone;
                l.Text(Skin.Esc(tsi != null ? TaskState.Title(tsi) : c.Title) + "   " + Skin.Color(status, Skin.Dim));
                if (tsi == null || tsi.status != CurrentTaskStatus.Active) continue;
                var active = tsi.objectives.Where(o => o?.objective != null && o.status == ObjectiveStatus.Active).ToList();
                if (active.Count == 0) l.Dim(Strings.NoActiveObjectives, 1);
                foreach (var o in active)
                {
                    l.Text("• " + Skin.Esc(TaskState.Title(o)), 1);
                    var row = Curated.Get(TaskState.Key(tsi, o));
                    if (row == null) continue;
                    if (Cfg.Hints.ShowBlockers.Value)
                    {
                        string needs = Blockers.Line(row.Needs);
                        if (needs != null) l.Small(Skin.Esc(needs), 2);
                    }
                    string hint = row.HintFor(tier);
                    if (!string.IsNullOrEmpty(hint)) l.Small(Skin.Esc(hint), 2);
                }
            }
        }
    }
}
