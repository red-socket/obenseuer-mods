using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Workshop
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.workshop";
        public const string Name = "Workshop";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();
        private static bool _compatReported;
        private static CraftingBase _lastStation;

        // Each feature is patched as its own group (spec §3.1), only when it is on at launch. A group checks the game
        // members it needs first; a missing member or a patch exception unpatches that group's own targets, logs one
        // error and leaves the other groups working. Every patch body also re-reads its config entry live. The repeat
        // key, the queue pump and the recipe row's state run from Update; there is no SavingStarted handler (the game
        // does not guard it, and a throw there aborts the save).
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // S-048 Fetch and the S-040 repeat button share the recipe row (W1); the controller routes need it (W2).
            RecipeRow.Patched = PatchGroup("recipe row", Cfg.Fetch.Enabled.Value || (Cfg.Queue.Enabled.Value && Cfg.Queue.RepeatButton.Value),
                Needs(Vanilla.ShowRecipe, Vanilla.CompareRecipeToItems, Vanilla.StorageChanged, Vanilla.StorageOnUpdate, Vanilla.ManuActive,
                    Vanilla.AllRecipes, Vanilla.MoveToOtherInventory, Vanilla.GlobalDragField),
                Targets(Vanilla.ShowRecipe), typeof(ShowRecipePatch));
            NavRoutes.Patched = PatchGroup("gamepad", RecipeRow.Patched && Cfg.Interface.GamepadRoutes.Value,
                Needs(Vanilla.NavMove, Vanilla.SelectButton, Vanilla.SwitchToNextCraftingButton),
                Targets(Vanilla.NavMove), typeof(NavRoutes));

            // S-040 Machine Queue (spec §5).
            QueuePatches.Patched = PatchGroup("queue", Cfg.Queue.Enabled.Value,
                Needs(Vanilla.Craft, Vanilla.GlobalStateOnSavingGame, Vanilla.GlobalStateOnLoadingGame, Vanilla.CraftingBaseOnLoadingGame,
                    Vanilla.Crafts, Vanilla.CraftingInProgress, Vanilla.LastInstantCraftFrame, Vanilla.SelectedRecipe, Vanilla.AllRecipes,
                    Vanilla.ManuActive, Vanilla.CompareRecipeToItems),
                Targets(Vanilla.Craft, Vanilla.GlobalStateOnSavingGame, Vanilla.GlobalStateOnLoadingGame, Vanilla.CraftingBaseOnLoadingGame),
                typeof(CraftPatch), typeof(GlobalSavePatch), typeof(GlobalLoadPatch), typeof(StationLoadPatch));
            QueuePatches.VesselsPatched = PatchGroup("queue vessels", QueuePatches.Patched,
                Needs(Vanilla.StartProcess, Vanilla.SetActiveRecipe), Targets(Vanilla.StartProcess), typeof(StartProcessPatch));
            LookHint.Patched = PatchGroup("look hint", QueuePatches.Patched && Cfg.Queue.LookHint.Value,
                Needs(Vanilla.UpdateUseMessage), Targets(Vanilla.UpdateUseMessage), typeof(UseMessagePatch));
            CraftLabelPatch.Patched = PatchGroup("craft label", QueuePatches.Patched,
                Needs(Vanilla.CraftButtonMessage), Targets(Vanilla.CraftButtonMessage), typeof(CraftLabelPatch));

            // U-010 Cancel (spec §7): all four or none.
            CancelPatches.Patched = PatchGroup("cancel", Cfg.Cancel.Enabled.Value,
                Needs(Vanilla.ProgressSlotClick, Vanilla.CreateItems, Vanilla.IncreaseSkill, Vanilla.TestAndApply, Vanilla.Crafts,
                    Vanilla.CraftingInProgress, Vanilla.Timers, Vanilla.ManuActive),
                Targets(Vanilla.ProgressSlotClick, Vanilla.CreateItems, Vanilla.IncreaseSkill, Vanilla.TestAndApply),
                typeof(ProgressSlotClickPatch), typeof(CreateItemsPatch), typeof(IncreaseSkillPatch), typeof(TestAndApplyPatch));

            // S-086 Bigger Vats (spec §6); the layout is always on.
            VatSlots.Patched = PatchGroup("vat slots", Cfg.BiggerVats.Enabled.Value,
                Needs(Vanilla.StorageStart, Vanilla.StorageStartDone, Vanilla.CustomSlotAmount),
                Targets(Vanilla.StorageStart), typeof(StorageStartPatch));
            VatLayout.Patched = PatchGroup("vat layout", true,
                Needs(Vanilla.EnterManu), Targets(Vanilla.EnterManu), typeof(EnterManuPatch));

            // All static events: subscribed once.
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.LoadingDone += OnLoadingDone;

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
            Step("scene (gate)", PlayGate.OnSceneLoaded);
            Step("scene (confirm)", Confirm.ResetAll);
            Step("scene (caches)", () =>
            {
                RecipeRow.OnSceneLoaded();
                VatLayout.OnSceneLoaded();
                Sources.Clear();
                FetchAction.ClearCache();
                LookHint.OnSceneLoaded();
                Pump.OnSceneLoaded();
                _lastStation = null;
            });
            Step("scene (machines)", MachineStore.OnSceneLoaded);
            Step("scene (keys)", Keys.CheckCollisions);
            if (!_compatReported && GlobalState.instance != null)
            {
                _compatReported = true;
                Step("compat", ReportCompat);
            }
        }

        private static void OnLoadingDone()
        {
            Step("loading done (gate)", PlayGate.OnLoadingDone);
            Step("loading done (machines)", MachineStore.OnLoadingDone);
            Step("loading done (vats)", VatAudit.Run);
        }

        // Other mods' skipping patches on what we patch or call (spec §3.3), once all plugins have loaded.
        private static void ReportCompat()
        {
            if (RecipeRow.Patched)
            {
                Compat.Overlap(Vanilla.ShowRecipe, "the Fetch and repeat buttons");
                Compat.Foreign(Vanilla.MoveToOtherInventory, "Fetch's item moves", "may move less than planned (never more)");
            }
            if (NavRoutes.Patched) Compat.Foreign(Vanilla.NavMove, "the controller route to the Fetch and repeat buttons", "may not apply");
            if (QueuePatches.Patched) Compat.Foreign(Vanilla.Craft, "repeats and the repeat key", "may not start or record crafts");
            if (CancelPatches.Patched)
            {
                Compat.Foreign(Vanilla.CreateItems, "Cancel", "may not return the failed output");
                Compat.Foreign(Vanilla.IncreaseSkill, "Cancel's no-skill rule", "may not apply");
            }
            if (VatSlots.Patched) Compat.Foreign(Vanilla.StorageStart, "Bigger Vats", "may not apply");
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            Step("drag tracking", Game.TrackDrag);
            Step("key notes", Keys.ShowPending);
            Step("confirm", Confirm.Tick);
            Step("repeat key", RepeatKey.Tick);
            Step("pump", Pump.Tick);
            Step("recipe row", RecipeRow.Tick);
            Step("panel close", PanelClose);
        }

        // A station's panel just closed: give its queue a look now (it may have waited for the menu).
        private static void PanelClose()
        {
            var cur = CraftingBase.currentCraftingBase;
            if (ReferenceEquals(cur, _lastStation)) return;
            if (_lastStation != null) MachineStore.PollNow.Add(_lastStation);
            _lastStation = cur;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.LoadingDone -= OnLoadingDone;
        }
    }
}
