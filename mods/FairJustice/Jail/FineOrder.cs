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
    // With KeepPermittedItems (D-4), stacks the player was allowed to have are set aside before vanilla's
    // GetStolenItems removal and get their owner back afterwards (Jail/PermittedItems.cs), whether or not SafePayFine
    // is on; this part never skips vanilla, so it does not yield to other mods.
    internal static class FineOrder
    {
        internal static bool Patched;

        private static MethodBase _target;
        internal static MethodBase Target => _target ?? (_target = AccessTools.Method(typeof(Crime), nameof(Crime.PayFine), Type.EmptyTypes));

        internal static bool Prefix(Crime crime, ref bool result, out PermittedItems.Hidden hidden)
        {
            hidden = null;
            if (!Patched) return true;
            if (Cfg.SafePayFine.Value && !Compat.Foreign(Target, "the safe fine payment"))
            {
                int num = Mathf.RoundToInt((float)crime.GetCrimeFineAmount());
                if (num <= 0 || Money.instance == null || !Money.instance.CheckMoney(num, RealMoney: true))
                {
                    result = false;
                    Log.Info($"PayFine refused (fine {num} RM); stolen goods kept.");
                    return false;
                }
                // Its own try, so a rescue failure never stops the permitted stacks from being set aside below.
                try { BagRescue.Rescue("fine"); }
                catch (Exception e) { Log.Exception("PayFine rescue", e); }
            }
            // After the rescue, so rescued permitted stacks are set aside too.
            hidden = PermittedItems.Hide("fine");
            return true;
        }
    }

    // P14: Crime.PayFine(). The postfix (or the finalizer, on an exception) gives set-aside permitted stacks their
    // owner back.
    [HarmonyPatch(typeof(Crime), nameof(Crime.PayFine))]
    internal static class PayFinePatch
    {
        private static bool Prefix(Crime __instance, ref bool __result, out PermittedItems.Hidden __state)
        {
            __state = null;
            try { return FineOrder.Prefix(__instance, ref __result, out __state); }
            catch (Exception e)
            {
                Log.Exception("PayFine prefix", e);
                return true;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(PermittedItems.Hidden __state)
        {
            try { PermittedItems.Restore(__state, "fine"); }
            catch (Exception e) { Log.Exception("PayFine postfix", e); }
        }

        private static Exception Finalizer(Exception __exception, PermittedItems.Hidden __state)
        {
            try { PermittedItems.Restore(__state, "fine"); }
            catch (Exception e) { Log.Exception("PayFine finalizer", e); }
            return __exception;
        }
    }
}
