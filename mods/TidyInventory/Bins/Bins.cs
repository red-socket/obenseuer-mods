using System;
using OS.Items;

namespace TidyInventory
{
    // Which storages are bins and which stacks a bin never throws away (spec §4.1, §4.2). Shared by the Empty button,
    // single-stack disposal and the daily emptying.
    internal static class Bins
    {
        internal enum Keep { None, Quest, Owned, Category, Valuable }

        // The bin prefabs (Trash Bin, Green Dumpster) the player can place. Tenement-upgrade bins don't count.
        private static readonly string[] PlacedTitles = { "Trash Bin", "Green Dumpster" };

        internal static bool IsBin(Storage s, bool ignorePlayerPlacedOnly = false)
        {
            try
            {
                if (s == null) return false;
                var cat = s.StorageSettings;
                if (string.IsNullOrEmpty(cat) || !Cfg.List(Cfg.Bins.BinCategories).Contains(cat)) return false;
                var title = s.title ?? "";
                bool titled = Cfg.List(Cfg.Bins.BinTitles).Contains(title.Trim());
                // Untitled open dumpsters (Open Sewer Bazaar): "Dumpster open" storages named "Storage" by Storage.Start
                // (SRC/Storage.cs:441-443) that spawn dumpster loot.
                bool openDumpster = string.Equals(cat, "Dumpster open", StringComparison.OrdinalIgnoreCase)
                                    && (title.Length == 0 || title == "Storage") && s.itemSpawnCategory == "Dumpster";
                if (!titled && !openDumpster) return false;
                if (s.itemSpawnCategory == "WashingMachine") return false;   // laundry baskets
                if (Cfg.Bins.SkipOwnedBins.Value && s.StorageOwner != null && s.StorageOwner.ID > 1 && !s.PermissionToTake) return false;
                if (!ignorePlayerPlacedOnly && Cfg.Bins.PlayerPlacedOnly.Value && !IsPlayerPlaced(s)) return false;
                return true;
            }
            catch (Exception e) { Log.Exception("bin check", e); return false; }
        }

        internal static bool IsPlayerPlaced(Storage s)
        {
            var fp = s != null ? s.GetComponentInParent<FurniturePlaceable>() : null;
            if (fp == null || fp.isPreplacedFurniture || fp.furniture == null) return false;
            return Array.IndexOf(PlacedTitles, fp.furniture.title) >= 0;
        }

        // A stack that is never thrown away, and why. Any exception keeps the stack.
        internal static bool Protected(SlotController sc, out Keep reason)
        {
            reason = Keep.None;
            try
            {
                var st = sc?.itemStack;
                if (st == null || st.itemAmount <= 0 || st.itemId < 0) return false;
                var meta = st.Meta;
                if (ItemMetaUtilities.GetMetaOfType<ItemTaskItemData>(meta) != null || PackageHoldsTaskItem(meta)) { reason = Keep.Quest; return true; }
                if (ItemMetaUtilities.GetMetaOfType<ItemShopData>(meta) != null || ItemMetaUtilities.GetMetaOfType<ItemOwnerPermission>(meta) != null)
                {
                    reason = Keep.Owned;
                    return true;
                }
                var item = st.itemReference?.Item ?? ItemDatabase.instance?.FetchItemByID(st.itemId);
                if (item == null) { reason = Keep.Quest; return true; }
                if (item.Categories != null)
                {
                    var cats = Cfg.List(Cfg.Bins.ProtectCategories);
                    foreach (var c in item.Categories)
                        if (c != null && cats.Contains(c)) { reason = Keep.Category; return true; }
                }
                if (Cfg.Bins.ProtectStolen.Value && st.ownerId > 1) { reason = Keep.Owned; return true; }
                int above = Cfg.Bins.ProtectValueAbove.Value;
                if (above > 0 && ItemValue.Of(st) >= above) { reason = Keep.Valuable; return true; }
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("bin protection", e);
                reason = Keep.Quest;
                return true;
            }
        }

        private static bool PackageHoldsTaskItem(object[] meta)
        {
            var pkg = ItemMetaUtilities.GetMetaOfType<ItemPackageData>(meta);
            if (pkg?.containedItems == null) return false;
            foreach (var child in pkg.containedItems)
                if (child != null && ItemMetaUtilities.GetMetaOfType<ItemTaskItemData>(child.meta) != null) return true;
            return false;
        }
    }
}
