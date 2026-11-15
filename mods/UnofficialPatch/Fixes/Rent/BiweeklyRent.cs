using System;
using System.Reflection;
using HarmonyLib;

namespace UnofficialPatch
{
    // S-003 (spec §2). DifficultyInfo.GetRentPayment (SRC/DifficultyInfo.cs:173-213) uses the Weekly condition for
    // Biweekly (every Saturday after 19:00) while GetNumberOfPayments divides the monthly rent by 2, so Biweekly charged
    // twice the monthly rent. TenementController.UpdateRentPayment (:422) and Money.CurrentTime (loan downpayments,
    // Money.cs:655) both ask this method. The prefix only adds "at least 14 days since the last payday" for Biweekly;
    // when that holds, vanilla decides, so the Saturday-19:00 rule is unchanged. Both lastPayment fields are only ever
    // set on a payday (TenementController.cs:424, Money.cs:659), so the gap is a multiple of 7 and 14 means "every
    // second Saturday". Stateless.
    internal static class BiweeklyRent
    {
        internal static bool Patched;

        private static MethodBase _target;
        internal static MethodBase Target => _target ?? (_target = AccessTools.Method(typeof(DifficultyInfo), nameof(DifficultyInfo.GetRentPayment),
            new[] { typeof(TimeAndDay) }));

        private const int MinDays = 14;

        private static int _lastLoggedDay = -1;

        // Returns false (with result false) to skip a payday; true lets vanilla decide.
        internal static bool Prefix(DifficultyInfo info, TimeAndDay lastPayment, ref bool result)
        {
            if (!Patched || !Cfg.BiweeklyFix.Value || info.rentPayments != RentPayments.Biweekly) return true;
            if (Compat.Foreign(Target, "the biweekly rent fix")) return true;
            var tod = TimeOfDayAzure.instance;
            // A new game has no payday yet (day 0): vanilla's own 2-day rule applies.
            if (lastPayment == null || lastPayment.currentDay <= 0 || tod == null) return true;
            var now = tod.currentTimeAndDay;
            if (now == null || now.currentDay - lastPayment.currentDay >= MinDays) return true;

            result = false;
            // Called once per in-game minute: log once on the skipped Saturday evening, never allocate otherwise.
            if (now.weekDay == 6 && now.hours >= 19f && lastPayment.currentDay != now.currentDay && _lastLoggedDay != now.currentDay)
            {
                _lastLoggedDay = now.currentDay;
                Log.Info($"Rent: biweekly schedule, skipping this Saturday (last payday was day {lastPayment.currentDay}).");
            }
            return false;
        }
    }

    // DifficultyInfo.GetRentPayment(TimeAndDay).
    [HarmonyPatch]
    internal static class GetRentPaymentPatch
    {
        private static MethodBase TargetMethod() => BiweeklyRent.Target;

        private static bool Prefix(DifficultyInfo __instance, TimeAndDay lastPayment, ref bool __result)
        {
            try { return BiweeklyRent.Prefix(__instance, lastPayment, ref __result); }
            catch (Exception e)
            {
                Log.Exception("Rent: biweekly prefix", e);
                return true;
            }
        }
    }
}
