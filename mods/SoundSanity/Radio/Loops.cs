using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace SoundSanity
{
    // Background radio and bar music loops (spec §4.2). AR1 scales SoundscapeSoundLoop.SoundVolumePercent(), so the
    // vanilla Volume getter (volume * SoundVolumePercent()) and every fade target pick the gain up exactly once. At gain 0
    // PlayLoopAudio takes its "== 0" branch and fades the loop out; Zeroed remembers which loops that happened to, so a
    // later unmute restarts only those. A SoundscapeSoundLoop belongs to the shared Soundscape asset, so two areas that
    // use the same asset share it (Interior Bazaar Storage, Interior Tenement Deekula B): the live pass reads Zeroed for
    // every candidate before any Volume read updates it.
    internal static class Loops
    {
        internal static readonly HashSet<SoundscapeSoundLoop> Zeroed = new HashSet<SoundscapeSoundLoop>();
        private static readonly HashSet<SoundscapeSoundLoop> Logged = new HashSet<SoundscapeSoundLoop>();
        private static readonly List<SoundscapeArea> Areas2 = new List<SoundscapeArea>(2);
        private static readonly List<(SoundscapeArea area, SoundscapeSoundLocation loc, SoundscapeSoundLoop loop, bool wasZeroed)> Cands =
            new List<(SoundscapeArea, SoundscapeSoundLocation, SoundscapeSoundLoop, bool)>();

        // Only a full level load ends the level's loops. An additive load (a door's LoadSceneAsyncTrigger preloading the
        // next level while the player is still here) keeps them.
        internal static void OnSceneLoaded(LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            Zeroed.Clear();
            Logged.Clear();
        }

        // AR1 body.
        internal static float Scale(SoundscapeSoundLoop loop, float result)
        {
            if (result <= 0f) return result;
            float g = RadioGain.Ambient(loop.audioClip);
            if (g == 0f) Zeroed.Add(loop);
            else Zeroed.Remove(loop);
            if (Logged.Add(loop) && Clips.IsMusic(loop.audioClip))
                Log.InfoOnce("radio-a|" + loop.audioClip.name, $"Radio music: '{loop.audioClip.name}' (ambient)");
            return g != 1f ? result * g : result;
        }

        // Live pass over the music loops of the playing areas (spec §4.2 steps 1-7).
        internal static void LivePass()
        {
            if (!Groups.AmbientRadios.On) return;
            var sc = SoundscapeController.instance;
            if (sc == null) return;
            Areas2.Clear();
            if (sc.currentSoundscape != null) Areas2.Add(sc.currentSoundscape);
            if (sc.currentBackgroundSoundscape != null && sc.currentBackgroundSoundscape != sc.currentSoundscape) Areas2.Add(sc.currentBackgroundSoundscape);

            // 1. Snapshot: Zeroed is keyed by the shared loop, and the first Volume read below updates it.
            Cands.Clear();
            foreach (var area in Areas2)
            {
                if (area.loopSoundPlayLocations == null) continue;
                foreach (var loc in area.loopSoundPlayLocations)
                {
                    if (loc == null || !(loc.soundscapeSound is SoundscapeSoundLoop loop) || loc.audioSource == null || !Clips.IsMusic(loop.audioClip)) continue;
                    Cands.Add((area, loc, loop, Zeroed.Contains(loop)));
                }
            }

            // 2. Apply.
            int n = 0;
            foreach (var (area, loc, loop, wasZeroed) in Cands)
            {
                var src = loc.audioSource;
                float target = loop.Volume;                                 // base x weather/SMV x gain
                bool started = SoundCoroutine.IndexExistsInList(area.loopSoundsStartCoroutines, loc.index);
                if (started && target <= 0f)
                    area.StopLoopSound(src, src.volume, loc.index);         // vanilla fade-out; removes the start latch
                else if (started)
                {
                    foreach (var e in area.loopSoundsStartCoroutines)       // stop a fade-in that is still climbing
                        if (e.index == loc.index)
                        {
                            if (e.coroutine != null) area.StopCoroutine(e.coroutine);
                            break;
                        }
                    src.volume = target;
                }
                else if (target > 0f && wasZeroed)
                    sc.PlayLoopAudio(area, loc, false);                     // vanilla re-checks hours, weather and chance
                else continue;
                n++;
            }
            Cands.Clear();
            Log.Verbose($"Radio music: live pass over {n} background loop(s).");
        }
    }
}
