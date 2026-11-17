using System;
using HarmonyLib;
using UnityEngine.Rendering.PostProcessing;

namespace DisplayOptions
{
    // S-084 per-effect toggles as a post-blend runtime override (spec §6, D8). PostProcessManager.UpdateSettings
    // (PPV2/PostProcessManager.cs:236) first resets the layer's own bundle copies from the base settings (ReplaceData),
    // then blends every volume into them. The renderers read bundle.settings.IsEnabledAndSupported afterwards, and all six
    // effects require enabled.value there (MotionBlur.cs:25, ChromaticAberration.cs:32, Grain.cs:39, Vignette.cs:69,
    // DepthOfField.cs:39, LensDistortion.cs:57). Forcing enabled.value = false on the blended copy turns the effect off for
    // that camera until the next blend, which this postfix follows again.
    //
    // No profile is edited, shared or instanced, no volume weight changes, nothing is stored and nothing needs restoring:
    // GameDefault takes effect on the next blend. Story effects, flashes, QuickVolumes, animated weights and per-frame
    // script writes all go through the same blend, so nothing needs re-applying after scene loads or quality changes.
    internal static class EffectClamp
    {
        internal static bool Broken;

        internal static void Clamp(PostProcessLayer l)
        {
            if (!Cfg.Hot.AnyEffectOff || Broken || l == null) return;
            try
            {
                if (Cfg.Hot.MotionBlurOff) Off<MotionBlur>(l);
                if (Cfg.Hot.ChromaticOff) Off<ChromaticAberration>(l);
                if (Cfg.Hot.GrainOff) Off<Grain>(l);
                if (Cfg.Hot.VignetteOff) Off<Vignette>(l);
                if (Cfg.Hot.DepthOfFieldOff) Off<DepthOfField>(l);
                if (Cfg.Hot.LensOff) Off<LensDistortion>(l);
            }
            catch (Exception e)
            {
                // GetSettings indexes the bundle dictionary directly (PostProcessLayer.cs:553-561) and can throw. Once, never
                // an exception per frame: the effects go back to the game's own state.
                Broken = true;
                Log.Exception("Effects: clamp", e);
            }
        }

        private static void Off<T>(PostProcessLayer l) where T : PostProcessEffectSettings
        {
            var s = l.GetSettings<T>();
            if (s != null) s.enabled.value = false;
        }
    }

    // P5 (spec §3.1): UpdateSettings is internal, so the target comes from Vanilla (AccessTools).
    [HarmonyPatch]
    internal static class UpdateSettingsPatch
    {
        private static System.Reflection.MethodBase TargetMethod() => Vanilla.UpdateSettings;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PostProcessLayer __0) => EffectClamp.Clamp(__0);
    }

    // P5b: the fallback when UpdateSettings can't be found. UpdateVolumeSystem blends at most once per frame and may be
    // called up to three times (PostProcessLayer.cs:673-708); the clamp is idempotent.
    [HarmonyPatch]
    internal static class UpdateVolumeSystemPatch
    {
        private static System.Reflection.MethodBase TargetMethod() => Vanilla.UpdateVolumeSystem;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PostProcessLayer __instance) => EffectClamp.Clamp(__instance);
    }
}
