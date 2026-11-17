using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace SoundSanity
{
    // S-074 routing fix (spec §3.2). The only writer of AudioSource.outputAudioMixerGroup in the mod. It moves a source
    // only when its group provably bypasses the volume sliders:
    //  - Duplicate: a group of the second MainMixer copy (same mixer name, other instance) that vanilla never resolved;
    //  - Null: no group at all (even Master does not apply), for world sounds only;
    //  - MainGroup: the real mixer's root group "Main" (Master only), opt-in through Routing.FixMainGroup.
    // Every other group of the real mixer (Soundscape, BackgroundSoundscape, Music, Effects, Menu, SleepingScreen...) and
    // any mixer with another name (another mod's) is never touched, so the game's ambience balance stays as designed.
    // Volume is never changed here. The original group is remembered so a live switch-off can put it back.
    internal static class Router
    {
        internal enum Ctx { HookPath, SweepImmediate, SweepRoot, SweepSettle }

        internal enum Case { None, Duplicate, Null, MainGroup }

        private sealed class Rec
        {
            internal AudioMixerGroup Original, Target;
            internal Case Case;
            internal bool Character;
        }

        private static readonly ConditionalWeakTable<AudioSource, Rec> Orig = new ConditionalWeakTable<AudioSource, Rec>();
        private static readonly List<WeakReference<AudioSource>> Restore = new List<WeakReference<AudioSource>>();
        private static readonly Dictionary<AudioMixer, bool> CopyMixers = new Dictionary<AudioMixer, bool>();
        private static readonly Dictionary<AudioMixerGroup, AudioMixerGroup> CopyTargets = new Dictionary<AudioMixerGroup, AudioMixerGroup>();
        private static readonly Dictionary<Transform, bool> ModOwnedMemo = new Dictionary<Transform, bool>();
        private static readonly Dictionary<Assembly, bool> GameAssemblies = new Dictionary<Assembly, bool>();
        private static readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>();
        private static AudioMixer _copyCacheMain;

        internal static int Moved;

        internal static bool Active => Groups.Routing.On && Cfg.Routing.Enabled.Value;

        // ---- Classification ----

        internal static void Handle(AudioSource s, Ctx ctx, string where)
        {
            if (s == null || !Active) return;
            var main = Mixers.Main;
            if (main == null) return;
            var g = s.outputAudioMixerGroup;
            Case kind;
            AudioMixerGroup target;
            bool character = false;
            if (g == null)
            {
                if (!NullEligible(s, ctx)) return;
                kind = Case.Null;
                target = Mixers.Effects;
            }
            else
            {
                var m = g.audioMixer;
                if (m == null) return;
                if (m == main)
                {
                    if (!Cfg.Routing.FixMainGroup.Value || g != Mixers.MainGroup || !MainEligible(s, ctx)) return;
                    kind = Case.MainGroup;
                    target = Mixers.Effects;
                }
                else if (IsCopy(m, main))
                {
                    kind = Case.Duplicate;
                    target = CopyTarget(g, main);
                }
                else return;
                if (ctx != Ctx.HookPath)
                {
                    character = s.GetComponentInParent<NPCInfo>(true) != null;
                    if (character && !Cfg.Routing.FixCharacterSounds.Value) return;
                }
            }
            if (target == null || target == g) return;
            Reroute(s, g, target, kind, character, where);
        }

        // Null-group sources (spec §3.2). Game code that meant to use the default group (FromClip, the AddSound helpers) is
        // always fixed. Serialized ones found by a scene or root sweep only when they are 3D world sounds that pause with the
        // game, in a gameplay scene, not on a character (bark voices are null-group 3D sources) and not added by a mod.
        // Settle sweeps run after Start, when runtime null sources exist, and never fix null groups.
        private static bool NullEligible(AudioSource s, Ctx ctx)
        {
            if (ctx == Ctx.HookPath) return true;
            if (ctx == Ctx.SweepSettle) return false;
            if (s.clip == null) return false;
            if (s.spatialBlend < 0.5f || s.ignoreListenerPause)
            {
                if (Log.VerboseOn) Log.Verbose($"Routing: left '{s.clip.name}' on '{s.gameObject.name}' alone (no group, but a 2D or pause-proof sound).");
                return false;
            }
            if (!GameplayScene(s.gameObject.scene)) return false;
            if (s.GetComponentInParent<NPCInfo>(true) != null) return false;
            if (ModOwned(s.transform))
            {
                if (Log.VerboseOn) Log.Verbose($"Routing: left '{s.clip.name}' on '{s.gameObject.name}' alone (no group, but on an object a mod added to).");
                return false;
            }
            return true;
        }

        // The opt-in Main-group fix keeps the same world-sound limits on sweeps, so a 2D or pause-proof source on Main
        // never ends up muted in the pause menu.
        private static bool MainEligible(AudioSource s, Ctx ctx) =>
            ctx == Ctx.HookPath || (s.spatialBlend >= 0.5f && !s.ignoreListenerPause && GameplayScene(s.gameObject.scene));

        private static bool GameplayScene(Scene sc)
        {
            if (!sc.IsValid()) return false;
            string n = sc.name;
            return n != "DontDestroyOnLoad" && n != "Main Menu";
        }

        // Same mixer name, other instance: the second MainMixer copy. Cached per mixer instance (no name reads on hot paths).
        private static bool IsCopy(AudioMixer m, AudioMixer main)
        {
            if (_copyCacheMain != main)
            {
                CopyMixers.Clear();
                CopyTargets.Clear();
                _copyCacheMain = main;
            }
            if (!CopyMixers.TryGetValue(m, out bool copy))
            {
                copy = m.name == main.name;
                CopyMixers[m] = copy;
            }
            return copy;
        }

        // The matching group of the real mixer. "Main" goes to Effects, as FromClip does for it. ResolveMixerGroup returns
        // its input when it fails, so its result is checked.
        private static AudioMixerGroup CopyTarget(AudioMixerGroup g, AudioMixer main)
        {
            if (CopyTargets.TryGetValue(g, out var t) && t != null) return t;
            string name = g.name;
            if (name == "Main") t = Mixers.Effects;
            else
            {
                var r = AudioSourceMaker.ResolveMixerGroup(g);
                t = r != null && r.audioMixer == main ? r : Mixers.Exact(name);
                if (t == null) t = Mixers.Effects;
            }
            if (t != null) CopyTargets[g] = t;
            return t;
        }

        // A MonoBehaviour from an assembly outside the game's Managed folder on the source or any parent.
        private static bool ModOwned(Transform t)
        {
            if (t == null) return false;
            if (ModOwnedMemo.TryGetValue(t, out bool v)) return v;
            v = HasForeign(t) || ModOwned(t.parent);
            ModOwnedMemo[t] = v;
            return v;
        }

        private static bool HasForeign(Transform t)
        {
            t.GetComponents(Behaviours);
            foreach (var mb in Behaviours)
                if (mb != null && !IsGameAssembly(mb.GetType().Assembly)) return true;
            return false;
        }

        private static bool IsGameAssembly(Assembly a)
        {
            if (GameAssemblies.TryGetValue(a, out bool game)) return game;
            string name = a.GetName().Name ?? "";
            game = name == "Assembly-CSharp" || name == "Assembly-CSharp-firstpass" || name.StartsWith("UnityEngine", StringComparison.Ordinal);
            if (!game)
            {
                try
                {
                    string loc = a.IsDynamic ? "" : a.Location;
                    game = !string.IsNullOrEmpty(loc) && System.IO.Path.GetFullPath(loc)
                        .StartsWith(System.IO.Path.GetFullPath(BepInEx.Paths.ManagedPath), StringComparison.OrdinalIgnoreCase);
                }
                catch { game = false; }
            }
            GameAssemblies[a] = game;
            return game;
        }

        // ---- Re-routing and restoring ----

        private static void Reroute(AudioSource s, AudioMixerGroup g, AudioMixerGroup target, Case kind, bool character, string where)
        {
            if (Orig.TryGetValue(s, out var rec))
            {
                if (g != rec.Target) rec.Original = g;
                rec.Target = target;
                rec.Case = kind;
                rec.Character = character;
            }
            else
            {
                Orig.Add(s, new Rec { Original = g, Target = target, Case = kind, Character = character });
                Restore.Add(new WeakReference<AudioSource>(s));
            }
            s.outputAudioMixerGroup = target;
            Moved++;
            Log.Reroute(kind, g, target, s, where);
        }

        private static bool Wanted(Rec r) =>
            Active && (r.Case != Case.MainGroup || Cfg.Routing.FixMainGroup.Value) && (!r.Character || Cfg.Routing.FixCharacterSounds.Value);

        // Live pass for Routing.Enabled, FixMainGroup and FixCharacterSounds (spec §3.2): sources of a case that was switched
        // off get their original group back when nothing else has moved them since; switched back on, they get the fix again
        // and one settle sweep picks up the rest. This is also the in-game A/B switch for test T0.
        internal static void OnToggle()
        {
            if (!Groups.Routing.Patched)
            {
                if (Cfg.Routing.Enabled.Value) Log.InfoOnce("routing-launch", "Routing: the fix starts at the next launch (it was off when the game started).");
                return;
            }
            int restored = 0, reapplied = 0;
            for (int i = Restore.Count - 1; i >= 0; i--)
            {
                if (!Restore[i].TryGetTarget(out var s) || s == null) { Restore.RemoveAt(i); continue; }
                if (!Orig.TryGetValue(s, out var rec)) continue;
                var cur = s.outputAudioMixerGroup;
                if (!Wanted(rec))
                {
                    if (cur == rec.Target) { s.outputAudioMixerGroup = rec.Original; restored++; }
                }
                else if (cur == rec.Original) { s.outputAudioMixerGroup = rec.Target; reapplied++; }
            }
            if (restored > 0 || reapplied > 0)
                Log.Info($"Routing: settings changed; {restored} sound(s) back on their original group, {reapplied} fixed again.");
            if (Active) Scheduler.QueueSettle(0f, false);
        }

        internal static void OnSceneLoaded()
        {
            for (int i = Restore.Count - 1; i >= 0; i--)
                if (!Restore[i].TryGetTarget(out var s) || s == null) Restore.RemoveAt(i);
            ModOwnedMemo.Clear();
        }
    }
}
