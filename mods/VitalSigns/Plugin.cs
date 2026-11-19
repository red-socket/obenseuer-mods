using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VitalSigns
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.vitalsigns";
        public const string Name = "Vital Signs";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();
        private static bool _compatReported, _newGamePending, _doorPending;
        private static bool _items, _ticks, _moodTick, _tooltips, _icon, _window, _access, _duck;
        private static readonly List<string> LabelsFailed = new List<string>();
        private static float _nextSlow, _nextScales;

        // Each feature is patched as its own group (spec §3), only when its switch is on at launch. A group checks the
        // game members it needs first; a missing member or a patch exception unpatches that group's own targets, logs
        // one error and leaves the other groups working. Patch bodies also read their switch live (Cfg.Hot).
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);
            bool log = Cfg.DamageLog.Enabled.Value, mood = Cfg.MentalHealth.Enabled.Value;

            // S-053 Damage Log. H3 sits in log-core so pool debits are still labelled "Food or drink" when log-items fails.
            Tracker.CoreOn = PatchGroup("log-core", log,
                Needs(Vanilla.Damage, Vanilla.HealthUpdateStats, Vanilla.HealthUpdateValues, Vanilla.PlayerDeath),
                Targets(Vanilla.Damage, Vanilla.HealthUpdateStats, Vanilla.HealthUpdateValues, Vanilla.PlayerDeath),
                typeof(DamagePatch), typeof(HealthTickPatch), typeof(HealthValuesPatch), typeof(DeathPatch));
            _items = Tracker.ItemsOn = PatchGroup("log-items", log && Tracker.CoreOn,
                Needs(Vanilla.StatsUpdateValues), Targets(Vanilla.StatsUpdateValues), typeof(StatsValuesPatch));
            _ticks = PatchGroup("log-ticks", log && Tracker.CoreOn,
                Needs(Vanilla.TirednessUpdateStats, Vanilla.WaitOnWaiting, Vanilla.MushroomsUpdateStats),
                Targets(Vanilla.TirednessUpdateStats, Vanilla.WaitOnWaiting, Vanilla.MushroomsUpdateStats),
                typeof(TirednessTickPatch), typeof(WaitPatch), typeof(SmvTickPatch));
            if (log && Tracker.CoreOn)
            {
                LabelPatch("toilet", Vanilla.EmptyToToilet, typeof(ToiletPatch));
                LabelPatch("self-harm", Vanilla.HurtYourself, typeof(SelfHarmPatch));
                LabelPatch("fungal treatment", Vanilla.FungalBuyTreatment, typeof(FungalTreatmentPatch));
                LabelPatch("crafting", Vanilla.CraftTestAndApply, typeof(CraftingPatch));
                if (LabelsFailed.Count == 0) _on.Add("log-labels");
                else _failed.Add("log-labels (" + string.Join(", ", LabelsFailed.ToArray()) + ")");
            }
            else _off.Add("log-labels");

            // S-012 mental-health explainer. The mood tick frame also tells a breakdown from a blackout in the damage
            // log, so it is patched for either feature.
            _moodTick = PatchGroup("mood-tick", mood || Tracker.CoreOn,
                Needs(Vanilla.MentalUpdateStats), Targets(Vanilla.MentalUpdateStats), typeof(MoodTickPatch));
            MoodModel.Patched = PatchGroup("mood", mood && _moodTick,
                Needs(Vanilla.HygieneMentalEffect), Targets(Vanilla.HygieneMentalEffect), typeof(HygieneEffectPatch));
            _icon = PatchGroup("mood-icon", Cfg.MentalHealth.AlwaysShowIcon.Value,
                Needs(Vanilla.UpdateColor), Targets(Vanilla.UpdateColor), typeof(MoodIconPatch));
            PatchGroup("gambling-fix", Cfg.Fixes.GamblingMoodTerm.Value,
                Needs(Vanilla.MentalHealthStatsUpdate), Targets(Vanilla.MentalHealthStatsUpdate), typeof(GamblingTermPatch));
            // D17 fatigue cap: the dose postfix is its own group; the on-load cap runs from LoadingDone (no patch of its
            // own, so a failed group can't roll back clamp-load's postfix on the shared OnLoadingGame target).
            FatigueCap.Patched = PatchGroup("fatigue-cap", Cfg.Fixes.FatigueCapDoses.Value > 0f,
                Needs(Vanilla.MentalUpdateValues), Targets(Vanilla.MentalUpdateValues), typeof(FatigueCapPatch));

            // S-006 addiction fixes. C1 and C3 serve both the approved alcohol/mushroom clamp and the separate smoking clamp
            // (D11); C2 only touches alcohol and mushroom.
            bool clampAny = Cfg.Fixes.ClampAddictions.Value || Cfg.Fixes.ClampSmoking.Value;
            Clamp.Patched = PatchGroup("clamp", clampAny,
                Needs(Vanilla.AddictionsUpdateValues), Targets(Vanilla.AddictionsUpdateValues), typeof(AddictionItemPatch));
            PatchGroup("clamp-events", Cfg.Fixes.ClampAddictions.Value && Clamp.Patched,
                Needs(Vanilla.EditAlcoholAddiction, Vanilla.EditMushroomAddiction),
                Targets(Vanilla.EditAlcoholAddiction, Vanilla.EditMushroomAddiction), typeof(EditAlcoholPatch), typeof(EditMushroomPatch));
            PatchGroup("clamp-load", Cfg.Fixes.ClampOnLoad.Value && Clamp.Patched,
                Needs(Vanilla.StatsOnLoadingGame), Targets(Vanilla.StatsOnLoadingGame), typeof(LoadClampPatch));
            _duck = DuckTime.Patched = PatchGroup("duck-time", Cfg.Fixes.DuckCravingByTime.Value,
                Needs(Vanilla.DeltaSeconds, Vanilla.DuckUpdateStats, Vanilla.DuckCount),
                Targets(Vanilla.DeltaSeconds, Vanilla.DuckUpdateStats), typeof(DeltaSecondsPatch), typeof(DuckPatch));

            // UI: tooltips, the window and its ways in.
            _tooltips = PatchGroup("tooltips", Cfg.DamageLog.HealthTooltip.Value || Cfg.MentalHealth.TooltipBreakdown.Value || Cfg.Addictions.TooltipEta.Value,
                Needs(Vanilla.GetStatToolTipInfo), Targets(Vanilla.GetStatToolTipInfo), typeof(TooltipPatch));
            _window = VitalWindow.Available = PatchGroup("window", true,
                Needs(Vanilla.PauseNavUpdate, Vanilla.HandleBackButtonClick), Targets(Vanilla.PauseNavUpdate, Vanilla.HandleBackButtonClick),
                typeof(PauseNavUpdatePatch), typeof(HandleBackButtonPatch));
            _access = CharacterPageInput.Available = PatchGroup("character-access", Cfg.Access.CharacterPageKey.Value && _window,
                Needs(Vanilla.GetButtonInfo, Vanilla.PageSelect), Targets(Vanilla.GetButtonInfo, Vanilla.PageSelect),
                typeof(ButtonInfoPatch), typeof(PageSelectPatch));
            bool forced = PauseEntry.Forced = _window && Cfg.Access.CharacterPageKey.Value && !_access;
            if (forced) Log.Warn("Ui: the Character page key failed; the pause menu entry is added so the window can still be opened.");
            // The pause entry has no patches: Access.PauseMenuEntry is read live (PauseEntry.Wanted). Listed here as at launch.
            FeatureGroup("pause-entry", _window && (Cfg.Access.PauseMenuEntry.Value || forced), Needs());
            if (!Cfg.Access.CharacterPageKey.Value && !Cfg.Access.PauseMenuEntry.Value && !Keys.Bound(Cfg.Access.OpenKey))
                Log.Info("Ui: no way to open the window is configured (CharacterPageKey, PauseMenuEntry and OpenKey are all off); tooltips and notes still work.");

            Cfg.Access.CharacterPageKey.SettingChanged += (s, a) => CharacterPageInput.RefreshHint();

            // All static events: subscribed once. SecondsPassed first, so GameClock is current inside every stats tick.
            TimeOfDayAzure.SecondsPassed += GameClock.OnSecondsPassed;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.PlayerWillLoadGame += OnPlayerWillLoadGame;
            SaveController.PlayerWillChangeLevel += OnPlayerWillChangeLevel;
            SaveController.LoadingDone += OnLoadingDone;

            string gambling = !_on.Contains("gambling-fix") ? "off" : GamblingGate.Patched ? "patched" : "unmatched";
            Log.Info($"{Name} {Version} on game {Application.version}; on: {List(_on)}; off: {List(_off)}; failed: {List(_failed)}; gambling fix: {gambling}");
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

        // L1-L4: each in its own try; a failed one only loses its label (the reason label is used instead).
        private static void LabelPatch(string name, MethodBase target, Type type)
        {
            try
            {
                if (target == null) throw new MissingMemberException("the game method is missing (see the error above)");
                Harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception e)
            {
                Rollback(new[] { target });
                Log.Error($"Log: the '{name}' label failed; those losses keep their plain reason label. {e.Message}");
                LabelsFailed.Add(name);
            }
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
            Step("scene (window)", VitalWindow.Close);
            Step("scene (input)", InputBlock.Release);
            Step("scene (gate)", PlayGate.OnSceneLoaded);
            Step("scene (tracker)", Tracker.Invalidate);
            Step("scene (labels)", Ctx.OnSceneLoaded);
            Step("scene (mood)", MoodModel.OnSceneLoaded);
            Step("scene (pause entry)", PauseEntry.OnSceneLoaded);
            Step("scene (keys)", Keys.CheckCollisions);
            if (scene.name == "Main Menu")
            {
                // The next character starts its ledgers over; a new game gets a divider when its first baseline is taken.
                _newGamePending = true;
                _doorPending = false;
                Step("scene (menu)", Tracker.MarkFresh);
            }
            if (!_compatReported && GlobalState.instance != null)
            {
                _compatReported = true;
                Step("compat", ReportCompat);
            }
        }

        // Every door fires this too: ChangeLevelWhenReady fires PlayerWillChangeLevel, then ChangeLevelDelay saves to an
        // autosave slot and reloads it through LoadGameWithMigration, which fires PlayerWillLoadGame
        // (SRC/SaveController.cs:328-345, :409-425, :1606). Only a load without a level change pending is a load from disk.
        private static void OnPlayerWillLoadGame()
        {
            if (_doorPending)
            {
                _doorPending = false;
                return;
            }
            _newGamePending = false;
            Step("file load (tracker)", Tracker.MarkFresh);
            Step("file load (alerts)", Alerts.OnFreshLoad);
            Step("file load (divider)", () => DamageLog.Divider(Strings.LoadedSave));
        }

        private static void OnPlayerWillChangeLevel()
        {
            _doorPending = true;
            Step("level change (window)", VitalWindow.Close);
        }

        // ChangeLevelSame (no level name) fires PlayerWillChangeLevel but never loads, then clears SaveController.Loading.
        // A flag left from it would make the next real load look like a door.
        private static void DoorCheck()
        {
            if (_doorPending && !SaveController.Loading) _doorPending = false;
        }

        // A door keeps everything and reconciles (should be no change); a load from disk starts the ledgers over. The
        // fatigue cap runs first: the stats are restored by now (every OnLoadingGame ran in the same frame, before
        // LoadingDone, SRC/SaveController.cs:807-838) and no stats tick has run since.
        private static void OnLoadingDone()
        {
            Step("loading done (fatigue cap)", FatigueCap.OnLoad);
            Step("loading done (gate)", PlayGate.OnLoadingDone);
            Step("loading done (tracker)", () =>
            {
                Tracker.Invalidate();
                Tracker.Account();
            });
        }

        // Other mods' skipping patches on what we patch (spec §4 Compat), once all plugins have loaded.
        private static void ReportCompat()
        {
            if (Tracker.CoreOn) Compat.Overlap(Vanilla.Damage, "the damage log");
            if (_window)
            {
                Compat.Overlap(Vanilla.PauseNavUpdate, "the Vital Signs window's input hand-over");
                Compat.Foreign(Vanilla.HandleBackButtonClick, "Back in the Vital Signs window", "may resume the game instead");
            }
            if (_duck)
            {
                Compat.Overlap(Vanilla.DeltaSeconds, "the duck craving fix");
                Compat.Foreign(Vanilla.DuckUpdateStats, "the duck craving fix", "may not apply as intended");
            }
            if (GamblingGate.Patched) Compat.Foreign(Vanilla.MentalHealthStatsUpdate, "the gambling fix", "may not apply as intended");
            if (FatigueCap.Patched) Compat.Foreign(Vanilla.MentalUpdateValues, "the fatigue cap", "may not apply as intended");
            if (Clamp.Patched) Compat.Foreign(Vanilla.AddictionsUpdateValues, "the addiction clamp", "may not apply as intended");
            if (_tooltips) Compat.Overlap(Vanilla.GetStatToolTipInfo, "the tooltip lines");
            if (_access)
            {
                Compat.Overlap(Vanilla.GetButtonInfo, "the Character page key hint");
                Compat.Overlap(Vanilla.PageSelect, "the Character page key hint");
            }
            if (_items) Compat.Overlap(Vanilla.StatsUpdateValues, "item names in the damage log");
            if (_ticks) Compat.Overlap(Vanilla.WaitOnWaiting, "wait names in the damage log");
            if (_moodTick) Compat.Overlap(Vanilla.MentalUpdateStats, "the mood breakdown");
            if (_icon) Compat.Overlap(Vanilla.UpdateColor, "the always-shown mood icon");
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            Step("door", DoorCheck);
            Step("ctx", Ctx.FrameCheck);
            Step("tracker", Tracker.Account);
            Step("new game divider", NewGameDivider);
            Step("access", CharacterPageInput.Tick);
            Step("pause entry", PauseEntry.Tick);
            Step("window", VitalWindow.Update);
            Step("alerts", Alerts.Tick);
            float now = Time.unscaledTime;
            if (now >= _nextSlow)
            {
                _nextSlow = now + 0.5f;
                Step("caffeine", CaffeineList.ClearIfSpent);
                Step("log tick", DamageLog.Tick);
            }
            if (now >= _nextScales)
            {
                _nextScales = now + 2f;
                Step("toast scales", ToastStack.RefreshScales);
            }
        }

        // A new game sends no PlayerWillLoadGame; its divider is written once the level is playable.
        private static void NewGameDivider()
        {
            if (!_newGamePending || !PlayGate.Open || PlayerStats.instance == null) return;
            _newGamePending = false;
            if (Tracker.Live) DamageLog.Divider(Strings.NewGame);
        }

        private void OnGUI()
        {
            if (!VitalWindow.IsOpen && !ToastStack.Any) return;
            try
            {
                var screen = Skin.Begin();
                VitalWindow.Draw(screen);
                ToastStack.Draw(screen);
            }
            catch (Exception e) { Log.Exception("draw", e); }
        }

        private void OnDestroy()
        {
            TimeOfDayAzure.SecondsPassed -= GameClock.OnSecondsPassed;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.PlayerWillLoadGame -= OnPlayerWillLoadGame;
            SaveController.PlayerWillChangeLevel -= OnPlayerWillChangeLevel;
            SaveController.LoadingDone -= OnLoadingDone;
            InputBlock.Release();
        }
    }
}
