using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterPlacement
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.betterplacement";
        public const string Name = "Better Placement";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();
        private static bool _wasOpen;

        // Each feature is patched as its own group (spec §3.2), only when it is on at launch. A group checks the game
        // members it needs first; a missing member or a patch exception removes that group's own patch methods (not the
        // other groups' patches on the same method: ShowPreview carries five groups), logs one error and leaves the
        // other groups working. Every patch body also re-reads its config entry live.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // S-083: the controller trigger fix (T1).
            TriggerFilter.Patched = PatchGroup("trigger fix", Cfg.Rotation.TriggerFix.Value,
                Needs(Vanilla.ShowPreview), Targets(Vanilla.ShowPreview), typeof(TriggerReadPatch));
            if (TriggerFilter.Patched && !TriggerFilter.Active)
            {
                // The transpiler found the wrong number of trigger reads and left ShowPreview alone (error logged).
                Rollback(Targets(Vanilla.ShowPreview), typeof(TriggerReadPatch));
                TriggerFilter.Patched = false;
                _on.Remove("trigger fix");
                _failed.Add("trigger fix");
            }

            // S-083: rotation snapping (R1) and remembered angles (M1, M2).
            RotationSnap.Patched = PatchGroup("rotation snap", Cfg.Rotation.SnapEnabled.Value,
                Needs(Vanilla.ShowPreview, Vanilla.RotationModeCooldown, Vanilla.CurrentRot, Vanilla.AttachPoint),
                Targets(Vanilla.ShowPreview), typeof(RotationSnapPatch));
            AngleMemory.Patched = PatchGroup("remember angle", Cfg.Rotation.RememberAngle.Value,
                Needs(Vanilla.Place, Vanilla.ChangeCurrentObject, Vanilla.CurrentRot, Vanilla.AttachPoint),
                Targets(Vanilla.Place, Vanilla.ChangeCurrentObject), typeof(PlaceRememberPatch), typeof(ChangeObjectPatch));

            // U-008: alignment (A1, A2, A3; A4 only when UpdateConnectionIndicator exists, see its Prepare).
            AlignPatches.Patched = PatchGroup("align", Cfg.Align.Enabled.Value,
                Needs(Vanilla.ShowPreview, Vanilla.ChangeColor, Vanilla.Place, Vanilla.WarningTextAnimation, Vanilla.CurrentPos,
                    Vanilla.CurrentRot, Vanilla.Hit, Vanilla.AttachPoint, Vanilla.MovingPlacedItem, Vanilla.PreviewType),
                Targets(Vanilla.ChangeColor, Vanilla.ShowPreview, Vanilla.Place, Vanilla.UpdateConnectionIndicator),
                typeof(ChangeColorPatch), typeof(AlignPreviewPatch), typeof(SettleGuardPatch), typeof(ConnectionDeferPatch));
            Solver.ConnectionPatched = AlignPatches.Patched && Ours(Vanilla.UpdateConnectionIndicator);

            // S-083 #5848: flat items (C1, C2, C3), off by default.
            FlatItems.Patched = PatchGroup("flat items", Cfg.FlatItems.Enabled.Value,
                Needs(Vanilla.TriggerEnter, Vanilla.TriggerExit, Vanilla.ShowPreview, Vanilla.Collision, Vanilla.Hit,
                    Vanilla.AttachPoint, Vanilla.MovingPlacedItem),
                Targets(Vanilla.TriggerEnter, Vanilla.TriggerExit, Vanilla.ShowPreview),
                typeof(FlatEnterPatch), typeof(FlatExitPatch), typeof(FlatPreviewPatch));

            // The controls hint (H1, H2), only for the features that have keys.
            Hint.Patched = PatchGroup("hint", Cfg.ShowControlsHint.Value && (AlignPatches.Patched || RotationSnap.Patched),
                Needs(Vanilla.GetRotationMode, Vanilla.GetButtonInfo, Vanilla.ControlsInfoText),
                Targets(Vanilla.GetRotationMode, Vanilla.GetButtonInfo), typeof(RotationModeTextPatch), typeof(ButtonInfoPatch));

            SceneManager.sceneLoaded += OnSceneLoaded;

            Log.Info($"{Name} {Version} on game {Application.version}; on: {List(_on)}; off: {List(_off)}; failed: {List(_failed)}");
        }

        private void Start()
        {
            Step("keys", Keys.Recheck);
            StartCoroutine(Steps.Run());
        }

        // Whether one of our prefixes is on the method.
        private static bool Ours(MethodBase m)
        {
            if (m == null) return false;
            try
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) return false;
                foreach (var p in info.Prefixes)
                    if (p.owner == Guid) return true;
            }
            catch (Exception e) { Log.Exception("patch check", e); }
            return false;
        }

        private static string List(List<string> l) => l.Count == 0 ? "none" : string.Join(", ", l.ToArray());

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
                Rollback(targets, types);
                Log.Error($"Patching '{name}' failed; that feature is off this session: {e}");
                _failed.Add(name);
                return false;
            }
        }

        // Removes only this group's patch methods: the house Unpatch(m, All, Guid) would also strip the other groups on a
        // shared target.
        private static void Rollback(MethodBase[] targets, params Type[] types)
        {
            var mine = new HashSet<Type>(types);
            foreach (var m in targets)
            {
                if (m == null) continue;
                try
                {
                    var info = Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    var remove = new List<MethodInfo>();
                    foreach (var list in new[] { info.Prefixes, info.Postfixes, info.Transpilers, info.Finalizers })
                        foreach (var p in list)
                            if (p.owner == Guid && p.PatchMethod != null && mine.Contains(p.PatchMethod.DeclaringType)) remove.Add(p.PatchMethod);
                    foreach (var pm in remove) Harmony.Unpatch(m, pm);
                }
                catch (Exception e) { Log.Exception("unpatch " + m.DeclaringType?.Name + "." + m.Name, e); }
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Step("scene (caches)", () =>
            {
                Footprints.Clear();
                Neighbours.Clear();
                FlatItems.Clear();
                Solver.Reset();
                AngleMemory.OnSceneLoaded();
                Settle.ChangeStep = -1;
            });
            Step("scene (toggle)", AlignInput.ResetToggle);
            Step("scene (keys)", Keys.Recheck);
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            Step("device", Device.Tick);
            Step("align input", AlignInput.Tick);
            Step("menu", MenuEdge);
            Step("guides", Guides.Watchdog);
            Step("snap hint", StaleSnap);
            Step("key notes", Keys.ShowPending);
        }

        // The building menu opened or closed. The first open reports other mods on our targets (every plugin has loaded
        // by then); a close drops the footprint caches and Toggle state.
        private static void MenuEdge()
        {
            bool open = Game.BuildOpen;
            if (open == _wasOpen) return;
            _wasOpen = open;
            if (open)
            {
                Compat.Report();
                return;
            }
            Footprints.Clear();
            Solver.Reset();
            AlignInput.ResetToggle();
            RotationSnap.Clear();
        }

        // ShowPreview stopped running (Take/Move slot, raycast miss): the held snap step no longer applies.
        private static void StaleSnap()
        {
            if (RotationSnap.HeldStep != 0 && RotationSnap.LastFrame < Time.frameCount - 1) RotationSnap.Clear();
        }

        private void OnApplicationFocus(bool focused)
        {
            Step("focus", () =>
            {
                Keys.OnFocus(focused);
                if (!focused) AlignInput.ResetToggle();
            });
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }
}
