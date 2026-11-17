using System.Collections.Generic;

namespace SoundSanity
{
    // Interactive radios and record players (spec §4.2). Registered the first time they play (RA1); turntables are Radio
    // components too and never stations, so the machine and radio gains cannot stack.
    internal static class Radios
    {
        private static readonly HashSet<global::Radio> All = new HashSet<global::Radio>();
        private static readonly List<global::Radio> Dead = new List<global::Radio>();

        // RA1 body: runs before vanilla's ChangeClip + SoundInfo.PlaySound(), which G2 then scales with the new clip.
        internal static void BeforePlay(global::Radio r)
        {
            var si = Vanilla.RadioSoundInfo(r);
            if (si == null) return;
            Registry.Register(si, Registry.Kind.Radio, null);
            All.Add(r);
            var ra = Vanilla.RadioSounds(r);
            int idx = r.CurrentState - 1;
            if (ra == null || ra.audioClips == null || idx < 0 || idx >= ra.audioClips.Length) return;
            var clip = ra.audioClips[idx];
            if (clip != null && Clips.IsMusic(clip)) Log.InfoOnce("radio-i|" + clip.name, $"Radio music: '{clip.name}' (interactive)");
        }

        // RA2 body: the game has just moved these SoundInfos to the Soundscape or BackgroundSoundscape group. The radio gain
        // depends on the group (RadioGain.Interactive), so registered radios get it again at once. ApplyLive keeps vanilla's
        // gates: a radio that is off, or held at 0 by a stopped area, is left alone, and G4 handles an area that starts.
        internal static void AfterChannelChange(List<SoundInfo> soundInfos)
        {
            if (!Groups.Radios.On || !Groups.GainCore.On) return;
            foreach (var si in soundInfos)
                if (si != null && Registry.TryGet(si, out var e) && e.Kind == Registry.Kind.Radio) Registry.ApplyLive(si, e);
        }

        // Drops radios Unity has destroyed (every level change reloads the scene). Called on each sceneLoaded and before a
        // live pass.
        internal static void Prune()
        {
            Dead.Clear();
            foreach (var r in All)
                if (r == null) Dead.Add(r);
            foreach (var r in Dead) All.Remove(r);
            Dead.Clear();
        }

        // Live pass: a radio that is off has playing == false and is skipped by ApplyLive.
        internal static void LivePass()
        {
            if (!Groups.Radios.On || !Groups.GainCore.On) return;
            Prune();
            int n = 0;
            foreach (var r in All)
            {
                var si = Vanilla.RadioSoundInfo(r);
                if (si != null && Registry.TryGet(si, out var e)) { Registry.ApplyLive(si, e); n++; }
            }
            Log.Verbose($"Radio music: live pass over {n} radio(s).");
        }
    }
}
