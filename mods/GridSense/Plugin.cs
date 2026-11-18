using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GridSense
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.gridsense";
        public const string Name = "Grid Sense";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private static readonly List<string> On = new List<string>();
        private static readonly List<string> Off = new List<string>();
        private static readonly List<string> Failed = new List<string>();
        private static readonly List<string> Lazy = new List<string>();
        private static bool _shop, _build, _started;

        // Each feature is patched as its own group (spec §3). A group checks the game members it needs first; a missing
        // member or a patch exception unpatches that group's own targets, logs one error and leaves the other groups
        // working. No two groups share a target, so unpatching by owner is safe. The wattage groups are patched at launch
        // when their switch is on (turning one off works at once, turning one on needs a restart); the flicker group is
        // patched lazily, the first time its switch is on, so with the defaults the game's power code is never touched.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // S-009 part 1: wattage in the shop and workbench info panel (G1) and on the build bar (G2). Both need every
            // power member, or the numbers would not be exact.
            _shop = PatchGroup("shop-line", Cfg.Wattage.ShopInfo.Value,
                Needs(Vanilla.UpdateFurnitureInfo, Vanilla.CurrentDescription, Vanilla.UpdateContentVisibility, Vanilla.RebuildLayout, Vanilla.Power),
                Targets(Vanilla.UpdateFurnitureInfo), typeof(ShopLinePatch));
            _build = PatchGroup("build-bar", Cfg.Wattage.BuildBar.Value,
                Needs(Vanilla.ChangeSelection, Vanilla.Slots, Vanilla.CurrentVisibleSlot, Vanilla.CurrentSelectionName, Vanilla.CurrentSelectionText,
                    Vanilla.Power),
                Targets(Vanilla.ChangeSelection), typeof(BuildBarPatch));

            // S-009 part 2: the no-contract flicker switch (G3), lazy.
            Lazy.Add("supply-flicker");
            SupplyFlicker.InitAtLaunch();

#if GRIDSENSE_TESTS
            PowerTable.Bind(Config);
#endif

            Config.SettingChanged += OnSettingChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static string List(List<string> l) => l.Count == 0 ? "-" : string.Join(", ", l.ToArray());

        private static object[] Needs(params object[] members) => members;

        private static MethodBase[] Targets(params MethodBase[] targets) => targets;

        private static bool PatchGroup(string name, bool enabled, object[] needs, MethodBase[] targets, Type type, string failNote = null)
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
                Harmony.CreateClassProcessor(type).Patch();
                On.Add(name);
                return true;
            }
            catch (Exception e)
            {
                Rollback(targets);
                Log.Error($"Patching '{name}' failed; that feature is off this session." + (failNote != null ? " " + failNote : "") + $" {e}");
                Failed.Add(name);
                return false;
            }
        }

        // The flicker switch's lazy group (spec §3, §5.2): same rules, on the main thread, at launch or the frame after a
        // config change.
        internal static bool PatchLazy(string name, object[] needs, MethodBase target, Type type, string failNote)
        {
            Lazy.Remove(name);
            bool ok = PatchGroup(name, true, needs, new[] { target }, type, failNote);
            if (ok && _started) Log.Info($"Patched '{name}' now that it is needed.");
            return ok;
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

        // ---- Events (spec §3, §6) ----

        private static void OnSettingChanged(object sender, SettingChangedEventArgs a)
        {
            Step("config", Cfg.Hot.Refresh);
            var entry = a?.ChangedSetting;
            if (entry == Cfg.Accessibility.StopNoContractFlicker) Step("config (flicker)", SupplyFlicker.OnConfigChanged);
            else if (entry == Cfg.Wattage.ShopInfo && Cfg.Hot.ShopInfo && !_shop)
                Log.Info("Shop: ShopInfo was off at launch; restart the game to apply it.");
            else if (entry == Cfg.Wattage.BuildBar && Cfg.Hot.BuildBar && !_build)
                Log.Info("Build: BuildBar was off at launch; restart the game to apply it.");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive && scene != SceneManager.GetActiveScene()) return;
            if (_started || GlobalState.instance == null) return;
            _started = true;
            Step("compat", ReportCompat);
            Step("startup", Startup);
#if GRIDSENSE_TESTS
            Step("power table", PowerTable.Run);
#endif
        }

        // Other mods on what we patch (spec §9), once all plugins have loaded. A lazy flicker patch made later scans its
        // own target then (SupplyFlicker.Tick).
        private static void ReportCompat()
        {
            if (_shop) Compat.Overlap(Vanilla.UpdateFurnitureInfo, "the shop power line");
            if (_build) Compat.Overlap(Vanilla.ChangeSelection, "the build bar wattage");
            if (SupplyFlicker.Patched) Compat.Overlap(Vanilla.Flickering, "the no-contract flicker switch");
        }

        private static void Startup()
        {
            Log.Info($"{Name} {Version} on game {Application.version}; on: {List(On)}; lazy: {List(Lazy)}; off: {List(Off)}; failed: {List(Failed)}");
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update() => Step("flicker", SupplyFlicker.Tick);

        private void OnDestroy()
        {
            Config.SettingChanged -= OnSettingChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }
}
