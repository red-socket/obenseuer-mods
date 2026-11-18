using System;
using UnityEngine;

namespace SafeTravels
{
    // The outcome of a gate: clear, blocked silently (the player is busy and knows it) or blocked with a note.
    internal readonly struct GateResult
    {
        internal readonly bool Blocked;
        internal readonly string Message;   // null: silent
        internal readonly string Reason;    // for the Verbose log

        private GateResult(bool blocked, string message, string reason)
        {
            Blocked = blocked;
            Message = message;
            Reason = reason;
        }

        internal static readonly GateResult Clear = new GateResult(false, null, null);

        internal static GateResult Silent(string reason) => new GateResult(true, null, reason);

        internal static GateResult Note(string message, string reason) => new GateResult(true, message, reason);
    }

    // The rows of spec §4.3 that Unstuck and Rerail share, adapted from Time Keeper's PlayGate. A missing core
    // singleton blocks (fails closed); optional panels count as closed when absent. Never calls Prison.InPrison (it logs
    // on every call) or any panel's Controls() (they read input).
    internal static class PlayGate
    {
        private static Prison _prison;
        private static bool _prisonSearched;
        private static FallDamage _fall;
        private static bool _fallSearched;

        // Called on sceneLoaded: the Prison and FallDamage are looked up again lazily.
        internal static void OnSceneLoaded()
        {
            _prison = null;
            _prisonSearched = false;
            _fall = null;
            _fallSearched = false;
        }

        internal static Prison CachedPrison
        {
            get
            {
                if (!_prisonSearched)
                {
                    _prisonSearched = true;
                    _prison = UnityEngine.Object.FindAnyObjectByType<Prison>();
                }
                return _prison;
            }
        }

        private static FallDamage CachedFall
        {
            get
            {
                if (!_fallSearched || (_fall == null && PlayerLocator.instance != null))
                {
                    _fallSearched = true;
                    var pl = PlayerLocator.instance;
                    _fall = pl != null ? pl.GetComponentInChildren<FallDamage>(true) : null;
                    if (_fall == null) _fall = UnityEngine.Object.FindAnyObjectByType<FallDamage>();
                }
                return _fall;
            }
        }

        // Core row: no game running (main menu), loading, a level change, a fade, the loading pause or death. Silent.
        internal static GateResult Core(bool needTrains = false)
        {
            if (SaveController.Loading) return GateResult.Silent("loading");
            if (Changelevel.ChangeLevelActive) return GateResult.Silent("changing level");
            if (LoadingScreen.instance != null && LoadingScreen.instance.Fading) return GateResult.Silent("fading");
            if (TimeOfDayAzure.instance == null) return GateResult.Silent("no time of day");
            if (TimeOfDayAzure.instance.updateTimeDisabled) return GateResult.Silent("level starting");
            if (Death.instance != null && Death.instance.dead) return GateResult.Silent("dead");
            if (GameController.instance == null) return GateResult.Silent("no game controller");
            if (PlayerLocator.instance == null) return GateResult.Silent("no player");
            if (OpenSewerCharacterController.instance == null) return GateResult.Silent("no character controller");
            if (PlayerLevelEntrypoints.instance == null) return GateResult.Silent("no entrypoints");
            if (needTrains && TrainController.instance == null) return GateResult.Silent("no train controller");
            return GateResult.Clear;
        }

        // Free row: console, typing, pause. Silent.
        internal static GateResult Free()
        {
            if (Game.ConsoleOpen) return GateResult.Silent("console open");
            if (Game.Typing) return GateResult.Silent("typing");
            if (Game.Paused) return GateResult.Silent("paused");
            if (UnityEngine.Time.timeScale == 0f) return GateResult.Silent("time stopped");
            return GateResult.Clear;
        }

        // Busy row: menus, dialogue, minigames, waits and anything that disables the player's controls or saving
        // (cutscenes add themselves to those lists). Returns the reason or null. `own` is the seat or ladder the player is
        // on (OwnLock()): its own movement lock and forced interactable are not a Busy reason, so the caller can show the
        // Vehicle note, while every other row (a menu, the clock, another lock in the list) still counts.
        internal static string Busy(GameObject own = null)
        {
            var gc = GameController.instance;
            if (gc.keysDisabled) return "controls disabled";
            if (gc.inventoryDisabled) return "inventory disabled";
            var occ = OpenSewerCharacterController.instance;
            if (Vanilla.OccDisabledList != null && Vanilla.LiveCount(Vanilla.OccDisabledList(occ), own) > 0) return "movement disabled";
            if (GameUIController.instance != null && GameUIController.instance.IsMenuVisible) return "menu open";
            if (DialogueController.instance == null) return "no dialogue controller";
            if (DialogueController.instance.DialogueActive) return "dialogue";
            if (ShopController.instance != null && ShopController.instance.menuIsOpen) return "shop or crafting";
            if (BuildingSystemMenu.instance != null && BuildingSystemMenu.instance.menuIsOpen) return "building";
            if (BuildingSystem.instance != null && BuildingSystem.instance.IsMovingPlacedItem) return "moving an object";
            if (LockpickUI.instance != null && LockpickUI.instance.lockpicking) return "lockpicking";
            if (RotaryLockUI.instance != null && RotaryLockUI.instance.gameObject.activeInHierarchy) return "rotary lock";
            if (ListPanelUI.instance != null && ListPanelUI.instance.gameObject.activeInHierarchy) return "list panel";
            if (DoctorPanelControls.instance != null && DoctorPanelControls.instance.gameObject.activeInHierarchy) return "doctor panel";
            var tc = TenementController.instance;
            if (tc != null && tc.upgradePanel != null && tc.upgradePanel.upgradeListPanel != null && tc.upgradePanel.upgradeListPanel.activeInHierarchy)
                return "tenement upgrades";
            if (TradePanel.instance != null && TradePanel.instance.activeTrade != null) return "trade";
            if (HoldToUseController.instance != null && HoldToUseController.instance.IsUpdating) return "holding to use";
            var forced = InteractObjects.instance != null ? InteractObjects.instance.savedForceInteractable : null;
            if (forced != null && (own == null || forced.gameObject != own)) return "forced interaction";
            if (PauseMenu.instance != null && Vanilla.SaveDisabledList != null && Vanilla.LiveCount(Vanilla.SaveDisabledList(PauseMenu.instance), null) > 0)
                return "saving disabled";
            if (WaitingController.instance != null && WaitingController.instance.waitingType != WaitingController.Type.None) return "waiting";
            if (WaitingUI.instance != null && WaitingUI.instance.IsActive()) return "a clock is open";
            if (SleepEventController.instance != null && SleepEventController.instance.eventInProgress) return "sleep event";
            return null;
        }

        // Vehicle row: riding the minitrain, seated, on a ladder, parented to something (an elevator cabin, a vehicle)
        // or standing on a train. Returns the reason or null. Riding comes first: the minitrain's seat is an
        // InteractableChair (minitrain_001_usable.prefab), so a rider is also seated.
        internal static string Vehicle()
        {
            if (Riding) return "riding the minitrain";
            if (InteractableChair.PlayerIsSitting) return "seated";
            if (Vanilla.Climbing) return "on a ladder";
            var pl = PlayerLocator.instance;
            if (pl.transform.parent != pl.root) return "parented";
            if (OnTrain) return "standing on a train";
            return null;
        }

        // The seat, minitrain seat or ladder the player is on, or null. InteractableChair.Sit and InteractableLadder put
        // their own object in the OCC disabledList (ControlsDisabledMove, InteractableChair.cs:384,
        // InteractableLadder.cs:309) and set InteractObjects.savedInteractable (only they set it); the ladder also sets
        // the forced interactable. Passed to Busy() so those locks alone don't hide the Vehicle note ("Get up first."
        // etc.). With ladder false a ladder gives null (Rerail has no ladder row, so its lock must keep blocking).
        internal static GameObject OwnLock(bool ladder = true)
        {
            var io = InteractObjects.instance;
            var si = io != null ? io.savedInteractable : null;
            if (si == null) return null;
            if (si is InteractableChair && InteractableChair.PlayerIsSitting) return si.gameObject;
            if (ladder && si is InteractableLadder && Vanilla.Climbing) return si.gameObject;
            return null;
        }

        // The note for a Vehicle() reason: each says what to get off. Parented (an elevator cabin or another moving
        // parent) gets the general note.
        internal static string VehicleNote(string reason)
        {
            switch (reason)
            {
                case "seated": return Strings.Seated;
                case "on a ladder": return Strings.Ladder;
                case "riding the minitrain":
                case "standing on a train": return Strings.OffTrain;
                default: return Strings.Busy;
            }
        }

        internal static bool Riding
        {
            get
            {
                var tc = TrainController.instance;
                return tc != null && tc.UsingMiniTrain();
            }
        }

        // The ground under the player belongs to a train.
        internal static bool OnTrain
        {
            get
            {
                var occ = OpenSewerCharacterController.instance;
                var col = occ != null && occ.movement != null ? occ.movement.groundCollider : null;
                return col != null && col.GetComponentInParent<TrackTrain>() != null;
            }
        }

        internal static bool Jailed
        {
            get
            {
                var p = CachedPrison;
                return p != null && Vanilla.PrisonInPrison != null && Vanilla.PrisonInPrison(p);
            }
        }

        // An arrest is running or an officer pursues the player (off-scene officers following the player count too).
        internal static bool Police
        {
            get
            {
                var crime = Crime.instance;
                if (crime == null) return false;
                if (crime.ArrestInProgress) return true;
                var list = Vanilla.CrimeActivePolices != null ? Vanilla.CrimeActivePolices(crime) : null;
                if (list == null) return false;
                foreach (var p in list)
                {
                    try { if (p != null && p.IsPursuingPlayer) return true; }
                    catch (Exception) { }
                }
                return false;
            }
        }

        internal static bool Swimming
        {
            get
            {
                var w = WaterController.instance;
                return w != null && w.playerIsSwimming;
            }
        }

        // FallDamage is counting air time (and the player is not swimming), or the player drops fast without ground.
        internal static bool Falling
        {
            get
            {
                if (Swimming) return false;
                var f = CachedFall;
                if (f != null && Vanilla.FallAirtime != null && Vanilla.FallAirtime(f) > 0f) return true;
                var occ = OpenSewerCharacterController.instance;
                var m = occ != null ? occ.movement : null;
                return m != null && !m.isGrounded && m.velocity.y < -1f;
            }
        }
    }
}
