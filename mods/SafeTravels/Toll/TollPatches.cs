using System;
using HarmonyLib;

namespace SafeTravels
{
    internal struct MoneySnap
    {
        internal bool Toll;
        internal int Oc;
        internal float Rm;
    }

    // P1 (spec §3.1). InteractableMoney.Interact charges on every Interact press with no paid state
    // (SRC/InteractableMoney.cs:25-49), and the gate only arms after the relay's 1 s delay, so a quick second press
    // charges again for nothing. While the toll slot is locked the press is refused silently: no charge, no sound, no
    // red flash (the prompt already reads "Paid - walk through"). Other coin slots and slot machines pass straight
    // through. A payment is detected from the money before and after, so it stays right if another mod changes Buy.
    [HarmonyPatch(typeof(InteractableMoney), nameof(InteractableMoney.Interact))]
    internal static class MoneyInteractPatch
    {
        private static bool Prefix(InteractableMoney __instance, bool __runOriginal, out MoneySnap __state)
        {
            __state = default;
            try
            {
                if (!__runOriginal || !ReferenceEquals(__instance, TollSlot.Slot) || !Cfg.Toll.Enabled.Value) return true;
                if (TollSlot.Locked(out string why))
                {
                    Log.Verbose($"Toll: press ignored ({why})");
                    return false;
                }
                var money = Money.instance;
                if (money != null) __state = new MoneySnap { Toll = true, Oc = money.OCMoneyAmount, Rm = money.RMoneyAmount };
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("toll interact", e);
                return true;
            }
        }

        private static void Postfix(InteractableMoney __instance, MoneySnap __state, bool __runOriginal)
        {
            try
            {
                var money = Money.instance;
                if (!__state.Toll || !__runOriginal || money == null) return;
                bool paid = __instance.RM ? money.RMoneyAmount < __state.Rm : money.OCMoneyAmount < __state.Oc;
                if (!paid) return;
                TollSlot.LastPaid = UnityEngine.Time.time;
                Log.Verbose($"Toll: paid {__instance.price}");
            }
            catch (Exception e) { Log.Exception("toll paid", e); }
        }
    }

    // P2 (spec §3.2): while locked, the slot's prompt reads "Paid - walk through" in green instead of "Pay 10 OC".
    // Runs every frame while the slot is aimed at: a reference compare and a few field reads.
    [HarmonyPatch(typeof(InteractableMoney), nameof(InteractableMoney.UpdateUseMessage))]
    internal static class MoneyUseMessagePatch
    {
        private static void Postfix(InteractableMoney __instance)
        {
            try
            {
                if (!ReferenceEquals(__instance, TollSlot.Slot) || !Cfg.Toll.Enabled.Value || UseText.instance == null) return;
                if (TollSlot.Locked(out _))
                    UseText.instance.ChangeUseText(__instance.title, Strings.TollPaid, showUseText: true, redColor: false, greenColor: true);
            }
            catch (Exception e) { Log.Exception("toll prompt", e); }
        }
    }
}
