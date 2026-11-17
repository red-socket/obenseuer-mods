using BepInEx.Configuration;
using UnityEngine;

namespace FairMarket
{
    // Every tunable value (spec §10). Entries marked LaunchNote gate a patch group at launch: turning them off works at
    // once (patch bodies read Hot), turning them on needs a restart. Hot caches the values that patch bodies read on hot
    // paths and is refreshed on SettingChanged. Description strings are public text: humanized 2026-11-17; run them
    // through the humanizer again after any change.
    internal static class Cfg
    {
        private const string LaunchNote = " Restart the game after turning this on.";

        internal enum WeekLineMode { Never, WhenClosed, Always }

        internal static ConfigEntry<bool> VerboseLog;

        internal static class Stock
        {
            internal static ConfigEntry<bool> Enabled, OnlyWhereAlreadyStocked, ApplyToNextRestock;
            internal static ConfigEntry<string> Materials, Traders;
            internal static ConfigEntry<int> ExtraCopies;
        }

        internal static class Buyers
        {
            internal static ConfigEntry<bool> Enabled;
            internal static ConfigEntry<string> Canteen, SlumShack, Butcher, Restaurant, Custom;
        }

        internal static class Hours
        {
            internal static ConfigEntry<bool> Doors, Stalls;
            internal static ConfigEntry<WeekLineMode> WeekLine;
        }

        internal static class Dishwasher
        {
            internal static ConfigEntry<bool> PreciseOdds, SkippedNote;
        }

        internal static class Certificates
        {
            internal static ConfigEntry<bool> MatchFirst;
        }

        internal static class Invoices
        {
            internal static ConfigEntry<bool> Markers;
        }

        internal static class InvoiceList
        {
            internal static ConfigEntry<bool> Enabled, NotesPageKey, PauseMenuEntry, ShowExpiredScams, AllowDismissZero;
            internal static ConfigEntry<KeyboardShortcut> OpenKey;
            internal static ConfigEntry<int> ConfirmSeconds;
            internal static ConfigEntry<float> UiScale;
        }

        // The four default buyer entries (spec §6.2): config key, scene, Trade GUID, owner NPC id.
        internal sealed class BuyerSlot
        {
            internal string Key, Scene, Guid;
            internal int Owner;
            internal ConfigEntry<string> Entry;
        }

        internal static BuyerSlot[] BuyerSlots;

        // Cached switches read by patch bodies (spec §10). One field read per call instead of a ConfigEntry lookup.
        internal static class Hot
        {
            internal static bool Stock, ApplyNext, Buyers, HoursDoors, HoursStalls, PreciseOdds, SkipNote, CertFix, Markers,
                ListOn, NotesPageKey, ShowExpiredScams, AllowDismissZero;
            internal static WeekLineMode WeekLine;

            internal static void Refresh()
            {
                Stock = Cfg.Stock.Enabled.Value;
                ApplyNext = Cfg.Stock.ApplyToNextRestock.Value;
                Buyers = Cfg.Buyers.Enabled.Value;
                HoursDoors = Cfg.Hours.Doors.Value;
                HoursStalls = Cfg.Hours.Stalls.Value;
                WeekLine = Cfg.Hours.WeekLine.Value;
                PreciseOdds = Cfg.Dishwasher.PreciseOdds.Value;
                SkipNote = Cfg.Dishwasher.SkippedNote.Value;
                CertFix = Cfg.Certificates.MatchFirst.Value;
                Markers = Cfg.Invoices.Markers.Value;
                ListOn = Cfg.InvoiceList.Enabled.Value;
                NotesPageKey = Cfg.InvoiceList.NotesPageKey.Value;
                ShowExpiredScams = Cfg.InvoiceList.ShowExpiredScams.Value;
                AllowDismissZero = Cfg.InvoiceList.AllowDismissZero.Value;
            }
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLog = c.Bind("General", "VerboseLog", false,
                "Write extra detail to the BepInEx log: each shop list Fair Market changes, which traders get extra goods, the " +
                "opening hours found in each area, and each invoice paid or dismissed from the list.");

            Stock.Enabled = c.Bind("Stock", "Enabled", false,
                "Make building materials turn up more often in the shops listed under Traders. This changes the game's " +
                "balance: progression materials become easier to find, and the shop's other common goods turn up a bit less " +
                "often." + LaunchNote);
            Stock.Materials = c.Bind("Stock", "Materials", "16690, 12710, 35150, 15010, 20170, 19140, 14860, 17290",
                "Item IDs to make more common, separated by commas. The defaults are Rail, Leather Roll, Glass Pane, " +
                "Insulation Wool, Car Battery, Electric Motor, Tarp and Agar. Agar is a liquid, so it only turns up inside " +
                "containers, in shops that already sell it that way.");
            Stock.Traders = c.Bind("Stock", "Traders", "General Trader, One stop shop, Henning, OS Mining Services, Keke, Ari, Brom's, Kalle Ketku, Fredrik",
                "The shops whose stock lists get the extra copies, separated by commas. Add :N to a name to give that shop N " +
                "copies instead of ExtraCopies (for example Keke:4). Names: General Trader (General Store Bazaar), One stop " +
                "shop (General Store Trader), Henning (Hardware Store Stall), OS Mining Services, Keke (Keke Roinaneva), Ari " +
                "(Ari Pulsunmäki), Brom's (Broms), Kalle Ketku, Fredrik. The game's own list names work too. World container " +
                "lists (Storage, Industrial, SewingSupplies, LargeBag, MushroomsLanternFactory, MinesSteelCabinet) are " +
                "accepted with a warning: they change loot, not shops. The canteen's menu (Anna Markkanen) is never changed.");
            Stock.ExtraCopies = c.Bind("Stock", "ExtraCopies", 2,
                new ConfigDescription("How many extra copies of each material go into a shop's common list. The game " +
                    "picks one entry of the list at random for each shelf slot, so more copies mean better odds.",
                    new AcceptableValueRange<int>(1, 10)));
            Stock.OnlyWhereAlreadyStocked = c.Bind("Stock", "OnlyWhereAlreadyStocked", true,
                "Only boost a material in shops that can already stock it. Turn off to let the listed shops start selling " +
                "materials they never had.");
            Stock.ApplyToNextRestock = c.Bind("Stock", "ApplyToNextRestock", true,
                "The game decides each restock one restock ahead. With this on, the next restock of a changed shop uses the " +
                "new odds at once, instead of about two game days later." + LaunchNote);

            Buyers.Enabled = c.Bind("Buyers", "Enabled", false,
                "Let the traders below buy goods the game doesn't let them buy, at the game's normal prices and within the " +
                "money they have. This adds new ways to earn money." + LaunchNote);
            BuyerSlots = new[]
            {
                new BuyerSlot { Key = "Mines Canteen - Anna Markkanen", Scene = "Interior Mines Canteen", Guid = "41ea9fcb-a252-4998-97fa-f9bbbc1dc33b", Owner = 119 },
                new BuyerSlot { Key = "Slum Shack 5 - Ms Lotina", Scene = "Interior Bazaar Slum Shack 5", Guid = "ebb4bb72-379c-4f94-a692-69bf19b76374", Owner = 158 },
                new BuyerSlot { Key = "Butcher - Irma Kott", Scene = "Interior Bazaar Butchershop", Guid = "afe79480-28cc-43d9-8777-cea5d4a7bdbd", Owner = 148 },
                new BuyerSlot { Key = "Restaurant - Tonie Savisaar", Scene = "Interior Bazaar Restaurant", Guid = "9983c235-7f17-4d31-846b-0d68a009aae0", Owner = 279 },
            };
            const string tokens = " Entries are separated by commas: a game category name (exact case, such as Meat Raw), #ID for one " +
                "item, _item_ plus an item's English name, or ! before any of these to leave it out.";
            BuyerSlots[0].Entry = Buyers.Canteen = c.Bind("Buyers", BuyerSlots[0].Key, "Food, !Plant, !Animal, !CannedFood",
                "What the Mines canteen also buys." + tokens);
            BuyerSlots[1].Entry = Buyers.SlumShack = c.Bind("Buyers", BuyerSlots[1].Key, "Meat, #10500, #14130, #16510, #16520, #16530, #16540, #16550, #16560",
                "What Ms. Lotina in Slum Shack 5 also buys. The defaults are meat and the eight kinds of fish eggs. She buys " +
                "stolen goods, which the game already lets her do for raw meat, so she will also fence stolen meat and fish eggs." + tokens);
            BuyerSlots[2].Entry = Buyers.Butcher = c.Bind("Buyers", BuyerSlots[2].Key, "Meat Raw, #10500, #14130, #16510, #16520, #16530, #16540, #16550, #16560",
                "What Irma at the butcher's buys through her Sell option. The defaults are raw meat and the eight kinds of fish eggs. " +
                "She pays in RM, so this turns OC into RM: fish eggs bought from Saunaklonkku or meat bought at the One stop " +
                "shop sell to her for about 0.08-0.12 RM per OC you spent, a better rate than any exchange. To leave out the " +
                "fish eggs, use Meat Raw alone; to keep her as she is, leave this empty." + tokens);
            BuyerSlots[3].Entry = Buyers.Restaurant = c.Bind("Buyers", BuyerSlots[3].Key, "",
                "What Tonie Savisaar at the restaurant also buys. Empty by default: he is a customer who pays 1.5 times the " +
                "price in RM, so adding Food here makes easy money." + tokens);
            Buyers.Custom = c.Bind("Buyers", "Custom", "",
                "More traders, as Scene > Who: entries; with ; between traders. Who is the trader's name, the shop's name or " +
                "shared:<id>. Example: Interior Bazaar Bakery > Baker: Meat Raw. Certificate shops (the Gold Shop, Ari's " +
                "auction) and the Certificate category are refused. Your own cat is never bought.");

            Hours.Doors = c.Bind("Hours", "Doors", true,
                "Show opening hours on shop doors: \"open until 21:00\" while open, and when the shop opens next " +
                "while it's closed." + LaunchNote);
            Hours.Stalls = c.Bind("Hours", "Stalls", true,
                "Show \"open until\" on market stall traders that keep opening hours." + LaunchNote);
            Hours.WeekLine = c.Bind("Hours", "WeekLine", WeekLineMode.WhenClosed,
                "When to show the whole week's hours under a shop door's prompt: Never, WhenClosed or Always.");

            Dishwasher.PreciseOdds = c.Bind("Dishwasher", "PreciseOdds", true,
                "Show the dishwasher's real fail chance to one decimal, its base value, the machine's efficiency and the full " +
                "wash time." + LaunchNote);
            Dishwasher.SkippedNote = c.Bind("Dishwasher", "SkippedNote", true,
                "List the items in a dishwasher that it won't wash, and why." + LaunchNote);

            Certificates.MatchFirst = c.Bind("Certificates", "MatchFirst", true,
                "Fix a game bug (#5813): when you sell to a trader who needs certificates, use the certificate made for that " +
                "item first, and never use more certificates than items sold." + LaunchNote);

            Invoices.Markers = c.Bind("Invoices", "Markers", true,
                "Mark invoice letters that are scams (no penalty if you don't pay) or that no longer match a debt." + LaunchNote);

            InvoiceList.Enabled = c.Bind("InvoiceList", "Enabled", true,
                "The Invoices list: every unpaid invoice the game has on record, with a Pay button that has the same cost and " +
                "effects as the invoice machine. Paying doesn't use up the letter." + LaunchNote);
            InvoiceList.NotesPageKey = c.Bind("InvoiceList", "NotesPageKey", true,
                "On the Notes page, Drop (R, or Y on a controller) opens the Invoices list." + LaunchNote);
            InvoiceList.PauseMenuEntry = c.Bind("InvoiceList", "PauseMenuEntry", false,
                "Add \"Invoices\" to the pause menu. Takes effect the next time the pause menu is closed.");
            InvoiceList.OpenKey = c.Bind("InvoiceList", "OpenKey", KeyboardShortcut.Empty,
                "A key that opens the Invoices list. None by default. Fair Market ignores F1-F12, Insert, PageUp, PageDown, " +
                "Home, End, Delete, U, Left Alt, Shift, 1-9, the middle and side mouse buttons, controller buttons, the game's " +
                "own keys and our other mods' keys.");
            InvoiceList.ShowExpiredScams = c.Bind("InvoiceList", "ShowExpiredScams", false,
                "Show old scam letters (past their due date) when the list opens, instead of folding them under one row.");
            InvoiceList.AllowDismissZero = c.Bind("InvoiceList", "AllowDismissZero", false,
                "Add a Dismiss button to invoices for 0. It removes the record from your save for good, as if it had been " +
                "paid.");
            InvoiceList.ConfirmSeconds = c.Bind("InvoiceList", "ConfirmSeconds", 5,
                new ConfigDescription("How long Pay or Dismiss waits for the second press, in seconds.", new AcceptableValueRange<int>(2, 15)));
            InvoiceList.UiScale = c.Bind("InvoiceList", "UiScale", 1f,
                new ConfigDescription("Size of the Invoices list.", new AcceptableValueRange<float>(0.75f, 2f)));

            Hot.Refresh();
            CheckOpenKey();
            InvoiceList.OpenKey.SettingChanged += (s, a) => { CheckOpenKey(); Keys.Recheck(); };
        }

        // Keys the brief forbids (spec §10): our other mods, UnityExplorer, the console, screenshots, popular community
        // mods, the number row, U, Left Alt, Shift and the extra mouse buttons. Controller buttons are refused in Keys; the
        // D-pad is an axis in the legacy input manager, so it can't be bound here at all. A forbidden key is ignored
        // (Keys.Down), not just warned about.
        internal static bool Forbidden(KeyCode k) =>
            (k >= KeyCode.F1 && k <= KeyCode.F12) || (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) ||
            (k >= KeyCode.Keypad1 && k <= KeyCode.Keypad9) ||
            k == KeyCode.Insert || k == KeyCode.PageUp || k == KeyCode.PageDown || k == KeyCode.Home || k == KeyCode.End ||
            k == KeyCode.Delete || k == KeyCode.U || k == KeyCode.LeftAlt || k == KeyCode.LeftShift || k == KeyCode.RightShift ||
            k == KeyCode.Mouse2 || k == KeyCode.Mouse3 || k == KeyCode.Mouse4;

        private static void CheckOpenKey()
        {
            var k = InvoiceList.OpenKey.Value.MainKey;
            if (k != KeyCode.None && Forbidden(k))
                Log.Warn($"Keys: InvoiceList.OpenKey is {k}, which is reserved (another mod, the console, screenshots or the game); the key is ignored.");
        }
    }
}
