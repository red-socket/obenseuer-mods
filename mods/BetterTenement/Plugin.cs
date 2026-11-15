using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterTenement
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.bettertenement";
        public const string Name = "Better Tenement";
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
            Removal.Patched = PatchGroup("materials", Cfg.FixRemainder.Value || Cfg.StolenFirst.Value,
                typeof(RemoveRequiredItemsPatch), typeof(TakeRequiredItemsPatch));
            StoragePanel.Patched = PatchGroup("stash and storage panel", Cfg.Stash.Value || Cfg.Chests.Value || Cfg.PrepareForRemoval.Value,
                typeof(StoragePanelPatch), typeof(NavMovePatch));
            StockInfo.SnapshotPatched = PatchGroup("snapshot refresh", Cfg.RefreshSnapshot.Value,
                typeof(ExitStoragePatch), typeof(SaveTenementPatch));
            Card.Patched = PatchGroup("upgrade cards", Cfg.CardStockMarkers.Value || Cfg.LedgerCardLine.Value,
                typeof(UpgradeCardPatch));
            StockInfo.MarkersPatched = Card.Patched && PatchGroup("card stock markers", Cfg.CardStockMarkers.Value,
                typeof(RequiredItemPatch));
            Chests.Patched = PatchGroup("resource chests", Cfg.Chests.Value || Cfg.PrepareForRemoval.Value,
                typeof(StorageStartPatch), typeof(StorageLoadPatch), typeof(GetStoragePatch), typeof(SwitchSlotPatch));
            if (Cfg.TimerFix.Value && !TimerFix.CheckShape()) _off.Add("renovation timer (game code changed)");
            else TimerFix.Patched = PatchGroup("renovation timer", Cfg.TimerFix.Value, typeof(TimerSetterPatch));
            Doctor.PanelPatched = PatchGroup("Doctor panel details", Cfg.DoctorPanelDetails.Value, typeof(ApartmentInfoPatch));
            LedgerCard.TooltipPatched = PatchGroup("Ledger tooltip", Cfg.LedgerCategoryTooltip.Value,
                typeof(UpgradeButtonTooltipPatch), typeof(TooltipInfoPatch));

            SceneManager.sceneLoaded += OnSceneLoaded;
            Camera.onPostRender += DoctorOutline.OnPostRender;

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

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Step("sceneLoaded", Game.OnSceneLoaded);
            Step("sceneLoaded (stash)", Stash.OnSceneLoaded);
            Step("sceneLoaded (scroll view)", StorageScroll.OnSceneLoaded);
            Step("sceneLoaded (chest counter)", Chests.OnSceneLoaded);
            Step("sceneLoaded (Ledger)", LedgerPanel.OnSceneLoaded);
            Step("sceneLoaded (Ledger tooltip)", LedgerCard.OnSceneLoaded);
            Step("sceneLoaded (Doctor)", DoctorWindow.OnSceneLoaded);
        }

        private static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            Step("stash hotkey", Stash.Update);
            Step("chest header", Chests.Update);
            Step("storage page keys", StorageScroll.Update);
            Step("Ledger", LedgerPanel.Update);
            Step("Doctor", DoctorWindow.Update);
        }

        private void OnGUI()
        {
            if (!LedgerPanel.Visible && !DoctorWindow.Open) return;
            try
            {
                Vector2 screen = Skin.Begin();
                try { LedgerPanel.OnGUI(screen); }
                catch (Exception e)
                {
                    // GUILayout throws ExitGUIException on purpose; it must propagate.
                    if (e is ExitGUIException) throw;
                    Log.Exception("Ledger OnGUI", e);
                }
                try { DoctorWindow.OnGUI(screen); }
                catch (Exception e)
                {
                    if (e is ExitGUIException) throw;
                    Log.Exception("Doctor OnGUI", e);
                }
            }
            catch (Exception e)
            {
                if (e is ExitGUIException) throw;
                Log.Exception("OnGUI", e);
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Camera.onPostRender -= DoctorOutline.OnPostRender;
            DoctorWindow.OnDestroy();
            InputBlock.Release();
        }
    }
}
