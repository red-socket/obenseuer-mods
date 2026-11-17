using TMPro;
using UnityEngine;

namespace FairMarket
{
    // The dishwasher's recipe details (spec §8.1). Vanilla prints "Fail chance: 69%" (the rate truncated) and "Process
    // time: 2h" (only the largest unit) (SRC/RecipeInfoUI.cs:265-266, :274-275). Both lines are rebuilt from the same
    // values the game used and replaced only when they match what the game printed (the "oracle" check), so a change
    // upstream or another mod's edit keeps the game's text. Odds use recipe.FailChance as the game does; washing a
    // liquid container takes its value from the container, as in the game.
    internal static class WashInfo
    {
        private const string FailKey = "\nFail chance: ";
        private const string TimeKey = "\nProcess time: ";

        // The details TMP is the last one under the list panel whose text has the fail line: ShowRecipe destroys the old
        // children with a deferred Object.Destroy, so they are still there in this frame, before the new ones.
        internal static TMP_Text Details(GameObject listPanel)
        {
            if (listPanel == null) return null;
            TMP_Text found = null;
            foreach (var t in listPanel.GetComponentsInChildren<TMP_Text>(true))
                if (t != null && t.text != null && t.text.Contains(FailKey)) found = t;
            return found;
        }

        internal static void Rewrite(TMP_Text target, Recipe recipe, float equipmentQuality)
        {
            if (target == null || recipe == null || PlayerStats.instance == null) return;
            string text = target.text;
            float p = recipe.CurrentFailChance(recipe.RelevantSkill, equipmentQuality, recipe.SkillFactor);
            int secs = recipe.GetProcessTime(1f, equipmentQuality);

            string failOld = FailKey + (int)(p * 100f) + "%";
            string failNew = Strings.FailLine(Fmt.Pct1(p), Fmt.Pct1(recipe.FailChance), Mathf.RoundToInt(equipmentQuality * 100f));
            if (!ReplaceLine(ref text, failOld, failNew))
                Log.WarnOnce("wash fail oracle", "Wash: the dishwasher's fail chance line isn't what the game normally prints; kept the game's text.");

            // A process-time factor other than 1 (never seen on a dishwasher) fails this match on purpose: the game's text stays.
            string timeOld = TimeKey + TimeOfDayAzure.GetCurrentTime((float)secs / 3600f);
            string timeNew = TimeKey + TimeOfDayAzure.GetCurrentTime((float)secs / 3600f, simple: false).TrimEnd();
            if (!ReplaceLine(ref text, timeOld, timeNew))
                Log.WarnOnce("wash time oracle", "Wash: the dishwasher's process time line isn't what the game normally prints; kept the game's text.");

            if (text != target.text) target.text = text;
        }

        // Replaces old only where it is a whole line (followed by a line break or the end of the text).
        private static bool ReplaceLine(ref string text, string old, string now)
        {
            int i = text.IndexOf(old, System.StringComparison.Ordinal);
            if (i < 0) return false;
            int end = i + old.Length;
            if (end < text.Length && text[end] != '\n') return false;
            text = text.Substring(0, i) + now + text.Substring(end);
            return true;
        }
    }
}
