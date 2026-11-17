using System.Collections.Generic;

namespace QuestHelper
{
    // The extra lines under an Active or Hidden objective (spec §5.2), shared by the task panel (P2) and the window's
    // Task list. Tier 0 shows only the player's own history ("Received from") and the Doctor note; curated text needs
    // tier 1 (nudge, needs) or 2 (who and where), and is shown only for objectives the player has.
    internal static class Lines
    {
        internal static List<string> For(TaskStatusInfo tsi, ObjectiveStatusInfo osi, bool forWindow = false)
        {
            var list = new List<string>();
            if (tsi?.task == null || osi?.objective == null) return list;
            string key = TaskState.Key(tsi, osi);
            int tier = Cfg.Tier;

            if (Plugin.JournalOn && Cfg.Journal.ShowWhereReceived.Value)
            {
                var rec = Records.Get(key);
                if (rec != null)
                {
                    string place = null;
                    if (rec.Pos.HasValue)
                    {
                        string near = Places.Nearest(rec.Map, rec.Pos.Value, Cfg.Journal.NearRadius.Value);
                        if (near != null) place = Strings.Near(near);
                    }
                    if (place == null && !string.IsNullOrEmpty(rec.Scene)) place = Strings.In(Game.ShortScene(rec.Scene));
                    string line = Strings.ReceivedFrom(rec.Giver, place);
                    if (!string.IsNullOrEmpty(line)) list.Add(line);
                }
            }

            if (!forWindow && Detector.StuckFor(tsi.Id)) list.Add(Strings.DoctorLine);

            var row = Curated.Get(key);
            if (row == null || tier < 1) return list;
            if (Cfg.Hints.ShowBlockers.Value)
            {
                string needs = Blockers.Line(row.Needs);
                if (needs != null) list.Add(needs);
            }
            string hint = row.HintFor(tier);
            if (!string.IsNullOrEmpty(hint)) list.Add(hint);
            if (tier >= 2)
            {
                string where = WhereText(row);
                if (where != null) list.Add(Strings.Where(where));
            }
            return list;
        }

        // "Tanja Nypan, Service Area (on the Mines map)": the person from the conversation title, the landmark's own title.
        private static string WhereText(Row row)
        {
            string giver = Game.GiverFromConversation(row.Conv);
            string place = null, map = null;
            if (!string.IsNullOrEmpty(row.Landmark))
            {
                var lm = Curated.LandmarkAsset(row.Landmark);
                if (lm != null && !string.IsNullOrEmpty(lm.title)) place = lm.title;
                string lmMap = Curated.MapOf(row.Landmark);
                string cur = Game.CurrentMapId;
                if (place != null && !string.IsNullOrEmpty(lmMap) && lmMap != cur) map = Strings.OnMap(Game.MapName(lmMap));
            }
            if (string.IsNullOrEmpty(giver) && place == null) return null;
            string s = string.IsNullOrEmpty(giver) ? place : place == null ? giver : giver + ", " + place;
            return map != null ? s + " " + map : s;
        }
    }
}
