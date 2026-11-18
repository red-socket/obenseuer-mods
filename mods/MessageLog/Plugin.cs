using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MessageLog
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.messagelog";
        public const string Name = "Message Log";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;
        internal static bool DialogueOn;     // the dialogue group is patched
        internal static bool SearchWanted;   // Window.Search was on at launch

        private static readonly List<string> On = new List<string>();
        private static readonly List<string> Off = new List<string>();
        private static readonly List<string> Failed = new List<string>();
        private static bool _startupReported, _capture, _window, _notesKey, _search;

        // Each feature is patched as its own group (spec §3), only when its switch is on at launch. A group checks the
        // game members it needs first; a missing member or a patch exception unpatches that group's own targets, logs
        // one error and leaves the other groups working. Patch bodies also read their switch live (Cfg.Hot), so turning
        // a feature off works at once; turning one on needs a restart. Saving and loading use the game's own static
        // events only: no patch touches SaveController, GlobalState, Notifications' save code or the notification UI.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // N1 (§4.1). Storage and the window work without it: history is still loaded and saved again.
            _capture = PatchGroup("capture", Cfg.Capture.Notifications.Value,
                Needs(Vanilla.CreateNotification), Targets(Vanilla.CreateNotification), typeof(NotifyCapture));
            // D1 (§4.2).
            DialogueOn = PatchGroup("dialogue", Cfg.Dialogue.ConversationLines.Value,
                Needs(Vanilla.OnConversationLine), Targets(Vanilla.OnConversationLine), typeof(DialogueCapture));
            // W1, W2 (§8.3).
            _window = LogWindow.Available = PatchGroup("window", Cfg.Window.Enabled.Value,
                Needs(Vanilla.PauseNavUpdate, Vanilla.HandleBackButtonClick), Targets(Vanilla.PauseNavUpdate, Vanilla.HandleBackButtonClick),
                typeof(PauseNavUpdatePatch), typeof(HandleBackButtonPatch));
            // H1 (§8.1).
            _notesKey = NotesPageInput.Available = PatchGroup("notes-key", _window && Cfg.Access.NotesPageKey.Value,
                Needs(Vanilla.GetButtonInfo, Vanilla.KeyCodeInfoToString), Targets(Vanilla.GetButtonInfo), typeof(ButtonInfoPatch));
            // T1-T5 (§8.6).
            SearchWanted = _window && Cfg.Window.Search.Value;
            _search = SearchGuard.Available = LogWindow.SearchPatched = PatchGroup("search", SearchWanted,
                Needs(Vanilla.GetKeyDown, Vanilla.GetAuxiliaryReloadDown, Vanilla.GetKeyDownKeyboardOnly, Vanilla.GetKey,
                    Vanilla.GetKeyWithHold, Vanilla.DisabledWritelockList),
                Targets(Vanilla.GetKeyDown, Vanilla.GetAuxiliaryReloadDown, Vanilla.GetKeyDownKeyboardOnly, Vanilla.GetKey, Vanilla.GetKeyWithHold),
                typeof(GuardGetKeyDown), typeof(GuardGetAuxiliaryReloadDown), typeof(GuardGetKeyDownKeyboardOnly), typeof(GuardGetKey),
                typeof(GuardGetKeyWithHold));
            bool forced = PauseEntry.Forced = _window && Cfg.Access.NotesPageKey.Value && !_notesKey;
            if (forced) Log.Warn("Window: the Notes page key failed; the pause menu entry is added so the log can still be opened.");
            // The pause entry has no patches: Access.PauseMenuEntry is read live (PauseEntry.Wanted). Listed here as at launch.
            if (_window && (Cfg.Access.PauseMenuEntry.Value || forced)) On.Add("pause-entry"); else Off.Add("pause-entry");

#if MESSAGELOG_TESTS
            Setups.Bind(Config);
#endif

            Config.SettingChanged += OnSettingChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.PlayerWillChangeLevel += OnPlayerWillChangeLevel;
            SaveController.PlayerWillLoadGame += OnPlayerWillLoadGame;
            SaveController.SavingDone += OnSavingDone;
            SaveController.LoadingStarted += OnLoadingStarted;
            SaveController.LoadingDone += OnLoadingDone;
        }

        private static string List(List<string> l) => l.Count == 0 ? "-" : string.Join(", ", l.ToArray());

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
        // Every handler body is guarded: SavingDone and LoadingDone are invoked unguarded by the game, and a throw from a
        // LoadingStarted handler would skip every OnLoading phase. Our handlers can never abort a save or break a load.

        private static void Guard(string site, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Once(site, e); }
        }

        private static void OnPlayerWillChangeLevel() => Guard("Load: level change", SlotTracker.OnPlayerWillChangeLevel);

        private static void OnPlayerWillLoadGame() => Guard("Load: will load", SlotTracker.OnPlayerWillLoadGame);

        private static void OnSavingDone() => Guard("Storage: saving done", SlotTracker.OnSavingDone);

        private static void OnLoadingStarted() => Guard("Load: loading started", SlotTracker.OnLoadingStarted);

        private static void OnLoadingDone()
        {
            Guard("Load: loading done", SlotTracker.OnLoadingDone);
            Guard("Load: loading done (gate)", PlayGate.OnLoadingDone);
            Guard("Load: loading done (model)", LogModel.MarkDirty);
        }

        private static void OnSettingChanged(object sender, SettingChangedEventArgs a)
        {
            Guard("settings", () =>
            {
                Cfg.Hot.Refresh();
                string section = a?.ChangedSetting?.Definition?.Section;
                string key = a?.ChangedSetting?.Definition?.Key;
                if (section == "Storage" && (key == "MaxNotifications" || key == "MaxDialogueLines")) History.ApplyCaps();
                if (section == "Access" || section == "Window")
                {
                    NotesPageInput.RefreshHint();
                    LogWindow.RefreshFooter();
                }
                LogModel.MarkDirty();
            });
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive && scene != SceneManager.GetActiveScene()) return;
            Guard("scene (window)", LogWindow.Close);
            Guard("scene (typing)", SearchGuard.ReleaseNow);
            Guard("scene (input)", InputBlock.Release);
            Guard("scene (dialogue)", DialogueCapture.OnSceneLoaded);
            Guard("scene (gate)", PlayGate.OnSceneLoaded);
            Guard("scene (pause entry)", PauseEntry.OnSceneLoaded);
            Guard("scene (keys)", Keys.CheckCollisions);
            // Quitting to the menu, the credits route and the start of a new game (which fires no load events) all pass
            // through the main menu: the log starts empty there.
            if (scene.name == "Main Menu")
            {
                Guard("scene (history)", History.Reset);
                Guard("scene (tracker)", SlotTracker.Reset);
            }
            if (!_startupReported && GlobalState.instance != null)
            {
                _startupReported = true;
                Guard("startup", ReportStartup);
            }
        }

        // The startup line and the Compat lines (spec §3, §11), once, at the first gameplay scene: all plugins have loaded
        // by then, so other mods' patches are visible.
        private static void ReportStartup()
        {
            Log.Info($"{Name} {Version}: on [{List(On)}]; off [{List(Off)}]; failed [{List(Failed)}]");
            if (_window)
            {
                var ways = new List<string>();
                if (_notesKey) ways.Add("the Notes page (Move Item)");
                if (Cfg.Access.PauseMenuEntry.Value || PauseEntry.Forced) ways.Add("the pause menu");
                if (Keys.Bound(Cfg.Access.OpenKey)) ways.Add("OpenKey (" + Cfg.Access.OpenKey.Value + ")");
                Log.Info(ways.Count > 0
                    ? "Window: opens from " + string.Join(", ", ways.ToArray()) + "."
                    : "Window: no way to open the log is configured (NotesPageKey, PauseMenuEntry and OpenKey are all off).");
            }

            if (_capture) Compat.Foreign(Vanilla.CreateNotification, "the notification log", "may miss what that mod hides");
            if (DialogueOn) Compat.Foreign(Vanilla.OnConversationLine, "the dialogue log", "may miss lines");
            if (_window)
            {
                Compat.Foreign(Vanilla.PauseNavUpdate, "the window's input hand-over", "may let the pause menu react underneath");
                Compat.Foreign(Vanilla.HandleBackButtonClick, "Back in the window", "may resume the game instead");
            }
            if (_notesKey) Compat.Foreign(Vanilla.GetButtonInfo, "the Notes page hint", "may not show");
            if (_search)
                foreach (var m in new MethodBase[] { Vanilla.GetKeyDown, Vanilla.GetAuxiliaryReloadDown, Vanilla.GetKeyDownKeyboardOnly, Vanilla.GetKey, Vanilla.GetKeyWithHold })
                    Compat.Foreign(m, "the search box's key block", "still applies (every guard only answers false)");
        }

        private void Update()
        {
            Guard("tracker", SlotTracker.Tick);
            Guard("access", NotesPageInput.Tick);
            Guard("pause entry", PauseEntry.Tick);
            Guard("window", LogWindow.Update);
        }

        private void OnGUI()
        {
            if (!LogWindow.IsOpen) return;
            try { LogWindow.Draw(Skin.Begin()); }
            catch (Exception e)
            {
                Log.Once("Window: draw", e);
                SearchGuard.ReleaseNow();
            }
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnSettingChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.PlayerWillChangeLevel -= OnPlayerWillChangeLevel;
            SaveController.PlayerWillLoadGame -= OnPlayerWillLoadGame;
            SaveController.SavingDone -= OnSavingDone;
            SaveController.LoadingStarted -= OnLoadingStarted;
            SaveController.LoadingDone -= OnLoadingDone;
            SearchGuard.ReleaseNow();
            InputBlock.Release();
        }
    }
}
