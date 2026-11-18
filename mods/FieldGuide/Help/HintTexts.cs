using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;

namespace FieldGuide
{
    // Builds each pop-up from Strings plus facts read from the station the player opened (spec §6.3-§6.8): power or fuel,
    // AlwaysOn, AllowSimultaneous, RequirePlayer and the sauna's steam threshold. Built at show time, so PT-BR's string
    // swaps apply. Format: title, one bulleted list with no header, then the reopen line as plain text.
    internal static class HintTexts
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // D8 / O1: the biogas gas and compressor bullets (spec §6.7, 4-7) were decoded from the prefab's relay graph but
        // never run. Each ships only after in-game test T-H5 confirms it: set its flag to true then, and cut the bullet
        // from Strings if the test disproves it. The test build shows all four so the tester can check the wording.
#if FIELDGUIDE_TESTS
        private static readonly bool[] GasConfirmed = { true, true, true, true };
#else
        private static readonly bool[] GasConfirmed = { false, false, false, false };
#endif

        // Sauna.minimumWorkingTemperature (SRC/Sauna.cs:25, private). 60 in all 28 non-passive saunas of 0.5.19.
        private static readonly AccessTools.FieldRef<Sauna, float> SteamFrom = SafeRef("minimumWorkingTemperature");
        private const float DefaultSteamFrom = 60f;

        private static AccessTools.FieldRef<Sauna, float> SafeRef(string name)
        {
            try { return AccessTools.FieldRefAccess<Sauna, float>(name); }
            catch { return null; }
        }

        internal static HintContent Build(HelpKind kind, CraftingBase cb)
        {
            switch (kind)
            {
                case HelpKind.Sauna: return Sauna(cb as Generator, Classify.SaunaOfStove(cb as Generator));
                case HelpKind.Mashing: return Mashing(cb);
                case HelpKind.Corker: return Corker(cb as manufacturing);
                case HelpKind.Biogas: return Biogas(Classify.BiogasLidOf(cb));
                case HelpKind.Compost: return Compost(cb as manufacturing);
                default: return null;
            }
        }

        // §6.4. stove == null: the walk-in variant (a sauna with no stove of its own, D6).
        internal static HintContent Sauna(Generator stove, Sauna sauna)
        {
            var b = new List<string>();
            if (stove != null && !stove.AlwaysOn)
            {
                if (stove._Power != null && stove._Power.RequiresFuel) b.Add(Strings.SaunaFuelStove);
                else if (stove._Power != null && stove._Power.Type == PowerSource.PowerSourceType.Electricity) b.Add(Strings.SaunaElectricStove);
            }
            string t = SteamThreshold(sauna);
            b.Add(string.Format(C, stove != null ? Strings.SaunaSteamStove : Strings.SaunaSteamRoom, t));
            b.Add(Strings.SaunaSteamFades);
            b.Add(Strings.SaunaEffects);
            b.Add(Strings.SaunaHeat);
            b.Add(Strings.SaunaSkill);
            return Make(Strings.SaunaTitle, b, stove != null ? StationLine() : Strings.ReopenLineRoom);
        }

        private static string SteamThreshold(Sauna s)
        {
            float v = DefaultSteamFrom;
            try { if (s != null && SteamFrom != null) v = SteamFrom(s); }
            catch { v = DefaultSteamFrom; }
            return v.ToString("0", C);
        }

        // §6.5.
        private static HintContent Mashing(CraftingBase cb)
        {
            var b = new List<string> { Strings.MashInputs, Strings.MashYeast };
            if (cb != null && !cb.AlwaysOn) b.Add(Fuel(cb) ? Strings.MashNeedsFuel : Strings.MashNeedsPower);
            b.Add(Strings.MashFail);
            b.Add(Strings.MashWater);
            b.Add(Strings.MashFill);
            return Make(Strings.MashTitle, b, StationLine());
        }

        // §6.6. No time speed-up claim: a bottle takes 1 in-game minute and the clock stays at normal speed (§0).
        private static HintContent Corker(manufacturing m)
        {
            var b = new List<string> { Strings.CorkerInputs, Strings.CorkerBottles, Strings.CorkerAmmo };
            if (m != null)
            {
                string s = (m.AlwaysOn ? Strings.CorkerNoPower : "") +
                    (m.AllowSimultaneous <= 1 ? Strings.CorkerOne : string.Format(C, Strings.CorkerMany, m.AllowSimultaneous)) +
                    (m.RequirePlayer ? Strings.CorkerWindow : "");
                b.Add(s);
            }
            b.Add(Strings.CorkerBreak);
            return Make(Strings.CorkerTitle, b, StationLine());
        }

        // §6.7. The lid and the compressor share it; the AlwaysOn bullet always describes the reactor (the lid).
        private static HintContent Biogas(manufacturing lid)
        {
            var b = new List<string> { Strings.BiogasInputs };
            if (lid != null) b.Add(lid.AlwaysOn ? Strings.LongNoPower : Strings.LongPower);
            b.Add(Strings.BiogasWaste);
            if (GasConfirmed[0]) b.Add(Strings.BiogasTank);
            if (GasConfirmed[1]) b.Add(Strings.BiogasBottle);
            if (GasConfirmed[2]) b.Add(Strings.BiogasCompressor);
            if (GasConfirmed[3]) b.Add(Strings.BiogasUse);
            return Make(Strings.BiogasTitle, b, StationLine());
        }

        // §6.8 (D1).
        private static HintContent Compost(manufacturing m)
        {
            var b = new List<string> { Strings.CompostInputs, Strings.CompostAsh };
            if (m != null)
            {
                b.Add(m.AlwaysOn ? Strings.LongNoPower : Strings.LongPower);
                b.Add(m.AllowSimultaneous <= 1 ? Strings.CompostOne : string.Format(C, Strings.CompostMany, m.AllowSimultaneous));
            }
            b.Add(Strings.CompostSoil);
            return Make(Strings.CompostTitle, b, StationLine());
        }

        // The last line at a station, chosen at show time: on a pad with a backpack worn, View opens the backpack (D25).
        private static string StationLine()
        {
            if (!HintHost.PadWithBackpack()) return Strings.ReopenLine;
            return Cfg.Hot.Mode == PopupMode.UntilHidden ? Strings.PackLineOnce : Strings.PackLine;
        }

        // A station that burns fuel (the tun: Combustion + RequiresFuel); anything else that isn't AlwaysOn runs on power.
        private static bool Fuel(CraftingBase cb) => cb._Power != null && cb._Power.RequiresFuel;

        private static HintContent Make(string title, List<string> bullets, string reopen) =>
            new HintContent(title).AddList(null, bullets.ToArray()).AddText(null, reopen);
    }
}
