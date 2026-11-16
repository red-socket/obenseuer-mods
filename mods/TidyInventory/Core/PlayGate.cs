using System;

namespace TidyInventory
{
    // The "a level is running" gate for our keys (spec §3.6, §6), adapted from Safe Travels' core row: a level scene
    // with the inventory and Game Logic, no save loading or level change, and the level started (LoadingDone seen
    // this scene, or the clock running for a scene that never sends LoadingDone). Our keys only act inside menus, so
    // the menu, dialogue and minigame rows of the other gates do not apply; console and typing are checked by
    // Keys.Down. Fails closed.
    internal static class PlayGate
    {
        private static bool _loadingDoneSeen;

        internal static void OnSceneLoaded() => _loadingDoneSeen = false;

        internal static void OnLoadingDone() => _loadingDoneSeen = true;

        internal static bool Open => Blocked() == null;

        internal static string Blocked()
        {
            try
            {
                if (SaveController.Loading) return "loading";
                if (Changelevel.ChangeLevelActive) return "changing level";
                if (GlobalState.instance == null) return "no game running";
                if (Inventory.instance == null) return "no inventory";
                if (GameController.instance == null) return "no game controller";
                if (!_loadingDoneSeen)
                {
                    var t = TimeOfDayAzure.instance;
                    if (t == null || t.updateTimeDisabled) return "level starting";
                }
                if (Death.instance != null && Death.instance.dead) return "dead";
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
