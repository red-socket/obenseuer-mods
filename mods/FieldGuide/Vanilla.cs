using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OS.Items;
using TMPro;
using UnityEngine;

namespace FieldGuide
{
    // Every game member Field Guide patches, reads or calls, resolved once through Harmony's AccessTools (spec §4,
    // docs/modding-guide.md §4). A required member that is missing logs one error and stays null; Plugin.PatchGroup checks
    // the members each group needs, so only the feature using it fails. Members the code calls directly through the
    // publicized reference are resolved here too, so a game update that removes one turns its group off at launch
    // instead of throwing at the first call. Checked against SRC and the shipped Assembly-CSharp.dll with Mono.Cecil
    // (design.md §15).
    internal static class Vanilla
    {
        // ---- Station help (H1, H2) ----

        // H1 SRC/CraftingBase.cs:1769, H2 :1816, both public virtual. currentCraftingBase :260 (public static).
        internal static readonly MethodInfo CraftingEnter = Method(typeof(CraftingBase), "Enter", Type.EmptyTypes);
        internal static readonly MethodInfo CraftingExit = Method(typeof(CraftingBase), "Exit", Type.EmptyTypes);
        internal static readonly FieldInfo CurrentCraftingBase = Field(typeof(CraftingBase), "currentCraftingBase");
        // Read for classification and the per-instance bullets: Recipes :112, AlwaysOn :123, _Power :132,
        // ModifiersWithUpgrades :319; PowerSource.Type / RequiresFuel (SRC/PowerSource.cs:15, :18);
        // manufacturing.AllowSimultaneous / RequirePlayer (SRC/manufacturing.cs:60, :62); Sauna.passive (SRC/Sauna.cs:72).
        internal static readonly FieldInfo CbRecipes = Field(typeof(CraftingBase), "Recipes");
        internal static readonly FieldInfo CbAlwaysOn = Field(typeof(CraftingBase), "AlwaysOn");
        internal static readonly FieldInfo CbPower = Field(typeof(CraftingBase), "_Power");
        internal static readonly PropertyInfo CbModifiers = Prop(typeof(CraftingBase), "ModifiersWithUpgrades");
        internal static readonly FieldInfo PowerType = Field(typeof(PowerSource), "Type");
        internal static readonly FieldInfo PowerRequiresFuel = Field(typeof(PowerSource), "RequiresFuel");
        internal static readonly FieldInfo ManuSimultaneous = Field(typeof(manufacturing), "AllowSimultaneous");
        internal static readonly FieldInfo ManuRequirePlayer = Field(typeof(manufacturing), "RequirePlayer");
        internal static readonly FieldInfo SaunaPassive = Field(typeof(Sauna), "passive");
        // The pad's View button is also the backpack button in the inventory tab (SRC/InventoryNavigationHandler.cs:1247),
        // and opening the backpack exits the station (SRC/BackpackStorage.cs:100-104) and re-enters it on close (:164):
        // BackpackStorage.instance :58, Equipped :62, BackpackIsOpen() :202; InputManager.instance :14, inputType :20.
        internal static readonly FieldInfo BackpackInstance = Field(typeof(BackpackStorage), "instance");
        internal static readonly PropertyInfo BackpackEquipped = Prop(typeof(BackpackStorage), "Equipped");
        internal static readonly MethodInfo BackpackIsOpen = Method(typeof(BackpackStorage), "BackpackIsOpen", Type.EmptyTypes);
        internal static readonly FieldInfo InputInstance = Field(typeof(InputManager), "instance");
        internal static readonly FieldInfo InputType = Field(typeof(InputManager), "inputType");

        // HintOverlay API (SRC/HintOverlay.cs): Show :166, SetHiddenOverride :183, IsShowing :204, Hide :246,
        // HintsEnabled :88 (PlayerPrefs, read only), HintsToggled :111. Nothing in HintOverlay is patched.
        internal static readonly MethodInfo HintShow = Method(typeof(HintOverlay), "Show",
            new[] { typeof(UnityEngine.Object), typeof(HintContent), typeof(int), typeof(bool) });
        internal static readonly MethodInfo HintHide = Method(typeof(HintOverlay), "Hide", new[] { typeof(UnityEngine.Object) });
        internal static readonly MethodInfo HintSetHiddenOverride = Method(typeof(HintOverlay), "SetHiddenOverride",
            new[] { typeof(UnityEngine.Object), typeof(bool?) });
        internal static readonly MethodInfo HintIsShowing = Method(typeof(HintOverlay), "IsShowing", new[] { typeof(UnityEngine.Object) });
        internal static readonly PropertyInfo HintsEnabled = Prop(typeof(HintOverlay), "HintsEnabled");
        internal static readonly EventInfo HintsToggled = Event(typeof(HintOverlay), "HintsToggled");
        internal static readonly MethodInfo HintAddList = Method(typeof(HintContent), "AddList", new[] { typeof(string), typeof(string[]) });
        internal static readonly MethodInfo HintAddText = Method(typeof(HintContent), "AddText", new[] { typeof(string), typeof(string[]) });

        // ---- Sauna zone (H3, H4) ----

        // SRC/Sauna.cs:282 and :290, private Unity messages. ContestController.ContestActive :53 (public static
        // property); BuildingSystem.inBuildingArea :453 (public), instance :477.
        internal static readonly MethodInfo SaunaTriggerEnter = Method(typeof(Sauna), "OnTriggerEnter", new[] { typeof(Collider) });
        internal static readonly MethodInfo SaunaTriggerExit = Method(typeof(Sauna), "OnTriggerExit", new[] { typeof(Collider) });
        internal static readonly PropertyInfo ContestActive = Prop(typeof(ContestController), "ContestActive");
        internal static readonly FieldInfo InBuildingArea = Field(typeof(BuildingSystem), "inBuildingArea");

        // ---- Tooltip and item panel (T1, T2, P1) ----

        // T1 SRC/ToolTip.cs:163. T2 SRC/TooltipUI.cs:136. P1 SRC/ItemInfoPanel.cs:182; itemDescripion :36 (private).
        internal static readonly MethodInfo ConstructDataString = Method(typeof(ToolTip), "ConstructDataString",
            new[] { typeof(ItemData), typeof(int), typeof(int), typeof(object[]) });
        internal static readonly MethodInfo UpdateTooltip = Method(typeof(TooltipUI), "UpdateTooltip",
            new[] { typeof(string), typeof(string), typeof(Dictionary<ItemReference, float>), typeof(List<Furniture.ReseourceItem>) });
        internal static readonly MethodInfo UpdateInfo = Method(typeof(ItemInfoPanel), "UpdateInfo", new[] { typeof(ItemData), typeof(ChildItem) });
        internal static readonly AccessTools.FieldRef<ItemInfoPanel, TextMeshProUGUI> ItemDescription = Ref<ItemInfoPanel, TextMeshProUGUI>("itemDescripion");

        // ---- Calls used by Bought by (spec §5) ----

        // SlotController.CheckIfInAllowedCategoy :726 (public static); Trade.GetItemPrice :612 (public static),
        // CheckIfAllowed :557, IsStolen :544; Money.DisplayRm :263 (public static); TradePanel.activeTrade :10.
        internal static readonly MethodInfo CheckIfInAllowedCategoy = Method(typeof(SlotController), "CheckIfInAllowedCategoy",
            new[] { typeof(Item), typeof(string[]), typeof(string[]), typeof(object[]) });
        internal static readonly MethodInfo GetItemPrice = Method(typeof(Trade), "GetItemPrice",
            new[] { typeof(Item), typeof(int), typeof(object[]), typeof(Money.Currency), typeof(float), typeof(float), typeof(bool) });
        internal static readonly MethodInfo TradeCheckIfAllowed = Method(typeof(Trade), "CheckIfAllowed", new[] { typeof(ItemData) });
        internal static readonly MethodInfo TradeIsStolen = Method(typeof(Trade), "IsStolen", new[] { typeof(ItemData) });
        internal static readonly MethodInfo DisplayRm = Method(typeof(Money), "DisplayRm", new[] { typeof(float) });
        internal static readonly FieldInfo ActiveTrade = Field(typeof(TradePanel), "activeTrade");
        // Live trader fields read at hover time (D24): Trade.cs:20, :36, :80, :87, :105, :108.
        internal static readonly FieldInfo TradeGuid = Field(typeof(Trade), "GUID");
        internal static readonly FieldInfo TradeSharedId = Field(typeof(Trade), "sharedId");
        internal static readonly FieldInfo TradeSell = Field(typeof(Trade), "baseSellPrice");
        internal static readonly FieldInfo TradeSellMode = Field(typeof(Trade), "sellPriceMode");
        internal static readonly FieldInfo TradeBuysStolen = Field(typeof(Trade), "buysStolen");
        internal static readonly FieldInfo TradeStolenFactor = Field(typeof(Trade), "stolenPriceFactor");
        // The storage-slot part of Trade.CheckIfFull (:539-541, design.md §9.3): Trade.destroyPurchasedItems :102;
        // Storage.AllowedCategories / ForbiddenCategories (SRC/Storage.cs:80, :83); SlotController.DefaultForbiddenCategories
        // (SRC/SlotController.cs:29).
        internal static readonly FieldInfo TradeDestroysPurchases = Field(typeof(Trade), "destroyPurchasedItems");
        internal static readonly FieldInfo StorageAllowed = Field(typeof(Storage), "AllowedCategories");
        internal static readonly FieldInfo StorageForbidden = Field(typeof(Storage), "ForbiddenCategories");
        internal static readonly FieldInfo SlotDefaultForbidden = Field(typeof(SlotController), "DefaultForbiddenCategories");

        // ---- Visited places (spec §9.2) ----

        // SaveController.LastSaveFolderPath :233 (public static), GetCurrentSavePath() :1302, instance :225.
        internal static readonly FieldInfo LastSaveFolderPath = Field(typeof(SaveController), "LastSaveFolderPath");
        internal static readonly MethodInfo GetCurrentSavePath = Method(typeof(SaveController), "GetCurrentSavePath", Type.EmptyTypes);

        // ---- Helpers ----

        internal static MethodInfo Method(Type t, string name, Type[] args)
        {
            try
            {
                var m = AccessTools.DeclaredMethod(t, name, args);
                if (m == null) Missing($"{t.Name}.{name}", null);
                return m;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e); return null; }
        }

        private static FieldInfo Field(Type t, string name)
        {
            try
            {
                var f = AccessTools.DeclaredField(t, name);
                if (f == null) Missing($"{t.Name}.{name}", null);
                return f;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e); return null; }
        }

        private static PropertyInfo Prop(Type t, string name)
        {
            try
            {
                var p = AccessTools.DeclaredProperty(t, name);
                if (p == null) Missing($"{t.Name}.{name}", null);
                return p;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e); return null; }
        }

        private static EventInfo Event(Type t, string name)
        {
            try
            {
                var ev = t.GetEvent(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (ev == null) Missing($"{t.Name}.{name}", null);
                return ev;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e); return null; }
        }

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Missing($"{typeof(T).Name}.{name}", e); return null; }
        }

        private static void Missing(string what, Exception e)
        {
            string why = e != null ? $" ({e.Message})" : "";
            Log.Error($"{what} not found{why}; the feature using it is off.");
        }
    }
}
