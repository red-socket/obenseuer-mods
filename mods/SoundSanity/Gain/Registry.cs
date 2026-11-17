using System.Runtime.CompilerServices;
using UnityEngine;

namespace SoundSanity
{
    // Gains without compounding (spec §3.4). The mod never writes SoundInfo.SoundVolume, a ScriptableObject, a prefab, a
    // mixer parameter, Volume.json or a save. It writes source.volume right after a vanilla write that took it from
    // SoundVolume, or in a live pass, and always as an absolute value: Clamp01(SoundVolume) * gain. Nothing can stack,
    // Instantiate can never clone a scaled value, and at default settings nothing is written at all.
    internal static class Registry
    {
        internal enum Kind { Station, Radio }

        internal sealed class Entry
        {
            internal Kind Kind;
            internal CraftingBase Station;
            internal bool AreaStopped;
        }

        private static readonly ConditionalWeakTable<SoundInfo, Entry> Reg = new ConditionalWeakTable<SoundInfo, Entry>();

        internal static bool TryGet(SoundInfo si, out Entry e) => Reg.TryGetValue(si, out e);

        internal static void Register(SoundInfo si, Kind kind, CraftingBase station)
        {
            if (si == null || Reg.TryGetValue(si, out _)) return;
            Reg.Add(si, new Entry { Kind = kind, Station = station });
        }

        // src is the source being written: si.source for the persistent source, a fresh one for FromSound one-shots.
        internal static float Gain(SoundInfo si, Entry e, AudioSource src) => e.Kind == Kind.Station
            ? Stations.Factor(e.Station)
            : RadioGain.Interactive(si.Clip, src);

        internal static float Target(SoundInfo si, float g) => Mathf.Clamp01(si.SoundVolume) * g;

        // Vanilla-gate apply (G1-G4): vanilla just wrote SoundVolume to src. Scaled only below 100 %, so at default settings
        // nothing changes.
        internal static void ApplyAfterVanilla(SoundInfo si, Entry e, AudioSource src)
        {
            if (src == null) return;
            float g = Gain(si, e, src);
            if (g < 0.9999f) src.volume = Target(si, g);
        }

        // Live apply (spec §3.4): only a source that is playing in an audible area and that vanilla does not hold at 0.
        // isPlaying is not checked: it reads false while the pause menu pauses the listener, which is where sliders move.
        // Writes at gain 1 too, which puts back the vanilla volume.
        internal static void ApplyLive(SoundInfo si, Entry e)
        {
            var src = si.source;
            if (src == null || !si.playing || e.AreaStopped || !Areas.LiveAudible(si)) return;
            src.volume = Target(si, Gain(si, e, src));
        }

        // The gate SoundInfo.PlaySound and PlayDelayed use before writing SoundVolume (SRC/SoundInfo.cs:163, :211).
        internal static bool VanillaGate(SoundInfo si)
        {
            var area = si.soundscapeArea;
            if (area == null) return true;
            var sc = SoundscapeController.instance;
            return sc != null && (area == sc.currentSoundscape || area == sc.currentBackgroundSoundscape);
        }
    }
}
