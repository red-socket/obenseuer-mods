using System.Collections.Generic;
using System.Globalization;

namespace FieldGuide
{
    // Every player-visible string (spec §6, §9.4). Public, static and not readonly, so the PT-BR translation mod can swap
    // them; the pop-ups are built at show time, so a swap applies from the next pop-up. Bullets follow the vanilla rules
    // hints (SRC/Ristiseiska.cs:233): sentence case, no closing full stop. Every fact here is checked against SRC and the
    // game data in design.md §6; a wording change must keep the facts. Humanizer pass: see design.md O5. The item panel
    // has rich text off (and PT-BR's text fit may turn it on), so panel strings never contain '<'.
    public static class Strings
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // ---- Pop-ups: shared ----
        // The panel's own footer already says how to hide it ("Hide hints" with the key), so this line only says how a
        // hidden guide comes back.
        public static string ReopenLine = "A hidden guide comes back with {key:Hints} at a station like this";
        public static string ReopenLineRoom = "A hidden guide comes back with {key:Hints} in a sauna like this";
        // A controller with a backpack worn, at a station: View opens the backpack there (D25). Once: Mode = UntilHidden.
        public static string PackLineOnce = "With a backpack on, View opens the backpack here. Once you leave, this guide stays hidden";
        public static string PackLine = "With a backpack on, View opens the backpack here, so it can't hide this guide";

        // ---- Sauna (spec §6.4) ----
        public static string SaunaTitle = "Sauna";
        public static string SaunaFuelStove = "Keep the stove lit and fed with fuel";
        public static string SaunaElectricStove = "The stove runs on electricity, so keep it powered";
        // {0} = the sauna's steam threshold in °C (60 in every sauna of 0.5.19).
        public static string SaunaSteamStove = "Throw water on the stove for steam. It only steams above {0} °C, so give the room time to heat up";
        public static string SaunaSteamRoom = "Throw water on the stove for steam. It only steams above {0} °C";
        public static string SaunaSteamFades = "Almost all the good comes from the steam, and it fades, so keep throwing water";
        public static string SaunaEffects = "Steam sobers you up, cleans you, eases depression and alcohol and smoking addiction, and makes you thirsty";
        public static string SaunaHeat = "Stay too long in the heat and you get hurt, and more steam heats you up faster. Cool off under a shower or in deep water";
        public static string SaunaSkill = "Your Sauna skill only grows while there's steam, and a higher skill lets you stay in longer";

        // ---- Mashing (spec §6.5) ----
        public static string MashTitle = "Mashing";
        public static string MashInputs = "Put the ingredients, yeast and water in the storage. Liquids stay in their containers";
        public static string MashYeast = "Green mushrooms in place of yeast make a glowing mash. Speed yeast is faster but makes worse mash";
        public static string MashNeedsFuel = "It needs fuel and has to be switched on before Start works. Switching it off or running out of fuel only pauses it";
        public static string MashNeedsPower = "It needs power and has to be switched on before Start works. Switching it off or running out of power only pauses it";
        public static string MashFail = "A better vessel and more Mashing skill mean fewer failures. Being drunk means more";
        public static string MashWater = "Spring or pure water makes better mash. Dirty, shroom or sea water makes it worse";
        public static string MashFill = "Pressing Abort ruins the batch. When it's done, press Fill to pour the mash into empty containers in the storage";

        // ---- Bottle corker (spec §6.6) ----
        public static string CorkerTitle = "Bottle corker";
        public static string CorkerInputs = "Each bottle takes an empty bottle, a printed label, some glue and the drink in its container. No corks needed";
        public static string CorkerBottles = "Beer, ale, sahti, kumis and orb brew go in glass bottles, moonshine in clear bottles, and wine and cider in wine bottles";
        public static string CorkerAmmo = "A full sheet of labels does 6 bottles, and a full bottle of glue does 10";
        public static string CorkerNoPower = "It needs no power. ";
        public static string CorkerOne = "It bottles one at a time";
        // {0} = manufacturing.AllowSimultaneous of the opened corker.
        public static string CorkerMany = "It bottles up to {0} at a time";
        public static string CorkerWindow = ", and only while this window is open";
        public static string CorkerBreak = "Now and then a bottle breaks into glass shards";

        // ---- Biogas reactor (spec §6.7) ----
        public static string BiogasTitle = "Biogas reactor";
        public static string BiogasInputs = "Feed it shit, sawdust, paper trash, crumpled paper, junk wood, juniper branches or plant matter. Sewage, bottoms and ruined mash work too";
        public static string LongNoPower = "It needs no power and never fails, but a batch takes a long time";
        public static string LongPower = "It never fails, but a batch takes a long time";
        public static string BiogasWaste = "Each batch leaves moldy waste, which you can burn as fuel or compost";
        // Bullets 4-7 ship only after in-game test T-H5 confirms them (D8, HintTexts.GasConfirmed).
        public static string BiogasTank = "While a batch runs, the gas tank fills up";
        public static string BiogasBottle = "An empty gas bottle in the compressor gets filled once the tank is full, or when you switch the compressor on";
        public static string BiogasCompressor = "The compressor needs electricity";
        public static string BiogasUse = "Full gas bottles fuel gas furnaces and brick furnaces";

        // ---- Compost (spec §6.8, D1) ----
        public static string CompostTitle = "Compost";
        public static string CompostInputs = "Fill it with moldy waste, shit, sawdust, paper trash, crumpled paper, junk wood, juniper branches, plant matter or feathers. Bottoms and ruined mash work too";
        public static string CompostAsh = "Adding ash means less waste per batch";
        public static string CompostOne = "One batch at a time";
        // {0} = manufacturing.AllowSimultaneous of the opened compost.
        public static string CompostMany = "Up to {0} batches can run at once";
        public static string CompostSoil = "Each batch gives fertile soil, which you need to build planters";

        // ---- Bought by (spec §9.4) ----
        public static string BoughtBy = "Bought by:";
        public static string AlsoBoughtBy = "Also bought by:";
        public static string Separator = " · ";
        public static string NeedsCertificate = "needs certificate";
        public static string FairMarketNote = "Fair Market's extra buyers are listed only for traders in this place";

        public static string Price(string price, bool certificate) => certificate ? price + ", " + NeedsCertificate : price;
        public static string Oc(float p) => p + " OC";
        public static string Rm(string shown) => shown + " RM";
        public static string More(int n) => "+" + n.ToString(C) + " more";
        public static string Unvisited(int h) => "+" + h.ToString(C) + " in places you haven't been";
        public static string UnvisitedOnly(int h) => h == 1 ? "1 trader in places you haven't been" : h.ToString(C) + " traders in places you haven't been";
        public static string Count(int n, bool also) =>
            (also ? "Also bought by " : "Bought by ") + (n == 1 ? "1 trader" : n.ToString(C) + " traders");

        // Trader names are the game's own strings, typos included ("Selmas's Stall"), so they match PT-BR's catalogue.
        // PT-BR (or any mod) can fill this to rename them; empty by default.
        public static Dictionary<string, string> NameMap = new Dictionary<string, string>();

        internal static string Name(string name) => name != null && NameMap.TryGetValue(name, out var n) && !string.IsNullOrEmpty(n) ? n : name;
    }
}
