using System;
using System.Reflection;
using HarmonyLib;

namespace FairMarket
{
    // H1: DoorChangelevel.UpdateUseMessage() postfix (spec §7.3). Shop doors that lead into another scene.
    [HarmonyPatch]
    internal static class DoorChangelevelTextPatch
    {
        private static MethodBase TargetMethod() => Vanilla.DoorChangelevelUseMessage;

        private static void Postfix(DoorChangelevel __instance)
        {
            if (!Cfg.Hot.HoursDoors || __instance == null) return;
            try { HoursText.Door(__instance, __instance._Lock); }
            catch (Exception e) { Log.Exception("hours door", e); }
        }
    }

    // H2: Door.UpdateDoorUseMessage(bool) postfix. Doors inside a scene (Mallment shops, the bank's side door). A
    // bypassed lock shows the game's text.
    [HarmonyPatch]
    internal static class DoorTextPatch
    {
        private static MethodBase TargetMethod() => Vanilla.DoorUseMessage;

        private static void Postfix(Door __instance, bool bypassLock)
        {
            if (!Cfg.Hot.HoursDoors || bypassLock || __instance == null) return;
            try { HoursText.Door(__instance, __instance._Lock); }
            catch (Exception e) { Log.Exception("hours inner door", e); }
        }
    }

    // H3: Trade.UpdateUseMessage() postfix (declared on Trade; TradeSpecial inherits it). Market stall traders.
    [HarmonyPatch]
    internal static class TradeTextPatch
    {
        private static MethodBase TargetMethod() => Vanilla.TradeUseMessage;

        private static void Postfix(Trade __instance)
        {
            if (!Cfg.Hot.HoursStalls || __instance == null) return;
            try { HoursText.Stall(__instance); }
            catch (Exception e) { Log.Exception("hours stall", e); }
        }
    }
}
