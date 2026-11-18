using System.Collections.Generic;
using BepInEx.Configuration;

namespace FieldGuide
{
    internal enum PopupMode
    {
        UntilHidden = 0,
        Always = 1,
        Collapsed = 2,
    }

    internal enum TraderScope
    {
        Visited = 0,
        All = 1,
    }

    // Every tunable value (spec §12). Entries marked LaunchNote gate a patch group at launch: turning them off works at
    // once (patch bodies read Hot), turning them on needs a restart. Hot caches the values that patch bodies read and is
    // refreshed on SettingChanged; Version counts refreshes so the Bought-by caches can key on it. [Popups] Hidden is the
    // mod's only persistent state (D3): it lives in this file, never in a save. Description strings are public text and go
    // through the humanizer before release (O5). There are no hotkeys.
    internal static class Cfg
    {
        private const string LaunchNote = " Restart the game after turning this on.";

        internal static ConfigEntry<bool> VerboseLog;

        internal static class Popups
        {
            internal static ConfigEntry<bool> Enabled, PublicSaunas;
            internal static ConfigEntry<PopupMode> Mode;
            internal static ConfigEntry<bool> Sauna, Mashing, Corker, Biogas, Compost;
            internal static ConfigEntry<string> Hidden;
        }

        internal static class BoughtBy
        {
            internal static ConfigEntry<bool> Tooltip, ItemPanel, CountUnvisited, CertificateShops, InTradeWindow;
            internal static ConfigEntry<TraderScope> Traders;
            internal static ConfigEntry<int> TooltipNames, PanelNames;
        }

        // Cached values read by patch bodies (spec §4). One field read per call instead of a ConfigEntry lookup.
        internal static class Hot
        {
            internal static bool PopupsOn, PublicSaunas;
            internal static PopupMode Mode;
            internal static readonly bool[] KindOn = new bool[5];
            internal static HashSet<HelpKind> Hidden = new HashSet<HelpKind>();
            internal static bool TooltipOn, PanelOn, AllTraders, CountUnvisited, CertShops, InTradeWindow;
            internal static int TooltipNames, PanelNames;

            // Bumped on every refresh: the Bought-by caches compare it.
            internal static int Version;

            internal static bool On(HelpKind k) => PopupsOn && KindOn[(int)k];

            internal static void Refresh()
            {
                PopupsOn = Popups.Enabled.Value;
                PublicSaunas = Popups.PublicSaunas.Value;
                Mode = Popups.Mode.Value;
                KindOn[(int)HelpKind.Sauna] = Popups.Sauna.Value;
                KindOn[(int)HelpKind.Mashing] = Popups.Mashing.Value;
                KindOn[(int)HelpKind.Corker] = Popups.Corker.Value;
                KindOn[(int)HelpKind.Biogas] = Popups.Biogas.Value;
                KindOn[(int)HelpKind.Compost] = Popups.Compost.Value;
                Hidden = Kinds.Parse(Popups.Hidden.Value);
                TooltipOn = BoughtBy.Tooltip.Value;
                PanelOn = BoughtBy.ItemPanel.Value;
                AllTraders = BoughtBy.Traders.Value == TraderScope.All;
                CountUnvisited = BoughtBy.CountUnvisited.Value;
                CertShops = BoughtBy.CertificateShops.Value;
                InTradeWindow = BoughtBy.InTradeWindow.Value;
                TooltipNames = BoughtBy.TooltipNames.Value;
                PanelNames = BoughtBy.PanelNames.Value;
                Version++;
            }
        }

        // Adds a kind to [Popups] Hidden (D2, D3). BepInEx saves the file when a value is set.
        internal static void AddHidden(HelpKind k)
        {
            var set = Kinds.Parse(Popups.Hidden.Value);
            if (!set.Add(k)) return;
            Popups.Hidden.Value = Kinds.Format(set);
            Log.Verbose($"Help: {Kinds.Name(k)} added to [Popups] Hidden ({Popups.Hidden.Value}).");
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLog = c.Bind("General", "VerboseLog", false,
                "Write extra detail to the BepInEx log.");

            Popups.Enabled = c.Bind("Popups", "Enabled", true,
                "Show a short guide when you open a sauna stove, mash tun, bottle corker, biogas reactor or compost bin." + LaunchNote);
            Popups.Mode = c.Bind("Popups", "Mode", PopupMode.UntilHidden,
                "UntilHidden: open the guide each time until you hide it once, then keep it one key press away. " +
                "Always: open it every time. Collapsed: show it only when you press the hints key.");
            Popups.Sauna = c.Bind("Popups", "Sauna", true, "Guide for saunas.");
            Popups.Mashing = c.Bind("Popups", "Mashing", true, "Guide for mash tuns and mashing kettles.");
            Popups.Corker = c.Bind("Popups", "Corker", true, "Guide for the bottle corker.");
            Popups.Biogas = c.Bind("Popups", "Biogas", true, "Guide for the biogas reactor and its compressor.");
            Popups.Compost = c.Bind("Popups", "Compost", true, "Guide for compost bins.");
            Popups.PublicSaunas = c.Bind("Popups", "PublicSaunas", true,
                "Also show the sauna guide when you walk into a sauna that has no stove of its own." + LaunchNote);
            Popups.Hidden = c.Bind("Popups", "Hidden", "",
                "Guides you have hidden, e.g. \"Sauna, Mashing\". Clear a name to have that guide open again. Kept here, " +
                "not in your saves.");

            BoughtBy.Tooltip = c.Bind("BoughtBy", "Tooltip", true,
                "List the traders who buy an item in its mouse-over tooltip." + LaunchNote);
            BoughtBy.ItemPanel = c.Bind("BoughtBy", "ItemPanel", true,
                "Also list them in the item panel (works with a controller), when there is room." + LaunchNote);
            BoughtBy.Traders = c.Bind("BoughtBy", "Traders", TraderScope.Visited,
                "Visited: only name traders in places you have been. All: name every trader.");
            BoughtBy.CountUnvisited = c.Bind("BoughtBy", "CountUnvisited", true,
                "With Visited, say how many more traders are in places you haven't been (tooltip only).");
            BoughtBy.TooltipNames = c.Bind("BoughtBy", "TooltipNames", 3,
                new ConfigDescription("How many traders the tooltip names.", new AcceptableValueRange<int>(1, 10)));
            BoughtBy.PanelNames = c.Bind("BoughtBy", "PanelNames", 2,
                new ConfigDescription("How many traders the item panel names at most, if they fit.", new AcceptableValueRange<int>(1, 3)));
            BoughtBy.CertificateShops = c.Bind("BoughtBy", "CertificateShops", true,
                "Also list shops that need a certificate, marked \"needs certificate\".");
            BoughtBy.InTradeWindow = c.Bind("BoughtBy", "InTradeWindow", true,
                "While trading, list the other traders who would buy your item.");

            Hot.Refresh();
        }
    }
}
