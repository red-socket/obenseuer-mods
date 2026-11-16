using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WieldableItem;

namespace FewerClicks
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup checks the members each group needs, so only the feature using it fails (spec §2).
    // Optional members log one warning and the feature runs without the part that needs them.
    internal static class Vanilla
    {
        // ---- Top up (P1) ----

        // Storage.slotPanel (SRC/Storage.cs:248): the panel a storage's slots are captured into.
        internal static readonly AccessTools.FieldRef<Storage, Transform> SlotPanel = Ref<Storage, Transform>("slotPanel");

        // StoragePanelUI.takeAllSound (SRC/StoragePanelUI.cs:68).
        internal static readonly AccessTools.FieldRef<StoragePanelUI, SoundInfo> TakeAllSound = Ref<StoragePanelUI, SoundInfo>("takeAllSound");

        // ItemData.globaldrag (SRC/ItemData.cs:36): the item being dragged by mouse or controller, null otherwise.
        internal static readonly FieldInfo GlobalDragField = Field(typeof(ItemData), "globaldrag");

        // ItemData.itemMoveDisabled (SRC/ItemData.cs:47). Optional: without it Top up only checks itemDisabled.
        internal static readonly AccessTools.FieldRef<ItemData, bool> ItemMoveDisabled = Ref<ItemData, bool>("itemMoveDisabled", optional: true);

        internal static readonly MethodInfo UpdateStoragePanelUI = Method(typeof(StoragePanelUI), "UpdateStoragePanelUI", new[] { typeof(Storage) });

        // ---- Top up gamepad (P2) ----

        // InventoryNavigationMovement.Move (SRC/InventoryNavigationMovement.cs:428).
        internal static readonly MethodInfo NavMove = Method(typeof(InventoryNavigationMovement), "Move",
            new[] { typeof(MovementDirection), typeof(InventoryNavigationData), typeof(InputManager.InputType) });

        // SRC/InventoryNavigationMovement.cs:1300, :1315 (both private).
        internal static readonly MethodInfo SelectButton = Method(typeof(InventoryNavigationMovement), "SelectButton",
            new[] { typeof(InventoryNavigationData), typeof(GameObject) });
        internal static readonly MethodInfo SwitchToNextStorageSortButton = Method(typeof(InventoryNavigationMovement), "SwitchToNextStorageSortButton",
            new[] { typeof(InventoryNavigationData) });

        // ---- Auto Swing (P3) ----

        // MeleeWeapon.holdStarted / damageType (SRC/WieldableItem/MeleeWeapon.cs:30, :43).
        internal static readonly AccessTools.FieldRef<MeleeWeapon, bool> HoldStarted = Ref<MeleeWeapon, bool>("holdStarted");
        internal static readonly AccessTools.FieldRef<MeleeWeapon, DamageReceiver.DamageType> DamageType =
            Ref<MeleeWeapon, DamageReceiver.DamageType>("damageType");

        // WorkableResourceSource.currentStock (SRC/WorkableResourceSource.cs:65).
        internal static readonly AccessTools.FieldRef<WorkableResourceSource, int> CurrentStock = Ref<WorkableResourceSource, int>("currentStock");

        // MeleeWeapon.onPrimaryHold (SRC/WieldableItem/MeleeWeapon.cs:491), public; its body is just Use().
        internal static readonly MethodInfo OnPrimaryHold = Method(typeof(MeleeWeapon), "onPrimaryHold", Type.EmptyTypes);

        // MeleeWeapon.Hit(RaycastHit) (SRC/WieldableItem/MeleeWeapon.cs:266), private: called every frame from Update
        // with the current melee ray; lands once per swing while hitEnabled (animation events EnableHit/DisableHit).
        internal static readonly MethodInfo MeleeHit = Method(typeof(MeleeWeapon), "Hit", new[] { typeof(RaycastHit) });

        // ---- Quick Eat, Auto Eat, Slot keys (P4, P5) ----

        // GameController.InteractControls (SRC/GameController.cs:252), private.
        internal static readonly MethodInfo InteractControls = Method(typeof(GameController), "InteractControls", Type.EmptyTypes);

        // PlayerAutoConsume.needs and Need.threshold / allowUnsafe (SRC/PlayerAutoConsume.cs:8-21, :37). Optional: without
        // them Quick Eat runs with the game's values (threshold 50, unsafe fallback allowed).
        internal static readonly FieldInfo Needs = Field(typeof(PlayerAutoConsume), "needs", optional: true);
        private static readonly Type NeedType = Inner(typeof(PlayerAutoConsume), "Need");
        internal static readonly FieldInfo NeedThreshold = NeedType != null ? Field(NeedType, "threshold", optional: true) : null;
        internal static readonly FieldInfo NeedAllowUnsafe = NeedType != null ? Field(NeedType, "allowUnsafe", optional: true) : null;

        // KeyBindings.GetKey(string) (SRC/KeyBindings.cs:266), private. The public CheckKey switches the input type to
        // keyboard when the key is held (:257-264), so the collision check uses this side-effect-free one. Optional.
        internal static readonly Func<KeyBindings, string, KeyCode> BindingKey = BindingKeyDelegate();

        // InteractableList._listVisible / _pooledObjects (SRC/InteractableList.cs:45, :39). In third person the list
        // shows during free play and reads DPadVertical to move its selection (:84-158), so Quick Eat's D-pad button
        // stays quiet while it shows entries. Optional: without them the D-pad button is quiet in third person.
        internal static readonly AccessTools.FieldRef<InteractableList, bool> ListVisible = Ref<InteractableList, bool>("_listVisible", optional: true);
        internal static readonly AccessTools.FieldRef<InteractableList, System.Collections.Generic.List<GameObject>> ListItems =
            Ref<InteractableList, System.Collections.Generic.List<GameObject>>("_pooledObjects", optional: true);

        // Inventory.Start (SRC/Inventory.cs:213), private.
        internal static readonly MethodInfo InventoryStart = Method(typeof(Inventory), "Start", Type.EmptyTypes);

        // ---- Fill all (P6) ----

        // LiquidStoragePanelUI.OnEnable (SRC/LiquidStoragePanelUI.cs:103), private; StopFilling and PressFill are public.
        internal static readonly MethodInfo LiquidPanelOnEnable = Method(typeof(LiquidStoragePanelUI), "OnEnable", Type.EmptyTypes);
        internal static readonly MethodInfo StopFilling = Method(typeof(LiquidStoragePanelUI), "StopFilling", Type.EmptyTypes);

        // LiquidStoragePanelUI.crimeCounter (SRC/LiquidStoragePanelUI.cs:79). Optional: without it StopWhenSeen is off.
        internal static readonly AccessTools.FieldRef<LiquidStoragePanelUI, float> CrimeCounter =
            Ref<LiquidStoragePanelUI, float>("crimeCounter", optional: true);

        // ---- Methods we only call, checked for other mods' skipping patches (Compat, spec §10) ----

        internal static readonly MethodInfo PrimaryStopHold = Method(typeof(PlayerHandItems), "PrimaryStopHold", Type.EmptyTypes, optional: true);
        internal static readonly MethodInfo PressFill = Method(typeof(LiquidStoragePanelUI), "PressFill", Type.EmptyTypes, optional: true);
        internal static readonly MethodInfo MoveToOtherInventory = Method(typeof(SlotController), "MoveToOtherInventory",
            new[] { typeof(SlotController), typeof(int), typeof(int).MakeByRefType() }, optional: true);

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

        private static Type Inner(Type t, string name)
        {
            try
            {
                var inner = AccessTools.Inner(t, name);
                if (inner == null) Missing($"{t.Name}.{name}", null, true);
                return inner;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, true); return null; }
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
    }
}
