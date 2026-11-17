using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GreenThumb
{
    // S-060 (spec §6, D5-D7, D9, D20). Growing.OnPowerOut has its logic inverted (SRC/Growing.cs:723-735): a grower that
    // needs power never pauses, and an AlwaysOn one pauses on the rare Break/broken paths and, since OnPowerOn never
    // unpauses AlwaysOn growers (:737-753), stays paused for ever (Growing.paused and grow.Paused are saved).
    //
    // P1/P2 follow the callbacks; P3/P4 block the AlwaysOn pause at the source and hold every pause and unpause while a
    // save or door is loading (Window). After the offline catch-up has run under the saved pause state, the settle
    // coroutine releases the hold and reconciles every grower with its real power (D6). A grower touched by a power
    // callback is reconciled again on the next frame (FlushDirty), because the callbacks fire before PowerSource and
    // CraftingBase store the new state (PowerSource.ToggleInput, CraftingBase.Toggle :1633).
    //
    // Saves: only the vanilla bools Growing.paused and grow.Paused (plus the unused GrowOnlyDuringDay that vanilla
    // PauseGrowth writes). After removal, vanilla's own LoadDelay -> UpdateState -> OnPowerOn resumes powered growers.
    internal static class PowerFix
    {
        internal static bool Patched;

        // True from PlayerWillLoadGame / PlayerWillChangeLevel / sceneLoaded until the settle has run.
        internal static bool Holding;

        private static readonly HashSet<Growing> Dirty = new HashSet<Growing>();
        private static readonly List<Growing> Scratch = new List<Growing>();
        private static Coroutine _settle;

        private const float SettleTimeout = 60f;

        internal static bool Window
        {
            get
            {
                if (Holding || SaveController.Loading) return true;
                var t = TimeOfDayAzure.instance;
                return t == null || t.updateTimeDisabled;
            }
        }

        internal static void MarkDirty(Growing g)
        {
            if (g != null) Dirty.Add(g);
        }

        // PlayerWillLoadGame, PlayerWillChangeLevel. The settle starts right away too: a ChangeLevelSame or a failed load
        // never sends sceneLoaded, and the settle waits until loading is over anyway (a real door keeps
        // SaveController.Loading set until its own sceneLoaded restarts the settle).
        internal static void Hold()
        {
            if (!Patched) return;
            Holding = true;
            BeginSettle();
        }

        internal static void OnSceneLoaded()
        {
            if (!Patched) return;
            Dirty.Clear();
            Holding = true;
            BeginSettle();
        }

        // sceneLoaded (fresh scenes never send LoadingDone) and LoadingDone: (re)start the one settle coroutine.
        internal static void BeginSettle()
        {
            if (!Patched || Plugin.Instance == null) return;
            if (_settle != null) Plugin.Instance.StopCoroutine(_settle);
            _settle = Plugin.Instance.StartCoroutine(Settle());
        }

        private static IEnumerator Settle()
        {
            float start = Time.unscaledTime;
            bool timedOut = false;
            while (SaveController.Loading || TimeOfDayAzure.instance == null || TimeOfDayAzure.instance.updateTimeDisabled)
            {
                if (Time.unscaledTime - start > SettleTimeout)
                {
                    timedOut = true;
                    break;
                }
                yield return null;
            }
            if (!timedOut)
            {
                // Past grow.LoadDelay's offline catch-up and CraftingBase.LoadDelay's UpdateState (both: clock running,
                // then two end-of-frames), PowerGrid.StartLate (6 frames) and the tenement's LoadingDone re-parenting.
                for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();
                for (int i = 0; i < 8; i++) yield return null;
            }
            _settle = null;
            Holding = false;
            if (timedOut)
            {
                if (SceneManager.GetActiveScene().name == "Main Menu" || GlobalState.instance == null) yield break;
                Log.WarnOnce("settle timeout", "Power: the level didn't finish loading within 60 s; growers are matched to their power now.");
            }
            Plugin.Step("power settle", SettleAll);
        }

        private static void SettleAll()
        {
            Dirty.Clear();
            if (!Cfg.Hot.Power) return;   // switched off at runtime: vanilla behaviour, nothing to match
            int paused = 0, resumed = 0;
            foreach (var g in Object.FindObjectsByType<Growing>(FindObjectsSortMode.None))
            {
                int r = Reconcile(g);
                if (r > 0) paused++;
                else if (r < 0) resumed++;
            }
            if (paused + resumed > 0) Log.Info($"Power: {paused} growers paused (no power), {resumed} resumed");
        }

        // Plugin.Update: growers touched by a power callback, once the state they report is final.
        internal static void FlushDirty()
        {
            if (Dirty.Count == 0 || Window) return;
            Scratch.Clear();
            Scratch.AddRange(Dirty);
            Dirty.Clear();
            int paused = 0, resumed = 0;
            foreach (var g in Scratch)
            {
                int r = Reconcile(g);
                if (r > 0) paused++;
                else if (r < 0) resumed++;
            }
            Scratch.Clear();
            if (paused + resumed > 0) Log.Verbose($"Power: {paused} growers paused, {resumed} resumed");
        }

        // Matches one grower (and each of its plants) to its real power. Never called inside a power callback or while
        // Window is set. Returns 1 when it paused the grower, -1 when it resumed it, 0 otherwise.
        internal static int Reconcile(Growing g)
        {
            if (g == null || !g.isActiveAndEnabled) return 0;
            bool want = Cfg.Hot.Power && !g.AlwaysOn && !g.PoweredAndActive;
            int changed = 0;
            if (want && !g.paused)
            {
                g.PauseGrowth();
                if (g.paused) changed = 1;
            }
            else if (!want && g.paused)
            {
                g.UnpauseGrowth();
                if (!g.paused) changed = -1;
            }
            // PauseGrowth/UnpauseGrowth return early on the station flag, and a rotting plant's next stage is added by
            // GrowingSpot.AddPlant without PlantExternal (SRC/grow.cs:673-677), so a plant can disagree with its grower.
            var spots = g.GrowingSlots;
            if (spots != null)
                for (int i = 0; i < spots.Count; i++)
                {
                    var spot = spots[i];
                    if (spot == null || spot.Plant == null) continue;
                    var p = spot.Plant.GetComponent<grow>();   // the lookup PauseGrowth uses
                    if (p != null && p.Paused != g.paused) p.Paused = g.paused;
                }
            return changed;
        }

        // Power.PauseWithoutPower changed at runtime (group patched). Off: back to vanilla, where a grower that needs
        // power never pauses. On: reconcile everything.
        internal static void OnSwitchChanged()
        {
            if (!Patched) return;
            if (!Cfg.Hot.Power)
            {
                int n = 0;
                foreach (var g in Object.FindObjectsByType<Growing>(FindObjectsSortMode.None))
                {
                    if (g == null || g.AlwaysOn || !g.paused) continue;
                    g.UnpauseGrowth();
                    if (!g.paused) n++;
                }
                Log.Info($"Power: fix turned off; {n} growers resumed");
                return;
            }
            if (!Window) SettleAll();
        }
    }
}
