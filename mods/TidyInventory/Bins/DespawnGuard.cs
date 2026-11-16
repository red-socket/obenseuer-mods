using System;
using System.Reflection;
using HarmonyLib;

namespace TidyInventory
{
    // Optional despawn protection (spec §4.6, off by default). Dropped items expire on a timer whatever they are worth
    // (SRC/Collectible.cs:232-275). When a valuable one would expire, its timer is set again for RecheckMinutes instead,
    // the same mechanism as vanilla's own 10 s retry when the player stands close (:270): the fired timer is removed after
    // its callback. Value is never judged in StartExpirationTimer, which runs before amount and meta are set
    // (SRC/ItemOperations.cs:371-374). Without the mod a kept item expires at its next check.
    internal static class DespawnGuard
    {
        internal static bool Patched;

        internal static bool Keep(Collectible c)
        {
            float min = Cfg.Despawn.MinValue.Value;
            switch (c)
            {
                case CollectibleItem ci:
                    if (ci.spawner != null && !Cfg.Despawn.IncludeSpawnerLoot.Value) return false;
                    return ItemValue.Of(ci.Item?.Item, ci.meta, ci.amount) >= min;
                case CollectibleCoin coin:
                    return coin.isRealMoney || coin.Amount >= min;
                case CollectiblePackage pkg:
                    if (Cfg.Despawn.ProtectTaskPackages.Value && pkg.taskItems != null && pkg.taskItems.Count > 0) return true;
                    float sum = 0f;
                    if (pkg.items != null)
                        foreach (var st in pkg.items) sum += ItemValue.Of(st);
                    return sum >= min;
                default:
                    return false;
            }
        }
    }

    // P28: Collectible.Expire(bool) (SRC/Collectible.cs:256). No subclass overrides it.
    [HarmonyPatch]
    internal static class CollectibleExpirePatch
    {
        private static MethodBase TargetMethod() => Vanilla.CollectibleExpire;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Collectible __instance, bool __runOriginal)
        {
            if (!__runOriginal || !Cfg.Despawn.ProtectValuables.Value || __instance == null || TimeOfDayAzure.instance == null) return true;
            bool keep;
            try { keep = DespawnGuard.Keep(__instance); }
            catch (Exception e)
            {
                Log.Exception("despawn value", e);
                keep = true;   // errors count as protected
            }
            if (!keep) return true;
            try
            {
                __instance.ExpirationTimer = new TimeOfDayAzure.Timer(__instance.Expire, Cfg.Despawn.RecheckMinutes.Value * 60);
                Log.Verbose($"Despawn: kept {__instance.name}; next check in {Cfg.Despawn.RecheckMinutes.Value} game minutes.");
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("despawn keep", e);
                return true;
            }
        }
    }
}
