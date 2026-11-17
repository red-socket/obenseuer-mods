using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SoundSanity
{
    // RA1: Radio.PlaySound() (SRC/Radio.cs:126) prefix. Registers the radio's SoundInfo; vanilla then changes the clip and
    // calls SoundInfo.PlaySound(), where G2 applies the gain for the new clip. Never skips the original. A muted radio keeps
    // playing at volume 0, so CurrentState, RadioEnabled and the save are untouched (soundInfo is [fsIgnore] anyway).
    [HarmonyPatch]
    internal static class RadioPlaySoundPatch
    {
        private static MethodBase TargetMethod() => Vanilla.RadioPlaySound;

        [HarmonyPriority(Priority.Low)]
        private static void Prefix(global::Radio __instance)
        {
            if (__instance == null || !Groups.Radios.On) return;
            try { Radios.BeforePlay(__instance); }
            catch (Exception e) { Groups.Radios.Fail(e); }
        }
    }

    // RA2: SoundscapeSoundTypes.ChangeLocationsMixers(List<SoundInfo>, AudioMixerGroup, AudioMixerGroup) postfix
    // (SRC/SoundscapeSoundTypes.cs:18), the SoundInfo overload. SoundscapeController moves an area's SoundInfos to the
    // Soundscape or BackgroundSoundscape group here. PlaySoundscape follows it with SoundscapeAreaPlay (G4), but
    // SetBackgroundSoundscape skips that when the new background area is still the current one (SRC/
    // SoundscapeController.cs:175-181), which is how a room usually becomes the background as the player walks out of it.
    // The radio gain depends on the group, so it is re-applied here. Never changes the group or skips the original.
    [HarmonyPatch]
    internal static class ChangeMixersPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ChangeMixersInfos;

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(List<SoundInfo> soundInfos)
        {
            if (soundInfos == null || !Groups.RadioChannel.On) return;
            try { Radios.AfterChannelChange(soundInfos); }
            catch (Exception e) { Groups.RadioChannel.Fail(e); }
        }
    }

    // AR1: SoundscapeSoundLoop.SoundVolumePercent() (SRC/SoundscapeSoundLoop.cs:48) postfix. The 14-byte Volume getter
    // calls it and is deliberately not patched: patching both would square the gain. The shared Soundscape asset is never
    // changed.
    [HarmonyPatch]
    internal static class SoundVolumePercentPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SoundVolumePercent;

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(SoundscapeSoundLoop __instance, ref float __result)
        {
            if (__instance == null || !Groups.AmbientRadios.On) return;
            try { __result = Loops.Scale(__instance, __result); }
            catch (Exception e) { Groups.AmbientRadios.Fail(e); }
        }
    }
}
