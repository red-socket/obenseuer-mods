using System;
using System.Reflection;
using HarmonyLib;
using OS.Items;

namespace FairJustice
{
    // U-016 / S-036 (spec §3.6). Item use opens a UseFrame around the whole use, so P1 and P3 can tell its own check and
    // its leftover adds apart from everything else.
    //   Own (FreeOwnUse): the used stack sits in the player's own slots. The use's check on the item and the checks on
    //   same-owner leftovers (ItemsToSpawn, ammo empties, package contents, the empty-bottle swap) skip the theft path;
    //   leftovers of a stolen item get the stolen mark, leftovers of a permitted item inherit its ItemOwnerPermission.
    //   InPlace (FreeAmenityUse): hold-Use on a listed, NPC-owned world item without shop data or permission. Its own check
    //   is skipped and its leftovers come to the player with owner 0. The world item keeps its owner, so taking it is
    //   still theft.
    // Trades, money, equip-by-use (P6) and items in other storages never open a frame and stay vanilla.
    internal static class FreeUse
    {
        internal static bool Patched, PromptPatched;

        internal static UseFrame OpenForInvoke(ItemData itemData, Item.ItemAction action)
        {
            if (!Patched || !StolenTags.Patched || itemData == null) return null;
            int owner = itemData.owner;
            if (owner < 2) return null;
            var tp = TradePanel.instance;
            if (tp != null && tp.activeTrade != null) return null;   // InvokeAction trades instead; trade adds stay vanilla
            var item = itemData.item;
            if (item == null || item.ID <= 1) return null;
            var slot = itemData.slotController;
            if (slot != null)
            {
                if (action is ItemEquipable) return null;                 // P6 / IncludeEquipping keeps that job
                return OpenOwn(slot, owner, item.ID, itemData.meta);
            }
            if (!Cfg.FreeAmenityUse.Value || !Cfg.AmenityIds.Contains(item.ID)) return null;
            var meta = itemData.meta;
            if (Owned.HasAnyPermissionMeta(meta) || Owned.HasShopData(meta)) return null;   // shop stock and permitted items
            if (itemData.GetComponent<CollectibleItem>() == null) return null;             // world use only
            if (Compat.Foreign(StolenTags.CheckTarget, "free amenity use")) return null;
            Log.Verbose($"Amenity use: {item.Title} (id {item.ID}, owner {owner}) in place.");
            return UseFrames.Push(UseKind.InPlace, owner, item.ID, null);
        }

        // P24: the empty-container swap runs after InvokeAction returns (SRC/ItemInfo.cs:166-169, :202-209).
        internal static UseFrame OpenForLiquid(ItemData itemData)
        {
            if (!Patched || !StolenTags.Patched || itemData == null) return null;
            int owner = itemData.owner;
            if (owner < 2) return null;
            var tp = TradePanel.instance;
            if (tp != null && tp.activeTrade != null) return null;
            var item = itemData.item;
            if (item == null || item.ID <= 1 || itemData.slotController == null) return null;
            return OpenOwn(itemData.slotController, owner, item.ID, itemData.meta);
        }

        private static UseFrame OpenOwn(SlotController slot, int owner, int itemId, object[] meta)
        {
            if (!Cfg.FreeOwnUse.Value || !Owned.IsPlayerOwned(slot)) return null;
            if (Compat.Foreign(StolenTags.CheckTarget, "free own use")) return null;
            var src = Owned.FindPermission(meta);
            var perm = src == null ? null : new ItemOwnerPermission { ownerPermission = true, allowUse = src.allowUse };
            return UseFrames.Push(UseKind.Own, owner, itemId, perm);
        }

        // P3 prefix part, before the AddFrame decision. Inside a use of this owner's item: an Own use gives a permitted
        // source's leftovers the same permission (as vanilla AddItemWithPermission and UseLiquid already do for some of
        // them); an InPlace use hands its leftovers to the player (owner 0). The used item itself and money never change.
        internal static void BeforeAdd(Item item, ref int owner, ref object[] meta)
        {
            if (item == null || item.ID <= 1) return;
            var uf = UseFrames.Match(owner);
            if (uf == null) return;
            if (uf.kind == UseKind.InPlace)
            {
                if (item.ID == uf.itemId) return;
                Log.Verbose($"Amenity use: the {item.Title} it left is yours (was owner {owner}).");
                owner = 0;
                return;
            }
            if (uf.permission == null || Owned.HasAnyPermissionMeta(meta)) return;
            if (meta == null) meta = item.CloneMeta() ?? new object[0];
            ItemMetaUtilities.AddMeta(ref meta, new ItemOwnerPermission { ownerPermission = true, allowUse = uf.permission.allowUse });   // copies the array
            Log.Verbose($"Own use: the {item.Title} it left keeps the permission (owner {owner}).");
        }

        // P1 part. True: the check belongs to a live use frame and was handled here (skip vanilla).
        internal static bool HandleCheck(Item item, ref object[] meta, SlotController slot, int owner, AddFrame af, bool afMatch)
        {
            var uf = UseFrames.Match(owner);
            if (uf == null) return false;
            if (uf.kind == UseKind.InPlace)
            {
                // The use's own check on the world item (null slot). The tag it would write lands on a throwaway copy of
                // CollectibleItem.meta anyway (SRC/CollectibleItem.cs:403).
                if (item.ID != uf.itemId || slot != null) return false;
                Log.Verbose($"Amenity use: {item.Title} (id {item.ID}, owner {owner}): not theft.");
                return true;
            }
            if (slot != null && !Owned.IsPlayerOwned(slot)) return false;
            string what = StolenTags.MarkLikeVanilla(item, ref meta, slot, owner, af, afMatch);
            Log.Verbose($"Own use: {item.Title} (id {item.ID}, owner {owner}): theft check skipped{what}");
            return true;
        }
    }

    // P23: Item.InvokeAction(ItemData itemData, ItemAction action, bool worlduse) (SRC/Item.cs:720-730), the single caller of
    // ItemAction.Invoke: inventory Use, child and liquid actions, UseItemDirectly (auto-eat, Fewer Clicks) and world
    // hold-use. Public and non-virtual, and it spans the subclass Invoke, so every leftover add falls inside the frame.
    [HarmonyPatch(typeof(Item), nameof(Item.InvokeAction), new[] { typeof(ItemData), typeof(Item.ItemAction), typeof(bool) })]
    internal static class InvokeActionPatch
    {
        private static void Prefix(ItemData itemData, Item.ItemAction action, out UseFrame __state)
        {
            __state = null;
            try { __state = FreeUse.OpenForInvoke(itemData, action); }
            catch (Exception e) { Log.Exception("InvokeAction prefix", e); }
        }

        private static Exception Finalizer(Exception __exception, UseFrame __state)
        {
            if (__state != null) UseFrames.Pop(__state);
            return __exception;
        }
    }

    // P24: ItemInfo.UseLiquid(ItemData itemData, ItemLiquidData liquidData) (SRC/ItemInfo.cs:211-245).
    [HarmonyPatch(typeof(ItemInfo), nameof(ItemInfo.UseLiquid), new[] { typeof(ItemData), typeof(ItemLiquidData) })]
    internal static class UseLiquidPatch
    {
        private static void Prefix(ItemData itemData, out UseFrame __state)
        {
            __state = null;
            try { __state = FreeUse.OpenForLiquid(itemData); }
            catch (Exception e) { Log.Exception("UseLiquid prefix", e); }
        }

        private static Exception Finalizer(Exception __exception, UseFrame __state)
        {
            if (__state != null) UseFrames.Pop(__state);
            return __exception;
        }
    }

    // P25: CollectibleItem.UpdateUseMessage() (SRC/CollectibleItem.cs:618-680). Vanilla shows a red "Steal" for an owned,
    // unpermitted item and returns before the "Hold - Use" hint (:624-628), so in-place use of a listed item would stay
    // hidden. For those items only, show "Steal" plus the hold hint, built the way vanilla builds it (:661-679).
    [HarmonyPatch(typeof(CollectibleItem), nameof(CollectibleItem.UpdateUseMessage))]
    internal static class UseMessagePatch
    {
        private static void Postfix(CollectibleItem __instance)
        {
            if (!FreeUse.PromptPatched || !Cfg.FreeAmenityUse.Value) return;
            try { Amend(__instance); }
            catch (Exception e) { Log.Exception("UpdateUseMessage postfix", e); }
        }

        private static void Amend(CollectibleItem c)
        {
            var item = c?.Item?.Item;
            if (item == null || !Cfg.AmenityIds.Contains(item.ID)) return;
            // Advertise the hold-use only when OpenForInvoke would open its InPlace frame: not while another mod holds the
            // theft check (Compat caches the scan and warns once, so this is cheap every frame).
            if (!FreeUse.Patched || !StolenTags.Patched || Compat.Foreign(StolenTags.CheckTarget, "free amenity use")) return;
            if (c.owner == null || c.owner.ID < 2 || c.permissionToTake) return;
            if (Owned.HasAnyPermissionMeta(c.meta) || Owned.HasShopData(c.meta)) return;
            var action = item.FirstAction;
            if (!(action is ItemConsumable || action is ItemReadable || action is ItemMisc)) return;   // world-usable kinds (:463-479)
            var liquid = Owned.FindLiquid(c.meta);
            if (liquid != null && liquid.liquidItem != null) return;   // a drinkable container uses its liquid's action: leave it
            if (!c.InventoryIsNotFull()) return;                       // vanilla shows "Inventory full" then
            var ut = UseText.instance;
            if (ut == null) return;
            string hold = "Hold - " + action.Name;
            bool warn = false;
            var data = Vanilla.GetOrCreateItemDataF?.Invoke(c);
            if (data != null && !action.CanUse(data, out string cant))
            {
                hold = string.IsNullOrEmpty(cant) ? "Not usable" : cant;
                warn = true;
            }
            ut.ChangeUseText(c.title, ut.UseAndHoldText("Steal", hold, true, warn), true, false, false, false, false);
        }
    }
}
