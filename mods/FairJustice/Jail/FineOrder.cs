using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FairJustice
{
    // S-034 (spec §3.2C). Vanilla Crime.PayFine (SRC/Crime.cs:375-392) deletes stolen goods first and only then tries
    // Money.Buy, so a player who cannot pay loses the goods and keeps the fine. The prefix runs Money.CheckMoney, the
    // exact test Buy makes (Money.cs:304-387), and refuses when it fails; on success it rescues a stolen backpack's
    // owned contents and lets the vanilla body run unchanged (Buy then succeeds in the same frame). The fine excludes
    // stolen goods, so no amount changes. Lua PayFine() calls this method by MethodInfo, so the dialogue is covered.
    internal static class FineOrder
    {
        internal static bool Patched;

        private static MethodBase _target;
        internal static MethodBase Target => _target ?? (_target = AccessTools.Method(typeof(Crime), nameof(Crime.PayFine), Type.EmptyTypes));

        internal static bool Prefix(Crime crime, ref bool result)
        {
            if (!Patched || !Cfg.SafePayFine.Value) return true;
            if (Compat.Foreign(Target, "the safe fine payment")) return true;
            int num = Mathf.RoundToInt((float)crime.GetCrimeFineAmount());
            if (num <= 0 || Money.instance == null || !Money.instance.CheckMoney(num, RealMoney: true))
            {
                result = false;
                Log.Info($"PayFine refused (fine {num} RM); stolen goods kept.");
                return false;
            }
            BagRescue.Rescue("fine");
            return true;
        }
    }

    // P14: Crime.PayFine().
    [HarmonyPatch(typeof(Crime), nameof(Crime.PayFine))]
    internal static class PayFinePatch
    {
        private static bool Prefix(Crime __instance, ref bool __result)
        {
            try { return FineOrder.Prefix(__instance, ref __result); }
            catch (Exception e)
            {
                Log.Exception("PayFine prefix", e);
                return true;
            }
        }
    }
}
