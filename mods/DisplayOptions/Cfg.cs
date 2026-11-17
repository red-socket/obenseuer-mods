using BepInEx.Configuration;
using UnityEngine;

namespace DisplayOptions
{
    internal enum CapMode { GameDefault, MatchMonitor, Custom, Uncapped }

    internal enum ShadowPreset { Off, Light, Balanced, Strong, Custom }

    internal enum EffectMode { GameDefault, Off }

    // Every tunable value (spec §8). Everything works live; InMenuSlider takes effect at the next scene (the row is built
    // when the pause menu starts). Hot caches what patch bodies read on hot paths and is refreshed on SettingChanged, before
    // Plugin's own handler runs. Description strings are public text: humanized 2026-11-17; run them through the humanizer
    // again after any change.
    internal static class Cfg
    {
        internal static ConfigEntry<bool> VerboseLog;

        internal static class FrameRate
        {
            internal static ConfigEntry<bool> FixVSyncLimiter;
            internal static ConfigEntry<CapMode> CapWhenVSyncOff;
            internal static ConfigEntry<int> CustomCap;
        }

        internal static class Shadows
        {
            internal static ConfigEntry<ShadowPreset> Preset;
            internal static ConfigEntry<float> CustomMicroPropMaxSize, CustomLightShadowDistance, CustomMaxShadowDistance;
            internal static ConfigEntry<bool> CustomIncludeLooseItems;
            internal static ConfigEntry<int> CustomMaxShadowCascades;
        }

        internal static class PostProcessing
        {
            internal static ConfigEntry<EffectMode> MotionBlur, ChromaticAberration, Grain, Vignette, DepthOfField, LensDistortion;
        }

        internal static class UiScale
        {
            internal static ConfigEntry<float> Scale;
            internal static ConfigEntry<bool> ScaleDialogue, UltrawideDialogue, ScalePauseMenu, InMenuSlider;
        }

        // Cached values read by patch bodies and per-frame code (spec §8). One field read per call instead of a
        // ConfigEntry lookup. The shadow values are the preset's effective ones (Budget.Effective).
        internal static class Hot
        {
            internal static bool FixVSync;
            internal static CapMode CapMode;
            internal static int CustomCap;

            internal static float MicroMax, LightCap, MaxDist;
            internal static bool IncludeLoose;
            internal static int MaxCasc;

            internal static bool AnyEffectOff, MotionBlurOff, ChromaticOff, GrainOff, VignetteOff, DepthOfFieldOff, LensOff;

            internal static float Scale;
            internal static bool ScaleDialogue, Ultrawide, ScalePause, Row;

            // UI scale wants to change something (spec §7.1). Scaling.Active adds "or something is still scaled".
            internal static bool UiWanted;

            internal static void Refresh()
            {
                FixVSync = FrameRate.FixVSyncLimiter.Value;
                CapMode = FrameRate.CapWhenVSyncOff.Value;
                CustomCap = Mathf.Clamp(FrameRate.CustomCap.Value, 20, 500);

                var b = Budget.Effective();
                MicroMax = b.MicroMax;
                IncludeLoose = b.IncludeLoose;
                LightCap = b.LightCap;
                MaxDist = b.MaxDist;
                MaxCasc = b.MaxCasc;

                MotionBlurOff = PostProcessing.MotionBlur.Value == EffectMode.Off;
                ChromaticOff = PostProcessing.ChromaticAberration.Value == EffectMode.Off;
                GrainOff = PostProcessing.Grain.Value == EffectMode.Off;
                VignetteOff = PostProcessing.Vignette.Value == EffectMode.Off;
                DepthOfFieldOff = PostProcessing.DepthOfField.Value == EffectMode.Off;
                LensOff = PostProcessing.LensDistortion.Value == EffectMode.Off;
                AnyEffectOff = MotionBlurOff || ChromaticOff || GrainOff || VignetteOff || DepthOfFieldOff || LensOff;

                Scale = Mathf.Clamp(UiScale.Scale.Value, 0.75f, 1.5f);
                ScaleDialogue = UiScale.ScaleDialogue.Value;
                Ultrawide = UiScale.UltrawideDialogue.Value;
                ScalePause = UiScale.ScalePauseMenu.Value;
                Row = UiScale.InMenuSlider.Value;
                UiWanted = !Mathf.Approximately(Scale, 1f) || Ultrawide;
            }
        }

        internal static void Bind(ConfigFile c)
        {
            VerboseLog = c.Bind("General", "VerboseLog", false, "Write extra detail to the BepInEx log.");

            FrameRate.FixVSyncLimiter = c.Bind("FrameRate", "FixVSyncLimiter", true,
                "With VSync on, remove the game's second frame cap so VSync alone sets the pace. This fixes the lock at half " +
                "your refresh rate (30 FPS on a 60 Hz screen) in borderless and windowed mode. Exclusive fullscreen doesn't change. A " +
                "loose limit of twice your refresh rate (at least 240) stays in case VSync doesn't hold the rate.");
            FrameRate.CapWhenVSyncOff = c.Bind("FrameRate", "CapWhenVSyncOff", CapMode.GameDefault,
                "Frame cap while VSync is off. GameDefault is the game's own cap: your refresh rate setting, or 60. MatchMonitor " +
                "uses your display's real refresh rate, in borderless too. Custom uses CustomCap. Uncapped has no cap, which can " +
                "make your graphics card run hot.");
            FrameRate.CustomCap = c.Bind("FrameRate", "CustomCap", 60,
                new ConfigDescription("Frame cap when CapWhenVSyncOff is Custom.", new AcceptableValueRange<int>(20, 500)));

            Shadows.Preset = c.Bind("Shadows", "Preset", ShadowPreset.Off,
                "Fewer real-time shadows for more FPS. Off keeps the game's look. Light, Balanced and Strong each cut more than the " +
                "one before. Custom uses the Custom settings below.");
            Shadows.CustomMicroPropMaxSize = c.Bind("Shadows", "CustomMicroPropMaxSize", 0.25f,
                new ConfigDescription("Custom only. Small props such as bottles, cups and rocks stop casting shadows when they are " +
                    "smaller than this, in metres. Applied when a level loads. 0 turns this off.", new AcceptableValueRange<float>(0f, 1f)));
            Shadows.CustomIncludeLooseItems = c.Bind("Shadows", "CustomIncludeLooseItems", false,
                "Custom only. Small items you can pick up lose their shadows too.");
            Shadows.CustomLightShadowDistance = c.Bind("Shadows", "CustomLightShadowDistance", 15f,
                new ConfigDescription("Custom only. Lamps farther away than this, in metres, stop casting shadows, but their light " +
                    "stays. 0 turns this off; values between 0 and 8 count as 8.", new AcceptableValueRange<float>(0f, 60f)));
            Shadows.CustomMaxShadowDistance = c.Bind("Shadows", "CustomMaxShadowDistance", 80f,
                new ConfigDescription("Custom only. Upper limit on how far away shadows are drawn, in metres. It never raises the " +
                    "game's own value. 0 keeps the game's value; values between 0 and 10 count as 10.", new AcceptableValueRange<float>(0f, 150f)));
            Shadows.CustomMaxShadowCascades = c.Bind("Shadows", "CustomMaxShadowCascades", 2,
                new ConfigDescription("Custom only. Upper limit on the sun's shadow cascades (1 or 2). 0 keeps the game's value.",
                    new AcceptableValueList<int>(0, 1, 2)));

            const string effectNote = " Off removes it everywhere, story and status effects included. The game mostly uses it when " +
                                      "you're drunk or on mushrooms, in the Wasteland, in the main menu and in status effects.";
            PostProcessing.MotionBlur = c.Bind("PostProcessing", "MotionBlur", EffectMode.GameDefault, "Motion blur." + effectNote);
            PostProcessing.ChromaticAberration = c.Bind("PostProcessing", "ChromaticAberration", EffectMode.GameDefault,
                "Chromatic aberration (colour fringes)." + effectNote);
            PostProcessing.Grain = c.Bind("PostProcessing", "Grain", EffectMode.GameDefault, "Film grain." + effectNote);
            PostProcessing.Vignette = c.Bind("PostProcessing", "Vignette", EffectMode.GameDefault,
                "Vignette (dark screen edges)." + effectNote + " The game's other, very faint vignette stays.");
            PostProcessing.DepthOfField = c.Bind("PostProcessing", "DepthOfField", EffectMode.GameDefault,
                "Depth of field (blur away from the focus)." + effectNote);
            PostProcessing.LensDistortion = c.Bind("PostProcessing", "LensDistortion", EffectMode.GameDefault,
                "Lens distortion (bent image)." + effectNote);

            UiScale.Scale = c.Bind("UiScale", "Scale", 1f,
                new ConfigDescription("Size of the game's HUD, menus, conversations, shops, card game and loading screen. 1 is the " +
                    "game's size. If a size would push the game menu off the screen, the mod uses the largest size that fits.",
                    new AcceptableValueRange<float>(0.75f, 1.5f)));
            UiScale.ScaleDialogue = c.Bind("UiScale", "ScaleDialogue", true, "Conversations follow Scale.");
            UiScale.UltrawideDialogue = c.Bind("UiScale", "UltrawideDialogue", false,
                "On screens wider than 16:9, keep conversations at the size they have on a 16:9 screen.");
            UiScale.ScalePauseMenu = c.Bind("UiScale", "ScalePauseMenu", false,
                "The pause menu follows Scale too, as far as its pages fit. It changes size when you close it.");
            UiScale.InMenuSlider = c.Bind("UiScale", "InMenuSlider", false,
                "Add a \"UI Scale\" slider to Options > Quality in the pause menu. The page's other rows move up a little to make " +
                "room. Takes effect at the next level change.");

            Hot.Refresh();
        }
    }
}
