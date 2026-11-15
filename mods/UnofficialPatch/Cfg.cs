using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace UnofficialPatch
{
    // Every tunable value (spec §14). Entries are read live inside every patch body, so turning most fixes off takes
    // effect at once. A fix that was off at launch has no patches applied; it starts at the next launch. The item,
    // recipe and shelf data fixes (IceCreamFix, CoffeeFix, CoffeeVolumeFix, FixShelfFilters) change game data in place
    // and are not reverted mid-session: switching them off takes effect at the next launch (DataNote). No hotkeys.
    internal static class Cfg
    {
        private const string LaunchNote = " If this was off when the game started, it starts at the next launch.";
        private const string DataNote = " Switching it on or off takes effect at the next launch.";

        internal static ConfigEntry<bool> VerboseLogging;

        internal static ConfigEntry<bool> BiweeklyFix;

        internal static ConfigEntry<bool> HoldPayoutWhenFrozen, MergeDuplicateAccounts;

        internal static ConfigEntry<bool> RestoreExchangeTuning;
        internal static ConfigEntry<string> RestoreBanks;

        internal static ConfigEntry<bool> KeyboardMouseOnly, AxisDeadzone;
        internal static ConfigEntry<float> ControllerSwitchThreshold, NavigationDeadzone;

        internal static ConfigEntry<bool> BlockGameKeysWhileOpen, GuardPauseMenu;

        internal static ConfigEntry<bool> KeepSprintToggle, KeepCrouchToggle, WarnKeyConflicts;

        internal static ConfigEntry<bool> ReapplyBrightnessOnSceneLoad, ReapplyBrightnessAfterEffects;

        internal static ConfigEntry<bool> ClearMachineResidue, ClearTankResidue;
        internal static ConfigEntry<float> ResidueLitres;

        internal static ConfigEntry<bool> IceCreamFix, CoffeeFix, HealExistingItems, CoffeeVolumeFix;
        internal static ConfigEntry<float> IceCreamRefrigerationFactor, CoffeeRefrigerationFactor, CoffeeRecipeOutputLitres;
        internal static ConfigEntry<string> CoffeeItemIds;

        internal static ConfigEntry<bool> FixShelfFilters, LogFailedRestock;

        internal static ConfigEntry<bool> HuntRats, ProtectOwnedCatsFromMachines;
        internal static ConfigEntry<string> HuntTargets;

        internal static ConfigEntry<bool> AchievementDoctor, ResetOnNewGame, ResubmitPending, RecheckApartmentLevels, ReportOnly;
        internal static ConfigEntry<string> NeverResubmit;

        internal static ConfigEntry<bool> KeepWallSafetyDuringRenovation;

        // Parsed lists, rebuilt when the entry changes.
        internal static HashSet<string> RestoreBankSet = new HashSet<string>(StringComparer.Ordinal);
        internal static HashSet<int> CoffeeIds = new HashSet<int>();
        internal static HashSet<Stats.Type> HuntTypes = new HashSet<Stats.Type>();
        internal static HashSet<string> NeverResubmitSet = new HashSet<string>(StringComparer.Ordinal);

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Also log routine details: cat hunts started, corrected item counts, console guard on and off, minor restock failures.");

            BiweeklyFix = c.Bind("Rent", "BiweeklyFix", true,
                "Biweekly rent is paid every second Saturday instead of every Saturday. The game charged half a month's rent each " +
                "week, which is twice the monthly rent. Loan downpayments on Biweekly follow the same schedule. Only Custom " +
                "difficulty can pick Biweekly." + LaunchNote);

            HoldPayoutWhenFrozen = c.Bind("ScamInvoices", "HoldPayoutWhenFrozen", true,
                "When a scam invoice pays into a frozen bank account, the money stays in that account and you get an \"Account frozen\" " +
                "notice. Without this, the game opens a hidden second account with the same name and says the money was added." + LaunchNote);
            MergeDuplicateAccounts = c.Bind("ScamInvoices", "MergeDuplicateAccounts", true,
                "On load, move the money and debt from hidden duplicate bank accounts into the real account with the same name." + LaunchNote);

            RestoreExchangeTuning = c.Bind("Banks", "RestoreExchangeTuning", false,
                "On load, use the game's current income, spending and exchange ratios for the listed banks instead of older values " +
                "stored in the save (possible cause of #6537). Money supply is kept. The game then saves the restored values, so " +
                "this stays after removing the mod. Not confirmed on any real save; off by default." + LaunchNote);
            RestoreBanks = c.Bind("Banks", "RestoreBanks", "Money exchange,Casino",
                "Comma-separated bank names the restore applies to.");

            KeyboardMouseOnly = c.Bind("InputGuard", "KeyboardMouseOnly", false,
                "Never switch to controller prompts and navigation, and ignore joystick axes in menus, on ladders, in the " +
                "quick-slot bar and in the card game. For players without a gamepad whose mouse, wheel or other device makes " +
                "the inventory selection roll (#5811). A gamepad no longer works in those places while this is on." + LaunchNote);
            AxisDeadzone = c.Bind("InputGuard", "AxisDeadzone", false,
                "Use the two values below for joystick axes in menus: small stick drift is ignored, and only a strong push " +
                "switches to controller prompts. Ladders, the quick-slot bar and the card game keep the game's own stick " +
                "handling." + LaunchNote);
            ControllerSwitchThreshold = c.Bind("InputGuard", "ControllerSwitchThreshold", 0.9f,
                new ConfigDescription("How far a stick must move to switch to controller prompts (the game uses 0.5).",
                    new AcceptableValueRange<float>(0.5f, 1f)));
            NavigationDeadzone = c.Bind("InputGuard", "NavigationDeadzone", 0.35f,
                new ConfigDescription("Stick and D-pad values below this count as zero in menus.", new AcceptableValueRange<float>(0f, 0.9f)));

            BlockGameKeysWhileOpen = c.Bind("Console", "BlockGameKeysWhileOpen", true,
                "While the F1 console is open, keys you type no longer trigger game actions (#5942). Releasing a held key still " +
                "counts, so you never get stuck sprinting or crouching." + LaunchNote);
            GuardPauseMenu = c.Bind("Console", "GuardPauseMenu", true,
                "While the console is open, typing X or pressing Enter no longer clicks pause menu buttons." + LaunchNote);

            KeepSprintToggle = c.Bind("Controls", "KeepSprintToggle", true,
                "With toggle sprint, sprinting stays on after a door, sleep or jail transfer." + LaunchNote);
            KeepCrouchToggle = c.Bind("Controls", "KeepCrouchToggle", true,
                "With toggle crouch, crouching stays on after a door, sleep or jail transfer (not when you arrive in water).");
            WarnKeyConflicts = c.Bind("Controls", "WarnKeyConflicts", true,
                "Write a warning to the log when two actions that are used at the same time share a key, or an action uses a key " +
                "the game reserves (F1, F11, F12, 1-4)." + LaunchNote);

            ReapplyBrightnessOnSceneLoad = c.Bind("Display", "ReapplyBrightnessOnSceneLoad", false,
                "Apply your brightness setting again after every load. The game already does this; turn it on only if your " +
                "brightness still resets after a door.");
            ReapplyBrightnessAfterEffects = c.Bind("Display", "ReapplyBrightnessAfterEffects", false,
                "Apply your brightness setting again after SMV and Wasteland visual effects switch on or off, which resets it to " +
                "the default. Not yet confirmed in game; off by default." + LaunchNote);

            ClearMachineResidue = c.Bind("Liquids", "ClearMachineResidue", true,
                "When you open a still, mashing tun or similar machine, remove trace liquid it cannot hand out, and round " +
                "amounts just under 0.1 L up to 0.1 L so you can drain them into a container." + LaunchNote);
            ClearTankResidue = c.Bind("Liquids", "ClearTankResidue", true,
                "Empty tanks and sinks that hold only a trace of liquid, so another liquid can go in and they can be picked up. " +
                "Tanks that are being filled by an upgrade are left alone." + LaunchNote);
            ResidueLitres = c.Bind("Liquids", "ResidueLitres", 0.05f,
                new ConfigDescription("Amounts up to this many litres count as a trace.", new AcceptableValueRange<float>(0.001f, 0.09f)));

            IceCreamFix = c.Bind("Spoilage", "IceCreamFix", true,
                "Ice cream and ice cream cones keep in a freezer (#5905). The game needed more cold than a freezer gives." + DataNote);
            IceCreamRefrigerationFactor = c.Bind("Spoilage", "IceCreamRefrigerationFactor", 1.0f,
                new ConfigDescription("Cold needed to stop ice cream spoiling. A freezer gives 1.0; the game used 1.2.", new AcceptableValueRange<float>(0.1f, 1.2f)));
            CoffeeFix = c.Bind("Spoilage", "CoffeeFix", true,
                "Coffee stops going stale in a fridge or freezer. The game ignored cold for all coffees." + DataNote);
            CoffeeRefrigerationFactor = c.Bind("Spoilage", "CoffeeRefrigerationFactor", 0.5f,
                new ConfigDescription("Cold needed to stop coffee going stale (Plörö uses 0.5).", new AcceptableValueRange<float>(0.1f, 2f)));
            CoffeeItemIds = c.Bind("Spoilage", "CoffeeItemIds", "13380,13530,13540,13550",
                "Item IDs the coffee fix applies to: Coffee, Mushroom Coffee, Special Coffee, Milk Coffee.");
            HealExistingItems = c.Bind("Spoilage", "HealExistingItems", true,
                "Also correct ice cream and coffee you already have. Spoilage that already happened stays." + LaunchNote);
            CoffeeVolumeFix = c.Bind("Spoilage", "CoffeeVolumeFix", false,
                "A coffee maker brew gives 0.2 L, exactly one mug. The game brews 0.3 L: with one mug loaded the extra " +
                "0.1 L is lost, but with two or more mugs it fills a second mug too, so in that case this gives less coffee " +
                "per brew. Off by default." + DataNote);
            CoffeeRecipeOutputLitres = c.Bind("Spoilage", "CoffeeRecipeOutputLitres", 0.2f,
                new ConfigDescription("Litres of coffee per brew.", new AcceptableValueRange<float>(0.1f, 1f)));

            FixShelfFilters = c.Bind("Vendors", "FixShelfFilters", true,
                "Shop shelves whose item filter still uses an old item name restock again (the Rexus tablet shelf in the Bazaar " +
                "electronics store)." + DataNote);
            LogFailedRestock = c.Bind("Vendors", "LogFailedRestock", true,
                "Write a warning to the log when a shop shelf refuses an item during restock." + LaunchNote);

            HuntRats = c.Bind("Cats", "HuntRats", true,
                "Cats hunt rats again (#6213). The game stopped every hunt because cats never get hungry." + LaunchNote);
            HuntTargets = c.Bind("Cats", "HuntTargets", "Rat",
                "Comma-separated animal types cats may hunt: Rat, Chick, Bird, Chicken. Hunting chicks, birds or chickens puts " +
                "your livestock at risk. The player is never added.");
            ProtectOwnedCatsFromMachines = c.Bind("Cats", "ProtectOwnedCatsFromMachines", false,
                "Your own cats take no damage from crafting machines such as the Small Jaw Crusher (#5978)." + LaunchNote);

            AchievementDoctor = c.Bind("AchievementDoctor", "Enabled", false,
                "Fix achievements the game failed to unlock or carried over between saves. Steam unlocks cannot be undone." + LaunchNote);
            ResetOnNewGame = c.Bind("AchievementDoctor", "ResetOnNewGame", true,
                "When you start a new game, reset achievement progress to the game's defaults, so addiction-cured achievements no " +
                "longer unlock at character creation (#6227) and counters from the previous save don't carry over.");
            ResubmitPending = c.Bind("AchievementDoctor", "ResubmitPending", true,
                "After a load, send Steam the achievements your save says you earned but Steam never received (#6464).");
            RecheckApartmentLevels = c.Bind("AchievementDoctor", "RecheckApartmentLevels", true,
                "After a load, unlock the \"all apartments at level N\" achievements you already qualify for (#6494).");
            ReportOnly = c.Bind("AchievementDoctor", "ReportOnly", false,
                "Only write to the log what would be unlocked; send nothing to Steam.");
            NeverResubmit = c.Bind("AchievementDoctor", "NeverResubmit",
                "SMV_CURED,DUCK_ADDICTION_CURED,ALCOHOL_ADDICTION_CURED,SMOKING_ADDICTION_CURED,SHROOM_ADDICTION_CURED,GAMBLING_ADDICTION_CURED,ALL_ADDICTIONS_CURED",
                "Comma-separated achievement IDs that are never sent again after a load, because older saves may have them marked by mistake.");

            KeepWallSafetyDuringRenovation = c.Bind("SafeBeds", "KeepWallSafetyDuringRenovation", false,
                "While your apartment walls are being renovated, beds in your apartment keep the safety your finished walls " +
                "give (Teddy bear cameras included). It never makes a bed safer than your finished walls, and outside a " +
                "renovation your walls upgrade decides bed safety as usual. Off by default, because the game may intend the " +
                "flat to be unprotected during the work." + LaunchNote);

            Parse();
            RestoreBanks.SettingChanged += (s, a) => Parse();
            CoffeeItemIds.SettingChanged += (s, a) => Parse();
            HuntTargets.SettingChanged += (s, a) => Parse();
            NeverResubmit.SettingChanged += (s, a) => Parse();
        }

        private static void Parse()
        {
            var banks = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in Split(RestoreBanks.Value)) banks.Add(p);
            RestoreBankSet = banks;

            var ids = new HashSet<int>();
            foreach (var p in Split(CoffeeItemIds.Value))
                if (int.TryParse(p, out int id)) ids.Add(id);
                else Log.Warn($"Spoilage: CoffeeItemIds entry \"{p}\" is not a number; ignored.");
            CoffeeIds = ids;

            var types = new HashSet<Stats.Type>();
            foreach (var p in Split(HuntTargets.Value))
            {
                if (!Enum.TryParse(p, true, out Stats.Type t)) Log.Warn($"Cats: HuntTargets entry \"{p}\" is not an animal type; ignored.");
                else if (t == Stats.Type.Player || t == Stats.Type.Human) Log.Warn($"Cats: HuntTargets entry \"{p}\" is never allowed; ignored.");
                else types.Add(t);
            }
            HuntTypes = types;

            var never = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in Split(NeverResubmit.Value)) never.Add(p);
            NeverResubmitSet = never;
        }

        private static IEnumerable<string> Split(string value)
        {
            foreach (var part in (value ?? "").Split(','))
            {
                var s = part.Trim();
                if (s.Length > 0) yield return s;
            }
        }
    }
}
