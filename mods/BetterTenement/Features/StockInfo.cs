using System;
using HarmonyLib;

namespace BetterTenement
{
    // S-028 extras on the upgrade cards (spec §3.2.2-3.2.3): snapshot hooks, and a source marker plus colour on the
    // vanilla "{have}/{need}" text of each required item.
    internal static class StockInfo
    {
        internal static bool SnapshotPatched, MarkersPatched;

        private const string Amber = "#E0B050";

        // C2: called inside UpgradeUI.UpdateInfo, so Card.Current is the card being built.
        internal static void Mark(UpgradeRequirementUI ui, TenementUpgrade.UpgradeItem item, bool cantUpgrade)
        {
            var card = Card.Current;
            if (card == null || item?.Item == null || Vanilla.RequirementItemData == null) return;
            var tc = TenementController.instance;
            var info = Game.Difficulty;
            var data = Vanilla.RequirementItemData(ui);
            if (tc == null || info == null || data == null || data.AmountInfoText == null || Inventory.instance == null) return;

            var c = Vanilla.UpgradeUIContractor != null ? Vanilla.UpgradeUIContractor(card) : null;
            var live = Vanilla.CurrentSceneContractor != null ? Vanilla.CurrentSceneContractor(tc) : null;
            bool remote = c != null && c != live;   // vanilla reads the snapshot exactly then (TenementController.cs:1110)
            int id = item.Item.ID;
            int need = info.GetRenovationResourceCost(item.itemAmount);
            int inv = Inventory.instance.GetItemAmountInInventory(id);
            int box = (int)tc.GetItemAmount(id, c) - inv;

            string text = data.AmountInfoText.text ?? "";
            if (remote) text = "~" + text;
            // Not short overall, but the chest alone does not cover it: carried items will be used. Red stays vanilla's.
            if (!cantUpgrade && box < need) text = $"<color={Amber}>{text}</color>";
            data.AmountInfoText.text = text;
        }
    }

    // C1 context: UpgradeUI.UpdateInfo(...) (SRC/UpgradeUI.cs:174). The postfix also draws the Ledger card line.
    internal static class Card
    {
        internal static bool Patched;
        internal static UpgradeUI Current;
    }

    [HarmonyPatch(typeof(UpgradeUI), nameof(UpgradeUI.UpdateInfo))]
    internal static class UpgradeCardPatch
    {
        private static void Prefix(UpgradeUI __instance) => Card.Current = Card.Patched ? __instance : null;

        private static void Postfix(UpgradeUI __instance, TenementApartment apartment, TenementUpgrade upgrade, UpgradeButtonUI upgradeButton)
        {
            try
            {
                if (Card.Patched && Cfg.LedgerCardLine.Value) LedgerCard.CardLine(__instance, apartment, upgrade, upgradeButton);
            }
            catch (Exception e) { Log.Exception("Ledger card line", e); }
        }

        private static Exception Finalizer(Exception __exception)
        {
            Card.Current = null;
            return __exception;
        }
    }

    // C2: UpgradeRequirementUI.ShowRequiredItem(TenementUpgrade.UpgradeItem, int, bool), called only from
    // UpgradeUI.cs:326. BankUI.cs:642 uses the (Item, int, int, bool) overload, which stays untouched.
    [HarmonyPatch(typeof(UpgradeRequirementUI), nameof(UpgradeRequirementUI.ShowRequiredItem),
        new[] { typeof(TenementUpgrade.UpgradeItem), typeof(int), typeof(bool) })]
    internal static class RequiredItemPatch
    {
        private static void Postfix(UpgradeRequirementUI __instance, TenementUpgrade.UpgradeItem item, bool cantUpgrade)
        {
            try
            {
                if (StockInfo.MarkersPatched && Cfg.CardStockMarkers.Value) StockInfo.Mark(__instance, item, cantUpgrade);
            }
            catch (Exception e) { Log.Exception("card stock marker", e); }
        }
    }

    // N1: Storage.ExitExternally() (SRC/Storage.cs:844). Resource chests only; Snapshot.Write is idempotent, so the
    // method's early return and recursive paths are harmless.
    [HarmonyPatch(typeof(Storage), nameof(Storage.ExitExternally))]
    internal static class ExitStoragePatch
    {
        private static void Postfix(Storage __instance)
        {
            try
            {
                if (!StockInfo.SnapshotPatched || !Cfg.RefreshSnapshot.Value || !Game.IsResourceChest(__instance)) return;
                Snapshot.Write(TenementController.instance);
            }
            catch (Exception e) { Log.Exception("snapshot on chest close", e); }
        }
    }

    // N2: TenementController.OnSavingGame() (SRC/TenementController.cs:184). Catches materials moved without closing
    // the chest (every door is a save). When a contractor was opened here, vanilla's own refresh at :195 wins.
    [HarmonyPatch(typeof(TenementController), nameof(TenementController.OnSavingGame))]
    internal static class SaveTenementPatch
    {
        private static void Prefix(TenementController __instance)
        {
            try
            {
                if (!StockInfo.SnapshotPatched || !Cfg.RefreshSnapshot.Value || Vanilla.CurrentSceneContractor == null) return;
                if (Vanilla.CurrentSceneContractor(__instance) != null) return;
                Snapshot.Write(__instance);
            }
            catch (Exception e) { Log.Exception("snapshot on save", e); }
        }
    }
}
