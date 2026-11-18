using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace NoFloorSpill
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.nofloorspill";
        public const string Name = "No Floor Spill";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();

        // Two patch groups (spec §2), each patched only when enabled at launch; a group that fails logs one error and
        // leaves the other working.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // U-009 / S-088 refuse half (spec §2.2).
            PatchGroup("refuse overflow", "refuse overflow (" + Cfg.RefuseSummary() + ")",
                Cfg.RefuseModeEntry.Value != RefuseMode.Vanilla, RefuseSink.Group,
                Needs(Vanilla.SpawnRefuse, Vanilla.SpawnRefuseAll, Vanilla.SlotAddItem, RefuseSink.AddMethod),
                new MethodBase[] { Vanilla.SpawnRefuse, Vanilla.SpawnRefuseAll },
                typeof(SpawnRefusePatch), typeof(SpawnRefuseAllPatch));

            // S-088 byproduct half, #6570 (spec §2.3).
            PatchGroup("machine junk overflow", "machine junk overflow", Cfg.JunkEnabled.Value, JunkSink.Group,
                Needs(Vanilla.RemoveAmmo, Vanilla.StorageAddItem, Vanilla.ManuActive, Vanilla.OverflowSpot, Vanilla.DropValues,
                    Vanilla.StorageChanged, JunkSink.AddMethod),
                new MethodBase[] { Vanilla.RemoveAmmo },
                typeof(RemoveAmmoPatch));

            SaveController.PlayerWillChangeLevel += OnPlayerWillChangeLevel;
            SaveController.LoadingDone += OnLoadingDone;
            Application.quitting += OnQuitting;
            QuitHook.Apply(Harmony);

            Log.Info($"{Name} {Version} on game {Application.version}; on: {List(_on)}; off: {List(_off)}; failed: {List(_failed)}");
        }

        private static string List(List<string> l) => l.Count == 0 ? "—" : string.Join(", ", l);

        private static object[] Needs(params object[] members) => members;

        // House PatchGroup with the IlSwap check (spec §2.1): after patching, every transpiled target must have matched.
        // Otherwise the whole group is unpatched again, so there is never a half-patched state, and stays inactive.
        private void PatchGroup(string name, string label, bool enabled, SwapGroup group, object[] needs, MethodBase[] targets,
            params Type[] types)
        {
            group.Active = false;
            if (!enabled)
            {
                _off.Add(name);
                return;
            }
            try
            {
                foreach (var n in needs)
                    if (n == null) throw new MissingMemberException("a game member this feature needs is missing (see the error above)");
                group.Matched.Clear();
                foreach (var t in types) Harmony.CreateClassProcessor(t).Patch();

                var missing = new List<string>();
                foreach (var m in targets)
                    if (!group.Matched.TryGetValue(SwapGroup.Key(m), out bool ok) || !ok) missing.Add(SwapGroup.Key(m));
                if (missing.Count > 0)
                {
                    Rollback(targets);
                    Log.Error($"Patching '{name}' failed: {string.Join(", ", missing.ToArray())} did not match; that feature is off this " +
                        "session and the game's own behaviour runs.");
                    _failed.Add(name);
                    return;
                }
                group.Active = true;
                _on.Add(label);
            }
            catch (Exception e)
            {
                group.Active = false;
                Rollback(targets);
                Log.Error($"Patching '{name}' failed; that feature is off this session: {e}");
                _failed.Add(name);
            }
        }

        private static void Rollback(MethodBase[] targets)
        {
            foreach (var m in targets)
            {
                if (m == null) continue;
                try { Harmony.Unpatch(m, HarmonyPatchType.All, Guid); }
                catch (Exception e) { Log.Exception("unpatch " + SwapGroup.Key(m), e); }
            }
        }

        private static void OnPlayerWillChangeLevel()
        {
            Step("level change", () =>
            {
                Tally.Flush();
                NearbyStorage.ClearCache();
            });
        }

        private static void OnLoadingDone() => Step("load", NearbyStorage.ClearCache);

        // Only a graceful exit gets here (the OS closing the window: Alt+F4 or the title-bar X; or SteamManager's
        // Application.Quit); the game's quit buttons kill the process (QuitHook). Flush the disk log here too.
        private static void OnQuitting() => Step("quit", () =>
        {
            Tally.SessionSummary();
            QuitHook.FlushDiskLog();
        });

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            Tally.FlushIfFrameChanged();
        }

        private void OnDestroy()
        {
            SaveController.PlayerWillChangeLevel -= OnPlayerWillChangeLevel;
            SaveController.LoadingDone -= OnLoadingDone;
            Application.quitting -= OnQuitting;
        }
    }
}
