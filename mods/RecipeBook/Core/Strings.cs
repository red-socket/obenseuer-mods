using System.Globalization;

namespace RecipeBook
{
    // Every player-facing string (spec §3). Humanized 2026-11-17; run changed text through the humanizer again (CLAUDE.md). Text that
    // goes into the game's own TMP fields uses ASCII plus the middle dot Workshop already shows in the same fonts. The
    // controls-line hints are English upper case like the game's own ("UP)FILTER"), exempt from humanizing beyond
    // spelling (spec §8.2). The item panel's description has rich text off, so panel lines carry no tags.
    internal static class Strings
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // ---- Search bar (spec §6) ----
        internal const string SearchPlaceholder = "Search recipes";
        internal const string NoMatch = "No recipes match";
        internal const string ChipAll = "All";
        internal const string ChipReady = "Ready";
        internal const string ChipSkillMet = "Skill met";
        internal static string Chip(string value, int visible) => value + " · " + visible.ToString(C);

        // Skill names for the Show chip: the game's enum names, as the game shows them in the Character page.
        internal static string Skill(PlayerStats.PlayerSkillType t) => t.ToString();

        // ---- Controls hints (spec §8.2) ----
        internal const string HintFilter = "FILTER";
        internal const string HintShow = "SHOW";
        internal const string HintReset = "RESET";
        internal const string HintSearch = "SEARCH";
        internal const string PadUpFilter = "   UP)FILTER";
        internal const string PadLeftRightShow = "   LEFT/RIGHT)SHOW";
        // The game's own entry for the Use Item key (SRC/InventoryNavigationHandler.cs:447), taken out while on the bar.
        internal const string GameUseItem = "USE ITEM";

        // ---- Badges (spec §9) ----
        private static readonly string[] FailCache = BuildFail();

        private static string[] BuildFail()
        {
            var a = new string[101];
            for (int i = 0; i <= 100; i++) a[i] = "Fail " + i.ToString(C) + "%";
            return a;
        }

        internal static string Fail(int pct) => pct >= 0 && pct <= 100 ? FailCache[pct] : "Fail " + pct.ToString(C) + "%";
        internal static string FailPrecise(string pct) => "Fail " + pct;
        internal const string Lv = "Lv";
        internal const string HardMark = "!";

        // Short skill names for badges on stations that mix skills (D11).
        internal static string SkillShort(PlayerStats.PlayerSkillType t)
        {
            switch (t)
            {
                case PlayerStats.PlayerSkillType.Lockpicking: return "Lock";
                case PlayerStats.PlayerSkillType.Drinking: return "Drink";
                case PlayerStats.PlayerSkillType.Cooking: return "Cook";
                case PlayerStats.PlayerSkillType.Mashing: return "Mash";
                case PlayerStats.PlayerSkillType.Brewing: return "Brew";
                case PlayerStats.PlayerSkillType.Distilling: return "Distil";
                case PlayerStats.PlayerSkillType.Farming: return "Farm";
                case PlayerStats.PlayerSkillType.Malting: return "Malt";
                case PlayerStats.PlayerSkillType.Manufacturing: return "Manuf";
                case PlayerStats.PlayerSkillType.Paperwork: return "Paper";
                case PlayerStats.PlayerSkillType.Chemistry: return "Chem";
                case PlayerStats.PlayerSkillType.Carpentry: return "Carp";
                case PlayerStats.PlayerSkillType.Machining: return "Mach";
                case PlayerStats.PlayerSkillType.Engineering: return "Eng";
                case PlayerStats.PlayerSkillType.Bribery: return "Bribe";
                case PlayerStats.PlayerSkillType.Cleaning: return "Clean";
                case PlayerStats.PlayerSkillType.Sewing: return "Sew";
                case PlayerStats.PlayerSkillType.Electronics: return "Elec";
                case PlayerStats.PlayerSkillType.Mining: return "Mine";
                case PlayerStats.PlayerSkillType.Music: return "Music";
                case PlayerStats.PlayerSkillType.Smithing: return "Smith";
                case PlayerStats.PlayerSkillType.Medical: return "Med";
                case PlayerStats.PlayerSkillType.Computers: return "Comp";
                case PlayerStats.PlayerSkillType.Sauna: return "Sauna";
                default: return Lv;
            }
        }

        // ---- Used in (spec §10) ----
        internal const string UsedIn = "Used in:";
        internal static string More(int n) => n > 0 ? " (+" + n.ToString(C) + " more)" : "";
        internal static string UsedInCount(int n) => n == 1 ? "Used in 1 recipe" : "Used in " + n.ToString(C) + " recipes";

        // ---- Recipes remaining (spec §11) ----
        internal const string NewLeftLabel = "New recipes left:";
        internal const string NoneLeft = "No new recipes left";
        internal static string NewLeft(int left, int total) => left.ToString(C) + " of " + total.ToString(C);
        internal static string UnlockTip(int n) => n == 1 ? " (1 unlocks a new craft)" : " (" + n.ToString(C) + " unlock new crafts)";
        internal static string UnlockPanel(int n) => n == 1 ? " (1 new craft)" : " (" + n.ToString(C) + " new crafts)";
        internal static string NewLeftShort(int left, int total) => "New recipes: " + left.ToString(C) + "/" + total.ToString(C);
    }
}
