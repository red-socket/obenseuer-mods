using System;
using System.Reflection;
using HarmonyLib;

namespace FairMarket
{
    // B1: Trade.CheckIfAllowed(ItemData) postfix (spec §6.1). Only ever turns false into true, for the configured
    // traders. One patch covers every place the game asks: CanSell (money, then IsStolen, then this, then CheckIfFull),
    // TradeSpecial.CanSell, the Sell button and "Doesn't buy" text, the price label and drag refusal. Prices, the
    // trader's wallet, stolen rules and the full check stay vanilla. Your Cat (15650) is never bought.
    [HarmonyPatch]
    internal static class CheckIfAllowedPatch
    {
        private const int YourCat = 15650;

        private static MethodBase TargetMethod() => Vanilla.TradeCheckIfAllowed;

        private static void Postfix(Trade __instance, ItemData itemData, ref bool __result)
        {
            if (__result || !Cfg.Hot.Buyers) return;
            try
            {
                if (itemData == null || itemData.item == null) return;
                var r = BuyerRules.For(__instance);
                if (r == null) return;
                var item = itemData.item;
                int id = item.ID;
                if (id == YourCat || r.ExcludeIds.Contains(id)) return;
                var meta = itemData.meta;
                // An empty allowed list means "anything" to the game's check, so it is never passed as the allow list.
                if (r.Allow.Length > 0 && SlotController.CheckIfInAllowedCategoy(item, r.Allow, r.ForbiddenUnion, meta))
                {
                    __result = true;
                    return;
                }
                // #id: allowed unless forbidden (the empty allow list is the game's "not forbidden" test).
                if (r.AllowIds.Contains(id) && SlotController.CheckIfInAllowedCategoy(item, new string[0], r.ForbiddenUnion, meta))
                    __result = true;
            }
            catch (Exception e) { Log.Exception("buyers check", e); }
        }
    }
}
