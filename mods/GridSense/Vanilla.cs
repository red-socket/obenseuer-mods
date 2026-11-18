using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;

namespace GridSense
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4, spec §3). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup checks the members each group needs, so only the feature using it fails. Every target was checked
    // against SRC and the shipped Assembly-CSharp.dll with Mono.Cecil (design.md §12).
    internal static class Vanilla
    {
        // ---- Shop and workbench line (G1, P1) ----

        // P1 SRC/FurnitureInfoUI.cs:43, public. currentDescription :13, UpdateContentVisibility :160 and RebuildLayout
        // :147 (all private).
        internal static readonly MethodInfo UpdateFurnitureInfo = Method(typeof(FurnitureInfoUI), "UpdateFurnitureInfo",
            new[] { typeof(BuildingSystem.FurnitureInfo), typeof(BaseShop), typeof(BuildingSystemShopSlot.ShopSlotType) });
        internal static readonly AccessTools.FieldRef<FurnitureInfoUI, TextMeshProUGUI> CurrentDescription =
            Ref<FurnitureInfoUI, TextMeshProUGUI>("currentDescription");
        internal static readonly Action<FurnitureInfoUI> UpdateContentVisibility =
            Delegate<Action<FurnitureInfoUI>>(typeof(FurnitureInfoUI), "UpdateContentVisibility", Type.EmptyTypes);
        internal static readonly Action<FurnitureInfoUI> RebuildLayout =
            Delegate<Action<FurnitureInfoUI>>(typeof(FurnitureInfoUI), "RebuildLayout", Type.EmptyTypes);

        // ---- Build bar (G2, P2) ----

        // P2 SRC/BuildingSystemMenu.cs:472, private. slots :104, currentSelectionName :41 and currentSelectionText :39
        // (private); currentVisibleSlot :121 (public, read through a ref for the same missing-member check).
        internal static readonly MethodInfo ChangeSelection = Method(typeof(BuildingSystemMenu), "ChangeSelection", new[] { typeof(int), typeof(int) });
        internal static readonly AccessTools.FieldRef<BuildingSystemMenu, List<BuildingSystemSlot>> Slots =
            Ref<BuildingSystemMenu, List<BuildingSystemSlot>>("slots");
        internal static readonly AccessTools.FieldRef<BuildingSystemMenu, int> CurrentVisibleSlot = Ref<BuildingSystemMenu, int>("currentVisibleSlot");
        internal static readonly AccessTools.FieldRef<BuildingSystemMenu, string> CurrentSelectionName = Ref<BuildingSystemMenu, string>("currentSelectionName");
        internal static readonly AccessTools.FieldRef<BuildingSystemMenu, TextMeshProUGUI> CurrentSelectionText =
            Ref<BuildingSystemMenu, TextMeshProUGUI>("currentSelectionText");

        // ---- Supply flicker (G3, P3) ----

        // P3 SRC/PowerGrid.cs:200-221, the Flickering getter (152 B, one ldfld of unstableSupply at IL_0084). unstableSupply
        // :43 (public, only read). PowerGridListener.UpdateFlicker :98 (private).
        internal static readonly MethodInfo Flickering = Getter(typeof(PowerGrid), "Flickering");
        internal static readonly FieldInfo UnstableSupplyField = Field(typeof(PowerGrid), "unstableSupply");
        internal static readonly Action<PowerGridListener> UpdateFlicker =
            Delegate<Action<PowerGridListener>>(typeof(PowerGridListener), "UpdateFlicker", Type.EmptyTypes);

        // ---- Power members (G1 and G2, spec §3) ----

        // Public fields PowerInfo reads directly: checked here so a renamed one fails both wattage groups up front instead
        // of throwing on every selection. CraftingBase :94, :123, :132, :134; PowerSource :15, :18, :27; Generator :8, :11;
        // PowerGridListener :15; BatteryPowerSource.bidirectional :48.
        private static readonly object[] PublicPowerFields =
        {
            Field(typeof(CraftingBase), "AlwaysOn"), Field(typeof(CraftingBase), "_Power"), Field(typeof(CraftingBase), "PowerConsumptionIdle"),
            Field(typeof(CraftingBase), "ManuName"),
            Field(typeof(PowerSource), "Type"), Field(typeof(PowerSource), "RequiresFuel"), Field(typeof(PowerSource), "powerConsumption"),
            Field(typeof(Generator), "powergeneration"), Field(typeof(Generator), "linkToGrid"),
            Field(typeof(PowerGridListener), "PowerConsumption"),
            Field(typeof(BatteryPowerSource), "bidirectional"),
            Method(typeof(Furniture), "GetPrefab", new[] { typeof(int) }),
        };
        // BatteryPowerSource.powerListener :52 and powerbalanceperstack :56, Storage.customStorageSlotAmount :89 (private).
        internal static readonly AccessTools.FieldRef<BatteryPowerSource, PowerGridListener> BatteryListener =
            Ref<BatteryPowerSource, PowerGridListener>("powerListener");
        internal static readonly AccessTools.FieldRef<BatteryPowerSource, int> BatteryPerStack = Ref<BatteryPowerSource, int>("powerbalanceperstack");
        internal static readonly AccessTools.FieldRef<Storage, int> StorageSlots = Ref<Storage, int>("customStorageSlotAmount");

        // Non-null when every power member resolved: one shared entry in the Needs of G1 and G2.
        internal static readonly object Power = AllResolved(PublicPowerFields) && BatteryListener != null && BatteryPerStack != null &&
                                                StorageSlots != null ? new object() : null;

        // ManufacturingPowerUI.GetUnitString(int) (SRC/ManufacturingPowerUI.cs:150), public static. Optional: PowerText
        // falls back to a copy of its body.
        internal static readonly Func<int, string> GetUnitString = Static<Func<int, string>>(typeof(ManufacturingPowerUI), "GetUnitString",
            new[] { typeof(int) }, optional: true);

        // ---- Helpers ----

        private static bool AllResolved(object[] members)
        {
            foreach (var m in members)
                if (m == null) return false;
            return true;
        }

        private static MethodInfo Method(Type t, string name, Type[] args, bool optional = false)
        {
            try
            {
                var m = AccessTools.DeclaredMethod(t, name, args);
                if (m == null) Missing($"{t.Name}.{name}", null, optional);
                return m;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, optional); return null; }
        }

        private static MethodInfo Getter(Type t, string name)
        {
            try
            {
                var m = AccessTools.DeclaredPropertyGetter(t, name);
                if (m == null) Missing($"{t.Name}.{name}", null, false);
                return m;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, false); return null; }
        }

        private static FieldInfo Field(Type t, string name)
        {
            try
            {
                var f = AccessTools.DeclaredField(t, name);
                if (f == null) Missing($"{t.Name}.{name}", null, false);
                return f;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, false); return null; }
        }

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Missing($"{typeof(T).Name}.{name}", e, false); return null; }
        }

        private static D Delegate<D>(Type t, string name, Type[] args) where D : Delegate
        {
            var m = Method(t, name, args);
            if (m == null) return null;
            try { return AccessTools.MethodDelegate<D>(m); }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, false); return null; }
        }

        private static D Static<D>(Type t, string name, Type[] args, bool optional) where D : Delegate
        {
            var m = Method(t, name, args, optional);
            if (m == null || !m.IsStatic) return null;
            try { return (D)System.Delegate.CreateDelegate(typeof(D), m); }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, optional); return null; }
        }

        private static void Missing(string what, Exception e, bool optional)
        {
            string why = e != null ? $" ({e.Message})" : "";
            if (optional) Log.Warn($"{what} not found{why}; Grid Sense uses its own copy instead.");
            else Log.Error($"{what} not found{why}; the feature using it is off.");
        }
    }
}
