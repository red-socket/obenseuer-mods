using System;
using System.Reflection;
using HarmonyLib;

namespace FairMarket
{
    // S1: StorageSpawnCategoryDatabase.ConstructSpawnCategoryDatabase() postfix (spec §5.4). Runs after every JSON read
    // (Awake on each scene load, and our own re-read when a [Stock] setting changes). The plan is built first and only
    // applied when it built without an error, so a failure leaves the vanilla tables untouched.
    [HarmonyPatch]
    internal static class SpawnDatabasePatch
    {
        private static MethodBase TargetMethod() => Vanilla.ConstructSpawnDb;

        private static void Postfix(StorageSpawnCategoryDatabase __instance)
        {
            if (!Cfg.Hot.Stock)
            {
                StockPlan.Clear();
                return;
            }
            StockPlan plan;
            try { plan = StockPlan.Build(__instance); }
            catch (Exception e)
            {
                StockPlan.Clear();
                Log.Exception("stock plan", e);
                return;
            }
            try { plan.Apply(); }
            catch (Exception e) { Log.Exception("stock apply", e); }
        }
    }

    // S2: Storage.SpawnItems() prefix (spec §5.6). The game rolls each restock one restock ahead and saves that roll
    // (nextItemsToSpawn, [fsProperty], SRC/Storage.cs:161-162). For a shop whose list S1 changed, the roll is made again
    // just before the restock, so the boost shows at the next restock instead of the one after. Vanilla's own
    // SetNextItemsToSpawn does it; the restock then runs as usual. Stacks with Unofficial Patch's RestockLog.
    [HarmonyPatch]
    internal static class SpawnItemsPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SpawnItems;

        private static void Prefix(Storage __instance)
        {
            if (!Cfg.Hot.Stock || !Cfg.Hot.ApplyNext || __instance == null) return;
            try
            {
                string cat = __instance.itemSpawnCategory;
                if (cat == null || !StockPlan.EditedCategories.Contains(cat) || !Vanilla.SpawnFromCategory(__instance)) return;
                __instance.SetNextItemsToSpawn();
            }
            catch (Exception e) { Log.Exception("stock restock", e); }
        }
    }
}
