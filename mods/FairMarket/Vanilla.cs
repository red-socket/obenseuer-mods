using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OS.Items;
using TMPro;
using UnityEngine;

namespace FairMarket
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup checks the members each group needs, so only the feature using it fails (spec §3).
    // Every target was checked against SRC and the shipped Assembly-CSharp.dll with Mono.Cecil (design.md §4).
    internal static class Vanilla
    {
        // ---- Stock (S1, S2) ----

        // S1 SRC/StorageSpawnCategoryDatabase.cs:113, 47 B; lookups :63 and :85 (public).
        internal static readonly MethodInfo ConstructSpawnDb = Method(typeof(StorageSpawnCategoryDatabase), "ConstructSpawnCategoryDatabase", Type.EmptyTypes);
        internal static readonly MethodInfo FetchSpawnCategory = Method(typeof(StorageSpawnCategoryDatabase), "FetchStorageSpawnCategory", new[] { typeof(string) });
        internal static readonly MethodInfo GetSpawnCategories = Method(typeof(StorageSpawnCategoryDatabase), "GetStorageSpawnCategories", Type.EmptyTypes);
        // S2 SRC/Storage.cs:927, 269 B; SetNextItemsToSpawn :970, 544 B (public). spawnItemsFromCategory :145 (private).
        internal static readonly MethodInfo SpawnItems = Method(typeof(Storage), "SpawnItems", Type.EmptyTypes);
        internal static readonly MethodInfo SetNextItemsToSpawn = Method(typeof(Storage), "SetNextItemsToSpawn", Type.EmptyTypes);
        internal static readonly AccessTools.FieldRef<Storage, bool> SpawnFromCategory = Ref<Storage, bool>("spawnItemsFromCategory");

        // ---- Buyers (B1) ----

        // B1 SRC/Trade.cs:557, 30 B, non-virtual. SRC/SlotController.cs:726, static, 335 B.
        internal static readonly MethodInfo TradeCheckIfAllowed = Method(typeof(Trade), "CheckIfAllowed", new[] { typeof(ItemData) });
        internal static readonly MethodInfo CheckIfInAllowedCategoy = Method(typeof(SlotController), "CheckIfInAllowedCategoy",
            new[] { typeof(Item), typeof(string[]), typeof(string[]), typeof(object[]) });

        // ---- Hours (H1-H3) ----

        // H1 SRC/DoorChangelevel.cs:138, override, 79 B. H2 SRC/Door.cs:317, 84 B. H3 SRC/Trade.cs:313, override, 14 B
        // (TradeSpecial inherits it). UseText.overridden SRC/UseText.cs:49 (private).
        internal static readonly MethodInfo DoorChangelevelUseMessage = Method(typeof(DoorChangelevel), "UpdateUseMessage", Type.EmptyTypes);
        internal static readonly MethodInfo DoorUseMessage = Method(typeof(Door), "UpdateDoorUseMessage", new[] { typeof(bool) });
        internal static readonly MethodInfo TradeUseMessage = Method(typeof(Trade), "UpdateUseMessage", Type.EmptyTypes);
        internal static readonly AccessTools.FieldRef<UseText, bool> UseTextOverridden = Ref<UseText, bool>("overridden");

        // ---- Dishwasher (L1, L2) ----

        // L1 SRC/RecipeInfoUI.cs:115, 2420 B. recipeInfoListPanel :21 (private). L2 SRC/Dishwasher.cs:30, override, 487 B.
        internal static readonly MethodInfo ShowRecipe = Method(typeof(RecipeInfoUI), "ShowRecipe",
            new[] { typeof(Recipe), typeof(int), typeof(float), typeof(float), typeof(bool), typeof(bool) });
        internal static readonly AccessTools.FieldRef<RecipeInfoUI, GameObject> RecipeInfoListPanel = Ref<RecipeInfoUI, GameObject>("recipeInfoListPanel");
        internal static readonly MethodInfo DishwasherCheckItems = Method(typeof(Dishwasher), "CheckItems", new[] { typeof(bool) });

        // ---- Certificates (C1) ----

        // C1 SRC/TradeSpecial.cs:126, private, 471 B.
        internal static readonly MethodInfo FindValidCertificates = Method(typeof(TradeSpecial), "FindValidCertificates",
            new[] { typeof(ItemData), typeof(int), typeof(List<SlotController>).MakeByRefType() });

        // ---- Invoices (I1, I2, W1, W2, U1) ----

        // I1 SRC/ToolTip.cs:702, static, 38 B. I2 SRC/ItemInfoPanel.cs:182, 1255 B; itemValue :42 (private).
        internal static readonly MethodInfo InvoiceDataToString = Method(typeof(ToolTip), "InvoiceDataToString", new[] { typeof(ItemInvoiceData) });
        internal static readonly MethodInfo UpdateInfo = Method(typeof(ItemInfoPanel), "UpdateInfo", new[] { typeof(ItemData), typeof(ChildItem) });
        internal static readonly AccessTools.FieldRef<ItemInfoPanel, TextMeshProUGUI> ItemValue = Ref<ItemInfoPanel, TextMeshProUGUI>("itemValue");
        // W1 SRC/PauseNavigationHandler.cs:90, private (a Unity message, called directly). W2 :133, public, 585 B.
        internal static readonly MethodInfo PauseNavUpdate = Method(typeof(PauseNavigationHandler), "Update", Type.EmptyTypes);
        internal static readonly MethodInfo HandleBackButtonClick = Method(typeof(PauseNavigationHandler), "HandleBackButtonClick", Type.EmptyTypes);
        // U1 SRC/InventoryNavigationHandler.cs:400, 77 B; KeyCodeInfoToString :464, static.
        internal static readonly MethodInfo GetButtonInfo = Method(typeof(InventoryNavigationHandler), "GetButtonInfo",
            new[] { typeof(int), typeof(InventoryNavigationHandler.CurrentInventoryWindow), typeof(InputManager.InputType) });
        internal static readonly MethodInfo KeyCodeInfoToString = Method(typeof(InventoryNavigationHandler), "KeyCodeInfoToString",
            new[] { typeof(string), typeof(string), typeof(string), typeof(string) });

        // NotesPanelNavigation.keyboardUI (SRC/NotesPanelNavigation.cs:20), private. Optional: without it the Notes page
        // key stays off while that page is open (Game.NotesKeyboardVisible fails closed).
        internal static readonly AccessTools.FieldRef<NotesPanelNavigation, VirtualKeyboardUI> NotesKeyboard =
            Ref<NotesPanelNavigation, VirtualKeyboardUI>("keyboardUI", optional: true);

        // Relay.disableSaving (SRC/Relay.cs:45), protected. Optional: without it the pause clone's child Relays are destroyed.
        internal static readonly AccessTools.FieldRef<Relay, bool> RelayDisableSaving = Ref<Relay, bool>("disableSaving", optional: true);

        // ---- Keys ----

        // KeyBindings.GetKey(string) (SRC/KeyBindings.cs:266), private and side-effect free. Optional.
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
