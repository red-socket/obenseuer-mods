using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DisplayOptions
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.displayoptions";
        public const string Name = "Display Options";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private static readonly List<string> On = new List<string>();
        private static readonly List<string> Off = new List<string>();
        private static readonly List<string> Failed = new List<string>();
        private static readonly List<string> Lazy = new List<string>();
        private static bool _framerate, _quality, _effects, _ui, _row, _started;
        private static int _refitFrame, _driftFrame;

        // Each feature is patched as its own group (spec §3.1). A group checks the game members it needs first; a missing
        // member or a patch exception unpatches that group's own targets, logs one error and leaves the other groups
        // working. Each original belongs to exactly one group, so unpatching by owner is safe. The groups are patched
        // whatever the settings say (every default leaves the game as it is, and every setting works live); only the lamp
        // cap is patched lazily, the first time it is needed.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // S-005 part 1: the frame cap.
            _framerate = PatchGroup("framerate", true,
                Needs(Vanilla.ApplyFrameRateLimit, Vanilla.ApplyVerticalSync), Targets(Vanilla.ApplyFrameRateLimit, Vanilla.ApplyVerticalSync),
                typeof(ApplyFrameRateLimitPatch), typeof(ApplyVerticalSyncPatch));
            FrameLimiter.Broken = !_framerate;

            // S-005 part 2: the shadow budget.
            _quality = PatchGroup("quality-caps", true,
                Needs(Vanilla.ApplyQualitySettings), Targets(Vanilla.ApplyQualitySettings), typeof(ApplyQualitySettingsPatch));
            QualityCaps.Broken = !_quality;
            MicroProps.Available = FeatureGroup("micro-props", true,
                Needs(Vanilla.Door, Vanilla.DoorChangelevel, Vanilla.CharacterModel, Vanilla.NPCInfo, Vanilla.FurniturePlaceable,
                    Vanilla.FurnitureCollectable, Vanilla.CollectibleItem));
            LightCap.InitAtLaunch();
            if (!LightCap.Patched && !LightCap.Failed && !LightCap.StoodDown) Lazy.Add("light-cap");

            // S-084: the effect toggles. P5b only when P5's target is missing.
            if (Vanilla.UpdateSettings != null)
                _effects = PatchGroup("effects", true, Needs(Vanilla.UpdateSettings), Targets(Vanilla.UpdateSettings), typeof(UpdateSettingsPatch));
            else
                _effects = PatchGroup("effects (fallback)", true, Needs(Vanilla.UpdateVolumeSystem), Targets(Vanilla.UpdateVolumeSystem),
                    typeof(UpdateVolumeSystemPatch));
            EffectClamp.Broken = !_effects;

            // S-095: UI scale and its options row. The row group is patched even with InMenuSlider off, so turning it on
            // works at the next scene; its body reads the setting.
            _ui = PatchGroup("ui-scale", true,
                Needs(Vanilla.ScalerOnEnable, Vanilla.GameUIInstanceT, Vanilla.PauseMenuT, Vanilla.LoadingScreenT, Vanilla.CardGameUIT,
                    Vanilla.ShopBaseUIT, Vanilla.StartSelectionsUIT, Vanilla.DialogueSystemControllerT, Vanilla.CreditsT),
                Targets(Vanilla.ScalerOnEnable), typeof(ScalerOnEnablePatch));
            Scaling.Broken = !_ui;
            _row = PatchGroup(Cfg.Hot.Row ? "ui-row" : "ui-row (waits for InMenuSlider)", _ui,
                Needs(Vanilla.StartInit, Vanilla.FovSlider, Vanilla.FovNumbers, Vanilla.PostFX, Vanilla.NavButtons),
                Targets(Vanilla.StartInit), typeof(StartInitPatch));
            ScaleRow.Broken = !_row;

            Config.SettingChanged += OnSettingChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.LoadingDone += OnLoadingDone;
        }

        private static string List(List<string> l) => l.Count == 0 ? "—" : string.Join(", ", l.ToArray());

        private static object[] Needs(params object[] members) => members;

        private static MethodBase[] Targets(params MethodBase[] targets) => targets;

        private static bool PatchGroup(string name, bool enabled, object[] needs, MethodBase[] targets, params Type[] types)
        {
            if (!enabled)
            {
                Off.Add(name);
                return false;
            }
            try
            {
                foreach (var n in needs)
                    if (n == null) throw new MissingMemberException("a game member this feature needs is missing (see the error above)");
                foreach (var t in types) Harmony.CreateClassProcessor(t).Patch();
                On.Add(name);
                return true;
            }
            catch (Exception e)
            {
                Rollback(targets);
                Log.Error($"Patching '{name}' failed; that feature is off this session: {e}");
                Failed.Add(name);
                return false;
            }
        }

        // The lamp cap's lazy group (spec §3.1 P4): same rules, on the main thread, at launch or after a config change.
        internal static bool PatchLazy(string name, object[] needs, MethodBase target, Type type)
        {
            Lazy.Remove(name);
            bool ok = PatchGroup(name, true, needs, new[] { target }, type);
            if (ok && _started) Log.Info($"Patched '{name}' now that it is needed.");
            return ok;
        }

        // A feature with no patches of its own: available when every member it uses resolved.
        private static bool FeatureGroup(string name, bool enabled, object[] needs)
        {
            if (!enabled)
            {
                Off.Add(name);
                return false;
            }
            foreach (var n in needs)
                if (n == null)
                {
                    Log.Error($"'{name}' is off this session: a game member it needs is missing (see the error above).");
                    Failed.Add(name);
                    return false;
                }
            On.Add(name);
            return true;
        }

        private static void Rollback(MethodBase[] targets)
        {
            foreach (var m in targets)
            {
                if (m == null) continue;
                try { Harmony.Unpatch(m, HarmonyPatchType.All, Guid); }
                catch (Exception e) { Log.Exception("unpatch " + m.DeclaringType?.Name + "." + m.Name, e); }
            }
        }

        // ---- Events (spec §3.2) ----

        private static void OnSettingChanged(object sender, SettingChangedEventArgs a)
        {
            Step("config", Cfg.Hot.Refresh);
            switch (a?.ChangedSetting?.Definition?.Section)
            {
                case "FrameRate":
                    Step("config (frame rate)", FrameLimiter.Reapply);
                    break;
                case "Shadows":
                    Step("config (shadows)", Budget.Changed);
                    break;
                case "UiScale":
                    Step("config (ui scale)", Scaling.ApplyAll);
                    break;
                // PostProcessing: read on the next blend.
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive && scene != SceneManager.GetActiveScene()) return;
            if (!_started)
            {
                _started = true;
                Step("compat", ReportCompat);
                Step("startup", Startup);
            }
            Step("scene (ui scale)", Scaling.OnSceneLoaded);
            Step("scene (micro-props)", MicroProps.OnSceneLoaded);
            _driftFrame = Time.frameCount + 1;
        }

        private static void OnLoadingDone()
        {
            Step("loading done (micro-props)", MicroProps.OnLoadingDone);
            Step("loading done (light cap)", LightCap.ApplyNow);
            Step("loading done (frame cap)", FrameLimiter.DriftCheck);
        }

        // P2: Screen.SetResolution takes effect at the end of the frame; two frames later the cap and the UI fit follow.
        internal static void ScheduleDisplayRefit() => _refitFrame = Time.frameCount + 2;

        // Other mods on what we patch (spec §11.4, D19), once all plugins have loaded. Stand-downs are per feature, for the
        // session, with one warning each.
        private static void ReportCompat()
        {
            if (_framerate &&
                (Compat.AnyForeign(Vanilla.ApplyFrameRateLimit, "the frame cap fix", "is off this session") |
                 Compat.AnyForeign(Vanilla.ApplyVerticalSync, "the frame cap fix", "is off this session")))
                FrameLimiter.StoodDown = true;
            // Any foreign patch, postfixes included: one that writes shadowDistance would be overridden by our Last postfix.
            if (_quality && Compat.AnyForeign(Vanilla.ApplyQualitySettings, "the shadow distance and cascade caps", "are off this session"))
            {
                QualityCaps.Restore();
                QualityCaps.StoodDown = true;
            }
            if (LightCap.Patched && Compat.AnyForeign(Vanilla.LightUpdate, "the lamp shadow distance cap", "is off this session"))
                LightCap.StandDown();
            if (_ui &&
                (Compat.AnyForeign(Vanilla.ScalerOnEnable, "UI scale", "is off this session") |
                 Compat.AnyForeign(Vanilla.ScalerHandle, "UI scale", "is off this session") |
                 Compat.AnyForeign(Vanilla.ScalerHandleScreenSize, "UI scale", "is off this session")))
                Scaling.StandDown();
            if (_effects)
            {
                Compat.Overlap(Vanilla.UpdateSettings, "the camera effect switches");
                Compat.Overlap(Vanilla.UpdateVolumeSystem, "the camera effect switches");
            }
            if (_row) Compat.Overlap(Vanilla.StartInit, "the UI scale row");
            Compat.KeywordScan();
        }

        private static void Startup()
        {
            var down = new List<string>();
            if (FrameLimiter.StoodDown) down.Add("framerate");
            if (QualityCaps.StoodDown) down.Add("quality-caps");
            if (LightCap.StoodDown) down.Add("light-cap");
            if (Scaling.StoodDown) down.Add("ui-scale");
            Log.Info($"{Name} {Version} on game {Application.version}; on: {List(On)}; lazy: {List(Lazy)}; off: {List(Off)}; " +
                     $"failed: {List(Failed)}" + (down.Count > 0 ? $"; stood down: {List(down)}" : ""));

            // One line per setting that differs from the default.
            if (!Cfg.FrameRate.FixVSyncLimiter.Value) Log.Info("Fps: the VSync frame cap fix is off.");
            if (Cfg.FrameRate.CapWhenVSyncOff.Value != CapMode.GameDefault)
                Log.Info($"Fps: cap with VSync off: {Cfg.FrameRate.CapWhenVSyncOff.Value}" +
                         (Cfg.FrameRate.CapWhenVSyncOff.Value == CapMode.Custom ? $" {Cfg.Hot.CustomCap}." : "."));
            if (Cfg.Shadows.Preset.Value != ShadowPreset.Off) Log.Info($"Shadows: {Budget.Describe()}.");
            var fx = new List<string>();
            if (Cfg.Hot.MotionBlurOff) fx.Add("motion blur");
            if (Cfg.Hot.ChromaticOff) fx.Add("chromatic aberration");
            if (Cfg.Hot.GrainOff) fx.Add("grain");
            if (Cfg.Hot.VignetteOff) fx.Add("vignette");
            if (Cfg.Hot.DepthOfFieldOff) fx.Add("depth of field");
            if (Cfg.Hot.LensOff) fx.Add("lens distortion");
            if (fx.Count > 0) Log.Info($"Effects: off: {string.Join(", ", fx.ToArray())}.");
            if (!Mathf.Approximately(Cfg.Hot.Scale, 1f)) Log.Info($"Ui: scale {Cfg.Hot.Scale:0.00}.");
            if (!Cfg.Hot.ScaleDialogue) Log.Info("Ui: conversations keep the game's size.");
            if (Cfg.Hot.Ultrawide) Log.Info("Ui: conversations keep their 16:9 size on wider screens.");
            if (Cfg.Hot.ScalePause) Log.Info("Ui: the pause menu follows the scale.");
            if (Cfg.Hot.Row) Log.Info("Ui: UI scale row in the options menu.");
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        // ---- Deferred jobs (spec §3.3). Nothing here polls game state. ----

        private void Update()
        {
            int f = Time.frameCount;
            if (_refitFrame > 0 && f >= _refitFrame)
            {
                _refitFrame = 0;
                Step("display refit (frame cap)", FrameLimiter.Reapply);
                Step("display refit (ui)", Scaling.Refit);
            }
            if (_driftFrame > 0 && f >= _driftFrame)
            {
                _driftFrame = 0;
                Step("drift check", FrameLimiter.DriftCheck);
            }
            Step("micro-props", MicroProps.Tick);
            Step("light cap", LightCap.Tick);
            Step("ui row", ScaleRow.Tick);
            Step("ui pause", Scaling.Tick);
        }

        // Everything is runtime state; put the game's own back (spec §9).
        private void OnDestroy()
        {
            Config.SettingChanged -= OnSettingChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.LoadingDone -= OnLoadingDone;
            Step("restore (frame cap)", FrameLimiter.Restore);
            Step("restore (quality caps)", QualityCaps.Restore);
            Step("restore (lamps)", LightCap.StandDown);
            Step("restore (micro-props)", MicroProps.Restore);
            Step("restore (ui)", Scaling.StandDown);
        }
    }
}
