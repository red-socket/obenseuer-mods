using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace FairMarket
{
    // C1: TradeSpecial.FindValidCertificates(ItemData, int, out List<SlotController>) postfix (spec §8.3). Only when
    // vanilla found enough certificates; the result (true) is never touched, only which certificates Sell will take.
    // If Pick can't fill exactly `amount` (the only stack left is the one being sold), the game's own choice stays.
    // CanSell's own call gets the list too and discards it; SellAndMove's partial Sell re-runs this with its amount.
    [HarmonyPatch]
    internal static class FindCertificatesPatch
    {
        private static MethodBase TargetMethod() => Vanilla.FindValidCertificates;

        private static void Postfix(TradeSpecial __instance, ItemData itemData, int amount, ref List<SlotController> certSlots, ref bool __result)
        {
            if (!Cfg.Hot.CertFix || !__result || amount <= 0 || __instance == null || !__instance.requireCertificates) return;
            List<SlotController> pick;
            try { pick = CertPick.Pick(__instance, itemData, amount); }
            catch (Exception e)
            {
                Log.Exception("certificate pick", e);
                return;
            }
            if (pick != null && pick.Count == amount) certSlots = pick;
            else Log.WarnOnce("cert-mismatch", "Certs: couldn't pick certificates the fixed way for one sale; kept the game's own choice.");
        }
    }
}
