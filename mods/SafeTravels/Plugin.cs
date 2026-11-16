using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SafeTravels
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.safetravels";
        public const string Name = "Safe Travels";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();
        private static bool _compatReported;

        // Each feature is patched as its own group (spec §2), only when it is on at launch. A group checks the game
        // members it needs first; a missing member or a patch exception unpatches that group's own targets, logs one
        // error and leaves the other groups working. Every patch body also re-reads its config entry live. Unstuck and
        // Rerail patch nothing: their groups only check the members they use, and they follow their Enabled entry live.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // U-006 / S-039 (spec §3.1-3.2). The price override needs no patch and works even if this group fails.
            TollSlot.Patched = PatchGroup("toll", Cfg.Toll.Enabled.Value,
                Needs(Vanilla.MoneyInteract, Vanilla.MoneyUseMessage),
                Targets(Vanilla.MoneyInteract, Vanilla.MoneyUseMessage), typeof(MoneyInteractPatch), typeof(MoneyUseMessagePatch));

            // S-021 (spec §3.3-3.9).
            TrainWatch.Patched = PatchGroup("trainwatch", Cfg.Coupling.Enabled.Value || Cfg.Consist.Enabled.Value || Cfg.Rerail.Enabled.Value,
                Needs(Vanilla.TrainOnLoadingGame, Vanilla.UncoupleIfTooFarAfterLoad),
                Targets(Vanilla.TrainOnLoadingGame, Vanilla.UncoupleIfTooFarAfterLoad), typeof(TrainOnLoadingGamePatch), typeof(UncoupleAfterLoadPatch));
            CouplingRules.Patched = PatchGroup("coupling", Cfg.Coupling.Enabled.Value,
                Needs(Vanilla.CouplerEnter, Vanilla.CouplerStay, Vanilla.CouplerTest),
                Targets(Vanilla.CouplerEnter, Vanilla.CouplerStay), typeof(CouplerEnterPatch), typeof(CouplerStayPatch));
            GetDistancePatch.Patched = PatchGroup("decoupling", Cfg.Decoupling.Enabled.Value,
                Needs(Vanilla.CouplerGetDistance), Targets(Vanilla.CouplerGetDistance), typeof(GetDistancePatch));
            Consist.Patched = PatchGroup("consist", Cfg.Consist.Enabled.Value && TrainWatch.Patched,
                Needs(Vanilla.CouplerRelease, Vanilla.CouplerCouple, Vanilla.Rebuild),
                Targets(Vanilla.CouplerRelease, Vanilla.CouplerCouple), typeof(CouplerReleasePatch), typeof(CouplerCouplePatch));
            if (Cfg.Consist.Enabled.Value && !TrainWatch.Patched)
                Log.Warn("Consist: off this session (the train load hooks failed).");

            // S-030 (spec §4.2).
            Unstuck.Available = FeatureGroup("unstuck", Cfg.Unstuck.Enabled.Value,
                Needs(Vanilla.PlayerTeleport, Vanilla.FallAirtime, Vanilla.PrisonInPrison, Vanilla.CrimeActivePolices,
                    Vanilla.LadderClimbing, Vanilla.OccDisabledList, Vanilla.SaveDisabledList));
            // S-021 d (spec §4.8).
            Rerail.Available = FeatureGroup("rerail", Cfg.Rerail.Enabled.Value,
                Needs(Vanilla.ResetTrainPosition, Vanilla.TrainOrigFrontPos, Vanilla.TrainOrigBackPos, Vanilla.TrainOrigFrontDir, Vanilla.TrainOrigBackDir));

            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.LoadingStarted += OnLoadingStarted;
            SaveController.LoadingDone += OnLoadingDone;
            SaveController.PlayerWillChangeLevel += OnPlayerWillChangeLevel;

            Log.Info($"{Name} {Version} on game {Application.version}; on: {List(_on)}; off: {List(_off)}; failed: {List(_failed)}");
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

        // A feature with no patches: available when every member it uses resolved, whatever its Enabled entry says now.
        private bool FeatureGroup(string name, bool enabled, object[] needs)
        {
            foreach (var n in needs)
                if (n == null)
                {
                    Log.Error($"'{name}' is off this session: a game member it needs is missing (see the error above).");
                    _failed.Add(name);
                    return false;
                }
            (enabled ? _on : _off).Add(name);
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

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Every door is a full single-scene reload in 0.5.19; an additive scene that is not the active one (only a
            // test map preloads that way) leaves our per-scene state alone.
            if (mode == LoadSceneMode.Additive && scene != SceneManager.GetActiveScene()) return;
            Step("scene (gate)", PlayGate.OnSceneLoaded);
            Step("scene (toll)", () => TollSlot.OnSceneLoaded(scene));
            Step("scene (spots)", SafeSpots.OnSceneLoaded);
            Step("scene (unstuck)", Unstuck.OnSceneLoaded);
            Step("scene (trains)", TrainWatch.OnSceneLoaded);
            Step("scene (consist)", Consist.OnSceneLoaded);
            Step("scene (rerail)", Rerail.OnSceneLoaded);
            Step("scene (pause)", PauseButtons.OnSceneLoaded);
            Step("scene (keys)", Keys.CheckCollisions);
            if (!_compatReported)
            {
                _compatReported = true;
                Step("compat", ReportCompat);
            }
        }

        private static void OnLoadingStarted() => Step("loading started", () => SafeSpots.Clear("loading"));

        private static void OnLoadingDone() => Step("loading done", TrainWatch.OnLoadingDone);

        private static void OnPlayerWillChangeLevel() => Step("level change", () => SafeSpots.Clear("level change"));

        // Other mods' skipping patches on what we patch or call (spec §3.10), once all plugins have loaded.
        private static void ReportCompat()
        {
            if (TollSlot.Patched)
            {
                Compat.Foreign(Vanilla.MoneyInteract, "the toll lock", "may not stop a second payment");
                Compat.Overlap(Vanilla.MoneyUseMessage, "the paid prompt");
            }
            if (CouplingRules.Patched)
            {
                Compat.Foreign(Vanilla.CouplerEnter, "the coupling rules", "may not apply");
                Compat.Foreign(Vanilla.CouplerStay, "the coupling lever check", "may not apply");
            }
            if (GetDistancePatch.Patched) Compat.Foreign(Vanilla.CouplerGetDistance, "the decoupling grace", "may not apply");
            if (Consist.Patched)
            {
                Compat.Overlap(Vanilla.CouplerRelease, "the consist check");
                Compat.Overlap(Vanilla.CouplerCouple, "the consist check");
                Compat.Foreign(Vanilla.Rebuild, "the consist repair", "may not run as described");
            }
            if (TrainWatch.Patched)
            {
                Compat.Overlap(Vanilla.TrainOnLoadingGame, "the train load tracking");
                Compat.Overlap(Vanilla.UncoupleIfTooFarAfterLoad, "the train load tracking");
            }
            if (Unstuck.Available) Compat.Foreign(Vanilla.PlayerTeleport, "Unstuck", "may not move you");
            if (Rerail.Available)
            {
                Compat.Foreign(Vanilla.ResetTrainPosition, "Rerail", "may not move the train");
                Compat.Foreign(Vanilla.UncoupleWagons, "Rerail", "may not uncouple the wagons first");
            }
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            PlayClock.Tick();
            Step("key notes", Keys.ShowPending);
            Step("toll", TollSlot.Tick);
            Step("spots", SafeSpots.Tick);
            Step("unstuck", Unstuck.Tick);
            Step("trains", TrainWatch.Tick);
            Step("rerail", Rerail.Tick);
            Step("pause entries", PauseButtons.Tick);
        }

        // The consist check runs after every train's Update and the physics of this frame.
        private void LateUpdate()
        {
            Step("consist", Consist.LateTick);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.LoadingStarted -= OnLoadingStarted;
            SaveController.LoadingDone -= OnLoadingDone;
            SaveController.PlayerWillChangeLevel -= OnPlayerWillChangeLevel;
        }
    }
}
