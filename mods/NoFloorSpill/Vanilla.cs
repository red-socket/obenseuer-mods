using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NoFloorSpill
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4). A member that is missing logs one error and stays null; Plugin.PatchGroup
    // checks the members each group needs, so only the group that uses a missing member fails (spec §2.5).
    internal static class Vanilla
    {
        // ---- Fields ----

        // CraftingBase.manuActive (SRC/CraftingBase.cs:240): true between Enter() and Exit(), i.e. the station window is open.
        internal static readonly AccessTools.FieldRef<CraftingBase, bool> ManuActive = Ref<CraftingBase, bool>("manuActive");

        // Storage.overflowSpot / dropItemValues / storageChanged (SRC/Storage.cs:110, :114, :259).
        internal static readonly AccessTools.FieldRef<Storage, GameObject> OverflowSpot = Ref<Storage, GameObject>("overflowSpot");
        internal static readonly AccessTools.FieldRef<Storage, ItemOperations.DropItemValues> DropValues =
            Ref<Storage, ItemOperations.DropItemValues>("dropItemValues");
        internal static readonly AccessTools.FieldRef<Storage, bool> StorageChanged = Ref<Storage, bool>("storageChanged");

        // ---- Patch targets ----

        // SRC/Fuel.cs:259, :272 (both private).
        internal static readonly MethodInfo SpawnRefuse = Method(typeof(Fuel), "SpawnRefuse", Type.EmptyTypes);
        internal static readonly MethodInfo SpawnRefuseAll = Method(typeof(Fuel), "SpawnRefuseAll", Type.EmptyTypes);

        // SRC/CraftingBase.cs:1265 (private), exact types including the by-ref arrays.
        internal static readonly MethodInfo RemoveAmmo = Method(typeof(CraftingBase), "RemoveAmmo", new[]
        {
            typeof(ItemStack), typeof(int), typeof(Recipe.RecipeRequirementData[]).MakeByRefType(),
            typeof(List<float>).MakeByRefType(), typeof(bool), typeof(bool),
        });

        // ---- Callees swapped by the transpilers ----

        // SlotController.AddItem(Item, int, int, object[], GameObject, bool, int, DropItemValues) (SRC/SlotController.cs:54).
        internal static readonly MethodInfo SlotAddItem = Method(typeof(SlotController), "AddItem", new[]
        {
            typeof(Item), typeof(int), typeof(int), typeof(object[]), typeof(GameObject), typeof(bool), typeof(int),
            typeof(ItemOperations.DropItemValues),
        });

        // Storage.AddItem(Item, int, int, object[], int, bool) (SRC/Storage.cs:1242).
        internal static readonly MethodInfo StorageAddItem = Method(typeof(Storage), "AddItem", new[]
        {
            typeof(Item), typeof(int), typeof(int), typeof(object[]), typeof(int), typeof(bool),
        });

        // ---- Helpers ----

        // Declared method by name and parameter types. Logs and returns null when missing or ambiguous.
        internal static MethodInfo Method(Type t, string name, Type[] args)
        {
            try
            {
                var m = AccessTools.DeclaredMethod(t, name, args);
                if (m == null) Log.Error($"{t.Name}.{name} not found; the feature using it is off.");
                return m;
            }
            catch (Exception e) { Log.Error($"{t.Name}.{name} not found ({e.Message}); the feature using it is off."); return null; }
        }

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Log.Error($"{typeof(T).Name}.{name} not found ({e.Message}); the feature using it is off."); return null; }
        }
    }
}
