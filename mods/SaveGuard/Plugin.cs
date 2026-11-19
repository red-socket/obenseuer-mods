using System;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaveGuard
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.saveguard";
        public const string Name = "Save Guard";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        private Harmony _harmony;

        // Startup order (spec §1.1): every step runs in its own try/catch, so one failure never stops the others.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Step("paths", Paths.Init);
            if (Paths.Root == null || Paths.Backups == null)
            {
                Log.Error("Save Guard could not set up its folder and is inactive.");
                enabled = false;
                return;
            }
            Step("restore recovery", RestoreService.StartupRecover);
            Step("save recovery", SaveTxn.StartupRecover);
            Step("pre-update backup", PreUpdate.RunIfNeeded);
            Step("prune", BackupStore.PruneAll);

            _harmony = new Harmony(Guid);
            bool crashSafety = PatchGroup("crash safety and backups", typeof(SaveGamePatch), typeof(IsSameFolderPatch));
            bool loadPrompt = PatchGroup("damaged-load prompt", typeof(LoadMenuPatch));
            // Without it the in-game prompt still works; only the pause menu's pad input is not paused under it.
            PatchGroup("in-game prompt input", typeof(PauseNavigationPatch));
#if SAVEGUARD_FAULTS
            PatchGroup("fault injection (test build)", typeof(Faults.WriteSaveFilePatch));
            Faults.Hook();
#endif
            TimedAutosave.Init(this);
            SceneManager.sceneLoaded += OnSceneLoaded;
            // If the chainloader started after the main menu scene had loaded, run its scan now.
            if (SceneManager.GetActiveScene().name == "Main Menu") OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

            var patched = _harmony.GetPatchedMethods().Select(m => m.DeclaringType?.Name + "." + m.Name).ToArray();
            Log.Info($"{Name} {Version} on game {Application.version}; folder {Paths.Root}" +
                     (Paths.SameDir(Paths.BackupBase, Paths.Root) ? "" : $"; backups in {Paths.BackupBase}") +
                     $"; patched {patched.Length}: {string.Join(", ", patched)}" +
                     (crashSafety ? "" : "; crash safety and backups OFF") + (loadPrompt ? "" : "; damaged-load prompt OFF"));
        }

        private static void Step(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Error($"Startup step '{name}' failed: {e}"); }
        }

        // Patches the given classes of this assembly; a failure turns that feature off for the session.
        private bool PatchGroup(string name, params Type[] types)
        {
            try
            {
                foreach (var t in types) _harmony.CreateClassProcessor(t).Patch();
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Patching '{name}' failed; that feature is off this session: {e}");
                return false;
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try { GuardWindow.OnSceneLoaded(scene); }
            catch (Exception e) { Log.Exception("sceneLoaded", e); }
            try { TimedAutosave.OnSceneLoaded(scene.name); }
            catch (Exception e) { Log.Exception("sceneLoaded (timed autosave)", e); }
        }

        private void Update()
        {
            try { BackupStore.FlushPending(); }
            catch (Exception e) { Log.Exception("backup", e); }
            try { TimedAutosave.Update(); }
            catch (Exception e) { Log.Exception("timed autosave", e); }
            try { GuardWindow.ControllerUpdate(); }
            catch (Exception e) { Log.Exception("prompt controller input", e); }
            try { InputBlock.Update(); }
            catch (Exception e) { Log.Exception("input block cursor", e); }
            try
            {
                if (Cfg.ToggleWindow.Value.IsDown() && !(DeveloperConsole.Instance != null && DeveloperConsole.Instance.consoleOpened))
                    GuardWindow.OnHotkey();
            }
            catch (Exception e) { Log.Exception("hotkey", e); }
        }

        private void OnGUI()
        {
            try { GuardWindow.OnGUI(); }
            catch (Exception e)
            {
                // GUILayout throws ExitGUIException on purpose; it must propagate.
                if (e is ExitGUIException) throw;
                Log.Exception("OnGUI", e);
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            InputBlock.Release();
        }
    }
}
