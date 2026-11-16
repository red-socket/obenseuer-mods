using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Workshop
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4, spec §3.2). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup checks the members each group needs, so only the feature using it fails (spec §3.1). Every
    // member here was checked against the shipped Assembly-CSharp.dll with Mono.Cecil (design.md §9).
    internal static class Vanilla
    {
        // ---- Patch targets (W1-W15) ----

        internal static readonly MethodInfo ShowRecipe = Method(typeof(RecipeInfoUI), "ShowRecipe",
            new[] { typeof(Recipe), typeof(int), typeof(float), typeof(float), typeof(bool), typeof(bool) });
        internal static readonly MethodInfo NavMove = Method(typeof(InventoryNavigationMovement), "Move",
            new[] { typeof(MovementDirection), typeof(InventoryNavigationData), typeof(InputManager.InputType) });
        internal static readonly MethodInfo Craft = Method(typeof(manufacturing), "Craft", Type.EmptyTypes);
        internal static readonly MethodInfo StartProcess = Method(typeof(manufacturingProcess), "StartProcess", Type.EmptyTypes);
        // The overrides declared on GlobalState (SRC/GlobalState.cs:29-37).
        internal static readonly MethodInfo GlobalStateOnSavingGame = Method(typeof(GlobalState), "OnSavingGame", Type.EmptyTypes);
        internal static readonly MethodInfo GlobalStateOnLoadingGame = Method(typeof(GlobalState), "OnLoadingGame", Type.EmptyTypes);
        // The override declared on CraftingBase (SRC/CraftingBase.cs:401). manufacturing (:93) and manufacturingProcess
        // (:86) both call base.OnLoadingGame() first, so one postfix sees every station.
        internal static readonly MethodInfo CraftingBaseOnLoadingGame = Method(typeof(CraftingBase), "OnLoadingGame", Type.EmptyTypes);
        internal static readonly MethodInfo UpdateUseMessage = Method(typeof(CraftingBase), "UpdateUseMessage", Type.EmptyTypes);
        // manufacturing.CraftButtonMessage (SRC/manufacturing.cs:649), protected.
        internal static readonly MethodInfo CraftButtonMessage = Method(typeof(manufacturing), "CraftButtonMessage", Type.EmptyTypes);
        internal static readonly MethodInfo ProgressSlotClick = Method(typeof(ProgressSlot), "OnPointerClick", new[] { typeof(PointerEventData) });
        // CraftingBase.CreateItems (SRC/CraftingBase.cs:602), protected virtual; manufacturingProcess overrides it and
        // calls base, manufacturing does not override it.
        internal static readonly MethodInfo CreateItems = Method(typeof(CraftingBase), "CreateItems",
            new[] { typeof(Recipe), typeof(List<KeyValuePair<ItemStack, int>>), typeof(int), typeof(bool), typeof(CraftingBase.CraftingInputData), typeof(int) });
        internal static readonly MethodInfo IncreaseSkill = Method(typeof(PlayerStats), "IncreaseSkill",
            new[] { typeof(PlayerStats.PlayerSkillType), typeof(float), typeof(bool) });
        internal static readonly MethodInfo TestAndApply = Method(typeof(Recipe.RecipeStatsEditData), "TestAndApply", new[] { typeof(bool) });
        internal static readonly MethodInfo StorageStart = Method(typeof(Storage), "Start", Type.EmptyTypes);
        internal static readonly MethodInfo EnterManu = Method(typeof(ManufacturingProcessPanel), "EnterManu", new[] { typeof(manufacturingProcess) });

        // ---- Private calls ----

        // SRC/InventoryNavigationMovement.cs:1278, :1305 (both private).
        internal static readonly MethodInfo SwitchToNextCraftingButton = Method(typeof(InventoryNavigationMovement), "SwitchToNextCraftingButton",
            new[] { typeof(InventoryNavigationData) });
        internal static readonly MethodInfo SelectButton = Method(typeof(InventoryNavigationMovement), "SelectButton",
            new[] { typeof(InventoryNavigationData), typeof(GameObject) });
        // CraftingBase.CompareRecipeToItems (SRC/CraftingBase.cs:1432), protected.
        internal static readonly MethodInfo CompareRecipeToItems = Method(typeof(CraftingBase), "CompareRecipeToItems",
            new[] { typeof(Recipe), typeof(ItemStack[]), typeof(bool), typeof(bool) });
        // manufacturingProcess.SetActiveRecipe (SRC/manufacturingProcess.cs:481), private.
        internal static readonly MethodInfo SetActiveRecipe = Method(typeof(manufacturingProcess), "SetActiveRecipe", new[] { typeof(Recipe) });
        // The three-argument overload only: the two-argument one dereferences slot.playerSlot, null in closed storage.
        internal static readonly MethodInfo MoveToOtherInventory = Method(typeof(SlotController), "MoveToOtherInventory",
            new[] { typeof(SlotController), typeof(int), typeof(int).MakeByRefType() });

        // ---- Fields ----

        // Storage (SRC/Storage.cs:89, :255, :259, :213), all private.
        internal static readonly AccessTools.FieldRef<Storage, bool> StorageStartDone = Ref<Storage, bool>("startDone");
        internal static readonly AccessTools.FieldRef<Storage, int> CustomSlotAmount = Ref<Storage, int>("customStorageSlotAmount");
        internal static readonly AccessTools.FieldRef<Storage, bool> StorageChanged = Ref<Storage, bool>("storageChanged");
        internal static readonly AccessTools.FieldRef<Storage, Relay> StorageOnUpdate = Ref<Storage, Relay>("onUpdate");
        // manufacturing (SRC/manufacturing.cs:65, :68 protected; :72 private).
        internal static readonly AccessTools.FieldRef<manufacturing, List<CraftInProgress>> Crafts = Ref<manufacturing, List<CraftInProgress>>("craftsInProgresses");
        internal static readonly AccessTools.FieldRef<manufacturing, int> CraftingInProgress = Ref<manufacturing, int>("CraftingInProgress");
        internal static readonly AccessTools.FieldRef<manufacturing, int> LastInstantCraftFrame = Ref<manufacturing, int>("lastInstantCraftFrame");
        // CraftingBase (SRC/CraftingBase.cs:271, :249, :240), protected.
        internal static readonly AccessTools.FieldRef<CraftingBase, Recipe> SelectedRecipe = Ref<CraftingBase, Recipe>("selectedRecipe");
        internal static readonly AccessTools.FieldRef<CraftingBase, List<Recipe>> AllRecipes = Ref<CraftingBase, List<Recipe>>("AllRecipes");
        internal static readonly AccessTools.FieldRef<CraftingBase, bool> ManuActive = Ref<CraftingBase, bool>("manuActive");
        // TimeOfDayAzure.timers (SRC/TimeOfDayAzure.cs:229), protected.
        internal static readonly AccessTools.FieldRef<TimeOfDayAzure, List<TimeOfDayAzure.Timer>> Timers = Ref<TimeOfDayAzure, List<TimeOfDayAzure.Timer>>("timers");
        // ItemData.globaldrag (SRC/ItemData.cs:36) and itemMoveDisabled (:47, private).
        internal static readonly FieldInfo GlobalDragField = Field(typeof(ItemData), "globaldrag");
        internal static readonly AccessTools.FieldRef<ItemData, bool> ItemMoveDisabled = Ref<ItemData, bool>("itemMoveDisabled", optional: true);

        // ---- Keys ----

        // KeyBindings.GetKey(string) (SRC/KeyBindings.cs:266), private. The public CheckKey switches the input type to
        // keyboard when the key is held (:257-264), so the collision check uses this side-effect-free one. Optional.
        internal static readonly Func<KeyBindings, string, KeyCode> BindingKey = BindingKeyDelegate();

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

        // ---- Typed wrappers ----

        internal static DefaultUIButton Select(InventoryNavigationMovement nav, InventoryNavigationData data, GameObject go) =>
            (DefaultUIButton)SelectButton.Invoke(nav, new object[] { data, go });

        internal static bool Compare(CraftingBase cb, Recipe r, bool onlyExecutable = true) =>
            (bool)CompareRecipeToItems.Invoke(cb, new object[] { r, null, false, onlyExecutable });

        internal static string CraftLabel(manufacturing m) => (string)CraftButtonMessage.Invoke(m, null);

        internal static List<CraftInProgress> JobsOf(manufacturing m) => Crafts != null ? Crafts(m) : null;

        internal static bool IsActive(CraftingBase cb) => cb != null && ManuActive != null && ManuActive(cb);

        // The station's panel is open: it is the current station and active.
        internal static bool PanelOpen(CraftingBase cb) => cb != null && CraftingBase.currentCraftingBase == cb && IsActive(cb);
    }
}
