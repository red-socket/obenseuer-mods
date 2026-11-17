using System;
using System.Reflection;
using HarmonyLib;
using OS.Items;
using UnityEngine;

namespace VitalSigns
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup / FeatureGroup check the members each group needs, so only the feature using it fails (spec §3).
    // Every target was checked against SRC and the shipped Assembly-CSharp.dll with Mono.Cecil (design.md §4).
    internal static class Vanilla
    {
        // ---- Damage log (H1-H8, L1-L4, spec §4) ----

        // H1 SRC/PlayerStatsHealth.cs:91, 425 B. H2 :41, 374 B. H3 :159, 39 B.
        internal static readonly MethodInfo Damage = Method(typeof(PlayerStatsHealth), "Damage", new[] { typeof(float), typeof(DamageTypeNames), typeof(bool) });
        internal static readonly MethodInfo HealthUpdateStats = Method(typeof(PlayerStatsHealth), "UpdateStats", Type.EmptyTypes);
        internal static readonly MethodInfo HealthUpdateValues = Method(typeof(PlayerStatsHealth), "UpdateValues", new[] { typeof(ItemConsumable) });
        // H4 SRC/PlayerStats.cs:261, 123 B.
        internal static readonly MethodInfo StatsUpdateValues = Method(typeof(PlayerStats), "UpdateValues", new[] { typeof(ItemData), typeof(ItemConsumable) });
        // H5 SRC/PlayerStatsTiredness.cs:45, 678 B. H6 SRC/InteractableWait.cs:290, virtual, 878 B (the declared base).
        // H7 SRC/PlayerStatsMushrooms.cs:50, 477 B. H8 SRC/Death.cs:19, 175 B.
        internal static readonly MethodInfo TirednessUpdateStats = Method(typeof(PlayerStatsTiredness), "UpdateStats", new[] { typeof(int) });
        internal static readonly MethodInfo WaitOnWaiting = Method(typeof(InteractableWait), "OnWaiting", new[] { typeof(int) });
        internal static readonly MethodInfo MushroomsUpdateStats = Method(typeof(PlayerStatsMushrooms), "UpdateStats", new[] { typeof(int) });
        internal static readonly MethodInfo PlayerDeath = Method(typeof(Death), "PlayerDeath", new[] { typeof(DamageTypeNames) });
        // L1 SRC/PlayerStatsHungerAndThirst.cs:163, 479 B. L2 SRC/Depression.cs:52, static, 30 B.
        // L3 SRC/TreatmentFungalDisease.cs:141, the override, 358 B. L4 SRC/Recipe.cs:143 (nested RecipeStatsEditData), 208 B.
        internal static readonly MethodInfo EmptyToToilet = Method(typeof(PlayerStatsHungerAndThirst), "EmptyToToilet", new[] { typeof(int) });
        internal static readonly MethodInfo HurtYourself = Method(typeof(Depression), "HurtYourself", new[] { typeof(DamageTypeNames) });
        internal static readonly MethodInfo FungalBuyTreatment = Method(typeof(TreatmentFungalDisease), "BuyTreatment", new[] { typeof(DoctorPanelSelection.Treatment) });
        internal static readonly MethodInfo CraftTestAndApply = Method(typeof(Recipe.RecipeStatsEditData), "TestAndApply", new[] { typeof(bool) });

        // ---- Mood (M1, I1, G1) ----

        // M1 SRC/PlayerStatsMentalHealth.cs:46, 737 B; SRC/PlayerStatsHygiene.cs:77, 33 B.
        internal static readonly MethodInfo MentalUpdateStats = Method(typeof(PlayerStatsMentalHealth), "UpdateStats", new[] { typeof(int) });
        internal static readonly MethodInfo HygieneMentalEffect = Method(typeof(PlayerStatsHygiene), "GetMentalHealthEffect", Type.EmptyTypes);
        // G1 SRC/PlayerStatsMentalHealth.cs:111, private, 936 B.
        internal static readonly MethodInfo MentalHealthStatsUpdate = Method(typeof(PlayerStatsMentalHealth), "MentalHealthStatsUpdate", Type.EmptyTypes);
        // I1 SRC/PlayerStatsUI.cs:235, private, 473 B.
        internal static readonly MethodInfo UpdateColor = Method(typeof(PlayerStatsUI), "UpdateColor",
            new[] { typeof(PlayerStatUI), typeof(float), typeof(bool), typeof(bool), typeof(int), typeof(int) });

        // ---- Addictions (C1-C3, D1, D2) ----

        // C1 SRC/PlayerStatsAddictions.cs:299, 219 B. C2 SRC/PlayerStatsEdit.cs:29/:34, 24 B each (UnityEvent targets).
        internal static readonly MethodInfo AddictionsUpdateValues = Method(typeof(PlayerStatsAddictions), "UpdateValues", new[] { typeof(ItemConsumable) });
        internal static readonly MethodInfo EditAlcoholAddiction = Method(typeof(PlayerStatsEdit), "ChangeAlcoholAddiction", new[] { typeof(float) });
        internal static readonly MethodInfo EditMushroomAddiction = Method(typeof(PlayerStatsEdit), "ChangeMushroomAddiction", new[] { typeof(float) });
        // C3 SRC/PlayerStats.cs:136, the override, 373 B.
        internal static readonly MethodInfo StatsOnLoadingGame = Method(typeof(PlayerStats), "OnLoadingGame", Type.EmptyTypes);
        // D1 SRC/PlayerStats.cs:306, private, 266 B with its own try/catch. D2 SRC/PlayerStatsDuck.cs:27, 439 B.
        internal static readonly MethodInfo DeltaSeconds = Method(typeof(PlayerStats), "DeltaSeconds", new[] { typeof(int) });
        internal static readonly MethodInfo DuckUpdateStats = Method(typeof(PlayerStatsDuck), "UpdateStats", Type.EmptyTypes);

        // ---- Window and access (W1, W2, A1, A2, T1) ----

        // W1 SRC/PauseNavigationHandler.cs:89, private (a Unity message, called directly). W2 :133, public.
        internal static readonly MethodInfo PauseNavUpdate = Method(typeof(PauseNavigationHandler), "Update", Type.EmptyTypes);
        internal static readonly MethodInfo HandleBackButtonClick = Method(typeof(PauseNavigationHandler), "HandleBackButtonClick", Type.EmptyTypes);
        // A1 SRC/InventoryNavigationHandler.cs:400, 77 B. A2 SRC/UIPageButtons.cs:86, 285 B.
        internal static readonly MethodInfo GetButtonInfo = Method(typeof(InventoryNavigationHandler), "GetButtonInfo",
            new[] { typeof(int), typeof(InventoryNavigationHandler.CurrentInventoryWindow), typeof(InputManager.InputType) });
        internal static readonly MethodInfo PageSelect = Method(typeof(UIPageButtons), "Select", new[] { typeof(MenuPageNames) });
        // T1 SRC/PlayerStatsToolTipInfo.cs:35, 959 B.
        internal static readonly MethodInfo GetStatToolTipInfo = Method(typeof(PlayerStatsToolTipInfo), "GetStatToolTipInfo", Type.EmptyTypes);

        // ---- Fields ----

        // PlayerStatsDuck (SRC/PlayerStatsDuck.cs:10-25), private. currentAmount is the count vanilla just took at :56.
        internal static readonly AccessTools.FieldRef<PlayerStatsDuck, int> DuckCount = Ref<PlayerStatsDuck, int>("currentAmount");
        internal static readonly AccessTools.FieldRef<PlayerStatsDuck, ItemReference> DuckItem = Ref<PlayerStatsDuck, ItemReference>("requiredInventoryItem");

        // PlayerStatsMentalHealth.generalStatusEffectCurve (SRC/PlayerStatsMentalHealth.cs:31), private. Optional: without
        // it the explainer says it can't read the needs limit.
        internal static readonly AccessTools.FieldRef<PlayerStatsMentalHealth, AnimationCurve> MoodCurveRef =
            Ref<PlayerStatsMentalHealth, AnimationCurve>("generalStatusEffectCurve", optional: true);

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

        // ---- Typed wrappers ----

        internal static AnimationCurve MoodCurve(PlayerStatsMentalHealth m) => m != null && MoodCurveRef != null ? MoodCurveRef(m) : null;

        internal static Item DuckRequiredItem(PlayerStatsDuck d)
        {
            if (d == null || DuckItem == null) return null;
            var r = DuckItem(d);
            return r != null ? r.Item : null;
        }
    }
}
