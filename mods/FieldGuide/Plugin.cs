using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FieldGuide
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    // Soft dependencies only order the load (spec §10): Recipe Book's tooltip and panel lines come before ours, and
    // Fair Market's extra buyers are read through its config. Field Guide works without either.
    [BepInDependency(Compat.RecipeBookGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(Compat.FairMarketGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.fieldguide";
        public const string Name = "Field Guide";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private static readonly List<string> On = new List<string>();
        private static readonly List<string> Off = new List<string>();
        private static readonly List<string> Failed = new List<string>();
        private static bool _startupReported, _hintsHooked;
        private static bool _help, _zone, _tooltip, _panel;

        // Each feature is patched as its own group (spec §4), only when its switch is on at launch. A group checks the
        // game members it needs first; a missing member or a patch exception unpatches that group's own targets, logs
        // one error and leaves the other groups working. No two groups share a target, so the rollback by owner is safe.
        // Patch bodies read their switch live (Cfg.Hot), so turning a feature off works at once; turning one on needs a
        // restart. Nothing in Trade, the stations or HintOverlay is patched.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            TraderData.Load();
            Harmony = new Harmony(Guid);

            object[] hintApi = Needs(Vanilla.HintShow, Vanilla.HintHide, Vanilla.HintSetHiddenOverride, Vanilla.HintIsShowing,
                Vanilla.HintsEnabled, Vanilla.HintsToggled, Vanilla.HintAddList, Vanilla.HintAddText, Vanilla.SaunaPassive,
                Vanilla.CbAlwaysOn, Vanilla.CbPower, Vanilla.PowerType, Vanilla.PowerRequiresFuel);
            object[] padApi = Needs(Vanilla.BackpackInstance, Vanilla.BackpackEquipped, Vanilla.BackpackIsOpen,
                Vanilla.InputInstance, Vanilla.InputType);

            // Context help (§6): station pop-ups, then the walk-in saunas.
            _help = PatchGroup("station-help", Cfg.Popups.Enabled.Value,
                Join(Join(hintApi, padApi), Needs(Vanilla.CraftingEnter, Vanilla.CraftingExit, Vanilla.CurrentCraftingBase,
                    Vanilla.CbRecipes, Vanilla.CbModifiers, Vanilla.ManuSimultaneous, Vanilla.ManuRequirePlayer)),
                Targets(Vanilla.CraftingEnter, Vanilla.CraftingExit), typeof(EnterPatch), typeof(ExitPatch));
            // Not gated on [Popups] Sauna: the postfix reads it live, so turning it on later works without a restart.
            _zone = PatchGroup("sauna-zone", Cfg.Popups.Enabled.Value && Cfg.Popups.PublicSaunas.Value,
                Join(Join(hintApi, padApi), Needs(Vanilla.SaunaTriggerEnter, Vanilla.SaunaTriggerExit, Vanilla.ContestActive,
                    Vanilla.InBuildingArea)),
                Targets(Vanilla.SaunaTriggerEnter, Vanilla.SaunaTriggerExit), typeof(SaunaEnterPatch), typeof(SaunaExitPatch));

            // Bought by (§9): skipped when the trader data failed to load.
            object[] buyApi = Needs(Vanilla.CheckIfInAllowedCategoy, Vanilla.GetItemPrice, Vanilla.TradeCheckIfAllowed,
                Vanilla.TradeIsStolen, Vanilla.DisplayRm, Vanilla.ActiveTrade, Vanilla.TradeGuid, Vanilla.TradeSharedId,
                Vanilla.TradeSell, Vanilla.TradeSellMode, Vanilla.TradeBuysStolen, Vanilla.TradeStolenFactor,
                Vanilla.TradeDestroysPurchases, Vanilla.StorageAllowed, Vanilla.StorageForbidden, Vanilla.SlotDefaultForbidden,
                Vanilla.LastSaveFolderPath, Vanilla.GetCurrentSavePath);
            _tooltip = DataGroup("tooltip", Cfg.BoughtBy.Tooltip.Value,
                Join(buyApi, Needs(Vanilla.ConstructDataString, Vanilla.UpdateTooltip)),
                Targets(Vanilla.ConstructDataString, Vanilla.UpdateTooltip), typeof(ConstructDataStringPatch), typeof(UpdateTooltipPatch));
            _panel = DataGroup("item-panel", Cfg.BoughtBy.ItemPanel.Value,
                Join(buyApi, Needs(Vanilla.UpdateInfo, Vanilla.ItemDescription)),
                Targets(Vanilla.UpdateInfo), typeof(UpdateInfoPatch));

#if FIELDGUIDE_TESTS
            Setups.Bind(Config);
#endif

            Config.SettingChanged += OnSettingChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.PlayerWillLoadGame += OnPlayerWillLoadGame;
            SaveController.LoadingDone += OnLoadingDone;
            SaveController.SavingDone += OnSavingDone;
            if (_help || _zone)
            {
                try
                {
                    HintOverlay.HintsToggled += OnHintsToggled;
                    _hintsHooked = true;
                }
                catch (Exception e) { Log.Exception("Help: hints event", e); }
            }
        }

        private static string List(List<string> l) => l.Count == 0 ? "-" : string.Join(", ", l.ToArray());

        private static object[] Needs(params object[] members) => members;

        private static object[] Join(object[] a, object[] b)
        {
            var r = new object[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }

        private static MethodBase[] Targets(params MethodBase[] targets) => targets;

        private static bool DataGroup(string name, bool enabled, object[] needs, MethodBase[] targets, params Type[] types)
        {
            if (enabled && !TraderData.Ok)
            {
                Failed.Add(name + " (no trader data)");
                return false;
            }
            return PatchGroup(name, enabled, needs, targets, types);
        }

        private static bool PatchGroup(string name, bool enabled, object[] needs, MethodBase[] targets, params Type[] types)
        {
            if (!enabled)
            {
                Off.Add(name);
                return false;
            }
            try
            {
                foreach (var n in needs)
                    if (n == null) throw new MissingMemberException("a game member this feature needs is missing (see the error above)");
                foreach (var t in types) Harmony.CreateClassProcessor(t).Patch();
                On.Add(name);
                return true;
            }
            catch (Exception e)
            {
                Rollback(targets);
                Log.Error($"Patching '{name}' failed; that feature is off this session: {e}");
                Failed.Add(name);
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

        // ---- Events (spec §4) ----

        private static void OnSettingChanged(object sender, SettingChangedEventArgs a)
        {
            Step("settings", Cfg.Hot.Refresh);
            Step("settings (bought by)", BoughtBy.Clear);
            Step("settings (panel)", UpdateInfoPatch.Clear);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Trades of an additive scene are picked up; everything else follows the main scene only.
            Step("scene (traders)", LiveTraders.OnScene);
            Step("scene (bought by)", BoughtBy.Clear);
            Step("scene (panel)", UpdateInfoPatch.Clear);
            if (mode == LoadSceneMode.Additive && scene != SceneManager.GetActiveScene()) return;
            Log.NewScene();
            Step("scene (help)", HintHost.OnScene);
            Step("scene (classify)", Classify.Clear);
            Step("scene (places)", () => Visited.OnScene(scene));
            if (!_startupReported && Game.InGameplay)
            {
                _startupReported = true;
                Step("startup", ReportStartup);
            }
        }

        private static void OnPlayerWillLoadGame() => Step("game load (places)", Visited.OnPlayerWillLoadGame);

        private static void OnLoadingDone()
        {
            Step("loading done (places)", Visited.OnLoadingDone);
            // The save has just restored the traders' fields (D24).
            Step("loading done (traders)", LiveTraders.Touch);
        }

        private static void OnSavingDone() => Step("saving done (places)", Visited.OnSavingDone);

        private static void OnHintsToggled() => Step("Help: hints toggled", HintHost.OnToggled);

        // The startup line and the Compat lines (spec §11), once, at the first gameplay scene: all plugins have loaded by
        // then, so other mods' patches are visible.
        private static void ReportStartup()
        {
            string traders = TraderData.Ok ? $"traders {TraderData.Buyers.Count} groups (data {TraderData.GameVersion})" : "traders: no data";
            Log.Info($"{Name} {Version}: on [{List(On)}]; off [{List(Off)}]; failed [{List(Failed)}]; {traders}");
            if (_help)
            {
                Compat.Foreign(Vanilla.CraftingEnter, "the station guides", "may not open");
                Compat.Foreign(Vanilla.CraftingExit, "the station guides", "may stay open after the station closes");
            }
            if (_zone)
            {
                Compat.Foreign(Vanilla.SaunaTriggerEnter, "the walk-in sauna guide", "may not open");
                Compat.Foreign(Vanilla.SaunaTriggerExit, "the walk-in sauna guide", "may stay open after you leave");
            }
            if (_tooltip)
            {
                Compat.Overlap(Vanilla.ConstructDataString, "the Bought by tooltip line");
                Compat.Overlap(Vanilla.UpdateTooltip, "the Bought by tooltip line");
            }
            if (_panel) Compat.Overlap(Vanilla.UpdateInfo, "the Bought by item panel line");
            if (_tooltip || _panel)
            {
                Compat.ReadFairMarket();
                BoughtBy.Clear();
            }
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private static readonly Action TickHelp = HintHost.Tick;

        private void Update()
        {
            if ((_help || _zone) && HintHost.Busy) Step("Help: tick", TickHelp);
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnSettingChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.PlayerWillLoadGame -= OnPlayerWillLoadGame;
            SaveController.LoadingDone -= OnLoadingDone;
            SaveController.SavingDone -= OnSavingDone;
            Step("compat (unhook)", Compat.Unhook);
            if (_hintsHooked)
            {
                try { HintOverlay.HintsToggled -= OnHintsToggled; }
                catch { }
            }
        }
    }
}
