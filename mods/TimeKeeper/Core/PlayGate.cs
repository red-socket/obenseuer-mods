using System;

namespace TimeKeeper
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

        internal static GateResult Note(string message) => new GateResult(true, message, message);
    }

    // The free-gameplay part of the wait gate (spec §6.6 rows 1-4), adapted from Fewer Clicks' PlayGate. A missing core
    // singleton blocks (fails closed); the optional panels that GameController.Update itself null-checks (shop, building
    // menu, rotary lock, list panel, doctor, tenement) and Death count as closed when absent. Never calls
    // Prison.InPrison (it logs on every call) or any panel's Controls() (they read input).
    internal static class PlayGate
    {
        internal const string CantWait = "You can't wait right now.";

        // Row 1: no game running (main menu), loading, a level change or death. Silent.
        internal static GateResult Core()
        {
            if (SaveController.Loading) return GateResult.Silent("loading");
            if (Changelevel.ChangeLevelActive) return GateResult.Silent("changing level");
            if (Death.instance != null && Death.instance.dead) return GateResult.Silent("dead");
            if (GameController.instance == null) return GateResult.Silent("no game controller");
            if (GameUIController.instance == null) return GateResult.Silent("no game UI");
            if (WaitingController.instance == null) return GateResult.Silent("no waiting controller");
            if (WaitingUI.instance == null) return GateResult.Silent("no waiting clock");
            if (PlayerStats.instance == null) return GateResult.Silent("no player stats");
            if (TimeOfDayAzure.instance == null) return GateResult.Silent("no time of day");
            return GateResult.Clear;
        }

        // Rows 2-4: console, typing, pause (silent); busy with another clock or a sleep event (silent); menus, dialogue
        // and other panels (a note, or silent for the D-pad: menus use it for navigation, so a refusal there would flash
        // on every press).
        internal static GateResult Free(bool fromPad)
        {
            var gc = GameController.instance;
            if (Game.ConsoleOpen) return GateResult.Silent("console open");
            if (gc.writeLock) return GateResult.Silent("typing");
            if (Game.Paused) return GateResult.Silent("paused");

            var wc = WaitingController.instance;
            if (wc.waitingType != WaitingController.Type.None) return GateResult.Silent("already " + wc.waitingType);
            if (WaitingUI.instance.IsActive()) return GateResult.Silent("a clock is open");
            if (SleepEventController.instance != null && SleepEventController.instance.eventInProgress) return GateResult.Silent("sleep event");

            string busy = Busy();
            if (busy == null) return GateResult.Clear;
            return fromPad ? GateResult.Silent(busy) : GateResult.Note(CantWait);
        }

        // Dialogue only (the one row 4 check repeated at start; everything else needs the picker closed anyway).
        internal static bool InDialogue => DialogueController.instance != null && DialogueController.instance.DialogueActive;

        private static string Busy()
        {
            var gc = GameController.instance;
            if (gc.keysDisabled) return "controls disabled";
            if (gc.inventoryDisabled) return "inventory disabled";
            if (GameUIController.instance.IsMenuVisible) return "menu open";
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
            return null;
        }
    }
}
