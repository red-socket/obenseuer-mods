namespace MessageLog
{
    // Every player-facing literal of the window (spec §8.8). Each one is an expression-bodied getter or method, never a
    // const (inlined into other types) or a static readonly (set in the .cctor, which can run before a transpiler lands),
    // so a later PT-BR ModStrings target on MessageLog.Strings can rewrite them in place. Our own literals are never sent
    // through the Translator bridge. Public text: run through the humanizer again after any change (CLAUDE.md).
    internal static class Strings
    {
        // ---- Window ----
        internal static string Title => "Message Log";
        internal static string Close => "Close";
        internal static string Shown(string shown, string total) => shown + " shown of " + total;
        internal static string Empty => "Nothing logged for this save yet.";
        internal static string NoMatch => "No messages match.";
        internal static string Gap => "Some messages are missing here";
        internal static string You => "You";
        internal static string Times(int n) => " ×" + Fmt.Count(n);

        // ---- Chips (spec §5.2, D7) ----
        internal static string ChipAll => "ALL";
        internal static string ChipCrime => "CRIME";
        internal static string ChipMoney => "MONEY";
        internal static string ChipMail => "MAIL";
        internal static string ChipQuests => "QUESTS";
        internal static string ChipItems => "ITEMS";
        internal static string ChipDialogue => "DIALOGUE";
        internal static string ChipOther => "OTHER";

        // Category names in the detail strip.
        internal static string CatCrime => "Crime";
        internal static string CatMoney => "Money";
        internal static string CatMail => "Mail";
        internal static string CatQuests => "Quests";
        internal static string CatItems => "Items";
        internal static string CatDialogue => "Dialogue";
        internal static string CatOther => "Other";

        // ---- Search and pin ----
        internal static string SearchLabel => "Search:";
        internal static string Clear => "Clear";
        internal static string Only(string who) => "Only: " + who;
        internal static string Unpin => "x";
        internal static string SearchPad => "Search needs a keyboard. LB/RB filter by type, and Y shows only messages from the same person or of the same kind.";
        internal static string SearchOff => "Search is unavailable this session.";
        internal static string SearchStop => "Press Enter to search";

        // ---- Footer, in the game's own hint style ("B)BACK") ----
        internal static string PadFooter => "B)BACK   LB/RB)FILTER   Y)ONLY THIS   LT/RT)PAGE";
        internal static string BackHint => "BACK";
        internal static string FilterHint => "FILTER";
        internal static string OnlyHint => "ONLY THIS";
        internal static string SearchHint => "ENTER)SEARCH";

        // The Notes page hint ("F)MESSAGES", pad "X)MESSAGES").
        internal static string HintLabel => "MESSAGES";

        // ---- Time ----
        internal static string DayWord => "Day";
        internal static string Weekday(int wd)
        {
            switch (((wd % 7) + 7) % 7)
            {
                case 0: return "Mon";
                case 1: return "Tue";
                case 2: return "Wed";
                case 3: return "Thu";
                case 4: return "Fri";
                case 5: return "Sat";
                default: return "Sun";
            }
        }
    }
}
