using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OS.Items;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TidyInventory
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup checks the members each group needs, so only the feature using it fails (spec §8.1, §8.3).
    // Optional members log one warning and the feature runs without the part that needs them. Every member here was
    // checked against the shipped Assembly-CSharp.dll with Mono.Cecil.
    internal static class Vanilla
    {
        private static readonly Type PtrEvent = typeof(PointerEventData);

        // ---- Pins (P1-P13) ----

        internal static readonly MethodInfo InventoryStart = Method(typeof(Inventory), "Start", Type.EmptyTypes);
        internal static readonly MethodInfo SortSlots = Method(typeof(Inventory), "SortSlots", new[] { typeof(SlotController[]) });
        internal static readonly MethodInfo MoveToOtherInventory = Method(typeof(SlotController), "MoveToOtherInventory",
            new[] { typeof(SlotController), typeof(int), typeof(int).MakeByRefType() });
        internal static readonly MethodInfo SlotDropItem = Method(typeof(SlotController), "DropItem",
            new[] { typeof(ItemData), typeof(int).MakeByRefType(), typeof(bool), typeof(bool), typeof(bool) });
        // ItemData.CantDrag (SRC/ItemData.cs:745), private.
        internal static readonly MethodInfo CantDrag = Method(typeof(ItemData), "CantDrag", Type.EmptyTypes);
        internal static readonly MethodInfo ItemDropItem = Method(typeof(ItemData), "DropItem", new[] { typeof(int), typeof(bool) });
        internal static readonly MethodInfo ShowSlider = Method(typeof(AmountSliderUI), "ShowSlider", new[] { typeof(ItemData), typeof(AmountSliderType) });
        internal static readonly MethodInfo TradeCanSell = Method(typeof(Trade), "CanSell", new[] { typeof(ItemData), typeof(int), typeof(bool), typeof(bool) });
        internal static readonly MethodInfo TradeSpecialCanSell = Method(typeof(TradeSpecial), "CanSell", new[] { typeof(ItemData), typeof(int), typeof(bool), typeof(bool) });
        internal static readonly MethodInfo QuickMove = Method(typeof(ItemData), "QuickMove", new[] { typeof(int) });
        // Slot.CheckItemMetas() (SRC/Slot.cs:505), the private parameterless entry; the two overloads are not patched.
        internal static readonly MethodInfo CheckItemMetas = Method(typeof(Slot), "CheckItemMetas", Type.EmptyTypes);
        internal static readonly MethodInfo Equip = Method(typeof(Inventory), "Equip", new[] { typeof(string), typeof(SlotController) });
        // The override declared on GlobalState (SRC/GlobalState.cs:35).
        internal static readonly MethodInfo GlobalStateOnLoadingGame = Method(typeof(GlobalState), "OnLoadingGame", Type.EmptyTypes);
        // BottleRecyclingPanel.open (SRC/BottleRecyclingPanel.cs:23).
        internal static readonly AccessTools.FieldRef<BottleRecyclingPanel, bool> RecyclingOpen = Ref<BottleRecyclingPanel, bool>("open");

        // ---- Pin return, capacity (P14-P16) ----

        internal static readonly MethodInfo AddItems = Method(typeof(ItemOperations), "AddItems",
            new[] { typeof(Item), typeof(int), typeof(int), typeof(object[]), typeof(SlotController), typeof(bool), typeof(bool) });
        internal static readonly MethodInfo AddItemAndReturnRemaining = Method(typeof(ItemOperations), "AddItemAndReturnRemaining",
            new[] { typeof(Item), typeof(int), typeof(SlotController), typeof(object[]), typeof(int), typeof(int), typeof(ItemOperations.ItemOperationSettings) });
        // The instance overload (SRC/SlotController.cs:791); never the static one.
        internal static readonly MethodInfo SlotHasSpaceForItem = Method(typeof(SlotController), "SlotHasSpaceForItem",
            new[] { typeof(Item), typeof(int).MakeByRefType(), typeof(int), typeof(int), typeof(int), typeof(object[]), typeof(bool) });
        // Rewards whose callers ignore a "full" answer (SRC/Inventory.cs:592-615, GrowItem.cs:240-289): P16 passes through.
        internal static readonly MethodInfo AddItemDialogue = Method(typeof(Inventory), "AddItemDialogue", new[] { typeof(double) });
        internal static readonly MethodInfo AddItemDialogueAmount = Method(typeof(Inventory), "AddItemDialogueAmount", new[] { typeof(double), typeof(double) });
        internal static readonly MethodInfo GrowItemGetItem = Method(typeof(GrowItem), "GetItem", Type.EmptyTypes);
        // ItemInfo.UseLiquid (SRC/ItemInfo.cs:211): re-adds the emptied container to its own slot, then writes the copied
        // skin and permission onto that slot's stack.
        internal static readonly MethodInfo UseLiquid = Method(typeof(ItemInfo), "UseLiquid", new[] { typeof(ItemData), typeof(ItemLiquidData) });

        // ---- Pin pad drag, key, cosmetics (P17-P25) ----

        internal static readonly MethodInfo StartDragging = Method(typeof(InventoryNavigationHandler), "StartDragging", new[] { typeof(bool) });
        internal static readonly MethodInfo ItemPointerDown = Method(typeof(ItemData), "OnPointerDown", new[] { PtrEvent });
        internal static readonly MethodInfo ItemPointerClick = Method(typeof(ItemData), "OnPointerClick", new[] { PtrEvent });
        internal static readonly MethodInfo ItemBeginDrag = Method(typeof(ItemData), "OnBeginDrag", new[] { PtrEvent });
        internal static readonly MethodInfo ItemDrag = Method(typeof(ItemData), "OnDrag", new[] { PtrEvent });
        internal static readonly MethodInfo ItemEndDrag = Method(typeof(ItemData), "OnEndDrag", new[] { PtrEvent });
        internal static readonly MethodInfo SlotPointerClick = Method(typeof(Slot), "OnPointerClick", new[] { PtrEvent });
        internal static readonly MethodInfo SlotPointerEnter = Method(typeof(Slot), "OnPointerEnter", new[] { typeof(ItemData) });
        internal static readonly MethodInfo UpdateInfo = Method(typeof(ItemInfoPanel), "UpdateInfo", new[] { typeof(ItemData), typeof(ChildItem) });
        // ItemInfoPanel.cantUseButtonText / dropButton (SRC/ItemInfoPanel.cs:26, :48).
        internal static readonly AccessTools.FieldRef<ItemInfoPanel, TextMeshProUGUI> CantUseText = Ref<ItemInfoPanel, TextMeshProUGUI>("cantUseButtonText");
        internal static readonly AccessTools.FieldRef<ItemInfoPanel, GameObject> DropButton = Ref<ItemInfoPanel, GameObject>("dropButton");
        // ItemData.globaldrag (SRC/ItemData.cs:36): the item being dragged by mouse or controller, null otherwise.
        internal static readonly FieldInfo GlobalDragField = Field(typeof(ItemData), "globaldrag");

        // ---- Storage buttons (P26) ----

        internal static readonly MethodInfo UpdateStoragePanelUI = Method(typeof(StoragePanelUI), "UpdateStoragePanelUI", new[] { typeof(Storage) });
        // Storage.slotPanel (SRC/Storage.cs:248): the panel a storage's slots are captured into.
        internal static readonly AccessTools.FieldRef<Storage, Transform> SlotPanel = Ref<Storage, Transform>("slotPanel");
        // StoragePanelUI.storageName (SRC/StoragePanelUI.cs:7).
        internal static readonly AccessTools.FieldRef<StoragePanelUI, TextMeshProUGUI> StorageName = Ref<StoragePanelUI, TextMeshProUGUI>("storageName");
        // StoragePanelUI.takeAllSound (SRC/StoragePanelUI.cs:68). Optional: Empty is silent without it.
        internal static readonly AccessTools.FieldRef<StoragePanelUI, SoundInfo> TakeAllSound = Ref<StoragePanelUI, SoundInfo>("takeAllSound", optional: true);

        // ---- Gamepad (P27) ----

        internal static readonly MethodInfo NavMove = Method(typeof(InventoryNavigationMovement), "Move",
            new[] { typeof(MovementDirection), typeof(InventoryNavigationData), typeof(InputManager.InputType) });
        // SRC/InventoryNavigationMovement.cs:1161, :1295, :1305, :1315 (all private).
        internal static readonly MethodInfo SelectButton = Method(typeof(InventoryNavigationMovement), "SelectButton",
            new[] { typeof(InventoryNavigationData), typeof(GameObject) });
        internal static readonly MethodInfo SwitchToNextStorageTakeAllButton = Method(typeof(InventoryNavigationMovement), "SwitchToNextStorageTakeAllButton",
            new[] { typeof(InventoryNavigationData) });
        internal static readonly MethodInfo SwitchToNextInventorySortButton = Method(typeof(InventoryNavigationMovement), "SwitchToNextInventorySortButton",
            new[] { typeof(InventoryNavigationData) });
        internal static readonly MethodInfo StoragePowerButtonActive = Method(typeof(InventoryNavigationMovement), "StoragePowerButtonActive", Type.EmptyTypes);
        // InventoryNavigationMovement.inventorySortButton (SRC/InventoryNavigationMovement.cs:14): the inventory's Sort button.
        internal static readonly AccessTools.FieldRef<InventoryNavigationMovement, GameObject> InventorySortButton =
            Ref<InventoryNavigationMovement, GameObject>("inventorySortButton");

        // ---- Despawn (P28) ----

        internal static readonly MethodInfo CollectibleExpire = Method(typeof(Collectible), "Expire", new[] { typeof(bool) });
        internal static readonly PropertyInfo ExpirationTimer = Property(typeof(Collectible), "ExpirationTimer");

        // ---- Stolen stacks (P29) ----

        internal static readonly MethodInfo CompareItemMetas = Method(typeof(ItemMetaUtilities), "CompareItemMetas",
            new[] { typeof(object[]), typeof(object[]), typeof(ItemMetaUtilities.MetaAction).MakeByRefType(), typeof(int), typeof(bool), typeof(bool) });
        // The static SlotController.SlotHasSpaceForItem (SRC/SlotController.cs:796), the only caller of MetaMergeAllowed
        // apart from ForceItemToSlot: it names the target stack of every merge check.
        internal static readonly MethodInfo SlotHasSpaceForItemStatic = Method(typeof(SlotController), "SlotHasSpaceForItem",
            new[] { typeof(Item), typeof(SlotController), typeof(ItemStack), typeof(int).MakeByRefType(), typeof(int), typeof(int), typeof(int),
                typeof(object[]), typeof(bool), typeof(bool), typeof(ItemOperations.ItemOperationSettings) });

        // ---- Duck (P30-P33) ----

        internal static readonly MethodInfo TaskItemSelected = Method(typeof(TaskItemUI), "Selected", Type.EmptyTypes);
        internal static readonly MethodInfo TaskItemUnselected = Method(typeof(TaskItemUI), "Unselected", Type.EmptyTypes);
        internal static readonly MethodInfo ActivateNavigationSelection = Method(typeof(ItemsUIPanel), "ActivateNavigationSelection", Type.EmptyTypes);
        internal static readonly MethodInfo GetButtonInfoTab0 = Method(typeof(InventoryNavigationHandler), "GetButtonInfoTab0",
            new[] { typeof(InventoryNavigationHandler.CurrentInventoryWindow), typeof(InputManager.InputType) });
        // TaskItemUI.item (SRC/TaskItemUI.cs:65); ItemsUIPanel.navRow / navSelected / itemListRowData (SRC/ItemsUIPanel.cs:30, :69, :75).
        internal static readonly AccessTools.FieldRef<TaskItemUI, TaskItem> TaskItemOf = Ref<TaskItemUI, TaskItem>("item");
        internal static readonly AccessTools.FieldRef<ItemsUIPanel, int> NavRow = Ref<ItemsUIPanel, int>("navRow");
        internal static readonly AccessTools.FieldRef<ItemsUIPanel, Component> NavSelected = Ref<ItemsUIPanel, Component>("navSelected");
        internal static readonly AccessTools.FieldRef<ItemsUIPanel, List<ItemListRowData>> ItemRows = Ref<ItemsUIPanel, List<ItemListRowData>>("itemListRowData");
        // PlayerStatsDuck.achievementEnabled (SRC/PlayerStatsDuck.cs:24). Without it the discard is refused (spec §6).
        internal static readonly AccessTools.FieldRef<PlayerStatsDuck, bool> DuckAchievementEnabled = Ref<PlayerStatsDuck, bool>("achievementEnabled");

        // ---- Keys ----

        // KeyBindings.GetKey(string) (SRC/KeyBindings.cs:266), private. The public CheckKey switches the input type to
        // keyboard when the key is held (:257-264), so the collision check uses this side-effect-free one. Optional.
        internal static readonly Func<KeyBindings, string, KeyCode> BindingKey = BindingKeyDelegate();

        // ---- Methods we only call, checked for other mods' skipping patches (Compat, spec §8.2) ----

        internal static readonly MethodInfo SlotRemoveItem = Method(typeof(SlotController), "RemoveItem", Type.EmptyTypes, optional: true);

        // ---- Helpers ----

        internal static MethodInfo Method(Type t, string name, Type[] args, bool optional = false)
        {
            try
            {
                var m = AccessTools.DeclaredMethod(t, name, args);
                if (m == null) Missing($"{t.Name}.{name}", null, optional);
                return m;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, optional); return null; }
        }

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name, bool optional = false)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Missing($"{typeof(T).Name}.{name}", e, optional); return null; }
        }

        private static FieldInfo Field(Type t, string name, bool optional = false)
        {
            try
            {
                var f = AccessTools.Field(t, name);
                if (f == null) Missing($"{t.Name}.{name}", null, optional);
                return f;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, optional); return null; }
        }

        private static PropertyInfo Property(Type t, string name, bool optional = false)
        {
            try
            {
                var p = AccessTools.DeclaredProperty(t, name);
                if (p == null || p.GetSetMethod(true) == null) Missing($"{t.Name}.{name}", null, optional);
                return p;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, optional); return null; }
        }

        private static Func<KeyBindings, string, KeyCode> BindingKeyDelegate()
        {
            var m = Method(typeof(KeyBindings), "GetKey", new[] { typeof(string) }, optional: true);
            if (m == null) return null;
            try { return (Func<KeyBindings, string, KeyCode>)Delegate.CreateDelegate(typeof(Func<KeyBindings, string, KeyCode>), m); }
            catch (Exception e) { Missing("KeyBindings.GetKey", e, true); return null; }
        }

        private static void Missing(string what, Exception e, bool optional)
        {
            string why = e != null ? $" ({e.Message})" : "";
            if (optional) Log.Warn($"{what} not found{why}; the part of the feature using it is off.");
            else Log.Error($"{what} not found{why}; the feature using it is off.");
        }

        // ---- Private calls ----

        internal static DefaultUIButton Select(InventoryNavigationMovement nav, InventoryNavigationData data, GameObject go) =>
            (DefaultUIButton)SelectButton.Invoke(nav, new object[] { data, go });
    }
}
