using System;
using System.Reflection;
using HarmonyLib;

namespace UnofficialPatch
{
    // S-085 (spec §4a; OFF by default, unconfirmed). Bank is a SavableScript that serializes its whole component
    // (SRC/Bank.cs:97-110), tuning fields included, so a save made before the 0.5.18 exchange fix would keep the old
    // income, spending and ratios (the #6537 hypothesis). The prefix captures the scene's values before
    // DeSerializeData; the postfix writes them back for the listed banks. Supply, lastCheck, accountData and the loan
    // fields are never touched. Vanilla then saves the restored values, so the change outlives the mod.
    // The OC-interest carry (spec §4b) is dropped: no bank in 0.5.19 can hold OC (.internal/design.md).
    internal static class ExchangeTuning
    {
        internal static bool Patched;

        private static MethodBase _load, _rate;
        private static MethodBase LoadTarget => _load ?? (_load = AccessTools.Method(typeof(Bank), nameof(Bank.OnLoadingGame), Type.EmptyTypes));
        private static MethodBase RateTarget => _rate ?? (_rate = AccessTools.Method(typeof(Bank), nameof(Bank.GetExchangeRate), new[] { typeof(bool) }));

        internal sealed class Tuning
        {
            internal int OcIncome, OcSpending, RmIncome, RmSpending;
            internal float OsRm, RmOs;
        }

        internal static Tuning Capture(Bank b)
        {
            if (!Patched || !Cfg.RestoreExchangeTuning.Value || b == null) return null;
            return new Tuning
            {
                OcIncome = b.DailyAverageOcIncome, OcSpending = b.DailyAverageOcSpending,
                RmIncome = b.DailyAverageRmIncome, RmSpending = b.DailyAverageRmSpending,
                OsRm = b.OS_RM_Ratio, RmOs = b.RM_OS_Ratio
            };
        }

        internal static void Restore(Bank b, Tuning t)
        {
            if (t == null || b == null || !Cfg.RestoreExchangeTuning.Value) return;
            if (b.BankName == null || !Cfg.RestoreBankSet.Contains(b.BankName)) return;
            if (Compat.Foreign(LoadTarget, "the exchange tuning restore") || Compat.Foreign(RateTarget, "the exchange tuning restore")) return;

            b.DailyAverageOcIncome = Fix(b.BankName, "OC income", b.DailyAverageOcIncome, t.OcIncome);
            b.DailyAverageOcSpending = Fix(b.BankName, "OC spending", b.DailyAverageOcSpending, t.OcSpending);
            b.DailyAverageRmIncome = Fix(b.BankName, "RM income", b.DailyAverageRmIncome, t.RmIncome);
            b.DailyAverageRmSpending = Fix(b.BankName, "RM spending", b.DailyAverageRmSpending, t.RmSpending);
            b.OS_RM_Ratio = Fix(b.BankName, "OC-to-RM ratio", b.OS_RM_Ratio, t.OsRm);
            b.RM_OS_Ratio = Fix(b.BankName, "RM-to-OC ratio", b.RM_OS_Ratio, t.RmOs);
        }

        private static T Fix<T>(string bank, string what, T saved, T game) where T : IEquatable<T>
        {
            if (saved.Equals(game)) return saved;
            Log.Info($"Exchange: \"{bank}\" saved {what} {saved} differs from the game's {game}; using the game's value.");
            return game;
        }
    }

    // Bank.OnLoadingGame() (SRC/Bank.cs:103).
    [HarmonyPatch(typeof(Bank), nameof(Bank.OnLoadingGame))]
    internal static class BankLoadPatch
    {
        private static void Prefix(Bank __instance, out ExchangeTuning.Tuning __state)
        {
            __state = null;
            try { __state = ExchangeTuning.Capture(__instance); }
            catch (Exception e) { Log.Exception("Exchange: capture", e); }
        }

        private static void Postfix(Bank __instance, bool __result, ExchangeTuning.Tuning __state)
        {
            if (!__result) return;
            try { ExchangeTuning.Restore(__instance, __state); }
            catch (Exception e) { Log.Exception("Exchange: restore", e); }
        }
    }
}
