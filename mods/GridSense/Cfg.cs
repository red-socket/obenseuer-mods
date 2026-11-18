using BepInEx.Configuration;

namespace GridSense
{
    // Every tunable value (spec §6). BepInEx sorts sections by name, so [Accessibility] comes first in the file and in
    // ConfigurationManager. The two Wattage switches gate a patch group at launch: turning one off works at once (patch
    // bodies read Hot), turning one on needs a restart. The flicker switch works live both ways (its group is patched
    // lazily). Hot caches what patch bodies read and is refreshed first on SettingChanged. Description strings are public
    // text: humanized 2026-11-17; run any changed text through the humanizer again (CLAUDE.md).
    internal static class Cfg
    {
        internal const string AccessibilitySection = "Accessibility";
        internal const string WattageSection = "Wattage";

        internal static ConfigEntry<bool> VerboseLog;

        internal static class Accessibility
        {
            internal static ConfigEntry<bool> StopNoContractFlicker;
        }

        internal static class Wattage
        {
            internal static ConfigEntry<bool> ShopInfo, BuildBar;
        }

        // Cached values read by patch bodies. One field read per call instead of a ConfigEntry lookup; P3 runs on every
        // lamp's grid update.
        internal static class Hot
        {
            internal static bool StopNoContractFlicker, ShopInfo, BuildBar;

            internal static void Refresh()
            {
                StopNoContractFlicker = Accessibility.StopNoContractFlicker.Value;
                ShopInfo = Wattage.ShopInfo.Value;
                BuildBar = Wattage.BuildBar.Value;
            }
        }

        internal static void Bind(ConfigFile c)
        {
            Accessibility.StopNoContractFlicker = c.Bind(AccessibilitySection, "StopNoContractFlicker", false,
                "Stops the lamp flicker the game uses to show that your tenement has no electricity contract (an unstable " +
                "supply). You can get a contract from the Open Sewer Power Company in the Bazaar. With this on, two signs remain: " +
                "the power meter's Max stays at the no-contract limit (8 kW on the default settings, where a contract raises it " +
                "to 15 kW or more), and no electricity bills arrive. If the Electricity amount difficulty setting is Infinite, " +
                "the meter shows no Max at all, so the missing bills are the only sign. Lamps on a running fuel generator " +
                "still flicker, and they still dim when its fuel runs low. Other flickering lights in the world don't change. " +
                "Works as soon as you change it.");

            VerboseLog = c.Bind("General", "VerboseLog", false,
                "Writes extra detail to the BepInEx log for bug reports.");

            Wattage.ShopInfo = c.Bind(WattageSection, "ShopInfo", true,
                "Adds a \"Power:\" line to the furniture info in shops and at the workbench, when you buy, sell, craft or " +
                "dismantle. Machines show what they draw while crafting and while idle. Generators and battery banks show " +
                "what they supply, and a battery bank also shows what it draws while charging. The numbers are the ones the " +
                "game uses for your grid and your bills. Turning it off works from your next selection. Turning it on needs a game restart.");
            Wattage.BuildBar = c.Bind(WattageSection, "BuildBar", true,
                "Adds the wattage after the furniture name in build mode, for example \"Bandsaw - 1.3 kW (300 W idle)\". " +
                "Turning it off works from your next selection. Turning it on needs a game restart.");

            Hot.Refresh();
        }
    }
}
