using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FairMarket
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.fairmarket";
        public const string Name = "Fair Market";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private static readonly List<string> On = new List<string>();
        private static readonly List<string> Off = new List<string>();
        private static readonly List<string> Failed = new List<string>();
        private static bool _startupReported;
        private static bool _stock, _buyers, _doors, _stalls, _washInfo, _washSkips, _certs, _marks, _window, _access;

        // [Stock] edits re-read the game's 420 KB StorageSpawnCategory.json. ConfigurationManager sets a text entry on
        // every keystroke, so the re-read waits until the section has been left alone for StockRereadDelay seconds
        // (unscaled): one parse per edit instead of one per character, and no warnings for half-typed values.
        private const float StockRereadDelay = 1f;
        private static float _stockRereadAt = -1f;

        // Each feature is patched as its own group (spec §3), only when its switch is on at launch. A group checks the
        // game members it needs first; a missing member or a patch exception unpatches that group's own targets, logs
        // one error and leaves the other groups working. Patch bodies also read their switch live (Cfg.Hot), so turning
        // a feature off works at once; turning one on needs a restart.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // S-011 stock (§5). S2 only when ApplyToNextRestock is on at launch.
            bool applyNext = Cfg.Stock.ApplyToNextRestock.Value;
            var stockTypes = applyNext ? new[] { typeof(SpawnDatabasePatch), typeof(SpawnItemsPatch) } : new[] { typeof(SpawnDatabasePatch) };
            _stock = PatchGroup("stock", Cfg.Stock.Enabled.Value,
                Needs(Vanilla.ConstructSpawnDb, Vanilla.FetchSpawnCategory, Vanilla.GetSpawnCategories, Vanilla.SpawnItems,
                    Vanilla.SetNextItemsToSpawn, Vanilla.SpawnFromCategory),
                Targets(Vanilla.ConstructSpawnDb, Vanilla.SpawnItems), stockTypes);

            // S-013 buyers (§6).
            _buyers = PatchGroup("buyers", Cfg.Buyers.Enabled.Value,
                Needs(Vanilla.TradeCheckIfAllowed, Vanilla.CheckIfInAllowedCategoy), Targets(Vanilla.TradeCheckIfAllowed),
                typeof(CheckIfAllowedPatch));

            // S-094 opening hours (§7), display only.
            _doors = PatchGroup("hours-doors", Cfg.Hours.Doors.Value,
                Needs(Vanilla.DoorChangelevelUseMessage, Vanilla.DoorUseMessage, Vanilla.UseTextOverridden),
                Targets(Vanilla.DoorChangelevelUseMessage, Vanilla.DoorUseMessage), typeof(DoorChangelevelTextPatch), typeof(DoorTextPatch));
            _stalls = PatchGroup("hours-stalls", Cfg.Hours.Stalls.Value,
                Needs(Vanilla.TradeUseMessage, Vanilla.UseTextOverridden), Targets(Vanilla.TradeUseMessage), typeof(TradeTextPatch));

            // S-043 dishwasher display (§8.1, §8.2) and the certificate fix (§8.3).
            _washInfo = PatchGroup("wash-info", Cfg.Dishwasher.PreciseOdds.Value,
                Needs(Vanilla.ShowRecipe, Vanilla.RecipeInfoListPanel), Targets(Vanilla.ShowRecipe), typeof(ShowRecipePatch));
            _washSkips = PatchGroup("wash-skips", Cfg.Dishwasher.SkippedNote.Value,
                Needs(Vanilla.DishwasherCheckItems), Targets(Vanilla.DishwasherCheckItems), typeof(DishwasherCheckPatch));
            _certs = PatchGroup("cert-fix", Cfg.Certificates.MatchFirst.Value,
                Needs(Vanilla.FindValidCertificates), Targets(Vanilla.FindValidCertificates), typeof(FindCertificatesPatch));

            // S-069 invoice markers and the Invoices list (§9).
            _marks = PatchGroup("invoice-marks", Cfg.Invoices.Markers.Value,
                Needs(Vanilla.InvoiceDataToString, Vanilla.UpdateInfo, Vanilla.ItemValue),
                Targets(Vanilla.InvoiceDataToString, Vanilla.UpdateInfo), typeof(InvoiceTooltipPatch), typeof(InvoicePanelPatch));
            _window = InvoiceWindow.Available = PatchGroup("list-window", Cfg.InvoiceList.Enabled.Value,
                Needs(Vanilla.PauseNavUpdate, Vanilla.HandleBackButtonClick), Targets(Vanilla.PauseNavUpdate, Vanilla.HandleBackButtonClick),
                typeof(PauseNavUpdatePatch), typeof(HandleBackButtonPatch));
            _access = NotesPageInput.Available = PatchGroup("list-access", _window && Cfg.InvoiceList.NotesPageKey.Value,
                Needs(Vanilla.GetButtonInfo, Vanilla.KeyCodeInfoToString), Targets(Vanilla.GetButtonInfo), typeof(ButtonInfoPatch));
            bool forced = PauseEntry.Forced = _window && Cfg.InvoiceList.NotesPageKey.Value && !_access;
            if (forced) Log.Warn("Invoices: the Notes page key failed; the pause menu entry is added so the list can still be opened.");
            // The pause entry has no patches: PauseMenuEntry is read live (PauseEntry.Wanted). Listed here as at launch.
            if (_window && (Cfg.InvoiceList.PauseMenuEntry.Value || forced)) On.Add("pause-entry"); else Off.Add("pause-entry");

#if FAIRMARKET_TESTS
            Setups.Bind(Config);
#endif

            Config.SettingChanged += OnSettingChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.PlayerWillChangeLevel += OnPlayerWillChangeLevel;
            SaveController.LoadingDone += OnLoadingDone;
        }

        private static string List(List<string> l) => l.Count == 0 ? "-" : string.Join(", ", l.ToArray());

        private static object[] Needs(params object[] members) => members;

        private static MethodBase[] Targets(params MethodBase[] targets) => targets;

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

        // ---- Events (spec §3) ----

        private static void OnSettingChanged(object sender, SettingChangedEventArgs a)
        {
            Cfg.Hot.Refresh();
            string section = a?.ChangedSetting?.Definition?.Section;
            if (section == "Stock" && _stock) _stockRereadAt = Time.unscaledTime + StockRereadDelay;
            else if (section == "Buyers") Step("buyers reset", BuyerRules.Clear);
            else if (section == "InvoiceList") Step("notes hint", NotesPageInput.RefreshHint);
        }

        // The game's own JSON re-read; S1 runs again on the fresh lists. Consumers fetch by name on every spawn. Held back
        // while a level loads.
        private static void StockRereadTick()
        {
            if (_stockRereadAt < 0f || Time.unscaledTime < _stockRereadAt) return;
            if (SaveController.Loading) { _stockRereadAt = Time.unscaledTime + StockRereadDelay; return; }
            _stockRereadAt = -1f;
            var db = StorageSpawnCategoryDatabase.instance;
            if (db != null) db.ConstructSpawnCategoryDatabase();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive && scene != SceneManager.GetActiveScene()) return;
            Step("scene (window)", InvoiceWindow.Close);
            Step("scene (input)", InputBlock.Release);
            Step("scene (confirm)", Confirm.ResetAll);
            Step("scene (gate)", PlayGate.OnSceneLoaded);
            Step("scene (buyers)", BuyerRules.Clear);
            Step("scene (hours)", HoursIndex.Clear);
            Step("scene (hours text)", HoursText.Clear);
            Step("scene (skip note)", SkipNote.OnSceneLoaded);
            Step("scene (pause entry)", PauseEntry.OnSceneLoaded);
            Step("scene (keys)", Keys.CheckCollisions);
            if (_buyers) Step("scene (buyers check)", () => BuyerRules.CheckScene(scene));
            if (!_startupReported && GlobalState.instance != null)
            {
                _startupReported = true;
                Step("startup", ReportStartup);
            }
        }

        private static void OnPlayerWillChangeLevel() => Step("level change (window)", InvoiceWindow.Close);

        private static void OnLoadingDone() => Step("loading done (gate)", PlayGate.OnLoadingDone);

        // The startup line and the Compat lines (spec §3, §12), once, at the first gameplay scene: all plugins have
        // loaded by then, so other mods' patches are visible.
        private static void ReportStartup()
        {
            Log.Info($"{Name} {Version}: on [{List(On)}]; off [{List(Off)}]; failed [{List(Failed)}]");
            if (_buyers) Log.Info($"Buyers: {BuyerRules.ConfiguredCount()} traders with extra goods.");
            if (_access) Log.Info("Invoices: opens from the Notes page (Drop).");
            if (_window && !Cfg.InvoiceList.NotesPageKey.Value && !Cfg.InvoiceList.PauseMenuEntry.Value && !Keys.Bound(Cfg.InvoiceList.OpenKey))
                Log.Info("Invoices: no way to open the list is configured (NotesPageKey, PauseMenuEntry and OpenKey are all off).");

            if (_stock)
            {
                Compat.Foreign(Vanilla.ConstructSpawnDb, "the stock boost", "may not apply");
                if (Cfg.Stock.ApplyToNextRestock.Value) Compat.Foreign(Vanilla.SpawnItems, "the next-restock refresh", "may not apply");
            }
            if (_buyers) Compat.Foreign(Vanilla.TradeCheckIfAllowed, "the extra buyers", "may not apply");
            if (_doors)
            {
                Compat.Foreign(Vanilla.DoorChangelevelUseMessage, "opening hours on doors", "may not show");
                Compat.Foreign(Vanilla.DoorUseMessage, "opening hours on doors", "may not show");
            }
            if (_stalls) Compat.Foreign(Vanilla.TradeUseMessage, "opening hours on stalls", "may not show");
            if (_washInfo) Compat.Overlap(Vanilla.ShowRecipe, "the dishwasher odds");
            if (_washSkips) Compat.Foreign(Vanilla.DishwasherCheckItems, "the \"Not washed\" note", "may not show");
            if (_certs) Compat.Foreign(Vanilla.FindValidCertificates, "the certificate fix", "may not apply");
            if (_marks)
            {
                Compat.Foreign(Vanilla.InvoiceDataToString, "the invoice markers", "may not show");
                Compat.Overlap(Vanilla.UpdateInfo, "the invoice tag");
            }
            if (_window)
            {
                Compat.Overlap(Vanilla.PauseNavUpdate, "the Invoices window's input hand-over");
                Compat.Foreign(Vanilla.HandleBackButtonClick, "Back in the Invoices window", "may resume the game instead");
            }
            if (_access) Compat.Overlap(Vanilla.GetButtonInfo, "the Notes page key hint");
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            Step("confirm", Confirm.Tick);
            Step("stock reread", StockRereadTick);
            Step("access", NotesPageInput.Tick);
            Step("pause entry", PauseEntry.Tick);
            Step("window", InvoiceWindow.Tick);
            Step("skip note", SkipNote.Tick);
        }

        private void OnGUI()
        {
            if (!InvoiceWindow.IsOpen) return;
            try { InvoiceWindow.Draw(Skin.Begin()); }
            catch (Exception e) { Log.Exception("draw", e); }
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnSettingChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.PlayerWillChangeLevel -= OnPlayerWillChangeLevel;
            SaveController.LoadingDone -= OnLoadingDone;
            InputBlock.Release();
        }
    }
}
