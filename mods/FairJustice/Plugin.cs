using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FairJustice
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.fairjustice";
        public const string Name = "Fair Justice";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        private Harmony _harmony;
        private readonly List<string> _off = new List<string>();

        // Each feature is patched as its own group (spec §4). A group that is off in the config is not patched (it
        // starts at the next launch); a group that fails logs one error and leaves only that feature off.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            _harmony = new Harmony(Guid);

            // Theft (spec §3.1) and item use (spec §3.6). P1-P3 also carry the use exemptions, so they are patched when
            // any of these features is on; each body reads its own switch.
            bool anyUse = Cfg.FreeOwnUse.Value || Cfg.FreeAmenityUse.Value;
            StolenTags.Patched = PatchGroup("stolen tagging", Cfg.FixStolenTagging.Value || Cfg.FreeInternalMoves.Value || anyUse,
                typeof(CheckStolenPatch), typeof(AddReturnRemainingPatch), typeof(AddItemsPatch));
            OwnMoves.Patched = StolenTags.Patched
                ? PatchGroup("free own moves", Cfg.FreeInternalMoves.Value, typeof(MoveToOtherInventoryPatch), typeof(DropItemPatch), typeof(EquipInvokePatch))
                : Skip("free own moves", Cfg.FreeInternalMoves.Value);
            FreeUse.Patched = StolenTags.Patched
                ? PatchGroup("free use", anyUse, typeof(InvokeActionPatch), typeof(UseLiquidPatch))
                : Skip("free use", anyUse);
            FreeUse.PromptPatched = FreeUse.Patched
                ? PatchGroup("amenity prompt", Cfg.FreeAmenityUse.Value, typeof(UseMessagePatch))
                : Skip("amenity prompt", Cfg.FreeAmenityUse.Value);

            // Jail (spec §3.2-3.4).
            SentencePanel.EnabledAtLaunch = Cfg.SentencePanel.Value;
            bool anyJail = Cfg.SentencePanel.Value || Cfg.CellDoorGrace.Value || Cfg.ReleaseGrace.Value || Cfg.StopPursuits.Value
                           || Cfg.DeferNightRelease.Value || Cfg.NoPassOutReArrest.Value || Cfg.KeepOverflowInBag.Value
                           || Cfg.KeepPermittedItems.Value;
            Jail.StatePatched = PatchGroup("jail state", anyJail,
                typeof(ImprisonPlayerPatch), typeof(PrisonLoadPatch), typeof(ReleasePatch), typeof(EscapePatch));
            NightRelease.Patched = PatchGroup("night release", Cfg.DeferNightRelease.Value, typeof(NightReleasePatch));
            // KeepPermittedItems (D-4) lives in the same three patches, so each is on when either of its features is.
            bool keep = Cfg.KeepPermittedItems.Value;
            Confiscation.Patched = PatchGroup("confiscation", Cfg.KeepOverflowInBag.Value || keep, typeof(ConfiscateItemsPatch));
            BagRescue.Patched = PatchGroup("arrest search", Cfg.RescueStolenBackpackContents.Value || keep, typeof(RemoveStolenGoodsPatch));
            FineOrder.Patched = PatchGroup("fine payment", Cfg.SafePayFine.Value || keep, typeof(PayFinePatch));
            bool anyGrace = Cfg.CellDoorGrace.Value || Cfg.ReleaseGrace.Value;
            Graces.EndPatched = PatchGroup("grace end", anyGrace, typeof(CommitCrimePatch), typeof(LoadFromMenuPatch));
            Graces.TrespassPatched = PatchGroup("trespass grace", Cfg.CellDoorGrace.Value || (Cfg.ReleaseGrace.Value && Cfg.ReleaseGraceTrespass.Value),
                typeof(TrespassTriggerStayPatch), typeof(TrespassDetectedPatch), typeof(TrespassDetectedByNpcPatch));
            bool police = Cfg.ReleaseGrace.Value && Cfg.ReleaseGracePolice.Value;
            Graces.PolicePatched = Graces.EndPatched
                ? PatchGroup("police grace", police, typeof(PoliceDetectedPatch))
                : Skip("police grace", police);
            PassOutGuard.Patched = PatchGroup("pass-out guard", Cfg.NoPassOutReArrest.Value, typeof(StartSleepPatch), typeof(ExecuteSleepEventPatch));

            // Police limits (spec §3.7). The locked-door stop has no patch: it runs from Update and is read live.
            PoliceLimits.ScenePatched = PatchGroup("police stop at scenes", Cfg.StopChaseAtTenement.Value, typeof(FollowTargetPatch));
            if (!Vanilla.HasPolice) _off.Add("locked-door stop (game members missing)");
            else if (!Cfg.StopAtLockedDoors.Value) _off.Add("locked-door stop (until switched on)");

            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.LoadingDone += RepairOnLoad.Run;
            SaveController.LoadingDone += LockedDoorStop.Reset;

            var patched = _harmony.GetPatchedMethods().Select(m => m.DeclaringType?.Name + "." + m.Name).ToArray();
            Log.Info($"{Name} {Version} on game {Application.version}; patched {patched.Length}: {string.Join(", ", patched)}" +
                     (_off.Count > 0 ? $"; off: {string.Join(", ", _off)}" : ""));
        }

        private bool PatchGroup(string name, bool enabled, params Type[] types)
        {
            if (!enabled)
            {
                _off.Add(name);
                return false;
            }
            try
            {
                foreach (var t in types) _harmony.CreateClassProcessor(t).Patch();
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Patching '{name}' failed; that feature is off this session: {e}");
                _off.Add(name + " (failed)");
                return false;
            }
        }

        // A group whose prerequisite group is not patched.
        private bool Skip(string name, bool enabled)
        {
            _off.Add(enabled ? name + " (needs a group that is off or failed)" : name);
            return false;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Step("sceneLoaded (jail)", () => Jail.OnSceneLoaded(scene));
            Step("sceneLoaded (panel)", () => SentencePanel.OnSceneLoaded(scene));
            Step("sceneLoaded (doors)", LockedDoorStop.Reset);
        }

        private static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            Step("frames", () => Frames.Check("between frames"));
            Step("graces", Jail.Tick);
            Step("sentence panel", SentencePanel.Update);
            Step("locked doors", LockedDoorStop.Tick);
        }

        private void OnGUI()
        {
            if (!SentencePanel.Visible) return;
            try
            {
                Vector2 screen = Skin.Begin();
                SentencePanel.OnGUI(screen);
            }
            catch (Exception e)
            {
                // GUILayout throws ExitGUIException on purpose; it must propagate.
                if (e is ExitGUIException) throw;
                Log.Exception("sentence panel OnGUI", e);
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.LoadingDone -= RepairOnLoad.Run;
            SaveController.LoadingDone -= LockedDoorStop.Reset;
        }
    }
}
