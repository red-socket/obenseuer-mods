using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GreenThumb
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.greenthumb";
        public const string Name = "Green Thumb";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;
        internal static Plugin Instance;

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();
        private static bool _compatReported, _fruit, _fresh, _weight, _bands, _hint, _reach, _ghost;

        // Each feature is patched as its own group (spec §3), only when its switch is on at launch. A group checks the
        // game members it needs first; a missing member or a patch exception unpatches that group's own targets, logs
        // one error and leaves the other groups working. Patch bodies also read their switch live (Cfg.Hot), so turning
        // a feature off works at once.
        private void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // S-044a "Growing fruits" percentage (G1, G2).
            _fruit = PatchGroup("fruit-percent", Cfg.Growth.FixFruitPercent.Value,
                Needs(Vanilla.GrowGetCurrentStage, Vanilla.GrowGetCurrentStageProcess, Vanilla.FullGrownAgeFinal, Vanilla.RandomGrowStartTime),
                Targets(Vanilla.GrowGetCurrentStage, Vanilla.GrowGetCurrentStageProcess),
                typeof(StagePatch), typeof(ProcessPatch));

            // S-044b sunlight refresh: no patches.
            SunRefresh.On = FeatureGroup("sun-refresh", Cfg.Sunlight.Refresh.Value,
                Needs(Vanilla.NlscGrowing, Vanilla.NlscRefreshModifiers, Vanilla.ManuActive));

            // S-060 power pause fix (P1-P4) and its hint (P5).
            PowerFix.Patched = PatchGroup("power-fix", Cfg.Power.PauseWithoutPower.Value,
                Needs(Vanilla.OnPowerOut, Vanilla.OnPowerOn, Vanilla.PauseGrowth, Vanilla.UnpauseGrowth),
                Targets(Vanilla.OnPowerOut, Vanilla.OnPowerOn, Vanilla.PauseGrowth, Vanilla.UnpauseGrowth),
                typeof(PowerOutPatch), typeof(PowerOnPatch), typeof(PausePatch), typeof(UnpausePatch));
            PatchGroup("power-hint", Cfg.Power.PausedHint.Value && PowerFix.Patched,
                Needs(Vanilla.GrowGetTitle), Targets(Vanilla.GrowGetTitle), typeof(PausedHintPatch));

            // S-061a fresh picking (F1), off by default.
            _fresh = PatchGroup("fresh-picking", Cfg.FreshPicking.Mode.Value != FreshMode.Off,
                Needs(Vanilla.UpdateSpoilTime, Vanilla.RandomSpoilTime, Vanilla.GrowItemMeta, Vanilla.GrowItemItem),
                Targets(Vanilla.UpdateSpoilTime), typeof(SpoilTimePatch));

            // S-061b freshness bands: the weighted merge (B3) first; bands (B1, B2) only on top of it, since bands without
            // weighting would let stale units join a fresher stack and take its freshness.
            bool stacking = Cfg.FreshnessStacking.Enabled.Value;
            _weight = PatchGroup("fresh-weight", stacking,
                Needs(Vanilla.MixStackQuality, Vanilla.RemoveMinutesFromIntegrity), Targets(Vanilla.MixStackQuality), typeof(MixQualityPatch));
            _bands = PatchGroup("fresh-bands", stacking && _weight,
                Needs(Vanilla.SlotHasSpaceForItemStatic, Vanilla.CompareItemMetas),
                Targets(Vanilla.SlotHasSpaceForItemStatic, Vanilla.CompareItemMetas),
                typeof(SlotSpacePatch), typeof(CompareMetasPatch));

            // S-070 harvest-all (H1), its hint (H2) and the opt-in reach (H3).
            HarvestAll.Patched = PatchGroup("harvest-all", Cfg.HarvestAll.Enabled.Value,
                Needs(Vanilla.RepeatHarvest, Vanilla.LastManualHarvestTime, Vanilla.RepeatHarvestReadyTime, Vanilla.GrowIsDead),
                Targets(Vanilla.RepeatHarvest), typeof(RepeatHarvestPatch));
            _hint = PatchGroup("harvest-hint", Cfg.HarvestAll.Enabled.Value && Cfg.HarvestAll.ShowHint.Value && HarvestAll.Patched,
                Needs(Vanilla.UseAndHoldText), Targets(Vanilla.UseAndHoldText), typeof(HoldHintPatch));
            _reach = PatchGroup("harvest-reach", Cfg.HarvestAll.Reach.Value == HarvestReach.WholeGrower && HarvestAll.Patched,
                Needs(Vanilla.IsWithinUseDistance), Targets(Vanilla.IsWithinUseDistance), typeof(ReachPatch));
            if (Cfg.HarvestAll.Reach.Value == HarvestReach.WholeGrower && HarvestAll.Patched && !_reach)
                Log.Warn("Harvest all: WholeGrower reach failed; harvest-all uses normal reach this session.");

            // S-070 quick-plant ghost (Q1, Q2).
            _ghost = PatchGroup("quick-plant", Cfg.QuickPlant.GhostMode.Value != GhostMode.Vanilla,
                Needs(Vanilla.UpdateQuickSlot, Vanilla.UpdateQuickPlantPreview, Vanilla.GrowIsDead),
                Targets(Vanilla.UpdateQuickSlot, Vanilla.UpdateQuickPlantPreview),
                typeof(QuickSlotPatch), typeof(QuickPreviewPatch));

            // All static events: subscribed once.
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.PlayerWillLoadGame += OnPlayerWillLoadGame;
            SaveController.PlayerWillChangeLevel += OnPlayerWillChangeLevel;
            SaveController.LoadingDone += OnLoadingDone;

            Log.Info($"{Name} {Version} on game {Application.version}; on: {List(_on)}; off: {List(_off)}; failed: {List(_failed)}");
        }

        private static string List(List<string> l) => l.Count == 0 ? "—" : string.Join(", ", l.ToArray());

        private static object[] Needs(params object[] members) => members;

        private static MethodBase[] Targets(params MethodBase[] targets) => targets;

        private bool PatchGroup(string name, bool enabled, object[] needs, MethodBase[] targets, params Type[] types)
        {
            if (!enabled)
            {
                _off.Add(name);
                return false;
            }
            try
            {
                foreach (var n in needs)
                    if (n == null) throw new MissingMemberException("a game member this feature needs is missing (see the error above)");
                foreach (var t in types) Harmony.CreateClassProcessor(t).Patch();
                _on.Add(name);
                return true;
            }
            catch (Exception e)
            {
                Rollback(targets);
                Log.Error($"Patching '{name}' failed; that feature is off this session: {e}");
                _failed.Add(name);
                return false;
            }
        }

        // A feature with no patches of its own: available when it is on and every member it uses resolved.
        private bool FeatureGroup(string name, bool enabled, object[] needs)
        {
            if (!enabled)
            {
                _off.Add(name);
                return false;
            }
            foreach (var n in needs)
                if (n == null)
                {
                    Log.Error($"'{name}' is off this session: a game member it needs is missing (see the error above).");
                    _failed.Add(name);
                    return false;
                }
            _on.Add(name);
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

        // ---- Events (spec §3) ----

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive && scene != SceneManager.GetActiveScene()) return;
            Step("scene (gate)", PlayGate.OnSceneLoaded);
            Step("scene (power)", PowerFix.OnSceneLoaded);   // fresh scenes never send LoadingDone
            Step("scene (sun)", SunRefresh.Reset);
            Step("scene (harvest)", HarvestAll.OnSceneLoaded);
            if (!_compatReported && GlobalState.instance != null)
            {
                _compatReported = true;
                Step("compat", ReportCompat);
            }
        }

        // Every door fires both: ChangeLevelWhenReady fires PlayerWillChangeLevel, then ChangeLevelDelay saves to an
        // autosave slot and reloads it (PlayerWillLoadGame; SRC/SaveController.cs:328-345, :409-425, :1606).
        private static void OnPlayerWillLoadGame() => Step("will load (power)", PowerFix.Hold);

        private static void OnPlayerWillChangeLevel() => Step("will change level (power)", PowerFix.Hold);

        private static void OnLoadingDone()
        {
            Step("loading done (gate)", PlayGate.OnLoadingDone);
            Step("loading done (power)", PowerFix.BeginSettle);
        }

        // Other mods' skipping patches on what we patch (spec §12), once all plugins have loaded.
        private static void ReportCompat()
        {
            if (PowerFix.Patched)
            {
                Compat.Foreign(Vanilla.OnPowerOut, "the power fix", "may not pause growers as intended");
                Compat.Foreign(Vanilla.OnPowerOn, "the power fix", "may not resume growers as intended");
            }
            if (HarvestAll.Patched)
                HarvestAll.ForeignSkip = Compat.Foreign(Vanilla.RepeatHarvest, "harvest-all", "stands down this session");
            if (_hint) Compat.Overlap(Vanilla.UseAndHoldText, "the harvest hint");
            if (_reach) Compat.Overlap(Vanilla.IsWithinUseDistance, "WholeGrower reach");
            if (_ghost) Ghost.ForeignSkip = Compat.Foreign(Vanilla.UpdateQuickSlot, "the quick-plant ghost setting", "stands down this session");
            if (_fresh) Compat.Foreign(Vanilla.UpdateSpoilTime, "fresh picking", "may not apply as intended");
            if (_fruit) Compat.Overlap(Vanilla.GrowGetCurrentStageProcess, "the fruit percentage");
            if (_weight) Compat.Overlap(Vanilla.MixStackQuality, "the weighted freshness merge");
            if (_bands)
            {
                Compat.Foreign(Vanilla.CompareItemMetas, "freshness bands", "may not apply as intended");
                Compat.Overlap(Vanilla.SlotHasSpaceForItemStatic, "freshness bands");
            }
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            Step("sun", SunRefresh.Tick);
            Step("harvest press", HarvestAll.TrackPress);
            Step("power flush", PowerFix.FlushDirty);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.PlayerWillLoadGame -= OnPlayerWillLoadGame;
            SaveController.PlayerWillChangeLevel -= OnPlayerWillChangeLevel;
            SaveController.LoadingDone -= OnLoadingDone;
        }
    }
}
