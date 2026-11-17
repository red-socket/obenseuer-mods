using BepInEx.Configuration;

namespace RecipeBook
{
    // Every tunable value (spec §13). Entries marked LaunchNote gate a patch group at launch: turning them off works at
    // once (patch bodies read Hot), turning them on needs a restart. Hot caches the values that patch bodies read on hot
    // paths and is refreshed on SettingChanged; Version counts refreshes so text caches can key on it. Description
    // strings are public text: humanized 2026-11-17; run any changed text through the humanizer again (CLAUDE.md).
    internal static class Cfg
    {
        private const string LaunchNote = " Restart the game after turning this on.";

        internal static ConfigEntry<bool> VerboseLog;

        internal static class Search
        {
            internal static ConfigEntry<bool> Enabled, TextField, MatchIngredients, FilterChip, ControllerRoute, ShowHints;
        }

        internal static class Badges
        {
            internal static ConfigEntry<bool> Enabled, FailChance, SkillRequirement;
            internal static ConfigEntry<int> WarnAt, StrongAt, FontSize, Width;
        }

        internal static class Lines
        {
            internal static ConfigEntry<bool> Tooltip, ItemPanel;
        }

        internal static class UsedIn
        {
            internal static ConfigEntry<bool> Enabled, KnownOnly;
            internal static ConfigEntry<int> TooltipNames, PanelNames;
        }

        internal static class Remaining
        {
            internal static ConfigEntry<bool> Enabled;
        }

        internal static class Discovery
        {
            internal static ConfigEntry<bool> ReadablesAlwaysNew, BooksRerollKnown;
        }

        // Cached values read by patch bodies (spec §13). One field read per call instead of a ConfigEntry lookup.
        internal static class Hot
        {
            internal static bool SearchOn, TextField, MatchIngredients, Chip, Pad, Hints;
            internal static bool BadgesOn, BadgeFail, BadgeSkill;
            internal static int WarnAt, StrongAt, FontSize, Width;
            internal static bool LinesTooltip, LinesPanel, UsedInOn, KnownOnly, RemainingOn;
            internal static int TooltipNames, PanelNames;
            internal static bool ReadablesAlwaysNew, BookReroll;

            // Bumped on every refresh: the tooltip, panel and search key caches compare it.
            internal static int Version;

            internal static void Refresh()
            {
                SearchOn = Search.Enabled.Value;
                TextField = Search.TextField.Value;
                MatchIngredients = Search.MatchIngredients.Value;
                Chip = Search.FilterChip.Value;
                Pad = Search.ControllerRoute.Value;
                Hints = Search.ShowHints.Value;
                BadgesOn = Badges.Enabled.Value;
                BadgeFail = Badges.FailChance.Value;
                BadgeSkill = Badges.SkillRequirement.Value;
                WarnAt = Badges.WarnAt.Value;
                StrongAt = Badges.StrongAt.Value;
                FontSize = Badges.FontSize.Value;
                Width = Badges.Width.Value;
                LinesTooltip = Lines.Tooltip.Value;
                LinesPanel = Lines.ItemPanel.Value;
                UsedInOn = UsedIn.Enabled.Value;
                KnownOnly = UsedIn.KnownOnly.Value;
                TooltipNames = UsedIn.TooltipNames.Value;
                PanelNames = UsedIn.PanelNames.Value;
                RemainingOn = Remaining.Enabled.Value;
                ReadablesAlwaysNew = Discovery.ReadablesAlwaysNew.Value;
                BookReroll = Discovery.BooksRerollKnown.Value;
                Version++;
            }
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLog = c.Bind("General", "VerboseLog", false,
                "Write extra detail to the BepInEx log: each filter change, typing lock and release, controller route " +
                "step, the used-in index build and each recipe book re-roll.");

            Search.Enabled = c.Bind("Search", "Enabled", true,
                "Adds a search bar above the recipe list at crafting stations. It only hides recipe buttons and doesn't " +
                "change crafting." + LaunchNote);
            Search.TextField = c.Bind("Search", "TextField", true,
                "Adds a text box to the bar. While you type in it, game keys are blocked." + LaunchNote);
            Search.MatchIngredients = c.Bind("Search", "MatchIngredients", true,
                "The search also matches ingredient and product names, so \"lettuce\" finds what you can make with it.");
            Search.FilterChip = c.Bind("Search", "FilterChip", true,
                "Adds a Show button next to the text box. It picks All, Ready (you can craft it now), Skill met, or one " +
                "skill on stations that mix skills. Left click goes forward, right click goes back.");
            Search.ControllerRoute = c.Bind("Search", "ControllerRoute", true,
                "Press Up on the top recipe to reach the bar. There, Left and Right change Show, A resets the search and the filter, and " +
                "Down goes back to the list. On a keyboard, Use Item on the bar puts the cursor in the text box." + LaunchNote);
            Search.ShowHints = c.Bind("Search", "ShowHints", true,
                "Show the search bar's buttons in the controls line at the bottom of the screen.");

            Badges.Enabled = c.Bind("Badges", "Enabled", true,
                "Show the fail chance and the skill level needed on recipe buttons." + LaunchNote);
            Badges.FailChance = c.Bind("Badges", "FailChance", true,
                "Show the fail chance. It's the same number as in the recipe panel.");
            Badges.SkillRequirement = c.Bind("Badges", "SkillRequirement", true,
                "Show your level and the level needed when a recipe needs level 2 or more. \"!\" means the game won't " +
                "let you craft it until you reach that level.");
            Badges.WarnAt = c.Bind("Badges", "WarnAt", 25,
                new ConfigDescription("Fail chance (%) from which the number turns amber.", new AcceptableValueRange<int>(1, 100)));
            Badges.StrongAt = c.Bind("Badges", "StrongAt", 50,
                new ConfigDescription("Fail chance (%) from which the number is also bold.", new AcceptableValueRange<int>(1, 100)));
            Badges.FontSize = c.Bind("Badges", "FontSize", 11,
                new ConfigDescription("Badge text size.", new AcceptableValueRange<int>(8, 16)));
            Badges.Width = c.Bind("Badges", "Width", 60,
                new ConfigDescription("Space in pixels kept free for the badge at the right end of a recipe button.", new AcceptableValueRange<int>(40, 100)));

            Lines.Tooltip = c.Bind("Lines", "Tooltip", true,
                "Add \"Used in\" and \"New recipes left\" to item tooltips (mouse)." + LaunchNote);
            Lines.ItemPanel = c.Bind("Lines", "ItemPanel", true,
                "Add the same lines to the inventory's item panel, which also works with a controller. They get shorter " +
                "or are left out when the item's description needs the room." + LaunchNote);

            UsedIn.Enabled = c.Bind("UsedIn", "Enabled", true,
                "Show which recipes use an item.");
            UsedIn.KnownOnly = c.Bind("UsedIn", "KnownOnly", true,
                "Only list recipes you can already see at a station: recipes that need no recipe paper, and the ones you " +
                "have found or crafted. Turn off to list every recipe, including ones you haven't found yet.");
            UsedIn.TooltipNames = c.Bind("UsedIn", "TooltipNames", 5,
                new ConfigDescription("How many recipe names the tooltip shows before \"+N more\".", new AcceptableValueRange<int>(1, 10)));
            UsedIn.PanelNames = c.Bind("UsedIn", "PanelNames", 3,
                new ConfigDescription("How many recipe names the item panel shows at most (fewer if they don't fit).", new AcceptableValueRange<int>(1, 5)));

            Remaining.Enabled = c.Bind("Remaining", "Enabled", true,
                "On magazines and books, show how many of the recipes they can teach are still new to you. It shows " +
                "only the numbers, not the recipe names.");

            Discovery.ReadablesAlwaysNew = c.Bind("Discovery", "ReadablesAlwaysNew", false,
                "Magazines and books always pick a recipe you don't have while any are left. In the game they do this 80% " +
                "of the time. This changes the game's balance: you find recipes faster." + LaunchNote);
            Discovery.BooksRerollKnown = c.Bind("Discovery", "BooksRerollKnown", false,
                "Recipe papers lying in the world swap a recipe you already have for one you don't, picked from the same " +
                "paper's own list. Only papers rolled from now on (first visits and respawns) change; ones your save " +
                "already has keep their recipe. This changes the game's balance: you find recipes faster." + LaunchNote);

            Hot.Refresh();
        }
    }
}
