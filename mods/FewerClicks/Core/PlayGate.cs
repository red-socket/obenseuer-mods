using System;

namespace FewerClicks
{
    // The free-gameplay gate for Quick Eat, Auto Eat and the slot keys (spec §2 P4). P4 already runs after every early
    // return in GameController.Update; this repeats those rows and adds the ones Update does not cover (trade,
    // console, card game, drinking contest, building RemoveOnly mode). Returns null when clear, otherwise the reason.
    // A missing core singleton blocks (fails closed); the optional panels that GameController.Update itself null-checks
    // (shop, building menu, rotary lock, list panel, doctor, tenement) and Death count as closed when absent. Rows
    // mirror Save Guard's SaveGate. Prison, the minitrain and sitting
    // are not blocked: the game lets you eat there. Never calls Prison.InPrison (it logs on every call) or any
    // panel's Controls() (they read input).
    internal static class PlayGate
    {
        internal static string Blocked()
        {
            try
            {
                if (SaveController.Loading) return "loading";
                if (Changelevel.ChangeLevelActive) return "changing level";
                if (Death.instance != null && Death.instance.dead) return "dead";   // optional, as in PauseMenu (:191)

                var gc = GameController.instance;
                if (gc == null) return "no game controller";
                if (gc.GameIsPaused) return "paused";
                if (gc.keysDisabled) return "controls disabled";
                if (gc.writeLock) return "typing";
                if (gc.inventoryDisabled) return "inventory disabled";

                if (PauseMenu.instance == null) return "no pause menu";
                if (PauseMenu.instance.PauseIsActive()) return "pause menu";
                if (Game.ConsoleOpen) return "console open";
                if (GameUIController.instance == null) return "no game UI";
                if (GameUIController.instance.IsMenuVisible) return "menu open";
                if (DialogueController.instance == null) return "no dialogue controller";
                if (DialogueController.instance.DialogueActive) return "dialogue";
                if (ShopController.instance != null && ShopController.instance.menuIsOpen) return "shop or crafting";
                if (BuildingSystemMenu.instance != null && BuildingSystemMenu.instance.menuIsOpen) return "building";
                if (LockpickUI.instance != null && LockpickUI.instance.lockpicking) return "lockpicking";
                if (RotaryLockUI.instance != null && RotaryLockUI.instance.gameObject.activeInHierarchy) return "rotary lock";
                if (ListPanelUI.instance != null && ListPanelUI.instance.gameObject.activeInHierarchy) return "list panel";
                if (DoctorPanelControls.instance != null && DoctorPanelControls.instance.gameObject.activeInHierarchy) return "doctor panel";
                var tc = TenementController.instance;
                if (tc != null && tc.upgradePanel != null && tc.upgradePanel.upgradeListPanel != null && tc.upgradePanel.upgradeListPanel.activeInHierarchy)
                    return "tenement upgrades";
                if (WaitingController.instance != null && WaitingController.instance.waitingType != WaitingController.Type.None) return "waiting";
                if (PlayerHandItems.instance == null) return "no hands";
                if (PlayerHandItems.instance.itemUseDisabled) return "item use disabled";
                if (Game.TradeActive) return "trade";
                if (PlayerStats.instance == null) return "no player stats";
                if (PlayerStats.instance.StatsProgressDisabled) return "stats paused";
                return null;
            }
            catch (Exception e)
            {
                Log.Exception("play gate", e);
                return "gate error";
            }
        }
    }
}
