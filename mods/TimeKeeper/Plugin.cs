using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TimeKeeper
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.timekeeper";
        public const string Name = "Time Keeper";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();
        private static bool _compatReported;

        // Each feature is patched as its own group (spec §4.1), only when it is on at launch. A group checks the game
        // members it needs first; a missing member or a patch exception unpatches that group's own targets, logs one
        // error and leaves the other groups working. Every patch body also re-reads its config entry live.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // U-004 / S-064 (spec §4.2 P2, P1). P2 alone covers the game's HUD; P1 lets other HUD mods follow.
            Weekday.Patched = PatchGroup("weekday", Cfg.Weekday.Enabled.Value,
                Needs(Vanilla.DayOfWeek, Vanilla.TimeMesh, Vanilla.HideDayOfWeek, Vanilla.ShowDayOfWeek, Vanilla.Equipped, Vanilla.CurrentItem),
                Targets(Vanilla.DayOfWeek), typeof(DayOfWeekPatch));
            PatchGroup("weekday property", Weekday.Patched && Cfg.Weekday.ShareWithHudMods.Value,
                Needs(Vanilla.CalendarEquippedGetter),
                Targets(Vanilla.CalendarEquippedGetter), typeof(CalendarEquippedPatch));

            // U-007 / S-079 (spec §4.2 P3-P5, P7): all or nothing, so a wait never runs without its label, steps, the
            // StartSleep guard or the per-second stat update that makes it cost what idle time costs.
            WaitSession.Patched = PatchGroup("wait", Cfg.Wait.Enabled.Value,
                Needs(Vanilla.StartSleep, Vanilla.GetTypeString, Vanilla.UpdateSleptTime, Vanilla.SetAlarmSlider, Vanilla.SetAlarm,
                    Vanilla.StatsDeltaSeconds, Vanilla.CallStatsDeltaSeconds),
                Targets(Vanilla.StartSleep, Vanilla.GetTypeString, Vanilla.UpdateSleptTime, Vanilla.StatsDeltaSeconds),
                typeof(StartSleepPatch), typeof(GetTypeStringPatch), typeof(UpdateSleptTimePatch), typeof(CostPatch));

            // Spec §4.2 P6. Without it DangerSeconds and StopWhenHurt do nothing; the police and robber checks still work.
            Danger.Patched = PatchGroup("danger", WaitSession.Patched,
                Needs(Vanilla.Damage), Targets(Vanilla.Damage), typeof(DamagePatch));
            if (WaitSession.Patched && !Danger.Patched)
                Log.Warn("Wait: DangerSeconds and StopWhenHurt are off this session (the damage hook failed).");

            SceneManager.sceneLoaded += OnSceneLoaded;

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
            Step("scene (wait)", WaitSession.OnSceneLoaded);
            Step("scene (keys)", Keys.CheckCollisions);
            if (!_compatReported)
            {
                _compatReported = true;
                Step("compat", ReportCompat);
            }
        }

        // Other mods' skipping patches on what we patch or call (spec §10), once all plugins have loaded.
        private static void ReportCompat()
        {
            if (Weekday.Patched)
            {
                Compat.Overlap(Vanilla.DayOfWeek, "the weekday on watches");
                Compat.Overlap(Vanilla.CalendarEquippedGetter, "the weekday on watches");
            }
            if (WaitSession.Patched)
            {
                Compat.Foreign(Vanilla.StartSleep, "Time Keeper's wait", "may not start, or may start a sleep instead");
                Compat.Foreign(Vanilla.Wait, "Time Keeper's wait", "may not run as described");
                Compat.Foreign(Vanilla.ShowTimeSetUI, "Time Keeper's waiting clock", "may not open");
                Compat.Foreign(Vanilla.Wakeup, "stopping a wait", "may not work");
                Compat.Foreign(Vanilla.ShowClock, "Time Keeper's waiting clock", "may not open");
                Compat.Foreign(Vanilla.CloseTimeSetUI, "closing Time Keeper's waiting clock", "may not work");
                Compat.Overlap(Vanilla.GetTypeString, "the \"Waiting\" label");
                Compat.Overlap(Vanilla.UpdateSleptTime, "the wait duration steps");
                Compat.Overlap(Vanilla.StatsDeltaSeconds, "what a wait costs");
            }
            if (Danger.Patched) Compat.Overlap(Vanilla.Damage, "the danger check");
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            if (WaitSession.Patched) Step("wait", WaitSession.Tick);
            Step("key notes", Keys.ShowPending);
        }

        // Stopping a wait runs after GameController.Update (spec §6.5), so the stop key never also reaches the game.
        private void LateUpdate()
        {
            if (WaitSession.Patched) Step("wait stop", WaitSession.LateTick);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }
}
