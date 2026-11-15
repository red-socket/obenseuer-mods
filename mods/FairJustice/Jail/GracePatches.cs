using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FairJustice
{
    // Cell-door grace (#5847, spec §3.3B) and release grace (#6183/#4405, spec §3.4A). While a grace runs, the listed
    // TrespassingAreas and (after a release) police sightings do nothing; the saved trespass fields are never written,
    // so the next physics step after the grace detects normally. Any CommitCrime ends both graces at once.
    internal static class Graces
    {
        internal static bool TrespassPatched, PolicePatched, EndPatched;

        // Verbose "skipped" lines, once per area per grace.
        private static readonly HashSet<string> SkipLogged = new HashSet<string>();
        internal static void ResetSkipLog() => SkipLogged.Clear();

        private static MethodBase _triggerStay, _detected0, _detected2, _police;
        internal static MethodBase TriggerStayTarget
        {
            get
            {
                if (_triggerStay != null) return _triggerStay;
                var m = AccessTools.Method(typeof(TrespassingArea), "OnTriggerStay");
                if (m == null || m.GetParameters().Length != 1) throw new MissingMethodException("TrespassingArea.OnTriggerStay(Collider) not found");
                return _triggerStay = m;
            }
        }
        internal static MethodBase Detected0Target =>
            _detected0 ?? (_detected0 = AccessTools.Method(typeof(TrespassingArea), nameof(TrespassingArea.OnPlayerDetected), Type.EmptyTypes));
        internal static MethodBase Detected2Target =>
            _detected2 ?? (_detected2 = AccessTools.Method(typeof(TrespassingArea), nameof(TrespassingArea.OnPlayerDetected),
                new[] { typeof(GameObject), typeof(NPC.NPCReference) }));
        internal static MethodBase PoliceTarget =>
            _police ?? (_police = AccessTools.Method(typeof(NPC.Act_Police), nameof(NPC.Act_Police.OnPlayerDetected),
                new[] { typeof(GameObject), typeof(NPC.NPCReference) }));

        // P15-P17 body after the AnyGrace check. True = skip this detection.
        internal static bool SkipTrespass(TrespassingArea a, MethodBase target)
        {
            if (!TrespassPatched || a == null) return false;
            string guid = a.GUID;
            if (string.IsNullOrEmpty(guid)) return false;
            string which = null;
            if (Jail.ImprisonGraceLeft > 0f && Cfg.CellDoorGrace.Value && a.crimeType == Crime.CrimeType.Trespassing && Cfg.CellDoorAreas.Contains(guid))
                which = "cell-door";
            else if (Jail.ReleaseGraceLeft > 0f && Cfg.ReleaseGrace.Value && Cfg.ReleaseGraceTrespass.Value && Cfg.ReleaseAreas.Contains(guid))
                which = "release";
            if (which == null) return false;
            Compat.Overlap(target, "the jail grace");
            if (SkipLogged.Add(guid)) Log.Verbose($"Trespass area {a.name} ({guid}) skipped during the {which} grace.");
            return true;
        }

        // P18 body after the AnyGrace check.
        internal static bool SkipPolice()
        {
            if (!PolicePatched || Jail.ReleaseGraceLeft <= 0f || !Cfg.ReleaseGrace.Value || !Cfg.ReleaseGracePolice.Value) return false;
            if (!Cfg.PoliceAnyScene.Value)
            {
                var logic = info_game_logic.instance;
                if (logic == null || SceneManager.GetActiveScene().name != logic.prisonLevelName) return false;
            }
            // The grace only covers stale chase state, never carried loot: if vanilla's own on-sight roll could find
            // anything (Act_Police.cs:358, same thresholds, no victim filter, so never less), vanilla runs.
            if (HasSomethingToQuestion()) return false;
            Compat.Overlap(PoliceTarget, "the release grace");   // Probable Cause (Nexus #46) is expected here
            if (SkipLogged.Add("police")) Log.Verbose("Police sighting ignored during the release grace.");
            return true;
        }

        // CountCrimes(countStolenGoods: true, 100, 10) as Act_Police.OnPlayerDetected calls it, at most once per frame
        // (it walks every inventory slot). It does not log. No Crime instance: let vanilla run.
        private static int _questionFrame = -1;
        private static bool _questionable;
        private static bool HasSomethingToQuestion()
        {
            int frame = Time.frameCount;
            if (frame == _questionFrame) return _questionable;
            _questionFrame = frame;
            var crime = Crime.instance;
            _questionable = crime == null || Inventory.instance == null || crime.CountCrimes(true, 100f, 10, null) > 0f;
            if (_questionable && SkipLogged.Add("police-carry")) Log.Verbose("Release grace: you carry something the police would check, so police sightings run as usual.");
            return _questionable;
        }
    }

    // P15: TrespassingArea.OnTriggerStay(Collider), private, every physics step. No Collider argument here, so the
    // plugin needs no PhysicsModule reference. Skipping it also skips Witness.IsWitnessed(invoke: true).
    [HarmonyPatch]
    internal static class TrespassTriggerStayPatch
    {
        private static MethodBase TargetMethod() => Graces.TriggerStayTarget;

        private static bool Prefix(TrespassingArea __instance)
        {
            if (!Jail.AnyGrace) return true;
            try { return !Graces.SkipTrespass(__instance, Graces.TriggerStayTarget); }
            catch (Exception e)
            {
                Log.Exception("trespass grace", e);
                return true;
            }
        }
    }

    // P16: TrespassingArea.OnPlayerDetected().
    [HarmonyPatch]
    internal static class TrespassDetectedPatch
    {
        private static MethodBase TargetMethod() => Graces.Detected0Target;

        private static bool Prefix(TrespassingArea __instance)
        {
            if (!Jail.AnyGrace) return true;
            try { return !Graces.SkipTrespass(__instance, Graces.Detected0Target); }
            catch (Exception e)
            {
                Log.Exception("trespass grace", e);
                return true;
            }
        }
    }

    // P17: TrespassingArea.OnPlayerDetected(GameObject, NPCReference), the NPC_FOV callback.
    [HarmonyPatch]
    internal static class TrespassDetectedByNpcPatch
    {
        private static MethodBase TargetMethod() => Graces.Detected2Target;

        private static bool Prefix(TrespassingArea __instance)
        {
            if (!Jail.AnyGrace) return true;
            try { return !Graces.SkipTrespass(__instance, Graces.Detected2Target); }
            catch (Exception e)
            {
                Log.Exception("trespass grace", e);
                return true;
            }
        }
    }

    // P18: NPC.Act_Police.OnPlayerDetected(GameObject, NPCReference), every 0.5 s per officer.
    // OnPlayerCommitingCrimeDetected is not patched: P19 ends the grace before delayed witness callbacks run.
    [HarmonyPatch]
    internal static class PoliceDetectedPatch
    {
        private static MethodBase TargetMethod() => Graces.PoliceTarget;

        private static bool Prefix()
        {
            if (!Jail.AnyGrace) return true;
            try { return !Graces.SkipPolice(); }
            catch (Exception e)
            {
                Log.Exception("police grace", e);
                return true;
            }
        }
    }

    // P19: Crime.CommitCrime(NPCReference, int, CrimeType, bool). Postfix only: every crime is recorded.
    [HarmonyPatch(typeof(Crime), nameof(Crime.CommitCrime),
        new[] { typeof(NPC.NPCReference), typeof(int), typeof(Crime.CrimeType), typeof(bool) })]
    internal static class CommitCrimePatch
    {
        private static void Postfix(Crime.CrimeType type)
        {
            if (!Jail.AnyGrace || !Graces.EndPatched) return;
            try { Jail.EndGraces("crime " + type); }
            catch (Exception e) { Log.Exception("CommitCrime postfix", e); }
        }
    }

    // P20: SaveController.LoadGameWithMigration(string, string, bool). An iterator: the prefix runs at the call site
    // (LoadMenu.cs:551 is the only fromMenu caller; door transitions pass false).
    [HarmonyPatch(typeof(SaveController), nameof(SaveController.LoadGameWithMigration),
        new[] { typeof(string), typeof(string), typeof(bool) })]
    internal static class LoadFromMenuPatch
    {
        private static void Prefix(bool fromMenu)
        {
            if (!fromMenu || !Graces.EndPatched) return;
            try
            {
                Jail.EndGraces("menu load");
                Jail.FromJailEvent = null;
            }
            catch (Exception e) { Log.Exception("LoadGameWithMigration prefix", e); }
        }
    }
}
