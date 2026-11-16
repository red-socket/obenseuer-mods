using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FewerClicks
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.fewerclicks";
        public const string Name = "Fewer Clicks";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();
        private static bool _compatReported;

        // Each feature is patched as its own group (spec §2), only when it is on at launch. A group checks the game
        // members it needs first; a missing member or a patch exception unpatches that group's own targets, logs one
        // error and leaves the other features working. Every patch body also re-reads its config entry live.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // U-005 / S-049(a) (spec §2 P1, §3).
            TopUp.Patched = PatchGroup("top up", Cfg.TopUp.Enabled.Value,
                Needs(Vanilla.UpdateStoragePanelUI, Vanilla.SlotPanel, Vanilla.TakeAllSound, Vanilla.GlobalDragField),
                Targets(Vanilla.UpdateStoragePanelUI), typeof(UpdateStoragePanelUIPatch));
            PatchGroup("top up gamepad", TopUp.Patched && Cfg.TopUp.Gamepad.Value,
                Needs(Vanilla.NavMove, Vanilla.SelectButton, Vanilla.SwitchToNextStorageSortButton),
                Targets(Vanilla.NavMove), typeof(NavMovePatch));

            // S-049(b) (spec §2 P3, §4). Also patched when only the toggle key is bound, so the toggle can turn it on.
            AutoSwing.Patched = PatchGroup("auto swing", Cfg.AutoSwing.Enabled.Value || Keys.Bound(Cfg.AutoSwing.ToggleKey),
                Needs(Vanilla.OnPrimaryHold, Vanilla.MeleeHit, Vanilla.HoldStarted, Vanilla.DamageType, Vanilla.CurrentStock),
                Targets(Vanilla.OnPrimaryHold, Vanilla.MeleeHit), typeof(OnPrimaryHoldPatch), typeof(MeleeHitPatch));

            // S-057 (spec §2 P4, P5).
            QuickEat.Patched = PatchGroup("quick keys", Cfg.QuickEat.Enabled.Value || Cfg.AutoEat.Enabled.Value || Cfg.SlotKeys.Enabled.Value,
                Needs(Vanilla.InteractControls), Targets(Vanilla.InteractControls), typeof(InteractControlsPatch));
            if (QuickEat.Patched) QuickEat.Subscribe();
            SlotKeys.LabelsPatched = PatchGroup("slot labels", QuickEat.Patched && Cfg.SlotKeys.Enabled.Value,
                Needs(Vanilla.InventoryStart), Targets(Vanilla.InventoryStart), typeof(InventoryStartPatch));
            if (SlotKeys.LabelsPatched) SlotKeys.HookSettings();

            // S-096 (spec §2 P6, §6).
            FillAll.Patched = PatchGroup("fill all", Cfg.FillAll.Enabled.Value,
                Needs(Vanilla.LiquidPanelOnEnable, Vanilla.StopFilling),
                Targets(Vanilla.LiquidPanelOnEnable), typeof(LiquidPanelOnEnablePatch));
            if (FillAll.Patched && Vanilla.CrimeCounter == null)
                Log.Warn("Fill all: StopWhenSeen is off this session (the liquid panel's theft counter was not found).");

            SceneManager.sceneLoaded += OnSceneLoaded;
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
            Step("scene (fill all)", () => FillAll.Cancel("scene"));
            Step("scene (buttons)", () =>
            {
                TopUpButton.OnSceneLoaded();
                FillAllButton.OnSceneLoaded();
            });
            Step("scene (item panel)", ItemInfoFix.OnSceneLoaded);
            Step("scene (auto swing)", AutoSwing.OnSceneLoaded);
            Step("scene (keys)", Keys.CheckCollisions);
            if (!_compatReported)
            {
                _compatReported = true;
                Step("compat", ReportCompat);
            }
        }

        private static void OnPlayerWillChangeLevel() => Step("level change (fill all)", () => FillAll.Cancel("level change"));

        // Other mods' skipping patches on what we patch or call (spec §10), once all plugins have loaded.
        private static void ReportCompat()
        {
            if (TopUp.Patched)
            {
                Compat.Overlap(Vanilla.UpdateStoragePanelUI, "the Top up button");
                Compat.Overlap(Vanilla.NavMove, "the Top up controller route");
                Compat.Overlap(Vanilla.MoveToOtherInventory, "Top up");
            }
            if (AutoSwing.Patched)
            {
                Compat.Overlap(Vanilla.PrimaryStopHold, "Auto Swing");
                Compat.Overlap(Vanilla.MeleeHit, "Auto Swing's miss for people who step into a swing");
            }
            if (QuickEat.Patched)
            {
                Compat.Overlap(Vanilla.InteractControls, "Quick Eat, Auto Eat and the slot keys");
                Compat.Overlap(Vanilla.InventoryStart, "the slot key labels");
            }
            if (FillAll.Patched)
            {
                Compat.Overlap(Vanilla.LiquidPanelOnEnable, "the Fill all button");
                Compat.Overlap(Vanilla.PressFill, "Fill all");
                Compat.Overlap(Vanilla.StopFilling, "Fill all");
            }
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            Step("drag tracking", Game.TrackDrag);
            Step("top up hotkey", TopUp.Update);
            Step("auto swing toggle", AutoSwing.Update);
            Step("key notes", Keys.ShowPending);
        }

        // Fill all steps after every Update, so it sees the panel's filling tick of the same frame (spec §6.3).
        private void LateUpdate()
        {
            Step("fill all", FillAll.LateUpdate);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.PlayerWillChangeLevel -= OnPlayerWillChangeLevel;
            QuickEat.Unsubscribe();
        }
    }
}
