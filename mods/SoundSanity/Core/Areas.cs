using System.Runtime.CompilerServices;

namespace SoundSanity
{
    // Whether a live pass may write a registered source (spec §3.4); it must never wake a sound vanilla holds at 0.
    // Vanilla's own gate (SoundInfo.PlaySound, SRC/SoundInfo.cs:163) looks only at soundInfo.soundscapeArea. Most radios
    // keep that null while sitting in an area's soundInfos list (845 of 890 areas have no id; Radio.AwakeDelay adds the
    // SoundInfo to whichever area is current), and vanilla then plays them at SoundVolume whatever that area is doing.
    // The list owner's stop and play still reach them (SoundscapeAreaStop / SoundscapeAreaPlay), and G5, G4 and G2 keep
    // Registry.Entry.AreaStopped in step with that, so with the gain core on such a source counts as audible unless
    // AreaStopped (checked by the caller). The owner recorded by G6 is only a fallback for when the gain core is off.
    internal static class Areas
    {
        private static bool Patched => Groups.AreaTracking.On;

        private static readonly ConditionalWeakTable<SoundInfo, SoundscapeArea> Owner = new ConditionalWeakTable<SoundInfo, SoundscapeArea>();

        // G6 body. CWT has no AddOrUpdate on Unity's Mono profile.
        internal static void SetOwner(SoundInfo si, SoundscapeArea area)
        {
            if (si == null || area == null) return;
            Owner.Remove(si);
            Owner.Add(si, area);
        }

        // True when a live pass may write this source's volume.
        internal static bool LiveAudible(SoundInfo si)
        {
            var area = si.soundscapeArea;
            if (area == null)
            {
                if (Groups.GainCore.On) return true;
                if (Owner.TryGetValue(si, out var owner)) area = owner;
            }
            if (!Patched && si.source != null && si.source.volume <= 0f) return false;
            if (area == null) return true;
            var sc = SoundscapeController.instance;
            return sc != null && sc.SoundscapeIsPlaying(area);
        }
    }
}
