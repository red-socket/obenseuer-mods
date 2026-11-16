using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TidyInventory
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.tidyinventory";
        public const string Name = "Tidy Inventory";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;
        internal static Plugin Instance;

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();
        private static bool _compatReported;

        // Each feature is patched as its own group (spec §8.1), only when it is on at launch. A group checks the game
        // members it needs first; a missing member or a patch exception unpatches that group's own targets, logs one
        // error and leaves the other groups working. Every patch body also re-reads its config entry live. Everything
        // is patched here in Awake, so small targets (CantDrag, the instance SlotHasSpaceForItem) are patched before the
        // JIT can inline them. Hover dispose, daily emptying and the inventory Combine button have no patches of their
        // own: they run from Update and game events.
        private void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // U-013 / S-056 Pinned Slots (spec §3).
            PinPatches.Patched = PatchGroup("pins", Cfg.Pins.Enabled.Value,
                Needs(Vanilla.InventoryStart, Vanilla.SortSlots, Vanilla.MoveToOtherInventory, Vanilla.SlotDropItem, Vanilla.CantDrag,
                    Vanilla.ItemDropItem, Vanilla.ShowSlider, Vanilla.TradeCanSell, Vanilla.TradeSpecialCanSell, Vanilla.QuickMove,
                    Vanilla.CheckItemMetas, Vanilla.Equip, Vanilla.GlobalStateOnLoadingGame, Vanilla.RecyclingOpen, Vanilla.GlobalDragField),
                Targets(Vanilla.InventoryStart, Vanilla.SortSlots, Vanilla.MoveToOtherInventory, Vanilla.SlotDropItem, Vanilla.CantDrag,
                    Vanilla.ItemDropItem, Vanilla.ShowSlider, Vanilla.TradeCanSell, Vanilla.TradeSpecialCanSell, Vanilla.QuickMove,
                    Vanilla.CheckItemMetas, Vanilla.Equip, Vanilla.GlobalStateOnLoadingGame),
                typeof(InventoryStartPatch), typeof(SortSlotsPatch), typeof(MoveToOtherInventoryPatch), typeof(SlotDropItemPatch),
                typeof(CantDragPatch), typeof(ItemDropItemPatch), typeof(ShowSliderPatch), typeof(TradeCanSellPatch),
                typeof(TradeSpecialCanSellPatch), typeof(QuickMovePatch), typeof(CheckItemMetasPatch), typeof(EquipPatch),
                typeof(GlobalStateLoadPatch));
            bool pins = PinPatches.Patched;
            PinPatches.ReturnPatched = PatchGroup("pin return", pins && Cfg.Pins.ReturnItemsToPin.Value,
                Needs(Vanilla.AddItems, Vanilla.AddItemAndReturnRemaining, Vanilla.UseLiquid),
                Targets(Vanilla.AddItems, Vanilla.AddItemAndReturnRemaining, Vanilla.UseLiquid),
                typeof(AddItemsPatch), typeof(AddItemAndReturnRemainingPatch), typeof(UseLiquidPatch));
            PinPatches.CapacityPatched = PatchGroup("pin capacity", pins && Cfg.Pins.CountReservedAsFull.Value,
                Needs(Vanilla.SlotHasSpaceForItem, Vanilla.AddItemDialogue, Vanilla.AddItemDialogueAmount, Vanilla.GrowItemGetItem),
                Targets(Vanilla.SlotHasSpaceForItem, Vanilla.AddItemDialogue, Vanilla.AddItemDialogueAmount, Vanilla.GrowItemGetItem),
                typeof(SlotHasSpacePatch), typeof(AddItemDialoguePatch), typeof(AddItemDialogueAmountPatch), typeof(GrowItemGetItemPatch));
            PatchGroup("pin pad drag", pins, Needs(Vanilla.StartDragging), Targets(Vanilla.StartDragging), typeof(StartDraggingPatch));
            PinInput.KeyPatched = PatchGroup("pin key", pins,
                Needs(Vanilla.ItemPointerDown, Vanilla.ItemPointerClick, Vanilla.ItemBeginDrag, Vanilla.ItemDrag, Vanilla.ItemEndDrag, Vanilla.SlotPointerClick),
                Targets(Vanilla.ItemPointerDown, Vanilla.ItemPointerClick, Vanilla.ItemBeginDrag, Vanilla.ItemDrag, Vanilla.ItemEndDrag, Vanilla.SlotPointerClick),
                typeof(ItemPointerDownPatch), typeof(ItemPointerClickPatch), typeof(ItemBeginDragPatch), typeof(ItemDragPatch),
                typeof(ItemEndDragPatch), typeof(SlotPointerClickPatch));
            PinOverlay.CosmeticsPatched = PatchGroup("pin cosmetics", pins,
                Needs(Vanilla.SlotPointerEnter, Vanilla.UpdateInfo, Vanilla.CantUseText, Vanilla.DropButton),
                Targets(Vanilla.SlotPointerEnter, Vanilla.UpdateInfo), typeof(SlotPointerEnterPatch), typeof(UpdateInfoPatch));

            // U-011 / S-092 Empty and S-062 storage Combine (spec §4.3).
            StorageButtons.Patched = PatchGroup("storage buttons", Cfg.Bins.Enabled.Value || Cfg.Combine.StorageButton.Value,
                Needs(Vanilla.UpdateStoragePanelUI, Vanilla.SlotPanel, Vanilla.StorageName),
                Targets(Vanilla.UpdateStoragePanelUI), typeof(UpdateStoragePanelUIPatch));
            NavMovePatch.Patched = PatchGroup("gamepad", (StorageButtons.Patched || Cfg.Combine.InventoryButton.Value) && Cfg.Bins.Gamepad.Value,
                Needs(Vanilla.NavMove, Vanilla.SelectButton, Vanilla.SwitchToNextStorageTakeAllButton, Vanilla.SwitchToNextInventorySortButton,
                    Vanilla.StoragePowerButtonActive, Vanilla.InventorySortButton),
                Targets(Vanilla.NavMove), typeof(NavMovePatch));

            // S-092 despawn protection (spec §4.6).
            DespawnGuard.Patched = PatchGroup("despawn", Cfg.Despawn.ProtectValuables.Value,
                Needs(Vanilla.CollectibleExpire, Vanilla.ExpirationTimer), Targets(Vanilla.CollectibleExpire), typeof(CollectibleExpirePatch));

            // S-062 stolen stacks (spec §5.1).
            StolenMerge.Patched = PatchGroup("stolen stacks", Cfg.StolenStacks.MergeSameOwner.Value,
                Needs(Vanilla.CompareItemMetas, Vanilla.SlotHasSpaceForItemStatic), Targets(Vanilla.CompareItemMetas, Vanilla.SlotHasSpaceForItemStatic),
                typeof(SlotSpaceTargetPatch), typeof(CompareItemMetasPatch));

            // S-106 blue duck (spec §6).
            DuckDiscard.Patched = PatchGroup("duck", Cfg.Duck.Enabled.Value,
                Needs(Vanilla.TaskItemSelected, Vanilla.TaskItemUnselected, Vanilla.ActivateNavigationSelection, Vanilla.TaskItemOf,
                    Vanilla.NavRow, Vanilla.NavSelected, Vanilla.ItemRows, Vanilla.DuckAchievementEnabled),
                Targets(Vanilla.TaskItemSelected, Vanilla.TaskItemUnselected, Vanilla.ActivateNavigationSelection),
                typeof(TaskItemSelectedPatch), typeof(TaskItemUnselectedPatch), typeof(ActivateNavigationSelectionPatch));
            DuckDiscard.HintPatched = PatchGroup("duck hint", DuckDiscard.Patched,
                Needs(Vanilla.GetButtonInfoTab0), Targets(Vanilla.GetButtonInfoTab0), typeof(DuckHintPatch));

            // All static events: subscribed once.
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.SavingStarted += OnSavingStarted;
            SaveController.LoadingDone += OnLoadingDone;
            TimeOfDayAzure.DayChanged += OnDayChanged;

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
            Step("scene (confirm)", Confirm.ResetAll);
            Step("scene (pins)", () =>
            {
                PinPatches.OnSceneLoaded();
                PinOverlay.OnSceneLoaded();
                PinStore.ReadThisScene = false;
                // Main menu and intro: no Game Logic, no character. A new game starts with no pins.
                if (GlobalState.instance == null) PinStore.Clear("no game running");
            });
            Step("scene (buttons)", () =>
            {
                StorageButtons.OnSceneLoaded();
                InventoryCombine.OnSceneLoaded();
            });
            Step("scene (duck)", DuckDiscard.OnSceneLoaded);
            Step("scene (daily emptying)", AutoEmpty.OnSceneLoaded);
            Step("scene (gate)", PlayGate.OnSceneLoaded);
            Step("scene (keys)", Keys.CheckCollisions);
            if (!_compatReported && GlobalState.instance != null)
            {
                _compatReported = true;
                Step("compat", ReportCompat);
            }
        }

        // The game does not guard SavingStarted (SRC/SaveController.cs:519): a throw would abort the save.
        private static void OnSavingStarted()
        {
            try { PinStore.OnSavingStarted(); }
            catch (Exception e) { Log.Exception("saving", e); }
        }

        private static void OnLoadingDone()
        {
            Step("loading done (gate)", PlayGate.OnLoadingDone);
            Step("loading done (pins)", PinStore.OnLoadingDone);
            Step("loading done (daily emptying)", AutoEmpty.OnLoadingDone);
        }

        private static void OnDayChanged(int day) => Step("day change", () => AutoEmpty.OnDayChanged(day));

        // Other mods' skipping patches on what we patch or call (spec §8.2), once all plugins have loaded.
        private static void ReportCompat()
        {
            if (PinPatches.Patched)
            {
                Compat.Foreign(Vanilla.SortSlots, "Sort skipping pinned slots", "may not apply");
                Compat.Foreign(Vanilla.MoveToOtherInventory, "the pin's move lock", "may not apply");
                Compat.Foreign(Vanilla.SlotDropItem, "the pin's drag and drop rules", "may not apply");
                Compat.Foreign(Vanilla.TradeCanSell, "the pin's sell lock", "may not apply");
                Compat.Foreign(Vanilla.TradeSpecialCanSell, "the pin's sell lock", "may not apply");
            }
            if (PinPatches.ReturnPatched)
            {
                Compat.Foreign(Vanilla.AddItems, "new copies going to pinned slots", "may not apply");
                Compat.Foreign(Vanilla.AddItemAndReturnRemaining, "new copies going to pinned slots", "may not apply");
            }
            if (StorageButtons.Patched)
            {
                Compat.Overlap(Vanilla.UpdateStoragePanelUI, "the Empty and Combine buttons");
                Compat.Overlap(Vanilla.SlotRemoveItem, "Empty");
            }
            if (NavMovePatch.Patched) Compat.Foreign(Vanilla.NavMove, "the controller route to our buttons", "may not apply");
            if (DespawnGuard.Patched) Compat.Foreign(Vanilla.CollectibleExpire, "despawn protection", "may not apply");
            if (StolenMerge.Patched) Compat.Overlap(Vanilla.CompareItemMetas, "the stolen stack merge");
            if (DuckDiscard.Patched) Compat.Foreign(Vanilla.ActivateNavigationSelection, "the controller duck discard", "may not apply");
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
            Step("pin key", PinInput.Tick);
            Step("bin dispose", HoverDispose.Tick);
            Step("duck key", DuckDiscard.Tick);
            Step("confirm", Confirm.Tick);
            Step("pin read", PinStore.FallbackRead);
            Step("pin empty", PinStore.Tick);
            Step("inventory combine", InventoryCombine.Tick);
        }

        private void LateUpdate()
        {
            Step("pin overlay", PinOverlay.Poll);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.SavingStarted -= OnSavingStarted;
            SaveController.LoadingDone -= OnLoadingDone;
            TimeOfDayAzure.DayChanged -= OnDayChanged;
        }
    }
}
