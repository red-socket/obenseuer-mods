using System;
using System.Reflection;
using HarmonyLib;
using OS.Items;
using UnityEngine;

namespace GreenThumb
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup / FeatureGroup check the members each group needs, so only the feature using it fails (spec §3).
    // Every target was checked against SRC and the shipped 0.5.19 Assembly-CSharp.dll with Mono.Cecil (design.md §16).
    internal static class Vanilla
    {
        // ---- grow (G1, G2, P5; HarvestAll, Ghost, SpaceCheck) ----

        // G1 SRC/grow.cs:595, public, 107 B. G2 :582, public, 87 B. P5 :547, private, 34 B.
        internal static readonly MethodInfo GrowGetCurrentStage = Method(typeof(grow), "GetCurrentStage", Type.EmptyTypes);
        internal static readonly MethodInfo GrowGetCurrentStageProcess = Method(typeof(grow), "GetCurrentStageProcess", Type.EmptyTypes);
        internal static readonly MethodInfo GrowGetTitle = Method(typeof(grow), "GetTitle", new[] { typeof(bool) });
        // :399, private, 28 B. Called through an open delegate (no reflection per call).
        private static readonly MethodInfo GrowIsDeadMethod = Method(typeof(grow), "IsDead", Type.EmptyTypes);
        internal static readonly Func<grow, bool> GrowIsDead = Delegate<Func<grow, bool>>(GrowIsDeadMethod, "grow.IsDead");

        // ---- GrowItem (FruitProgress, F1) ----

        // SRC/GrowItem.cs:23-81, protected or private fields (fsProperty).
        internal static readonly AccessTools.FieldRef<GrowItem, int> FullGrownAgeFinal = Ref<GrowItem, int>("fullGrownAgeFinal");
        internal static readonly AccessTools.FieldRef<GrowItem, int> RandomGrowStartTime = Ref<GrowItem, int>("randomGrowStartTime");
        internal static readonly AccessTools.FieldRef<GrowItem, int> RandomSpoilTime = Ref<GrowItem, int>("randomSpoilTime");
        internal static readonly AccessTools.FieldRef<GrowItem, object[]> GrowItemMeta = Ref<GrowItem, object[]>("meta");
        internal static readonly AccessTools.FieldRef<GrowItem, grow.ItemAmount> GrowItemItem = Ref<GrowItem, grow.ItemAmount>("item");
        // F1 SRC/GrowItem.cs:354, private, 248 B.
        internal static readonly MethodInfo UpdateSpoilTime = Method(typeof(GrowItem), "UpdateSpoilTime", new[] { typeof(int) });

        // ---- Growing (P1-P4, H1, Q1, Q2) ----

        // P1 SRC/Growing.cs:723 (override, 43 B). P2 :737 (override, 71 B). P3 :686, 146 B. P4 :706, 131 B.
        internal static readonly MethodInfo OnPowerOut = Method(typeof(Growing), "OnPowerOut", Type.EmptyTypes);
        internal static readonly MethodInfo OnPowerOn = Method(typeof(Growing), "OnPowerOn", Type.EmptyTypes);
        internal static readonly MethodInfo PauseGrowth = Method(typeof(Growing), "PauseGrowth", Type.EmptyTypes);
        internal static readonly MethodInfo UnpauseGrowth = Method(typeof(Growing), "UnpauseGrowth", Type.EmptyTypes);
        // H1 :782, public, 215 B. Q1 :760, emitted public virtual (IQuickSlotTarget), 166 B. Q2 :970, private, 447 B.
        internal static readonly MethodInfo RepeatHarvest = Method(typeof(Growing), "RepeatHarvest", new[] { typeof(Vector3) });
        internal static readonly MethodInfo UpdateQuickSlot = Method(typeof(Growing), "UpdateQuickSlot", new[] { typeof(RaycastHit) });
        internal static readonly MethodInfo UpdateQuickPlantPreview = Method(typeof(Growing), "UpdateQuickPlantPreview", Type.EmptyTypes);
        // :67, private static. Stamped by Growing.OnManualHarvest on every hand harvest (:755-758).
        internal static readonly AccessTools.FieldRef<float> LastManualHarvestTime = StaticRef<float>(typeof(Growing), "lastManualHarvestTime");

        // CraftingBase.manuActive (SRC/CraftingBase.cs:240), protected: the grower's panel is open.
        internal static readonly AccessTools.FieldRef<CraftingBase, bool> ManuActive = Ref<CraftingBase, bool>("manuActive");

        // ---- GameController (HarvestAll trigger) ----

        // SRC/GameController.cs:17-19, private. Set to unscaledTime + 0.4 on Interact down, +inf on key up or a menu
        // (:252-283).
        internal static readonly AccessTools.FieldRef<GameController, float> RepeatHarvestReadyTime = Ref<GameController, float>("repeatHarvestReadyTime");
        internal static readonly float RepeatHarvestHoldDelay = Const(typeof(GameController), "RepeatHarvestHoldDelay", 0.4f);

        // ---- Interaction and prompts (H2, H3) ----

        // H3 SRC/InteractObjects.cs:205, public, 178 B. H2 SRC/UseText.cs:121, public, 169 B.
        internal static readonly MethodInfo IsWithinUseDistance = Method(typeof(InteractObjects), "IsWithinUseDistance", new[] { typeof(Interactable) });
        internal static readonly MethodInfo UseAndHoldText = Method(typeof(UseText), "UseAndHoldText",
            new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) });

        // ---- Sunlight (no patches) ----

        // SRC/NaturalLightSourceChecker.cs:19, private (serialized; Awake fills it from the parent). :54, public.
        internal static readonly AccessTools.FieldRef<NaturalLightSourceChecker, Growing> NlscGrowing = Ref<NaturalLightSourceChecker, Growing>("growing");
        internal static readonly MethodInfo NlscRefreshModifiers = Method(typeof(NaturalLightSourceChecker), "RefreshModifiers", Type.EmptyTypes);

        // ---- Stacks (B1-B3) ----

        // B1 the static SlotController.SlotHasSpaceForItem (SRC/SlotController.cs:798, 182 B). The instance overload (:793)
        // forwards here.
        internal static readonly MethodInfo SlotHasSpaceForItemStatic = Method(typeof(SlotController), "SlotHasSpaceForItem",
            new[] { typeof(Item), typeof(SlotController), typeof(ItemStack), typeof(int).MakeByRefType(), typeof(int), typeof(int), typeof(int),
                typeof(object[]), typeof(bool), typeof(bool), typeof(ItemOperations.ItemOperationSettings) });
        // B2 SRC/ItemMetaUtilities.cs:174, public static, 2172 B.
        internal static readonly MethodInfo CompareItemMetas = Method(typeof(ItemMetaUtilities), "CompareItemMetas",
            new[] { typeof(object[]), typeof(object[]), typeof(ItemMetaUtilities.MetaAction).MakeByRefType(), typeof(int), typeof(bool), typeof(bool) });
        // B3 SRC/ItemStack.cs:262, private, 63 B (above the inlining limit).
        internal static readonly MethodInfo MixStackQuality = Method(typeof(ItemStack), "MixStackQuality", new[] { typeof(object[]), typeof(int) });
        // Called, never patched: SRC/OS/Items/ItemPerishabledata.cs:98 (238 B; UnofficialPatch prefixes it) and :135.
        internal static readonly MethodInfo RemoveMinutesFromIntegrity = Method(typeof(ItemPerishabledata), "RemoveMinutesFromIntegrity",
            new[] { typeof(float), typeof(float), typeof(float) });

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

        private static AccessTools.FieldRef<F> StaticRef<F>(Type t, string name)
        {
            try
            {
                var f = AccessTools.DeclaredField(t, name);
                if (f == null || !f.IsStatic) { Missing($"{t.Name}.{name}", null, false); return null; }
                return AccessTools.StaticFieldRefAccess<F>(f);
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, false); return null; }
        }

        private static D Delegate<D>(MethodInfo m, string what) where D : Delegate
        {
            if (m == null) return null;
            try { return AccessTools.MethodDelegate<D>(m); }
            catch (Exception e) { Missing(what, e, false); return null; }
        }

        // A compiler-inlined const, read once for the hold timing; the shipped value is used if it is ever renamed.
        private static float Const(Type t, string name, float fallback)
        {
            try
            {
                var f = AccessTools.DeclaredField(t, name);
                if (f != null && f.IsLiteral && f.GetRawConstantValue() is float v) return v;
            }
            catch (Exception) { }
            Log.Warn($"{t.Name}.{name} not found; harvest-all uses {fallback} s.");
            return fallback;
        }

        private static void Missing(string what, Exception e, bool optional)
        {
            string why = e != null ? $" ({e.Message})" : "";
            if (optional) Log.Warn($"{what} not found{why}; the part of the feature using it is off.");
            else Log.Error($"{what} not found{why}; the feature using it is off.");
        }
    }
}
