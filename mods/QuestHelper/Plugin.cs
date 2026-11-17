using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestHelper
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(SaveGuardBridge.Guid, BepInDependency.DependencyFlags.SoftDependency)]   // loads first, so the startup line sees it
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.questhelper";
        public const string Name = "Quest Helper";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";
        private const float PostLoadDelay = 1f;

        internal static Harmony Harmony;
        internal static bool JournalOn;   // the store group (P5/P6) and the objective hook are on

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();
        private static bool _compatReported;
        private static bool _objectiveHook, _taskInfo, _hints, _doctor, _store;
        private static float _postLoadAt = -1f;
        private static bool _armWhenPlayable;

        // Each feature is patched as its own group (spec §3.1), only when it is on at launch. A group checks the game
        // members it needs first; a missing member or a patch exception unpatches that group's own targets, logs one
        // error and leaves the other groups working. Feature groups without patches (TBC, map pin) only check members.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Curated.Load();
            Harmony = new Harmony(Guid);

            bool journal = Cfg.Journal.Enabled.Value, tbc = Cfg.ToBeContinued.Enabled.Value, doctor = Cfg.QuestDoctor.Enabled.Value;

            // S-002 Quest Doctor: the window, the pause entry and the pause input hand-over.
            _doctor = PatchGroup("doctor", doctor,
                Needs(Vanilla.PauseNavUpdate, Vanilla.HandleBackButtonClick, Vanilla.SaveDisabledList, Vanilla.EditTask, Vanilla.EditTaskStatus,
                    Vanilla.EditTaskCounter, Vanilla.AddTaskItem, Vanilla.EditTaskObjective, Vanilla.WaitForPendingMapWrite),
                Targets(Vanilla.PauseNavUpdate, Vanilla.HandleBackButtonClick), typeof(PauseNavUpdatePatch), typeof(HandleBackButtonPatch));
            HelperWindow.Available = Detector.Available = _doctor;

            // S-047 Journal+ storage: the journal string in GlobalState.
            _store = PatchGroup("store", journal,
                Needs(Vanilla.GlobalStateOnSavingGame, Vanilla.GlobalStateOnLoadingGame, Vanilla.SetState3),
                Targets(Vanilla.GlobalStateOnSavingGame, Vanilla.GlobalStateOnLoadingGame), typeof(GlobalSavePatch), typeof(GlobalLoadPatch));

            // P1 and P2 serve both the journal and To be continued.
            _objectiveHook = PatchGroup("objective-hook", journal || tbc,
                Needs(Vanilla.EditTaskObjective), Targets(Vanilla.EditTaskObjective), typeof(EditTaskObjectivePatch));
            _taskInfo = PatchGroup("task-info", journal || tbc,
                Needs(Vanilla.ShowTaskInfo, Vanilla.TaskList, Vanilla.VisibleObjectives, Vanilla.TaskInfoPanelActive, Vanilla.ObjectiveTitle,
                    Vanilla.CannotBeCompleted, Vanilla.ObjectiveStatusField),
                Targets(Vanilla.ShowTaskInfo), typeof(ShowTaskInfoPatch));
            JournalOn = journal && _taskInfo;
            if (journal && !_store) Log.Warn("Journal: the save hooks failed; recorded objectives last only until the next loading screen.");

            // To be continued: no patch of its own (P1, P2 and P7 serve it).
            TbcSection.Available = FeatureGroup("tbc", tbc,
                Needs(Vanilla.TaskListPanel, Vanilla.ActiveTaskTitle, Vanilla.RenovationTaskTitle, Vanilla.FinishedTaskTitle,
                    Vanilla.FinishedTaskTitlePrefab, Vanilla.RefreshPanels));

            // The Tasks-page hint line (P7) for both the Use Item and the Drop key.
            _hints = PatchGroup("tasks-hint", _doctor || TbcSection.Available,
                Needs(Vanilla.GetButtonInfo), Targets(Vanilla.GetButtonInfo), typeof(ButtonInfoPatch));

            MapPin.Available = FeatureGroup("mappin", Cfg.MapPin.Enabled.Value,
                Needs(Vanilla.ControllerMap, Vanilla.LandmarkPrefab, Vanilla.LandmarksArea, Vanilla.MapSize, Vanilla.LandmarkImage,
                    Vanilla.LandmarkTip, Vanilla.LandmarkUi, Vanilla.TaskPrefab, Vanilla.StatusActiveUnselected));

            Cfg.Hints.SpoilerTier.SettingChanged += (s, a) => { MapPin.MarkDirty(); RefreshTaskInfo(); };
            Cfg.Hints.ShowBlockers.SettingChanged += (s, a) => RefreshTaskInfo();
            Cfg.MapPin.Follows.SettingChanged += (s, a) => MapPin.MarkDirty();
            Cfg.MapPin.Enabled.SettingChanged += (s, a) => MapPin.MarkDirty();
            Cfg.MapPin.PinWhereReceived.SettingChanged += (s, a) => MapPin.MarkDirty();
            Cfg.MapPin.PinSize.SettingChanged += (s, a) => MapPin.OnSizeChanged();
            Cfg.ToBeContinued.Collapsed.SettingChanged += (s, a) => TbcSection.Dirty = true;
            Cfg.QuestDoctor.DisabledFixes.SettingChanged += (s, a) => Detector.Dirty = true;

            // All static events: subscribed once.
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.LoadingStarted += OnLoadingStarted;
            SaveController.LoadingDone += OnLoadingDone;
            SaveController.SavingDone += OnSavingDone;
            SaveController.PlayerWillChangeLevel += OnPlayerWillChangeLevel;
            SaveController.PlayerWillLoadGame += OnPlayerWillLoadGame;
            TaskController.TaskUpdated += OnTasksChanged;
            TaskController.ObjectiveUpdated += OnTasksChanged;

#if QUESTHELPER_TESTS
            Setups.Bind(Config);
#endif
            string sg = SaveGuardBridge.Installed ? (SaveGuardBridge.Usable ? "found, API ok" : "found, API missing") : "missing";
            Log.Info($"{Name} {Version} on game {Application.version}; on: {List(_on)}; off: {List(_off)}; failed: {List(_failed)}; " +
                     $"data: {Curated.Rows.Count} rows, {Curated.Landmarks.Count} landmarks; Save Guard: {sg}");
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

        // ---- Events (spec §3.3) ----

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive && scene != SceneManager.GetActiveScene()) return;
            Step("scene (window)", HelperWindow.Close);
            Step("scene (input)", InputBlock.Release);
            Step("scene (confirm)", Confirm.ResetAll);
            Step("scene (gate)", PlayGate.OnSceneLoaded);
            Step("scene (pin)", MapPin.OnSceneLoaded);
            Step("scene (tbc)", TbcSection.OnSceneLoaded);
            Step("scene (pause entry)", PauseEntry.OnSceneLoaded);
            Step("scene (keys)", Keys.CheckCollisions);
            if (scene.name == "Main Menu")
            {
                Step("scene (menu)", SaveFolder.Reset);
                Step("scene (menu journal)", Records.OnMainMenu);
                Step("scene (menu doctor)", Detector.OnMainMenu);
                _postLoadAt = -1f;
                _armWhenPlayable = false;
            }
            // A scene that never sends LoadingDone (a new game) still gets the post-load tick once it is playable;
            // a load or a door clears this in LoadingStarted and arms the tick at LoadingDone instead.
            else _armWhenPlayable = true;
            if (!_compatReported && GlobalState.instance != null)
            {
                _compatReported = true;
                Step("compat", ReportCompat);
            }
        }

        private static void OnLoadingStarted()
        {
            Step("loading started (window)", HelperWindow.Close);
            Step("loading started (confirm)", Confirm.ResetAll);
            Step("loading started (journal)", Records.OnLoadingStarted);
            Step("loading started (doctor)", Detector.OnLoadingStarted);
            _postLoadAt = -1f;
            _armWhenPlayable = false;
        }

        // The door save comes after this event, so the journal stays readable and writable here (design.md §2).
        private static void OnPlayerWillChangeLevel()
        {
            Step("level change (window)", HelperWindow.Close);
            Step("level change (confirm)", Confirm.ResetAll);
        }

        private static void OnPlayerWillLoadGame() => Step("file load", SaveFolder.OnFileLoadStarted);

        // Lua is applied before LoadingDone (SRC/SaveController.cs:827-838). The post-load tick waits 1 s and for the map.
        private static void OnLoadingDone()
        {
            Step("loading done (gate)", PlayGate.OnLoadingDone);
            Step("loading done (journal)", Records.OnLoadingDone);
            Step("loading done (save folder)", () => SaveFolder.OnLoadingDone(SaveController.LastSaveFolderPath));
            Step("loading done (landmarks)", Curated.LoadLandmarks);
            TbcSection.Dirty = true;
            _armWhenPlayable = false;
            _postLoadAt = Time.unscaledTime + PostLoadDelay;
        }

        private static void OnSavingDone()
        {
            Step("saving done", () => { if (SaveController.instance != null) SaveFolder.OnWritten(SaveController.instance.GetCurrentSavePath()); });
        }

        private static void OnTasksChanged()
        {
            TbcSection.Dirty = true;
            MapPin.MarkDirty();
            Detector.Dirty = true;
        }

        private static void PostLoadTick()
        {
            if (_armWhenPlayable && PlayGate.Open)
            {
                _armWhenPlayable = false;
                if (_postLoadAt < 0f) _postLoadAt = Time.unscaledTime + PostLoadDelay;
            }
            if (_postLoadAt < 0f || Time.unscaledTime < _postLoadAt) return;
            var mc = MapController.instance;
            if (mc != null && mc.loadingMap) return;
            _postLoadAt = -1f;
            Step("post-load (pin)", MapPin.Refresh);
            Step("post-load (doctor)", Detector.AfterLoad);
        }

        // ShowTaskInfo re-runs with the new tier when the task panel is showing.
        private static void RefreshTaskInfo()
        {
            try
            {
                if (TaskPanel.instance != null && TaskController.instance != null && TaskController.instance.currentTaskIndex >= 0)
                    TaskPanel.instance.UpdateCurrentTaskInfo();
            }
            catch (Exception e) { Log.Exception("task info refresh", e); }
        }

        // Other mods' skipping patches on what we patch or call (spec §10), once all plugins have loaded.
        private static void ReportCompat()
        {
            if (_objectiveHook) Compat.Overlap(Vanilla.EditTaskObjective, "the objective journal");
            if (_taskInfo) Compat.Foreign(Vanilla.ShowTaskInfo, "the journal lines", "may not show");
            if (_doctor)
            {
                Compat.Overlap(Vanilla.PauseNavUpdate, "the Quest Helper window's input hand-over");
                Compat.Foreign(Vanilla.HandleBackButtonClick, "Back in the Quest Helper window", "may resume the game instead");
                Compat.Foreign(Vanilla.EditTask, "the curated fixes", "may not add objectives");
                Compat.Foreign(Vanilla.EditTaskStatus, "the generic edits", "may not apply");
            }
            if (_store)
            {
                Compat.Foreign(Vanilla.GlobalStateOnSavingGame, "the journal", "may not be saved");
                Compat.Overlap(Vanilla.GlobalStateOnLoadingGame, "the journal");
            }
            if (_hints) Compat.Overlap(Vanilla.GetButtonInfo, "the Tasks page key hints");
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            Step("confirm", Confirm.Tick);
            Step("post-load", PostLoadTick);
            Step("pause entry", PauseEntry.Tick);
            Step("tasks page", TaskTabInput.Tick);
            Step("window", HelperWindow.Update);
            Step("detector", Detector.Tick);
        }

        private void LateUpdate()
        {
            Step("tbc", TbcSection.RelayoutIfDirty);
            Step("pin", MapPin.RefreshIfDirty);
        }

        private void OnGUI()
        {
            if (!HelperWindow.IsOpen && !Toast.Any) return;
            try
            {
                var screen = Skin.Begin();
                HelperWindow.Draw(screen);
                Toast.Draw(screen);
            }
            catch (Exception e) { Log.Exception("draw", e); }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.LoadingStarted -= OnLoadingStarted;
            SaveController.LoadingDone -= OnLoadingDone;
            SaveController.SavingDone -= OnSavingDone;
            SaveController.PlayerWillChangeLevel -= OnPlayerWillChangeLevel;
            SaveController.PlayerWillLoadGame -= OnPlayerWillLoadGame;
            TaskController.TaskUpdated -= OnTasksChanged;
            TaskController.ObjectiveUpdated -= OnTasksChanged;
            InputBlock.Release();
        }
    }
}
