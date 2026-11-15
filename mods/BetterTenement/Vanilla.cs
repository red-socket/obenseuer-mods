using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterTenement
{
    // Private game members, read through Harmony's AccessTools rather than the publicized reference:
    // docs/modding-guide.md §4 notes that Mono's tolerance for direct private access was never tested.
    // A member that is missing logs one error and stays null; the feature using it checks and stays off.
    internal static class Vanilla
    {
        // TenementController (SRC/TenementController.cs:88-173)
        internal static readonly AccessTools.FieldRef<TenementController, List<Storage>> ResourceStorages =
            Ref<TenementController, List<Storage>>("tenementResourceStorages");
        internal static readonly AccessTools.FieldRef<TenementController, Dictionary<string, Dictionary<int, float>>> Snapshots =
            Ref<TenementController, Dictionary<string, Dictionary<int, float>>>("resourceStorageSnapshotsByContractor");
        internal static readonly AccessTools.FieldRef<TenementController, TenementContractor> CurrentSceneContractor =
            Ref<TenementController, TenementContractor>("currentSceneContractor");
        internal static readonly AccessTools.FieldRef<TenementController, List<TenementUpgradeMarked>> MarkedUpgrades =
            Ref<TenementController, List<TenementUpgradeMarked>>("markedUpgrades");
        internal static readonly MethodInfo RefreshResourceStorageSnapshot =
            Method(typeof(TenementController), "RefreshResourceStorageSnapshot", Type.EmptyTypes);

        // Storage (SRC/Storage.cs:89-259)
        internal static readonly AccessTools.FieldRef<Storage, bool> StartDone = Ref<Storage, bool>("startDone");
        internal static readonly AccessTools.FieldRef<Storage, int> CustomSlotAmount = Ref<Storage, int>("customStorageSlotAmount");
        internal static readonly AccessTools.FieldRef<Storage, bool> OverrideSlotAmount = Ref<Storage, bool>("overrideCategotySlotAmount");
        internal static readonly AccessTools.FieldRef<Storage, Transform> SlotPanel = Ref<Storage, Transform>("slotPanel");
        internal static readonly AccessTools.FieldRef<Storage, bool> StorageChanged = Ref<Storage, bool>("storageChanged");
        internal static readonly AccessTools.FieldRef<Storage, Relay> OnUpdate = Ref<Storage, Relay>("onUpdate");

        // StoragePanelUI (SRC/StoragePanelUI.cs:7, 68)
        internal static readonly AccessTools.FieldRef<StoragePanelUI, TextMeshProUGUI> StorageName = Ref<StoragePanelUI, TextMeshProUGUI>("storageName");
        internal static readonly AccessTools.FieldRef<StoragePanelUI, SoundInfo> TakeAllSound = Ref<StoragePanelUI, SoundInfo>("takeAllSound");

        // InventoryNavigationMovement (SRC/InventoryNavigationMovement.cs:17, 1300, 1315)
        internal static readonly MethodInfo SelectButton = Method(typeof(InventoryNavigationMovement), "SelectButton",
            new[] { typeof(InventoryNavigationData), typeof(GameObject) });
        internal static readonly MethodInfo SwitchToNextStorageSortButton = Method(typeof(InventoryNavigationMovement), "SwitchToNextStorageSortButton",
            new[] { typeof(InventoryNavigationData) });

        // UpgradeUI (SRC/UpgradeUI.cs:70-156)
        internal static readonly AccessTools.FieldRef<UpgradeUI, TextMeshProUGUI> UpgradeUITime = Ref<UpgradeUI, TextMeshProUGUI>("time");
        internal static readonly AccessTools.FieldRef<UpgradeUI, TenementContractor> UpgradeUIContractor = Ref<UpgradeUI, TenementContractor>("contractor");

        // UpgradeRequirementUI (SRC/UpgradeRequirementUI.cs:30)
        internal static readonly AccessTools.FieldRef<UpgradeRequirementUI, ItemData> RequirementItemData = Ref<UpgradeRequirementUI, ItemData>("itemData");

        // UpgradeButtonUI (SRC/UpgradeButtonUI.cs:87-91)
        internal static readonly AccessTools.FieldRef<UpgradeButtonUI, TenementUpgrade> ButtonCurrentUpgrade = Ref<UpgradeButtonUI, TenementUpgrade>("currentUpgrade");
        internal static readonly AccessTools.FieldRef<UpgradeButtonUI, int> ButtonApartmentNumber = Ref<UpgradeButtonUI, int>("apartmentNumber");
        internal static readonly AccessTools.FieldRef<UpgradeButtonUI, bool> ButtonTenementUpgrade = Ref<UpgradeButtonUI, bool>("tenementUpgrade");

        // ToolTipInfo (SRC/ToolTipInfo.cs:7-10), protected
        internal static readonly AccessTools.FieldRef<ToolTipInfo, string> TooltipTitle = Ref<ToolTipInfo, string>("toolTipInfoTitle");
        internal static readonly AccessTools.FieldRef<ToolTipInfo, string> TooltipDetails = Ref<ToolTipInfo, string>("toolTipInfoDetails");

        // ItemData (SRC/ItemData.cs:36): the item being dragged by mouse or controller, null otherwise.
        internal static readonly FieldInfo GlobalDragField = Field(typeof(ItemData), "globaldrag");

        // FurniturePlaceable (SRC/FurniturePlaceable.cs:83, 112): objects that Take also takes.
        internal static readonly AccessTools.FieldRef<FurniturePlaceable, List<FurniturePlaceable>> ChildFurnitures =
            Ref<FurniturePlaceable, List<FurniturePlaceable>>("childFurnitures");
        internal static readonly AccessTools.FieldRef<FurniturePlaceable, List<FurniturePlaceable>> AttachedFurnitures =
            Ref<FurniturePlaceable, List<FurniturePlaceable>>("attachedFurnitures");

        // ApartmentInfoUI (SRC/ApartmentInfoUI.cs:33)
        internal static readonly AccessTools.FieldRef<ApartmentInfoUI, TextMeshProUGUI> NotAvailableInfoText = Ref<ApartmentInfoUI, TextMeshProUGUI>("notAvailableInfoText");

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Log.Error($"{typeof(T).Name}.{name} not found ({e.Message}); the feature using it is off."); return null; }
        }

        private static FieldInfo Field(Type t, string name)
        {
            try
            {
                var f = AccessTools.Field(t, name);
                if (f == null) Log.Error($"{t.Name}.{name} not found; the feature using it is off.");
                return f;
            }
            catch (Exception e) { Log.Error($"{t.Name}.{name} not found ({e.Message}); the feature using it is off."); return null; }
        }

        private static MethodInfo Method(Type t, string name, Type[] args)
        {
            try
            {
                var m = AccessTools.Method(t, name, args);
                if (m == null) Log.Error($"{t.Name}.{name} not found; the feature using it is off.");
                return m;
            }
            catch (Exception e) { Log.Error($"{t.Name}.{name} not found ({e.Message}); the feature using it is off."); return null; }
        }
    }
}
