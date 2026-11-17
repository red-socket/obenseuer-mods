using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SoundSanity
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.soundsanity";
        public const string Name = "Sound Sanity";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private readonly List<string> _on = new List<string>();
        private readonly List<string> _off = new List<string>();
        private readonly List<string> _failed = new List<string>();

        // Each feature is patched as its own group (spec §5), only when it is on at launch. A group checks the game members
        // it needs first; a missing member or a patch exception unpatches that group's own targets, logs one error and
        // leaves the other groups working. Every patch body re-reads its config entry live and falls back to vanilla after
        // its first exception (Core/Groups.cs). Sweeps and live passes run from Update, which also runs while paused.
        // Runtime only: nothing is written to saves, GlobalState, Volume.json or the mixer's parameters.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");
            Categories.Reparse();
            Clips.Recompile();

            Harmony = new Harmony(Guid);

            // S-074 routing fix (spec §3.2-3.3).
            PatchGroup(Groups.Routing, Cfg.Routing.Enabled.Value,
                Needs(Vanilla.FromClip, Vanilla.CreateAudioSource, Vanilla.ResolveMixerGroups),
                Targets(Vanilla.FromClip, Vanilla.CreateAudioSource, Vanilla.ResolveMixerGroups),
                typeof(FromClipPatch), typeof(CreateAudioSourcePatch), typeof(ResolveMixerGroupsPatch));
            PatchSpawns();
            PatchGroup(Groups.Characters, Groups.Routing.Patched,
                Needs(Vanilla.SpawnResidents, Vanilla.NpcList, Vanilla.AddNpcToList),
                Targets(Vanilla.SpawnResidents, Vanilla.AddNpcToList),
                typeof(SpawnResidentsPatch), typeof(AddNpcPatch));

            // Gains (spec §3.4): shared by machine volume and interactive radios.
            bool gains = Cfg.Machines.Enabled.Value || Cfg.Radio.Enabled.Value;
            PatchGroup(Groups.AreaTracking, gains, Needs(Vanilla.AddSoundInfo), Targets(Vanilla.AddSoundInfo), typeof(AddSoundInfoPatch));
            PatchGroup(Groups.GainCore, gains,
                Needs(Vanilla.FromSound, Vanilla.PlaySound, Vanilla.PlayDelayed, Vanilla.SoundscapeAreaPlay, Vanilla.SoundscapeAreaStop),
                Targets(Vanilla.FromSound, Vanilla.PlaySound, Vanilla.PlayDelayed, Vanilla.SoundscapeAreaPlay, Vanilla.SoundscapeAreaStop),
                typeof(FromSoundPatch), typeof(PlaySoundPatch), typeof(PlayDelayedPatch), typeof(SoundscapeAreaPlayPatch), typeof(SoundscapeAreaStopPatch));

            // S-074 machine volume (spec §4.1).
            PatchGroup(Groups.Machines, Cfg.Machines.Enabled.Value && Groups.GainCore.Patched,
                Needs(Vanilla.CraftingBaseStart), Targets(Vanilla.CraftingBaseStart), typeof(CraftingBaseStartPatch));

            // U-012 radio music (spec §4.2). Ambient loops do not need the gain core.
            PatchGroup(Groups.Radios, Cfg.Radio.Enabled.Value && Groups.GainCore.Patched,
                Needs(Vanilla.RadioPlaySound, Vanilla.RadioSoundInfo, Vanilla.RadioSounds), Targets(Vanilla.RadioPlaySound), typeof(RadioPlaySoundPatch));
            // RA2: re-applies the radio gain when the game moves a radio to another soundscape group. Without it, radios
            // still work but can keep the gain of their previous group until the next area change or live pass.
            PatchGroup(Groups.RadioChannel, Groups.Radios.Patched,
                Needs(Vanilla.ChangeMixersInfos), Targets(Vanilla.ChangeMixersInfos), typeof(ChangeMixersPatch));
            PatchGroup(Groups.AmbientRadios, Cfg.Radio.Enabled.Value,
                Needs(Vanilla.SoundVolumePercent), Targets(Vanilla.SoundVolumePercent), typeof(SoundVolumePercentPatch));
            PatchGroup(Groups.MusicSlider, Cfg.Radio.Enabled.Value,
                Needs(Vanilla.SaveVolume, Vanilla.GetVolume), Targets(Vanilla.SaveVolume, Vanilla.GetVolume),
                typeof(SaveVolumePatch), typeof(GetVolumePatch));

            // Static events: subscribed once, unsubscribed in OnDestroy.
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.LoadingDone += OnLoadingDone;

            Log.Info($"{Name} {Version} on game {Application.version}; on: {List(_on)}; off: {List(_off)}; failed: {List(_failed)}");
        }

        // R4, R5, R7: each target on its own; a missing or failing one drops only itself.
        private void PatchSpawns()
        {
            var g = Groups.Spawns;
            if (!Groups.Routing.Patched)
            {
                _off.Add(g.Name);
                return;
            }
            var parts = new (string name, MethodBase target, bool ok, Type type)[]
            {
                ("LightsON", Vanilla.LightsOnAddSound, true, typeof(LightsOnAddSoundPatch)),
                ("LightFlicker", Vanilla.LightFlickerAddSound, true, typeof(LightFlickerAddSoundPatch)),
                ("Bell", Vanilla.BellAddSound, true, typeof(BellAddSoundPatch)),
                ("UseTriggerMoney", Vanilla.MoneyAddSound, true, typeof(MoneyAddSoundPatch)),
                ("apartment upgrades", Vanilla.ProcessGuids, true, typeof(ProcessGuidsPatch)),
                ("building upgrades", Vanilla.UpdateInstantiatedPrefab, true, typeof(UpdateInstantiatedPrefabPatch)),
                ("shop previews", Vanilla.ChangeSelection, Vanilla.CurrentPreview != null, typeof(ShopPreviewPatch)),
            };
            var dropped = new List<string>();
            foreach (var p in parts)
            {
                if (p.target == null || !p.ok)
                {
                    dropped.Add(p.name);
                    continue;
                }
                try
                {
                    Harmony.CreateClassProcessor(p.type).Patch();
                    g.Patched = true;
                }
                catch (Exception e)
                {
                    Rollback(new[] { p.target });
                    Log.Warn($"Patching '{g.Name}: {p.name}' failed; only that part is off this session: {e.Message}");
                    dropped.Add(p.name);
                }
            }
            if (g.Patched) _on.Add(dropped.Count == 0 ? g.Name : $"{g.Name} (without {string.Join(", ", dropped.ToArray())})");
            else _failed.Add(g.Name);
        }

        private static string List(List<string> l) => l.Count == 0 ? "—" : string.Join(", ", l.ToArray());

        private static object[] Needs(params object[] members) => members;

        private static MethodBase[] Targets(params MethodBase[] targets) => targets;

        private void PatchGroup(Group group, bool enabled, object[] needs, MethodBase[] targets, params Type[] types)
        {
            if (!enabled)
            {
                _off.Add(group.Name);
                return;
            }
            try
            {
                foreach (var n in needs)
                    if (n == null) throw new MissingMemberException("a game member this feature needs is missing (see the error above)");
                foreach (var t in types) Harmony.CreateClassProcessor(t).Patch();
                group.Patched = true;
                _on.Add(group.Name);
            }
            catch (Exception e)
            {
                Rollback(targets);
                Log.Error($"Patching '{group.Name}' failed; that feature is off this session: {e}");
                _failed.Add(group.Name);
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
            Step("scene (caches)", () =>
            {
                Log.OnSceneLoaded();
                Clips.OnSceneLoaded();
                Loops.OnSceneLoaded(mode);
                Router.OnSceneLoaded();
                Stations.Prune();
                Radios.Prune();
            });
            Step("compat (plugins)", Compat.DetectOnce);
            if (GlobalState.instance != null) Step("compat", Compat.ReportOnce);
            if (Router.Active)
            {
                // After Awake, before Start: only serialized sources exist yet. The settles catch what Start adds.
                Step("scene sweep", () => Sweeps.SweepSceneImmediate(scene));
                Scheduler.QueueSettle(0f, true);
                Scheduler.QueueSettle(1f, true);
            }
        }

        private static void OnLoadingDone()
        {
            if (!Router.Active) return;
            Scheduler.QueueSettle(0f, false);
            Scheduler.QueueSettle(1f, false);
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            Step("scheduler", Scheduler.Tick);
            Step("live", Live.Pump);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.LoadingDone -= OnLoadingDone;
        }
    }
}
