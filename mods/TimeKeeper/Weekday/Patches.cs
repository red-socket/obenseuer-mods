using System;
using System.Reflection;
using HarmonyLib;

namespace TimeKeeper
{
    // P1: WatchSlotController.CalendarEquipped getter (SRC/WatchSlotController.cs:31), postfix. A worn 'Watch' reports as
    // a calendar watch, so vanilla's closed-inventory branch sets showDayOfWeek itself (SRC/ShowTime.cs:76) and HUD mods
    // that read the property follow. Only patched with ShareWithHudMods at launch; P2 alone covers the vanilla HUD.
    [HarmonyPatch]
    internal static class CalendarEquippedPatch
    {
        private static MethodBase TargetMethod() => Vanilla.CalendarEquippedGetter;

        private static void Postfix(WatchSlotController __instance, ref bool __result)
        {
            try
            {
                if (!__result && Weekday.Live && Cfg.Weekday.ShareWithHudMods.Value && Weekday.WornWatch(__instance)) __result = true;
            }
            catch (Exception e) { Log.Exception("weekday property", e); }
        }
    }

    // P2: ShowTime.DayOfWeek() (SRC/ShowTime.cs:116), private, postfix. Screen-space clocks only (HUD, sleep clock,
    // manufacturing clock): world clocks (timeMesh) are never touched, and neither is a clock that hides the weekday on
    // purpose or already shows it. DayOfWeek is only reached through UpdateTime, which vanilla calls while a clock shows;
    // a mod that shows the clock without a watch (Always Show Time) keeps "Time:" because Qualifies is false then.
    [HarmonyPatch]
    internal static class DayOfWeekPatch
    {
        private static MethodBase TargetMethod() => Vanilla.DayOfWeek;

        private static void Postfix(ShowTime __instance, ref string __result)
        {
            try
            {
                if (!Weekday.Live || __instance == null) return;
                if (Vanilla.TimeMesh(__instance) != null) return;   // Unity null: a world clock
                if (Vanilla.HideDayOfWeek(__instance) || Vanilla.ShowDayOfWeek(__instance)) return;
                var t = TimeOfDayAzure.instance;
                if (t == null || !Weekday.Qualifies()) return;
                __result = t.GetDayOfWeekString() + " ";
            }
            catch (Exception e) { Log.Exception("weekday text", e); }
        }
    }
}
