using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace UnofficialPatch
{
    // S-065 Achievement Doctor (spec §12; master switch OFF). Steam unlocks cannot be undone, so it only acts on
    // achievements the game itself marked as earned or whose condition it can recount, logs every submission, never
    // calls ClearAchievement, and wraps every Steam call.
    // (a) Snapshot: the first Achievements.Awake records each achievement's inspector defaults (enabled, progress,
    //     rewardsSent). Every scene carries a clean copy, so whichever Awake comes first is clean. Memory only.
    // (b) ResetOnNewGame: OnLoadingGameSpecial (SRC/Achievements.cs:300-319) copies a loaded save's values into the
    //     shared "original" objects and ResetValues (:402-413) restores from those, so *_ADDICTION_CURED stays enabled
    //     from the previous save and fires at character creation (#6227); counters such as BURGLAR_* carry over too.
    //     NewGame.StartNew restores the snapshot (both menu-return paths; the credits exit never calls ResetValues).
    // (c) ResubmitPending: Achievement.triggered calls SetAchievement once (:52-64); when it fails, _triggered stays
    //     true with _committedToAPI false and the game never retries (#6464). After a load, such achievements are
    //     checked against Steam and sent again.
    // (d) RecheckApartmentLevels: TENEMENT_ALL_APARTMENTS_LVLn is only checked when an upgrade completes
    //     (TenementUpgradeProgress.cs:216-219); after a load it is recounted through the game's own TriggerAchievement.
    // Prison Break is out of scope.
    internal static class AchievementDoctor
    {
        internal static bool Patched;

        private struct Defaults
        {
            internal bool Enabled, RewardsSent;
            internal float Progress;
        }

        private static Dictionary<string, Defaults> _snapshot;
        private static Coroutine _running;

        private const float TimeLimit = 60f, RetryEvery = 5f;

        internal static bool Live => Patched && Cfg.AchievementDoctor.Value;

        internal static void Snapshot(Achievements a)
        {
            if (!Patched || _snapshot != null || a == null || a.achievements == null) return;
            var snap = new Dictionary<string, Defaults>(StringComparer.Ordinal);
            foreach (var x in a.achievements)
            {
                if (x == null) continue;
                string id = Vanilla.AchId(x);
                if (string.IsNullOrEmpty(id) || snap.ContainsKey(id)) continue;
                snap[id] = new Defaults { Enabled = x.enabled, RewardsSent = x.rewardsSent, Progress = Vanilla.AchProgress(x) };
            }
            _snapshot = snap;
            Log.Verbose($"Achievements: recorded the defaults of {snap.Count} achievements.");
        }

        internal static void OnNewGame()
        {
            if (!Live || !Cfg.ResetOnNewGame.Value || _snapshot == null) return;
            var a = Achievements.instance;
            if (a == null || a.achievements == null) return;
            int changed = 0;
            var enabledAgain = new List<string>();
            var disabledAgain = new List<string>();
            foreach (var x in a.achievements)
            {
                if (x == null) continue;
                string id = Vanilla.AchId(x);
                if (id == null || !_snapshot.TryGetValue(id, out var d)) continue;
                bool differs = x.enabled != d.Enabled || x.rewardsSent != d.RewardsSent || Vanilla.AchProgress(x) != d.Progress
                               || Vanilla.AchTriggered(x) || Vanilla.AchCommitted(x);
                if (x.enabled != d.Enabled) (d.Enabled ? enabledAgain : disabledAgain).Add(id);
                // The list is aliased with the private "original" after any load, so this fixes both.
                x.enabled = d.Enabled;
                x.rewardsSent = d.RewardsSent;
                Vanilla.AchProgress(x) = d.Progress;
                Vanilla.AchTriggered(x) = false;
                Vanilla.AchCommitted(x) = false;
                if (differs) changed++;
            }
            if (changed == 0) return;
            string flags = (enabledAgain.Count > 0 ? "; enabled again: " + string.Join(", ", enabledAgain) : "")
                           + (disabledAgain.Count > 0 ? "; disabled again: " + string.Join(", ", disabledAgain) : "");
            Log.Info($"Achievements: new game, reset {changed} achievements left over from the previous save to the game's defaults{flags}.");
        }

        internal static void AfterLoad()
        {
            if (!Live || (!Cfg.ResubmitPending.Value && !Cfg.RecheckApartmentLevels.Value) || Plugin.Instance == null) return;
            if (_running != null) Plugin.Instance.StopCoroutine(_running);
            _running = Plugin.Instance.StartCoroutine(Run());
        }

        private static IEnumerator Run()
        {
            float start = Time.realtimeSinceStartup;
            // Wait for the game's own LoadAchievementsLate (initialized), a closed console (globalDisable) and Steam.
            while (true)
            {
                if (Time.realtimeSinceStartup - start > TimeLimit)
                {
                    Log.Warn("Achievements: the game or Steam was not ready within 60 seconds; nothing was checked this time.");
                    _running = null;
                    yield break;
                }
                if (Ready()) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (Cfg.ResubmitPending.Value)
            {
                var pending = Pending();
                foreach (var x in pending)
                {
                    // GetAchievement fails until Steam has the user's stats; retry every 5 s within the time limit.
                    while (true)
                    {
                        int r = Resubmit(x);
                        if (r >= 0) break;
                        if (Time.realtimeSinceStartup - start > TimeLimit)
                        {
                            Log.Warn($"Achievements: Steam did not answer for {Vanilla.AchId(x)}; it will be tried again after the next load.");
                            break;
                        }
                        yield return new WaitForSecondsRealtime(RetryEvery);
                    }
                }
            }

            if (Cfg.RecheckApartmentLevels.Value)
            {
                try { RecheckApartments(); }
                catch (Exception e) { Log.Exception("Achievements: apartment levels", e); }
            }
            _running = null;
        }

        private static bool Ready()
        {
            try
            {
                var a = Achievements.instance;
                return a != null && Vanilla.AchInitialized(a) && !a.globalDisable && Vanilla.SteamReady();
            }
            catch (Exception e)
            {
                Log.Exception("Achievements: ready check", e);
                return false;
            }
        }

        private static List<Achievements.Achievement> Pending()
        {
            var list = new List<Achievements.Achievement>();
            var a = Achievements.instance;
            if (a == null || a.achievements == null) return list;
            foreach (var x in a.achievements)
            {
                if (x == null || !x.enabled || !Vanilla.AchTriggered(x) || Vanilla.AchCommitted(x)) continue;
                string id = Vanilla.AchId(x);
                if (string.IsNullOrEmpty(id) || Cfg.NeverResubmitSet.Contains(id)) continue;
                list.Add(x);
            }
            return list;
        }

        // 1 = done (sent, already unlocked, or reported), 0 = gave up for this load, -1 = retry.
        private static int Resubmit(Achievements.Achievement x)
        {
            string id = Vanilla.AchId(x);
            try
            {
                if (Achievements.instance == null || Achievements.instance.globalDisable) return 0;
                if (!SteamUserStats.GetAchievement(id, out bool got)) return -1;
                if (got)
                {
                    Vanilla.AchCommitted(x) = true;
                    Log.Info($"Achievements: {id} is already unlocked on Steam; marked as sent.");
                    return 1;
                }
                if (Cfg.ReportOnly.Value)
                {
                    Log.Info($"Achievements: would submit {id} (the save marks it as earned but Steam never got it). ReportOnly is on, so nothing was sent.");
                    return 1;
                }
                if (!SteamUserStats.SetAchievement(id))
                {
                    Log.Warn($"Achievements: Steam refused {id}; it will be tried again after the next load.");
                    return 0;
                }
                SteamUserStats.StoreStats();
                Vanilla.AchCommitted(x) = true;
                Log.Info($"Achievements: submitted {id} to Steam (the save marks it as earned but Steam never got it).");
                return 1;
            }
            catch (Exception e)
            {
                Log.Exception("Achievements: Steam call for " + id, e);
                return 0;
            }
        }

        // TENEMENT_ALL_APARTMENTS_LVL1..4 through the game's own TriggerAchievement (keeps its enabled and globalDisable gates).
        private static void RecheckApartments()
        {
            var tc = TenementController.instance;
            var a = Achievements.instance;
            if (tc == null || a == null || a.achievements == null) return;
            if (tc.ignoreUpgradePricesAndItems || tc.completeUpgradesInstantly) return;   // saved cheat flags
            for (int n = 1; n <= 4; n++)
            {
                string id = "TENEMENT_ALL_APARTMENTS_LVL" + n;
                Achievements.Achievement x = null;
                foreach (var y in a.achievements)
                    if (y != null && Vanilla.AchId(y) == id) { x = y; break; }
                if (x == null || !x.enabled || Vanilla.AchTriggered(x)) continue;
                float max = Vanilla.AchMaxProgress(x);
                int count = tc.AllApartmentsLevelAtLevelCounter(n);
                if (max <= 0f || count < max) continue;
                if (Cfg.ReportOnly.Value)
                {
                    Log.Info($"Achievements: would unlock {id} ({count} of {max} apartments at level {n}). ReportOnly is on, so nothing was sent.");
                    continue;
                }
                Log.Info($"Achievements: {count} of {max} apartments are at level {n}; asking the game to unlock {id}.");
                a.TriggerAchievement(id, count, set: true);
            }
        }
    }

    // Achievements.Awake(), private: first call only.
    [HarmonyPatch]
    internal static class AchievementsAwakePatch
    {
        private static MethodBase TargetMethod() => Vanilla.AchAwake;

        private static void Prefix(Achievements __instance)
        {
            try { AchievementDoctor.Snapshot(__instance); }
            catch (Exception e) { Log.Exception("Achievements: snapshot", e); }
        }
    }

    // NewGame.StartNew().
    [HarmonyPatch(typeof(NewGame), nameof(NewGame.StartNew))]
    internal static class NewGameStartPatch
    {
        private static void Prefix()
        {
            try { AchievementDoctor.OnNewGame(); }
            catch (Exception e) { Log.Exception("Achievements: new game", e); }
        }
    }

    // Achievements.OnLoadingGameSpecial().
    [HarmonyPatch(typeof(Achievements), nameof(Achievements.OnLoadingGameSpecial))]
    internal static class AchievementsLoadPatch
    {
        private static void Postfix(bool __result)
        {
            if (!__result) return;
            try { AchievementDoctor.AfterLoad(); }
            catch (Exception e) { Log.Exception("Achievements: after load", e); }
        }
    }
}
