using System;
using System.IO;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaveGuard
{
    // Every condition under which a timed autosave must not start (spec §11.3). Read only.
    // Unity objects are null-checked with != null (never ?.). A row that throws counts as blocking.
    internal static class SaveGate
    {
        private delegate string Row(bool full);

        // Each row returns null when clear, or the blocking reason.
        private static readonly (string name, Row row)[] Rows =
        {
            ("gameplay", Gameplay),
            ("destructibles", Destructibles),
            ("loading", LoadingRow),
            ("saving", SavingRow),
            ("save disabled", SaveDisabled),
            ("paused", Paused),
            ("dialogue", Dialogue),
            ("menus", Menus),
            ("minigames", Minigames),
            ("waiting", Waiting),
            ("police", Police),
            ("prison", PrisonRow),
            ("death", DeathRow),
            ("building", Building),
            ("seated", Seated),
            ("airborne", Airborne),
        };

        // full=false skips the pause/timeScale row and the "runner already running" check, which the runner's own
        // saving overlay trips (ShowSavingUI sets GameIsPaused and timeScale 0).
        internal static bool Check(bool full, out string why)
        {
            foreach (var (name, row) in Rows)
            {
                string r;
                try { r = row(full); }
                catch (Exception e)
                {
                    if (Log.Once("gate:" + name)) Log.Warn($"Timed autosave gate row '{name}' failed ({e.GetType().Name}: {e.Message}); treated as blocking.");
                    r = "gate error: " + name;
                }
                if (r != null) { why = r; return false; }
            }
            why = null;
            return true;
        }

        internal static bool InGameplay()
        {
            try { return Gameplay(true) == null; }
            catch (Exception) { return false; }
        }

        private static string Gameplay(bool full)
        {
            if (SceneManager.GetActiveScene().name == "Main Menu") return "main menu";
            if (GameController.instance == null) return "no game controller";
            if (PlayerIdentity.identity == null) return "no player";
            if (SaveController.instance == null) return "no save controller";
            return null;
        }

        // SaveGame dereferences DestructibleList.instance unguarded (SRC/SaveController.cs:523).
        private static string Destructibles(bool full) => DestructibleList.instance == null ? "no destructible list" : null;

        private static string LoadingRow(bool full)
        {
            if (SaveController.Loading) return "loading";
            if (Changelevel.ChangeLevelActive) return "changing level";
            if (LoadingScreen.instance != null && (LoadingScreen.instance.Fading || LoadingScreen.instance.FadeActive)) return "loading screen";
            if (TimeOfDayAzure.instance != null && TimeOfDayAzure.instance.updateTimeDisabled) return "time stopped";
            if (Time.realtimeSinceStartup < TimedAutosave.GraceUntil) return "just loaded";
            return null;
        }

        private static string SavingRow(bool full)
        {
            if (SaveTxn.Ctx != null) return "a save is running";
            if (full && TimedAutosave.Running) return "timed autosave running";
            if (File.Exists(Paths.SaveJournal)) return "unresolved save transaction";
            return null;
        }

        private static string SaveDisabled(bool full)
        {
            if (PauseMenu.instance != null && Vanilla.AnyAlive(Vanilla.GetSaveDisabledList(PauseMenu.instance))) return "saving disabled by the game";
            if (Cfg.RespectNoSaveDifficulty.Value && DifficultyController.instance != null && DifficultyController.instance.currentDifficultyInfo != null
                && DifficultyController.instance.currentDifficultyInfo.userSavesDisabled) return "difficulty disables saving";
            return null;
        }

        private static string Paused(bool full)
        {
            if (!full) return null;
            if (PauseMenu.instance != null && PauseMenu.instance.PauseIsActive()) return "pause menu";
            if (PauseMenu.paused) return "time scale 0";
            if (GameController.instance.GameIsPaused) return "game paused";
            return null;
        }

        private static string Dialogue(bool full)
        {
            if (DialogueController.instance != null && DialogueController.instance.DialogueActive) return "dialogue";
            if (DialogueManager.isConversationActive) return "conversation";
            return null;
        }

        private static string Menus(bool full)
        {
            if (GameController.instance.keysDisabled) return "player controls disabled";
            if (Vanilla.AnyAlive(Vanilla.GetDisabledList(GameController.instance))) return "game input disabled";
            if (GameUIController.instance != null && GameUIController.instance.IsMenuVisible) return "menu open";
            if (DeveloperConsole.Instance != null && DeveloperConsole.Instance.consoleOpened) return "console open";
            if (GuardWindow.AnyOpen) return "Save Guard window open";
            return null;
        }

        // Panels that GameController.Update hands input to before the pause menu (GameController.cs:69-130) and that
        // do not go through disabledList or keysDisabled. Lockpicking only calls ControlsDisabledMove and DisableGameMenu
        // (PlayerLockpicking.cs:49-51); vanilla can never save during it (the pause key cancels it first).
        private static string Minigames(bool full)
        {
            if (LockpickUI.instance != null && LockpickUI.instance.lockpicking) return "lockpicking";
            if (Active(RotaryLockUI.instance)) return "combination lock";
            if (Active(ListPanelUI.instance)) return "list panel";
            if (Active(DoctorPanelControls.instance)) return "doctor panel";
            var tc = TenementController.instance;
            if (tc != null && tc.upgradePanel != null && tc.upgradePanel.upgradeListPanel != null && tc.upgradePanel.upgradeListPanel.activeInHierarchy)
                return "tenement upgrades";
            return null;
        }

        private static bool Active(Component c) => c != null && c.gameObject.activeInHierarchy;

        private static string Waiting(bool full)
        {
            if (WaitingController.instance != null && WaitingController.instance.waitingType != WaitingController.Type.None) return "sleeping or waiting";
            if (SleepEventController.instance != null && SleepEventController.instance.eventInProgress) return "sleep event";
            return null;
        }

        private static string Police(bool full)
        {
            var crime = Crime.instance;
            if (crime == null) return null;
            if (crime.ArrestInProgress) return "arrest";
            var police = Vanilla.GetActivePolices(crime);
            if (police != null)
                foreach (var p in police)
                    if (p != null && p.IsPursuingPlayer) return "police pursuit";
            return null;
        }

        private static string PrisonRow(bool full)
        {
            if (Cfg.AllowInPrison.Value) return null;
            return TimedAutosave.InPrison() ? "in prison" : null;
        }

        private static string DeathRow(bool full) => Death.instance != null && Death.instance.dead ? "dead" : null;

        private static string Building(bool full)
        {
            if (BuildingSystemMenu.instance != null && BuildingSystemMenu.instance.menuIsOpen) return "building menu";
            // A save mid-move destroys the carried object (BuildingSystem.DestroyCurrentObject).
            if (BuildingSystem.instance != null && BuildingSystem.instance.IsMovingPlacedItem) return "moving a placed item";
            return null;
        }

        private static string Seated(bool full)
        {
            if (Cfg.AllowWhileSeated.Value) return null;
            if (InteractableChair.PlayerIsSitting) return "sitting";
            if (Vanilla.LadderClimbing) return "on a ladder";
            if (TrainController.instance != null && TrainController.instance.UsingMiniTrain()) return "on the mini train";
            return null;
        }

        private static string Airborne(bool full)
        {
            var cc = OpenSewerCharacterController.instance;
            return cc != null && !cc.isGrounded ? "airborne" : null;
        }
    }
}
