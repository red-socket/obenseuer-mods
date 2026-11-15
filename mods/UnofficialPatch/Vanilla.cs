using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UnofficialPatch
{
    // Private game members, read through Harmony's AccessTools rather than the publicized reference:
    // docs/modding-guide.md §4 notes that Mono's tolerance for direct private access was never tested.
    // A member that is missing logs one error and stays null; Plugin.PatchGroup checks the members each group needs,
    // so only the groups that use a missing member fail (spec §1).
    internal static class Vanilla
    {
        // ---- Fields ----

        // SleepEventController._playerSafetyFactor (SRC/SleepEventController.cs:55), protected, saved.
        internal static readonly AccessTools.FieldRef<SleepEventController, float> PlayerSafetyFactor =
            Ref<SleepEventController, float>("_playerSafetyFactor");

        // OpenSewerCharacterController.walking / crouching (SRC/OpenSewerCharacterController.cs:46, :52). walking=false is sprinting.
        internal static readonly AccessTools.FieldRef<OpenSewerCharacterController, bool> Walking =
            Ref<OpenSewerCharacterController, bool>("walking");
        internal static readonly AccessTools.FieldRef<OpenSewerCharacterController, bool> Crouching =
            Ref<OpenSewerCharacterController, bool>("crouching");

        // VirtualKeyboardUI.useDevConsole (SRC/VirtualKeyboardUI.cs:30): the on-screen keyboard types into the console.
        internal static readonly AccessTools.FieldRef<VirtualKeyboardUI, bool> UseDevConsole = Ref<VirtualKeyboardUI, bool>("useDevConsole");

        // Achievements (SRC/Achievements.cs:279-288, :14-37): protected internal members of the nested Achievement.
        internal static readonly AccessTools.FieldRef<Achievements, bool> AchInitialized = Ref<Achievements, bool>("initialized");
        internal static readonly AccessTools.FieldRef<Achievements.Achievement, string> AchId = Ref<Achievements.Achievement, string>("API_ID");
        internal static readonly AccessTools.FieldRef<Achievements.Achievement, float> AchProgress = Ref<Achievements.Achievement, float>("progress");
        internal static readonly AccessTools.FieldRef<Achievements.Achievement, float> AchMaxProgress = Ref<Achievements.Achievement, float>("maxprogress");
        internal static readonly AccessTools.FieldRef<Achievements.Achievement, bool> AchTriggered = Ref<Achievements.Achievement, bool>("_triggered");
        internal static readonly AccessTools.FieldRef<Achievements.Achievement, bool> AchCommitted = Ref<Achievements.Achievement, bool>("_committedToAPI");

        // SteamManager.s_instance / m_bInitialized (SRC/SteamManager.cs:9-13). The public SteamManager.Initialized getter
        // creates a new SteamManager when none exists, so it is never called.
        private static readonly FieldInfo SteamInstanceField = Field(typeof(SteamManager), "s_instance");
        private static readonly AccessTools.FieldRef<SteamManager, bool> SteamInitialized = Ref<SteamManager, bool>("m_bInitialized");

        // Logic_liquidFiller.liquidstorage (SRC/Logic_liquidFiller.cs:27): the tank an upgrade fills.
        internal static readonly AccessTools.FieldRef<Logic_liquidFiller, LiquidStorage> FillerStorage =
            Ref<Logic_liquidFiller, LiquidStorage>("liquidstorage");

        // EffectsController.beautifyList (SRC/EffectsController.cs:12).
        internal static readonly AccessTools.FieldRef<EffectsController, List<EffectBeautify>> BeautifyList =
            Ref<EffectsController, List<EffectBeautify>>("beautifyList");

        // KeyBindings.GetKey(string) (SRC/KeyBindings.cs:267), private. CheckKey and InputManager.GetKeyCode are never
        // used for reading: both switch the input type as a side effect.
        internal static readonly Func<KeyBindings, string, KeyCode> BindingKey =
            Delegate<Func<KeyBindings, string, KeyCode>>(typeof(KeyBindings), "GetKey", new[] { typeof(string) });

        // ---- Patch targets (private or overloaded) ----

        internal static readonly MethodInfo HandleMail = Method(typeof(FakeInvoiceReceiver), "HandleMail", new[] { typeof(MailController.Mail) });
        internal static readonly MethodInfo AddItemToStorageSlot = Method(typeof(Storage), "AddItemToStorageSlot",
            new[] { typeof(ItemReference), typeof(int), typeof(int), typeof(object[]), typeof(List<int>) });
        internal static readonly MethodInfo TriggerHurtDamage = Method(typeof(TriggerHurt), "Damage", new[] { typeof(int), typeof(Stats) });
        internal static readonly MethodInfo ClearTraceLiquids = Method(typeof(CraftingBase), "ClearTraceLiquids", Type.EmptyTypes);
        internal static readonly MethodInfo RecipeLoadDatabase = Method(typeof(RecipeDatabase), "LoadDatabase", Type.EmptyTypes);
        internal static readonly MethodInfo ItemDbAwake = Method(typeof(ItemDatabase), "Awake", Type.EmptyTypes);
        internal static readonly MethodInfo ItemDbOnEnable = Method(typeof(ItemDatabase), "OnEnable", Type.EmptyTypes);
        internal static readonly MethodInfo InputLateUpdate = Method(typeof(InputManager), "LateUpdate", Type.EmptyTypes);
        internal static readonly MethodInfo InputStart = Method(typeof(InputManager), "Start", Type.EmptyTypes);
        internal static readonly MethodInfo ShouldToggleDevConsole = Method(typeof(DeveloperConsole), "ShouldToggleDevConsole", Type.EmptyTypes);
        internal static readonly MethodInfo PauseNavUpdate = Method(typeof(PauseNavigationHandler), "Update", Type.EmptyTypes);
        internal static readonly MethodInfo RightTriggerPressed = Method(typeof(InventoryNavigationHandler), "RightTriggerPressed", Type.EmptyTypes);
        internal static readonly MethodInfo HandleItemInteraction = Method(typeof(InventoryNavigationHandler), "HandleItemInteraction",
            new[] { typeof(InputManager.InputType) });
        internal static readonly MethodInfo VkChangeControlsInfo = Method(typeof(VirtualKeyboardUI), "ChangeControlsInfoIfNecessary",
            new[] { typeof(InputManager.InputType) });
        internal static readonly MethodInfo VkUpdate = Method(typeof(VirtualKeyboardUI), "Update", Type.EmptyTypes);
        internal static readonly MethodInfo ListMovementInput = Method(typeof(InteractableList), "HandleMovementInput", Type.EmptyTypes);
        internal static readonly MethodInfo AchAwake = Method(typeof(Achievements), "Awake", Type.EmptyTypes);

        // ---- Helpers ----

        internal static bool SteamReady()
        {
            if (SteamInstanceField == null || SteamInitialized == null) return false;
            var inst = SteamInstanceField.GetValue(null) as SteamManager;
            return inst != null && SteamInitialized(inst);
        }

        // Declared method by name (and parameter types, when given). Logs and returns null when missing or ambiguous.
        internal static MethodInfo Method(Type t, string name, Type[] args = null)
        {
            try
            {
                var m = args == null ? AccessTools.DeclaredMethod(t, name) : AccessTools.DeclaredMethod(t, name, args);
                if (m == null) Log.Error($"{t.Name}.{name} not found; the fix using it is off.");
                return m;
            }
            catch (Exception e) { Log.Error($"{t.Name}.{name} not found ({e.Message}); the fix using it is off."); return null; }
        }

        private static FieldInfo Field(Type t, string name)
        {
            var f = AccessTools.Field(t, name);
            if (f == null) Log.Error($"{t.Name}.{name} not found; the fix using it is off.");
            return f;
        }

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Log.Error($"{typeof(T).Name}.{name} not found ({e.Message}); the fix using it is off."); return null; }
        }

        private static D Delegate<D>(Type t, string name, Type[] args) where D : Delegate
        {
            try
            {
                var m = AccessTools.Method(t, name, args);
                if (m == null)
                {
                    Log.Error($"{t.Name}.{name} not found; the fix using it is off.");
                    return null;
                }
                return AccessTools.MethodDelegate<D>(m, null, false);
            }
            catch (Exception e) { Log.Error($"{t.Name}.{name} not found ({e.Message}); the fix using it is off."); return null; }
        }
    }
}
