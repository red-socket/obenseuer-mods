using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace UnofficialPatch
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.unofficialpatch";
        public const string Name = "Unofficial Patch";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Plugin Instance;
        internal static Harmony Harmony;

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();

        // Each fix is patched as its own group (spec §0.1). A group that is off in the config is not patched (it starts
        // at the next launch); a group that fails logs one error and leaves only that fix off.
        private void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // S-003 (spec §2).
            BiweeklyRent.Patched = PatchGroup("biweekly rent", Cfg.BiweeklyFix.Value, null, typeof(GetRentPaymentPatch));

            // S-033 (spec §3).
            ScamHold.Patched = PatchGroup("scam payout hold", Cfg.HoldPayoutWhenFrozen.Value, Needs(Vanilla.HandleMail),
                typeof(HandleMailPatch), typeof(CreateNotificationPatch));
            DuplicateAccounts.Patched = PatchGroup("duplicate bank accounts", Cfg.MergeDuplicateAccounts.Value, null, typeof(MoneyLoadPatch));

            // S-085 (spec §4).
            ExchangeTuning.Patched = PatchGroup("exchange tuning", Cfg.RestoreExchangeTuning.Value, null, typeof(BankLoadPatch));

            // S-041 (spec §5).
            InputGuard.Patched = PatchGroup("input guard: keyboard/mouse only", Cfg.KeyboardMouseOnly.Value,
                Needs(Vanilla.InputLateUpdate, Vanilla.HandleItemInteraction, Vanilla.VkChangeControlsInfo, Vanilla.RightTriggerPressed, Vanilla.ListMovementInput),
                typeof(ChangeInputTypePatch), typeof(InputLateUpdatePatch), typeof(ChangeControlsInfoPatch), typeof(HandleItemInteractionPatch),
                typeof(VkChangeControlsInfoPatch), typeof(RightTriggerPressedPatch), typeof(ListMovementInputPatch));
            if (Cfg.KeyboardMouseOnly.Value || Cfg.AxisDeadzone.Value)
            {
                string coverage = AxisFilter.PatchAll(Harmony);
                AxisFilter.Patched = coverage != null;
                if (AxisFilter.Patched) _on.Add("input guard: axis filter (" + coverage + ")");
                else _failed.Add("input guard: axis filter");
            }
            else _off.Add("input guard: axis filter");

            // S-068 (spec §6).
            ConsoleGuard.Patched = PatchGroup("console guard", Cfg.BlockGameKeysWhileOpen.Value,
                Needs(Vanilla.ShouldToggleDevConsole, Vanilla.VkUpdate, Vanilla.UseDevConsole),
                typeof(GetKeyDownPatch), typeof(GetKeyPatch), typeof(GetKeyWithHoldPatch), typeof(GetKeyDownKeyboardOnlyPatch),
                typeof(GetAuxiliaryReloadDownPatch), typeof(ShouldToggleDevConsolePatch), typeof(VirtualKeyboardUpdatePatch));
            ConsoleGuard.PausePatched = PatchGroup("console guard: pause menu", Cfg.GuardPauseMenu.Value, Needs(Vanilla.PauseNavUpdate),
                typeof(PauseNavigationUpdatePatch));

            // S-073 (spec §7).
            MovementToggles.SprintPatched = PatchGroup("sprint toggle", Cfg.KeepSprintToggle.Value, Needs(Vanilla.Walking),
                typeof(CharacterAwakePatch));
            MovementToggles.CrouchOn = Plain("crouch toggle", Vanilla.Crouching != null, Cfg.KeepCrouchToggle.Value);
            KeyConflicts.Patched = PatchGroup("key conflict warning", Cfg.WarnKeyConflicts.Value, Needs(Vanilla.InputStart, Vanilla.BindingKey),
                typeof(InputManagerStartPatch), typeof(SaveKeyBindingsPatch), typeof(GetKeyBindingsPatch));
            Brightness.SceneLoadOn = Plain("brightness on load", true, Cfg.ReapplyBrightnessOnSceneLoad.Value);
            Brightness.EffectsPatched = PatchGroup("brightness after effects", Cfg.ReapplyBrightnessAfterEffects.Value, Needs(Vanilla.BeautifyList),
                typeof(ChangeBeautifyProfilesPatch), typeof(ChangeBeautifyProfilesToDefaultPatch));

            // S-051 (spec §8).
            MachineResidue.Patched = PatchGroup("machine residue", Cfg.ClearMachineResidue.Value, Needs(Vanilla.ClearTraceLiquids),
                typeof(ClearTraceLiquidsPatch));
            TankResidue.Patched = PatchGroup("tank residue", Cfg.ClearTankResidue.Value, null,
                typeof(LiquidStorageLoadPatch), typeof(ItemCanBeFilledPatch), typeof(ItemCanBeUnfilledPatch), typeof(HasSaveableContentPatch));

            // S-093 (spec §9).
            bool spoil = Cfg.IceCreamFix.Value || Cfg.CoffeeFix.Value;
            SpoilageData.Patched = PatchGroup("spoilage data", spoil, Needs(Vanilla.ItemDbAwake, Vanilla.ItemDbOnEnable),
                typeof(ItemDatabaseAwakePatch), typeof(ItemDatabaseOnEnablePatch), typeof(ItemDatabaseReloadPatch));
            SpoilageData.HealPatched = PatchGroup("spoilage: existing items", spoil && Cfg.HealExistingItems.Value, null,
                typeof(RemoveMinutesFromIntegrityPatch));
            CoffeeVolume.Patched = PatchGroup("coffee volume", Cfg.CoffeeVolumeFix.Value, Needs(Vanilla.RecipeLoadDatabase),
                typeof(RecipeLoadDatabasePatch));

            // S-101 (spec §10).
            ShelfFilters.Patched = PatchGroup("shelf filters", Cfg.FixShelfFilters.Value, null, typeof(ShelfStartPatch));
            RestockLog.Patched = PatchGroup("restock log", Cfg.LogFailedRestock.Value, Needs(Vanilla.AddItemToStorageSlot),
                typeof(SpawnItemsPatch), typeof(AddItemToStorageSlotPatch));

            // S-058 (spec §11).
            CatHunting.Patched = PatchGroup("cat hunting", Cfg.HuntRats.Value, null, typeof(HuntTargetPatch));
            CatMachineGuard.Patched = PatchGroup("owned cats and machines", Cfg.ProtectOwnedCatsFromMachines.Value, Needs(Vanilla.TriggerHurtDamage),
                typeof(TriggerHurtDamagePatch));

            // S-065 (spec §12).
            AchievementDoctor.Patched = PatchGroup("achievement doctor", Cfg.AchievementDoctor.Value,
                Needs(Vanilla.AchAwake, Vanilla.AchInitialized, Vanilla.AchId, Vanilla.AchProgress, Vanilla.AchMaxProgress, Vanilla.AchTriggered, Vanilla.AchCommitted),
                typeof(AchievementsAwakePatch), typeof(NewGameStartPatch), typeof(AchievementsLoadPatch));

            // S-102 (spec §13).
            RenovationSafety.Patched = PatchGroup("bed safety during renovation", Cfg.KeepWallSafetyDuringRenovation.Value, Needs(Vanilla.PlayerSafetyFactor),
                typeof(SleepGetTitlePatch), typeof(SleepUsePatch), typeof(CalculateEventPatch));

            // U-014: no patches; runs from Update and reads its entries live.
            NpcDoors.Ready = Plain("npc doors", Vanilla.InWaypointChain != null && Vanilla.DoorDirection != null &&
                Vanilla.DoorSpawnAngles != null && Vanilla.DoorStartDone != null, Cfg.OpenDoors.Value);

            // U-015. Also patched (log only) when only VerboseLogging is on at launch, so the diagnostic can show what
            // closes the door in vanilla; the skip itself reads KeepCabinDoorOpen live.
            bool keep = Cfg.KeepCabinDoorOpen.Value;
            CabinDoors.Patched = PatchGroup(keep ? "cabin doors" : "cabin doors (log only)", keep || Cfg.VerboseLogging.Value,
                Needs(Vanilla.DoorClose, Vanilla.DoorUseDoor, Vanilla.ChairStopSitEnd, Vanilla.DoorDirection),
                typeof(DoorClosePatch), typeof(DoorUseDoorPatch), typeof(ChairStopSitEndPatch));
            if (CabinDoors.Patched)
                PatchGroup("cabin doors: diagnostic", true, Needs(Vanilla.ToiletSitEnd, Vanilla.DoorToggleInterrupt, Vanilla.DoorTriggerExit),
                    typeof(ToiletSitEndPatch), typeof(DoorToggleInterruptPatch), typeof(DoorTriggerExitPatch));

            SaveController.LoadingDone += OnLoadingDone;
            SaveController.PlayerWillChangeLevel += OnPlayerWillChangeLevel;
            SaveController.SavingStarted += OnSavingStarted;

            // The game may have loaded its databases and InputManager before this plugin; apply those fixes now too.
            Step("startup (spoilage data)", SpoilageData.ApplyItemFixes);
            Step("startup (coffee volume)", CoffeeVolume.Apply);
            Step("startup (key conflicts)", KeyConflicts.MarkDirty);

            Log.Info($"{Name} {Version} on game {Application.version}; fixes on: {List(_on)}; off: {List(_off)}; failed: {List(_failed)}");
        }

        private static string List(List<string> l) => l.Count == 0 ? "—" : string.Join(", ", l);

        internal static object[] Needs(params object[] members) => members;

        private bool PatchGroup(string name, bool enabled, object[] needs, params Type[] types)
        {
            if (!enabled)
            {
                _off.Add(name);
                return false;
            }
            try
            {
                if (needs != null)
                    foreach (var n in needs)
                        if (n == null) throw new MissingMemberException("a game member this fix needs is missing (see the error above)");
                foreach (var t in types) Harmony.CreateClassProcessor(t).Patch();
                _on.Add(name);
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Patching '{name}' failed; that fix is off this session: {e}");
                _failed.Add(name);
                return false;
            }
        }

        // A fix with no patches of its own (it runs from the load events and reads its ConfigEntry live, so it can be
        // switched on or off mid-session). Returns whether it can run at all; the lists show the launch-time setting.
        private bool Plain(string name, bool ready, bool enabled)
        {
            if (!ready) _failed.Add(name);
            else if (!enabled) _off.Add(name);
            else _on.Add(name);
            return ready;
        }

        private static void OnLoadingDone()
        {
            Step("load (movement toggles)", MovementToggles.OnLoadingDone);
            Step("load (brightness)", Brightness.OnLoadingDone);
            Step("load (spoilage)", SpoilageData.OnLoadingDone);
            Step("load (bed safety)", RenovationSafety.OnLoadingDone);
            Step("load (tank residue)", TankResidue.OnLoadingDone);
            Step("load (cats)", CatMachineGuard.OnLoadingDone);
            Step("load (npc doors)", NpcDoors.OnLevelChange);
            Step("load (cabin doors)", CabinDoors.OnLoadingDone);
        }

        private static void OnPlayerWillChangeLevel()
        {
            Step("level change (movement toggles)", MovementToggles.Capture);
            Step("level change (npc doors)", NpcDoors.OnLeaving);
        }

        private static void OnSavingStarted() => Step("save (npc doors)", NpcDoors.OnSaving);

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            if (KeyConflicts.Dirty) Step("key conflicts", KeyConflicts.Check);
            Step("npc doors", NpcDoors.Update);
            Step("cabin doors", CabinDoors.Update);
        }

        private void OnDestroy()
        {
            SaveController.LoadingDone -= OnLoadingDone;
            SaveController.PlayerWillChangeLevel -= OnPlayerWillChangeLevel;
            SaveController.SavingStarted -= OnSavingStarted;
        }
    }
}
