using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;

namespace DisplayOptions
{
    // Game, PPv2 and UGUI members, read through Harmony's AccessTools rather than the publicized reference
    // (docs/modding-guide.md §4). A required member that is missing logs one error and stays null; Plugin.PatchGroup /
    // FeatureGroup check the members each group needs, so only the feature using it fails (spec §3.4). Every target was
    // checked against SRC, PPV2 and UGUI for 0.5.19 (design.md §15).
    internal static class Vanilla
    {
        // ---- Frame limiter (P1, P2) ----

        // P1 SRC/DisplaySave.cs:90, public static, 15 B. P2 SRC/OptionsMenu.cs:715, public.
        internal static readonly MethodInfo ApplyFrameRateLimit = Method(typeof(DisplaySave), "ApplyFrameRateLimit", new[] { typeof(int) });
        internal static readonly MethodInfo ApplyVerticalSync = Method(typeof(OptionsMenu), "ApplyVerticalSync", Type.EmptyTypes);

        // ---- Shadow budget (P3, P4) ----

        // P3 SRC/OptionsMenu.cs:1260, public. P4 SRC/LightController.cs:260, private (a Unity message).
        internal static readonly MethodInfo ApplyQualitySettings = Method(typeof(OptionsMenu), "applyQualitySettings", Type.EmptyTypes);
        internal static readonly MethodInfo LightUpdate = Method(typeof(LightController), "Update", Type.EmptyTypes);

        // LightController (SRC/LightController.cs:10-14), private.
        internal static readonly AccessTools.FieldRef<LightController, float> LightLastDist = Ref<LightController, float>("lastdist");
        internal static readonly AccessTools.FieldRef<LightController, bool> LightForceUpdate = Ref<LightController, bool>("forceUpdate");
        internal static readonly AccessTools.FieldRef<LightController, Light> LightCull = Ref<LightController, Light>("cullLight");

        // ---- Effects (P5, P5b) ----

        // P5 PPV2/PostProcessManager.cs:236, internal. Optional: P5b (PPV2/PostProcessLayer.cs:673, public) is the fallback.
        internal static readonly MethodInfo UpdateSettings = Method(typeof(PostProcessManager), "UpdateSettings",
            new[] { typeof(PostProcessLayer), typeof(Camera) }, optional: true);
        internal static readonly MethodInfo UpdateVolumeSystem = Method(typeof(PostProcessLayer), "UpdateVolumeSystem",
            new[] { typeof(Camera), typeof(CommandBuffer) }, optional: true);

        // ---- UI scale (P6, P7) ----

        // P6 UGUI/CanvasScaler.cs:221, protected override. Handle (:240) and HandleScaleWithScreenSize (:278) are protected
        // virtual and only scanned by Compat.
        internal static readonly MethodInfo ScalerOnEnable = Method(typeof(CanvasScaler), "OnEnable", Type.EmptyTypes);
        internal static readonly MethodInfo ScalerHandle = Method(typeof(CanvasScaler), "Handle", Type.EmptyTypes, optional: true);
        internal static readonly MethodInfo ScalerHandleScreenSize = Method(typeof(CanvasScaler), "HandleScaleWithScreenSize", Type.EmptyTypes, optional: true);

        // P7 SRC/OptionsMenu.cs:195, public.
        internal static readonly MethodInfo StartInit = Method(typeof(OptionsMenu), "StartInit", new[] { typeof(MonoBehaviour) });

        // OptionsMenu (SRC/OptionsMenu.cs:65, :68, :118), private serialized.
        internal static readonly AccessTools.FieldRef<OptionsMenu, TextMeshProUGUI> FovNumbers = Ref<OptionsMenu, TextMeshProUGUI>("fovNumbers");
        internal static readonly AccessTools.FieldRef<OptionsMenu, Slider> FovSlider = Ref<OptionsMenu, Slider>("fovSlider");
        internal static readonly AccessTools.FieldRef<OptionsMenu, TextMeshProUGUI> PostFX = Ref<OptionsMenu, TextMeshProUGUI>("PostFX");

        // OptionsPageNavigationHelper.optionsButtons (SRC/OptionsPageNavigationHelper.cs:9), private; refilled in OnEnable only
        // when empty (:34-37).
        internal static readonly AccessTools.FieldRef<OptionsPageNavigationHelper, List<OptionsButton>> NavButtons =
            Ref<OptionsPageNavigationHelper, List<OptionsButton>>("optionsButtons");

        // Relay.disableSaving (SRC/Relay.cs:45), protected. Optional: without it the clone's Relays are destroyed.
        internal static readonly AccessTools.FieldRef<Relay, bool> RelayDisableSaving = Ref<Relay, bool>("disableSaving", optional: true);

        // PauseMenu.hidePause (SRC/PauseMenu.cs:10), private: active while the pause menu is open (:236, :268). Optional:
        // without it the pause menu counts as open, so the Pause kind is applied only on scene load and when the row closes.
        internal static readonly AccessTools.FieldRef<PauseMenu, GameObject> PauseHide = Ref<PauseMenu, GameObject>("hidePause", optional: true);

        // ---- Types (spec §3.4) ----

        // Micro-prop exclusions. All required: the pass fails rather than run with weaker exclusions. "Furniture" in the spec
        // is a ScriptableObject (SRC/Furniture.cs:7), so FurnitureCollectable (the furniture pickup) stands in (design §16).
        internal static readonly Type Door = TypeOf("Door");
        internal static readonly Type DoorChangelevel = TypeOf("DoorChangelevel");
        internal static readonly Type CharacterModel = TypeOf("CharacterModel");
        internal static readonly Type NPCInfo = TypeOf("NPCInfo");
        internal static readonly Type FurniturePlaceable = TypeOf("FurniturePlaceable");
        internal static readonly Type FurnitureCollectable = TypeOf("FurnitureCollectable");
        internal static readonly Type CollectibleItem = TypeOf("CollectibleItem");

        // Canvas classification (spec §7.2). The Start Selection canvas's script is StartSelectionsUI, on the canvas's
        // parent (Interior Start.unity: Start Selections/Start Selection UI).
        internal static readonly Type GameUIInstanceT = TypeOf("GameUIInstance");
        internal static readonly Type PauseMenuT = TypeOf("PauseMenu");
        internal static readonly Type LoadingScreenT = TypeOf("LoadingScreen");
        internal static readonly Type CardGameUIT = TypeOf("CardGameUI");
        internal static readonly Type ShopBaseUIT = TypeOf("ShopBaseUI");
        internal static readonly Type StartSelectionsUIT = TypeOf("StartSelectionsUI");
        internal static readonly Type DialogueSystemControllerT = TypeOf("PixelCrushers.DialogueSystem.DialogueSystemController");
        internal static readonly Type CreditsT = TypeOf("Credits");

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

        private static Type TypeOf(string name)
        {
            try
            {
                var t = AccessTools.TypeByName(name);
                if (t == null) Missing("type " + name, null, false);
                return t;
            }
            catch (Exception e) { Missing("type " + name, e, false); return null; }
        }

        private static void Missing(string what, Exception e, bool optional)
        {
            string why = e != null ? $" ({e.Message})" : "";
            if (optional) Log.Warn($"{what} not found{why}; the part of the feature using it is off.");
            else Log.Error($"{what} not found{why}; the feature using it is off.");
        }

        // ---- Typed wrappers ----

        // True unless the pause menu is known to be closed.
        internal static bool PauseOpen(PauseMenu p)
        {
            if (p == null) return false;
            if (PauseHide == null) return true;
            var h = PauseHide(p);
            return h == null || h.activeSelf;
        }
    }
}
