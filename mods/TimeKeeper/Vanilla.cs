using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TimeKeeper
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4, spec §5). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup checks the members each group needs, so only the feature using it fails (spec §4.1).
    // Optional members log one warning and the feature runs without the part that needs them.
    internal static class Vanilla
    {
        // ---- Weekday (P1, P2) ----

        // ShowTime.timeMesh / hideDayOfWeek / showDayOfWeek (SRC/ShowTime.cs:9, :18, :26). A world clock has a timeMesh.
        internal static readonly AccessTools.FieldRef<ShowTime, TextMeshPro> TimeMesh = Ref<ShowTime, TextMeshPro>("timeMesh");
        internal static readonly AccessTools.FieldRef<ShowTime, bool> HideDayOfWeek = Ref<ShowTime, bool>("hideDayOfWeek");
        internal static readonly AccessTools.FieldRef<ShowTime, bool> ShowDayOfWeek = Ref<ShowTime, bool>("showDayOfWeek");

        // ShowTime.DayOfWeek() (SRC/ShowTime.cs:116), private: "Time: " or "<Weekday> ".
        internal static readonly MethodInfo DayOfWeek = Method(typeof(ShowTime), "DayOfWeek", Type.EmptyTypes);

        // CharacterSlotController.equipped / currentItem (SRC/CharacterSlotController.cs:15, :18), protected.
        internal static readonly AccessTools.FieldRef<CharacterSlotController, bool> Equipped = Ref<CharacterSlotController, bool>("equipped");
        internal static readonly AccessTools.FieldRef<CharacterSlotController, Item> CurrentItem = Ref<CharacterSlotController, Item>("currentItem");

        // WatchSlotController.CalendarEquipped getter (SRC/WatchSlotController.cs:31).
        internal static readonly MethodInfo CalendarEquippedGetter = Getter(typeof(WatchSlotController), "CalendarEquipped");

        // ---- Wait (P3, P4, P5) ----

        // WaitingUI.StartSleep() (SRC/WaitingUI.cs:274), public: the clock's Sleep button and controller Interact.
        internal static readonly MethodInfo StartSleep = Method(typeof(WaitingUI), "StartSleep", Type.EmptyTypes);

        // WaitingUI.GetTypeString() (:154), private; UpdateSleptTime(float) (:122), public.
        internal static readonly MethodInfo GetTypeString = Method(typeof(WaitingUI), "GetTypeString", Type.EmptyTypes);
        internal static readonly MethodInfo UpdateSleptTime = Method(typeof(WaitingUI), "UpdateSleptTime", new[] { typeof(float) });

        // WaitingUI.setAlarmSlider / setAlarm (:31, :28): the duration slider (600-86400 in the Game Logic prefab) and
        // its row, active while the clock offers a duration.
        internal static readonly AccessTools.FieldRef<WaitingUI, Slider> SetAlarmSlider = Ref<WaitingUI, Slider>("setAlarmSlider");
        internal static readonly AccessTools.FieldRef<WaitingUI, GameObject> SetAlarm = Ref<WaitingUI, GameObject>("setAlarm");

        // WaitingUI.sleepButtons (:25): "Sleep Buttons", parent of "Sleep button/Button Text". Optional: without it the
        // start button keeps its "Sleep" label.
        internal static readonly AccessTools.FieldRef<WaitingUI, GameObject> SleepButtons = Ref<WaitingUI, GameObject>("sleepButtons", optional: true);

        // WaitingController.currentSleepSetTime (SRC/WaitingController.cs:79), private: the countdown CheckEvent runs
        // down (:156). Optional: without it a wait counts as done once the time waited reaches the target minus a minute.
        internal static readonly AccessTools.FieldRef<WaitingController, int> CurrentSleepSetTime =
            Ref<WaitingController, int>("currentSleepSetTime", optional: true);

        // Prison.inPrison (SRC/Prison.cs:80), private. Optional: without it, waiting is refused wherever a Prison exists
        // (unless AllowInJail). Never Prison.InPrison(): it logs on every call (:404-408).
        internal static readonly AccessTools.FieldRef<Prison, bool> InPrison = Ref<Prison, bool>("inPrison", optional: true);

        // InteractableLadder.playerClimbingGlobal (SRC/InteractableLadder.cs:31), protected static. Optional.
        internal static readonly FieldInfo PlayerClimbing = Field(typeof(InteractableLadder), "playerClimbingGlobal", optional: true);

        // PlayerStats.DeltaSeconds(int) (SRC/PlayerStats.cs:306), private, 266 B with a try/catch (never inlined): the
        // SecondsPassed handler that runs every stat. P7 feeds it one game second at a time during our wait; the
        // delegate calls it through its patched body, so P7's re-entry guard passes those calls through.
        internal static readonly MethodInfo StatsDeltaSeconds = Method(typeof(PlayerStats), "DeltaSeconds", new[] { typeof(int) });
        internal static readonly Action<PlayerStats, int> CallStatsDeltaSeconds = StatsDeltaSecondsDelegate();

        // ---- Danger (P6) ----

        // PlayerStatsHealth.Damage(float, DamageTypeNames, bool) (SRC/PlayerStatsHealth.cs:91).
        internal static readonly MethodInfo Damage = Method(typeof(PlayerStatsHealth), "Damage",
            new[] { typeof(float), typeof(DamageTypeNames), typeof(bool) });

        // ---- Keys ----

        // KeyBindings.GetKey(string) (SRC/KeyBindings.cs:266), private. The public CheckKey switches the input type to
        // keyboard when the key is held (:257-264), so the collision check uses this side-effect-free one. Optional.
        internal static readonly Func<KeyBindings, string, KeyCode> BindingKey = BindingKeyDelegate();

        // InteractableList._listVisible / _pooledObjects (SRC/InteractableList.cs:45, :39). In third person the list
        // reads DPadVertical during free play (:82-158), so our D-pad button stays quiet while it shows entries. The
        // other free-play reader of that axis is the minitrain throttle (JoystickAxis7, the same axis 6; SRC/Minitrain.cs
        // :216, :230), handled in Keys.PadEdge. Optional.
        internal static readonly AccessTools.FieldRef<InteractableList, bool> ListVisible = Ref<InteractableList, bool>("_listVisible", optional: true);
        internal static readonly AccessTools.FieldRef<InteractableList, List<GameObject>> ListItems =
            Ref<InteractableList, List<GameObject>>("_pooledObjects", optional: true);

        // ---- Methods we only call, checked for other mods' skipping patches (Compat, spec §10) ----

        internal static readonly MethodInfo Wait = Method(typeof(WaitingController), "Wait",
            new[] { typeof(int), typeof(WaitingController.Type), typeof(float), typeof(bool), typeof(IEnumerable<PlayerStatsTiredness.Reason>) }, optional: true);
        internal static readonly MethodInfo ShowTimeSetUI = Method(typeof(WaitingController), "ShowTimeSetUI",
            new[] { typeof(float), typeof(bool), typeof(SleepEvent[]), typeof(bool), typeof(float), typeof(Action), typeof(bool) }, optional: true);
        internal static readonly MethodInfo Wakeup = Method(typeof(WaitingController), "Wakeup", new[] { typeof(bool), typeof(bool) }, optional: true);
        internal static readonly MethodInfo ShowClock = Method(typeof(WaitingUI), "ShowClock", new[] { typeof(bool), typeof(bool) }, optional: true);
        internal static readonly MethodInfo CloseTimeSetUI = Method(typeof(WaitingUI), "CloseTimeSetUI", Type.EmptyTypes, optional: true);

        // ---- Helpers ----

        internal static MethodInfo Method(Type t, string name, Type[] args, bool optional = false)
        {
            try
            {
                var m = AccessTools.DeclaredMethod(t, name, args);
                if (m == null) Missing($"{t.Name}.{name}", null, optional);
                return m;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, optional); return null; }
        }

        private static MethodInfo Getter(Type t, string name)
        {
            try
            {
                var m = AccessTools.DeclaredPropertyGetter(t, name);
                if (m == null) Missing($"{t.Name}.{name} getter", null, false);
                return m;
            }
            catch (Exception e) { Missing($"{t.Name}.{name} getter", e, false); return null; }
        }

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name, bool optional = false)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Missing($"{typeof(T).Name}.{name}", e, optional); return null; }
        }

        private static FieldInfo Field(Type t, string name, bool optional = false)
        {
            try
            {
                var f = AccessTools.Field(t, name);
                if (f == null) Missing($"{t.Name}.{name}", null, optional);
                return f;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, optional); return null; }
        }

        private static Func<KeyBindings, string, KeyCode> BindingKeyDelegate()
        {
            var m = Method(typeof(KeyBindings), "GetKey", new[] { typeof(string) }, optional: true);
            if (m == null) return null;
            try { return (Func<KeyBindings, string, KeyCode>)Delegate.CreateDelegate(typeof(Func<KeyBindings, string, KeyCode>), m); }
            catch (Exception e) { Missing("KeyBindings.GetKey", e, true); return null; }
        }

        private static Action<PlayerStats, int> StatsDeltaSecondsDelegate()
        {
            if (StatsDeltaSeconds == null) return null;
            try { return (Action<PlayerStats, int>)Delegate.CreateDelegate(typeof(Action<PlayerStats, int>), StatsDeltaSeconds); }
            catch (Exception e) { Missing("PlayerStats.DeltaSeconds delegate", e, false); return null; }
        }

        private static void Missing(string what, Exception e, bool optional)
        {
            string why = e != null ? $" ({e.Message})" : "";
            if (optional) Log.Warn($"{what} not found{why}; the part of the feature using it is off.");
            else Log.Error($"{what} not found{why}; the feature using it is off.");
        }
    }
}
